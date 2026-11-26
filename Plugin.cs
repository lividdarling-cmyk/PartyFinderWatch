using System.Collections.Concurrent;
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
    private const uint WeaponsRefrain = 539;
    private const uint EpicOfAlexander = 694;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IPartyFinderGui partyFinder;
    private readonly IGameGui gameGui;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IPluginLog log;
    private readonly WatchWindow window;
    private readonly ConcurrentQueue<PendingListing> incoming = new();
    private readonly Dictionary<ulong, Listing> listings = [];
    private readonly Dictionary<ulong, DateTimeOffset> alerted = [];
    private readonly HashSet<ulong> unread = [];
    private readonly HashSet<int> responseBatches = [];
    private readonly HashSet<ulong> responseListings = [];
    private uint dataCenterId;
    private DateTimeOffset nextAttempt;
    private DateTimeOffset lastAttempt;
    private DateTimeOffset nextPrune;
    private DateTimeOffset responseDeadline;
    private DateTimeOffset? lastResponse;
    private DateTimeOffset lastAlert;
    private bool refreshRequested;
    private bool awaitingResponse;
    private bool manuallyRefresh;
    private bool previewSoundRequested;
    private bool nativeRefreshUnavailable;

    internal WatchConfiguration Config { get; }
    internal DutyCatalog Catalog { get; }
    internal string DataCenter { get; private set; } = "—";
    internal string Status { get; private set; } = "Waiting for Party Finder";

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commands, IFramework framework,
        IPartyFinderGui partyFinder, IGameGui gameGui, IClientState clientState, IPlayerState playerState,
        IDataManager dataManager, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.partyFinder = partyFinder;
        this.gameGui = gameGui;
        this.clientState = clientState;
        this.playerState = playerState;
        this.log = log;
        Catalog = new DutyCatalog(dataManager);
        Config = pluginInterface.GetPluginConfig() as WatchConfiguration ?? CreateConfiguration();
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

    internal IEnumerable<Listing> CurrentListings => listings.Values;
    internal int UnreadCount => unread.Count;
    internal bool IsFlashing => Config.FlashIcon && unread.Count > 0
        && DateTimeOffset.UtcNow - lastAlert < TimeSpan.FromSeconds(5);
    internal string ScanSummary => lastResponse is { } time
        ? $"Last data {time.ToLocalTime():HH:mm:ss} · {responseListings.Count} listings · {responseBatches.Count} batches"
        : "No listings received yet";

    internal string MatchNames(Listing listing) => string.Join(", ", Config.Watches
        .Where(watch => watch.Matches(listing)).Select(watch => watch.Name));

    internal void Save() => pluginInterface.SavePluginConfig(Config);
    internal void Acknowledge() => unread.Clear();
    internal void RequestRefresh() => manuallyRefresh = true;
    internal void PreviewAlertSound() => previewSoundRequested = true;
    internal void ResetTimer() => nextAttempt = DateTimeOffset.UtcNow.AddSeconds(Config.RefreshSeconds);

    private WatchConfiguration CreateConfiguration()
    {
        return new WatchConfiguration
        {
            Watches =
            [
                new()
                {
                    Name = "Paid UWU and TEA",
                    Category = DutyCategory.HighEndDuty,
                    Duties = [WeaponsRefrain, EpicOfAlexander],
                    Terms = ["c41", "supp to clear", "gil", "pay"],
                },
            ],
        };
    }

    private void Update(IFramework _)
    {
        var now = DateTimeOffset.UtcNow;
        if (!UpdateDataCenter())
        {
            if (!clientState.IsLoggedIn)
                incoming.Clear();
            Status = "Sign in to FFXIV to watch Party Finder.";
            return;
        }

        if (previewSoundRequested)
        {
            previewSoundRequested = false;
            PlayAlertSound();
        }

        while (incoming.TryDequeue(out var received))
        {
            if (received.DataCenterId == 0 || received.DataCenterId == dataCenterId)
                StoreListing(received);
        }

        if (now >= nextPrune)
        {
            RemoveExpired(now);
            nextPrune = now.AddSeconds(1);
        }

        if (awaitingResponse && now >= responseDeadline)
        {
            awaitingResponse = false;
            refreshRequested = false;
            Status = "No listings arrived after the last refresh. The page may be empty.";
        }

        if (!Config.AutomaticRefresh && !manuallyRefresh)
        {
            if (!awaitingResponse)
                Status = "Automatic refresh is off. Native Party Finder updates are still captured.";
            return;
        }

        if (!gameGui.GetAddonByName(PartyFinderAddon).IsVisible)
        {
            Status = "Open native Party Finder on Data Center → All for this test build.";
            return;
        }

        if (gameGui.GetAddonByName(ListingDetailAddon).IsVisible)
        {
            Status = "Refresh paused while a native listing is open.";
            return;
        }

        if (awaitingResponse || now < lastAttempt.AddSeconds(10) || (!manuallyRefresh && now < nextAttempt))
            return;

        manuallyRefresh = false;
        nextAttempt = now.AddSeconds(Math.Clamp(Config.RefreshSeconds, 10, 600));
        RefreshNative(now);
    }

    private bool UpdateDataCenter()
    {
        WorldDCGroupType dc = default;
        if (clientState.IsLoggedIn && (!playerState.IsLoaded
            || !playerState.CurrentWorld.TryGetValue(out var world)
            || !world.DataCenter.TryGetValue(out dc)))
            return false;

        if (dataCenterId != dc.RowId)
        {
            dataCenterId = dc.RowId;
            DataCenter = dataCenterId == 0 ? "—" : dc.Name.ToString();
            listings.Clear();
            alerted.Clear();
            unread.Clear();
            responseListings.Clear();
            responseBatches.Clear();
            lastResponse = null;
            nextAttempt = default;
            lastAttempt = default;
            refreshRequested = false;
            awaitingResponse = false;
        }

        return dataCenterId != 0;
    }

    private unsafe void RefreshNative(DateTimeOffset now)
    {
        if (nativeRefreshUnavailable)
            return;

        var agent = AgentLookingForGroup.Instance();
        if (agent == null || agent->SearchAreaTab != 0 || agent->CategoryTab != 0)
        {
            Status = "Select Data Center → All in native Party Finder to refresh all categories.";
            return;
        }

        try
        {
            lastAttempt = now;
            refreshRequested = true;
            awaitingResponse = true;
            responseDeadline = now.AddSeconds(8);
            Status = "Refreshing native Party Finder…";
            if (!agent->RequestListingsUpdate())
            {
                awaitingResponse = false;
                refreshRequested = false;
                Status = "Party Finder declined the refresh. It may be busy.";
            }
        }
        catch (Exception exception)
        {
            nativeRefreshUnavailable = true;
            awaitingResponse = false;
            refreshRequested = false;
            Status = "Automatic refresh unavailable. Use the native refresh button.";
            log.Warning(exception, "Could not refresh native Party Finder");
        }
    }

    private void ReceiveListing(IPartyFinderListing received, IPartyFinderListingEventArgs args)
    {
        if (received.Id == 0 || received.SecondsRemaining == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var dutyId = received.Duty.RowId;
        var dutyName = received.Duty.TryGetValue(out var duty) ? duty.Name.ToString() : string.Empty;
        var world = received.World.TryGetValue(out var listingWorld) ? listingWorld : default;
        var description = received.Description.TextValue;
        var listing = new Listing(received.Id, received.Category, dutyId, dutyName,
            world.RowId == 0 ? "—" : world.Name.ToString(), received.Name.TextValue,
            description, ListingWatch.Normalize(description), received.SlotsFilled,
            received.SlotsAvailable, now.AddSeconds(received.SecondsRemaining), now);
        incoming.Enqueue(new PendingListing(listing, args.BatchNumber, world.RowId == 0 ? 0 : world.DataCenter.RowId));
    }

    private void StoreListing(PendingListing received)
    {
        var listing = received.Listing;
        var now = listing.FirstSeen;
        if (refreshRequested || lastResponse is null || now - lastResponse.Value > TimeSpan.FromSeconds(2))
        {
            responseListings.Clear();
            responseBatches.Clear();
            refreshRequested = false;
        }

        awaitingResponse = false;
        lastResponse = now;
        responseListings.Add(listing.Id);
        responseBatches.Add(received.BatchNumber);
        Status = "Watching Party Finder";

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
}
