using System.Collections.Concurrent;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace PartyFinderWatch;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/pfwatch";
    private const string PartyFinderAddon = "LookingForGroup";
    private const string ListingDetailAddon = "LookingForGroupDetail";
    private const byte DataCenterTab = 0;
    private const byte AllCategoriesTab = 0;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPartyFinderGui partyFinder;
    private readonly IGameGui gameGui;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly WorldDataCenters worldDataCenters;
    private readonly WatchWindow window;
    private readonly ConcurrentQueue<PendingListing> incoming = new();
    private readonly Dictionary<ulong, Listing> listings = [];
    private readonly Dictionary<ulong, DateTimeOffset> alerted = [];
    private readonly HashSet<ulong> unread = [];
    private readonly HashSet<int> responseBatches = [];
    private readonly HashSet<ulong> responseListings = [];
    private uint dataCenterId;
    private string dataCenterName = "—";
    private bool dataCenterReady;
    private DateTimeOffset nextAttempt;
    private DateTimeOffset lastAttempt;
    private DateTimeOffset nextPrune;
    private DateTimeOffset responseDeadline;
    private DateTimeOffset? lastResponse;
    private DateTimeOffset lastAlert;
    private RefreshMode? pendingRefresh;
    private RefreshMode? lastResponseMode;
    private bool manuallyRefresh;
    private bool previewSoundRequested;
    private bool nativeRefreshUnavailable;
    private bool backgroundRefreshUnavailable;

    internal WatchConfiguration Config { get; }
    internal DutyCatalog Catalog { get; }
    internal string DataCenter => dataCenterReady ? dataCenterName : "—";
    internal string Status { get; private set; } = "Waiting for Party Finder";

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IFramework framework,
        IPartyFinderGui partyFinder, IGameGui gameGui, IClientState clientState, IPlayerState playerState,
        ICondition condition, IDataManager dataManager, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.partyFinder = partyFinder;
        this.gameGui = gameGui;
        this.clientState = clientState;
        this.playerState = playerState;
        this.condition = condition;
        this.log = log;
        worldDataCenters = new WorldDataCenters(dataManager);
        Catalog = new DutyCatalog(dataManager);
        Config = pluginInterface.GetPluginConfig() as WatchConfiguration ?? new WatchConfiguration { WatchSetupComplete = false };
        window = new WatchWindow(this);

        commands.AddHandler(Command, new CommandInfo((_, _) => window.Open())
        {
            HelpMessage = "Open the Party Finder Watch window.",
        });
        pluginInterface.UiBuilder.Draw += window.Draw;
        pluginInterface.UiBuilder.OpenMainUi += window.Open;
        pluginInterface.UiBuilder.OpenConfigUi += window.OpenSettings;
        partyFinder.ReceiveListing += ReceiveListing;
        framework.Update += Update;
    }

    public void Dispose()
    {
        framework.Update -= Update;
        partyFinder.ReceiveListing -= ReceiveListing;
        pluginInterface.UiBuilder.OpenConfigUi -= window.OpenSettings;
        pluginInterface.UiBuilder.OpenMainUi -= window.Open;
        pluginInterface.UiBuilder.Draw -= window.Draw;
        commands.RemoveHandler(Command);
    }

    internal IEnumerable<Listing> CurrentListings => dataCenterReady ? listings.Values : [];
    internal int UnreadCount => dataCenterReady ? unread.Count : 0;
    internal bool IsFlashing => Config.FlashIcon && UnreadCount > 0
        && DateTimeOffset.UtcNow - lastAlert < TimeSpan.FromSeconds(5);
    internal string ScanSummary => dataCenterReady && lastResponse is { } time
        ? $"Last data {time.ToLocalTime():HH:mm:ss} · {responseListings.Count} listings · {responseBatches.Count} batches"
            + (lastResponseMode == RefreshMode.Background ? " · Background refresh" : "")
        : "No listings received yet";

    internal string MatchNames(Listing listing) => string.Join(", ", Config.Watches
        .Where(watch => watch.Matches(listing)).Select(watch => watch.Name));

    internal void Save() => pluginInterface.SavePluginConfig(Config);
    internal void Acknowledge() => unread.Clear();
    internal void RequestRefresh() => manuallyRefresh = true;
    internal void PreviewAlertSound() => previewSoundRequested = true;
    internal void ResetTimer() => nextAttempt = DateTimeOffset.UtcNow.AddSeconds(Config.RefreshSeconds);

    private void Update(IFramework _)
    {
        var now = DateTimeOffset.UtcNow;
        dataCenterReady = UpdateDataCenter();
        if (!dataCenterReady)
        {
            if (!clientState.IsLoggedIn)
                incoming.Clear();
            Status = clientState.IsLoggedIn ? "Waiting for your current data center…" : "Sign in to FFXIV to watch Party Finder.";
            return;
        }

        if (previewSoundRequested)
        {
            previewSoundRequested = false;
            PlayAlertSound();
        }

        while (incoming.TryDequeue(out var received))
        {
            if (received.DataCenterId == dataCenterId)
                StoreListing(received);
        }

        if (now >= nextPrune)
        {
            RemoveExpired(now);
            nextPrune = now.AddSeconds(1);
        }

        if (pendingRefresh is { } timedOut && now >= responseDeadline)
        {
            pendingRefresh = null;
            Status = timedOut == RefreshMode.Background
                ? "No listings arrived after the background refresh. Click Refresh now to retry."
                : "No listings arrived after the last refresh. The page may be empty.";
        }

        if (!Config.AutomaticRefresh && !manuallyRefresh)
            return;

        if (pendingRefresh is not null || now < lastAttempt.AddSeconds(10) || (!manuallyRefresh && now < nextAttempt))
            return;

        if (condition[ConditionFlag.BetweenAreas] || condition[ConditionFlag.BetweenAreas51]
            || condition[ConditionFlag.BoundByDuty] || condition[ConditionFlag.BoundByDuty56]
            || condition[ConditionFlag.BoundByDuty95])
        {
            Status = "Refresh paused during duties and area transitions.";
            return;
        }

        if (gameGui.GetAddonByName(ListingDetailAddon).IsVisible)
        {
            Status = "Refresh paused while a native listing is open.";
            return;
        }

        manuallyRefresh = false;
        nextAttempt = now.AddSeconds(Math.Clamp(Config.RefreshSeconds, 10, 600));
        var mode = gameGui.GetAddonByName(PartyFinderAddon).IsVisible ? RefreshMode.Native : RefreshMode.Background;
        Refresh(now, mode);
    }

    private bool UpdateDataCenter()
    {
        WorldDCGroupType dc = default;
        if (clientState.IsLoggedIn && (!playerState.IsLoaded
            || !worldDataCenters.TryGet(playerState.CurrentWorld.RowId, out dc)))
            return false;

        if (dataCenterId != dc.RowId)
        {
            dataCenterId = dc.RowId;
            dataCenterName = dataCenterId == 0 ? "—" : dc.Name.ToString();
            listings.Clear();
            alerted.Clear();
            unread.Clear();
            responseListings.Clear();
            responseBatches.Clear();
            lastResponse = null;
            lastResponseMode = null;
            nextAttempt = default;
            lastAttempt = default;
            pendingRefresh = null;
        }

        return dataCenterId != 0;
    }

    private unsafe void Refresh(DateTimeOffset now, RefreshMode mode)
    {
        bool background = mode == RefreshMode.Background;
        if (background && (!Config.RefreshWithPartyFinderClosed || backgroundRefreshUnavailable))
        {
            Status = "Background refresh is unavailable. Open Party Finder on Data Center → All.";
            return;
        }

        if (!background && nativeRefreshUnavailable)
            return;

        var agent = AgentLookingForGroup.Instance();
        if (agent == null)
        {
            Status = "The Party Finder agent is unavailable. Try again after the area finishes loading.";
            return;
        }

        if (!background && (agent->SearchAreaTab != DataCenterTab || agent->CategoryTab != AllCategoriesTab))
        {
            Status = "Select Data Center → All in native Party Finder to refresh all categories.";
            return;
        }

        try
        {
            lastAttempt = now;
            pendingRefresh = mode;
            responseDeadline = now.AddSeconds(8);
            Status = background ? "Refreshing Party Finder in the background…" : "Refreshing native Party Finder…";
            if (!(background ? RequestBackgroundListings(agent) : agent->RequestListingsUpdate()))
            {
                pendingRefresh = null;
                Status = background ? "Party Finder declined the background refresh. Try again shortly."
                    : "Party Finder declined the refresh. It may be busy.";
            }
        }
        catch (Exception exception)
        {
            if (background)
                backgroundRefreshUnavailable = true;
            else
                nativeRefreshUnavailable = true;
            pendingRefresh = null;
            Status = background ? "Background refresh is unavailable. Open Party Finder on Data Center → All."
                : "Automatic refresh unavailable. Use the native refresh button.";
            log.Warning(exception, "Could not refresh Party Finder ({Mode})", mode);
        }
    }

    private static unsafe bool RequestBackgroundListings(AgentLookingForGroup* agent)
    {
        // The game's category request uses the search-area tab even when its addon is closed.
        byte previousSearchArea = agent->SearchAreaTab;
        agent->SearchAreaTab = DataCenterTab;
        try
        {
            return agent->RequestCategoryListings(AllCategoriesTab);
        }
        finally
        {
            agent->SearchAreaTab = previousSearchArea;
        }
    }

    private void ReceiveListing(IPartyFinderListing received, IPartyFinderListingEventArgs args)
    {
        if (received.Id == 0 || received.SecondsRemaining == 0
            || !worldDataCenters.TryGet(received.World.RowId, out var postedDataCenter)
            || !received.World.TryGetValue(out var world))
            return;

        var now = DateTimeOffset.UtcNow;
        var dutyId = received.Duty.RowId;
        var dutyName = received.Duty.TryGetValue(out var duty) ? duty.Name.ToString() : string.Empty;
        var description = received.Description.TextValue;
        var listing = new Listing(received.Id, received.Category, dutyId, dutyName,
            world.Name.ToString(), received.Name.TextValue,
            description, ListingWatch.Normalize(description), received.SlotsFilled,
            received.SlotsAvailable, now.AddSeconds(received.SecondsRemaining), now);
        incoming.Enqueue(new PendingListing(listing, args.BatchNumber, postedDataCenter.RowId));
    }

    private void StoreListing(PendingListing received)
    {
        var listing = received.Listing;
        var now = listing.FirstSeen;
        if (pendingRefresh is not null || lastResponse is null || now - lastResponse.Value > TimeSpan.FromSeconds(2))
        {
            responseListings.Clear();
            responseBatches.Clear();
            lastResponseMode = pendingRefresh;
        }

        pendingRefresh = null;
        lastResponse = now;
        responseListings.Add(listing.Id);
        responseBatches.Add(received.BatchNumber);
        Status = lastResponseMode == RefreshMode.Background ? "Watching Party Finder in the background" : "Watching Party Finder";

        if (string.IsNullOrWhiteSpace(listing.Duty))
            listing = listing with { Duty = DutyCatalog.CategoryName(listing.Category) };
        else if (listing.DutyId != 0)
            Catalog.Observe(listing.Category, listing.DutyId, listing.Duty);

        if (listings.TryGetValue(listing.Id, out var existing))
            listing = listing with { FirstSeen = existing.FirstSeen };
        listings[listing.Id] = listing;

        if (!Config.Watches.Any(watch => watch.Matches(listing)))
            return;

        if (alerted.TryGetValue(listing.Id, out var alertUntil))
        {
            alerted[listing.Id] = alertUntil > listing.Expires ? alertUntil : listing.Expires;
            return;
        }

        alerted[listing.Id] = listing.Expires;
        unread.Add(listing.Id);
        if (Config.PlaySound && now - lastAlert > TimeSpan.FromSeconds(2))
            PlayAlertSound();
        lastAlert = now;
    }

    private void PlayAlertSound()
    {
        var effect = (uint)Math.Clamp(Config.AlertSound, WatchConfiguration.FirstAlertSound, WatchConfiguration.LastAlertSound);
        UIGlobals.PlayChatSoundEffect(effect);
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var id in listings.Where(entry => entry.Value.Expires <= now).Select(entry => entry.Key).ToArray())
        {
            listings.Remove(id);
            unread.Remove(id);
        }

        foreach (var id in alerted.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToArray())
            alerted.Remove(id);
    }

    private readonly record struct PendingListing(Listing Listing, int BatchNumber, uint DataCenterId);

    private enum RefreshMode { Native, Background }
}
