using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.PartyFinder.Types;

namespace PartyFinderWatch;

internal sealed class WatchWindow(Plugin plugin)
{
    private const string SettingsTab = "Settings";
    private readonly Dictionary<Guid, string> keywordInputs = [];
    private readonly Dictionary<Guid, string> dutyInputs = [];
    private bool isOpen;
    private bool showSettings;
    private bool matchesOnly;
    private bool draggingIcon;
    private string listingSearch = string.Empty;

    internal void Open()
    {
        isOpen = true;
        if (plugin.UnreadCount > 0)
            matchesOnly = true;
        plugin.Acknowledge();
    }

    internal void OpenSettings()
    {
        Open();
        showSettings = true;
    }

    internal void Draw()
    {
        if (!isOpen)
        {
            DrawIcon();
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(760, 510), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(540, 320), new Vector2(float.MaxValue));
        if (ImGui.Begin("Party Finder Watch", ref isOpen) && ImGui.BeginTabBar("##pfwatch-tabs"))
        {
            if (ImGui.BeginTabItem("Party Finder"))
            {
                DrawListings();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(SettingsTab, showSettings ? ImGuiTabItemFlags.SetSelected : 0))
            {
                DrawSettings();
                ImGui.EndTabItem();
            }

            showSettings = false;
            ImGui.EndTabBar();
        }
        ImGui.End();
    }

    private void DrawIcon()
    {
        var viewport = ImGui.GetMainViewport();
        var defaultPosition = viewport.WorkPos + new Vector2(280, 75);
        var savedPosition = new Vector2(plugin.Config.IconX ?? defaultPosition.X, plugin.Config.IconY ?? defaultPosition.Y);
        var maximum = Vector2.Max(viewport.WorkPos, viewport.WorkPos + viewport.WorkSize - new Vector2(58, 44));
        ImGui.SetNextWindowPos(Vector2.Clamp(savedPosition, viewport.WorkPos, maximum), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3));

        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove;
        if (ImGui.Begin("##pfwatch-icon", flags))
        {
            int unread = plugin.UnreadCount;
            if (unread > 0)
            {
                float brightness = plugin.IsFlashing ? 0.69f + 0.3f * (float)Math.Sin(ImGui.GetTime() * 12) : 0.8f;
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(brightness, brightness * 0.53f, 0.14f, 1));
            }

            bool click = ImGui.Button(unread > 0 ? "PF !###open" : "PF###open", new Vector2(48, 34));
            if (unread > 0)
                ImGui.PopStyleColor();

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                draggingIcon = true;
                var position = Vector2.Clamp(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta, viewport.WorkPos, maximum);
                plugin.Config.IconX = position.X;
                plugin.Config.IconY = position.Y;
            }

            if (draggingIcon && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                plugin.Save();
                draggingIcon = false;
            }
            else if (click && !draggingIcon)
            {
                Open();
            }

            if (ImGui.IsItemHovered() && !draggingIcon)
                ImGui.SetTooltip(unread > 0 ? $"{unread} new matches · click to open · drag to move" : "Party Finder Watch · click to open · drag to move");
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawListings()
    {
        ImGui.TextUnformatted($"Data center  {plugin.DataCenter}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Refresh now"))
            plugin.RequestRefresh();
        ImGui.TextDisabled(plugin.Status);
        ImGui.TextDisabled(plugin.ScanSummary);
        ImGui.Separator();

        if (ImGui.RadioButton("All listings", !matchesOnly))
            matchesOnly = false;
        ImGui.SameLine();
        if (ImGui.RadioButton("Matches", matchesOnly))
            matchesOnly = true;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##listings-search", "Search received listings", ref listingSearch, 160);

        if (!ImGui.BeginChild("##party-finder-entries", Vector2.Zero, true))
        {
            ImGui.EndChild();
            return;
        }

        bool any = false;
        foreach (var listing in plugin.CurrentListings.OrderByDescending(item => item.FirstSeen))
        {
            string matches = plugin.MatchNames(listing);
            if ((matchesOnly && matches.Length == 0) || !ContainsSearch(listing))
                continue;

            any = true;
            ImGui.PushID(listing.Id.ToString());
            if (matches.Length > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.75f, 0.35f, 1f));
                ImGui.TextUnformatted(matches);
                ImGui.PopStyleColor();
            }

            ImGui.TextUnformatted($"{listing.Duty}  ·  {listing.SlotsFilled}/{listing.TotalSlots}");
            ImGui.TextDisabled($"{listing.Recruiter}  ·  {listing.World}  ·  {DutyCatalog.CategoryName(listing.Category)}");
            if (!string.IsNullOrEmpty(listing.Description))
            {
                ImGui.PushTextWrapPos();
                ImGui.TextUnformatted(listing.Description);
                ImGui.PopTextWrapPos();
            }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (!any)
            ImGui.TextDisabled(matchesOnly ? "No matching listings." : "No listings to show.");

        ImGui.EndChild();
    }

    private bool ContainsSearch(Listing listing) => listingSearch.Length == 0
        || listing.Duty.Contains(listingSearch, StringComparison.OrdinalIgnoreCase)
        || listing.Description.Contains(listingSearch, StringComparison.OrdinalIgnoreCase)
        || DutyCatalog.CategoryName(listing.Category).Contains(listingSearch, StringComparison.OrdinalIgnoreCase);

    private void DrawSettings()
    {
        var config = plugin.Config;
        bool automaticRefresh = config.AutomaticRefresh;
        if (ImGui.Checkbox("Automatic refresh", ref automaticRefresh))
        {
            config.AutomaticRefresh = automaticRefresh;
            if (automaticRefresh)
                plugin.RequestRefresh();
            plugin.Save();
        }

        var interval = config.RefreshSeconds;
        ImGui.SetNextItemWidth(230);
        if (ImGui.SliderInt("Refresh interval", ref interval, 10, 600, "%d sec", ImGuiSliderFlags.AlwaysClamp))
        {
            config.RefreshSeconds = interval;
            plugin.ResetTimer();
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
            plugin.Save();

        bool sound = config.PlaySound;
        if (ImGui.Checkbox("Play a sound for new matches", ref sound))
        {
            config.PlaySound = sound;
            plugin.Save();
        }

        int alertSound = Math.Clamp(config.AlertSound, WatchConfiguration.FirstAlertSound, WatchConfiguration.LastAlertSound);
        ImGui.SetNextItemWidth(180);
        if (ImGui.BeginCombo("##alert-sound", $"Sound {alertSound} (<se.{alertSound}>)", ImGuiComboFlags.HeightLarge))
        {
            for (int effect = WatchConfiguration.FirstAlertSound; effect <= WatchConfiguration.LastAlertSound; effect++)
            {
                if (ImGui.Selectable($"Sound {effect} (<se.{effect}>)", alertSound == effect))
                {
                    config.AlertSound = effect;
                    plugin.Save();
                }
                if (alertSound == effect)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Preview sound"))
            plugin.PreviewAlertSound();

        bool flash = config.FlashIcon;
        if (ImGui.Checkbox("Flash the minimized icon", ref flash))
        {
            config.FlashIcon = flash;
            plugin.Save();
        }
        ImGui.Separator();

        ImGui.TextUnformatted("Saved watches");
        ImGui.SameLine();
        if (ImGui.SmallButton("Add watch"))
        {
            config.Watches.Add(new ListingWatch { Enabled = false });
            plugin.Save();
        }

        if (!ImGui.BeginChild("##saved-watches", Vector2.Zero, true))
        {
            ImGui.EndChild();
            return;
        }

        ListingWatch? remove = null;
        foreach (var watch in config.Watches)
        {
            ImGui.PushID(watch.Id.ToString());
            if (ImGui.CollapsingHeader($"{watch.Name}###watch-header", ImGuiTreeNodeFlags.DefaultOpen) && DrawWatch(watch))
                remove = watch;
            ImGui.PopID();
        }

        if (remove is not null)
        {
            config.Watches.Remove(remove);
            keywordInputs.Remove(remove.Id);
            dutyInputs.Remove(remove.Id);
            plugin.Save();
        }
        ImGui.EndChild();
    }

    private bool DrawWatch(ListingWatch watch)
    {
        bool enabled = watch.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            watch.Enabled = enabled;
            plugin.Save();
        }
        ImGui.SameLine();
        var name = watch.Name;
        ImGui.SetNextItemWidth(230);
        if (ImGui.InputText("Watch name", ref name, 70))
            watch.Name = name;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            watch.Name = string.IsNullOrWhiteSpace(watch.Name) ? "New watch" : watch.Name.Trim();
            plugin.Save();
        }

        ImGui.SetNextItemWidth(230);
        if (ImGui.BeginCombo("Category", watch.Category is { } category ? DutyCatalog.CategoryName(category) : "All Party Finder"))
        {
            if (ImGui.Selectable("All Party Finder", watch.Category is null))
                SetCategory(watch, null);
            foreach (var option in DutyCatalog.Categories)
            {
                if (ImGui.Selectable(DutyCatalog.CategoryName(option), watch.Category == option))
                    SetCategory(watch, option);
            }
            ImGui.EndCombo();
        }

        if (watch.Category is { } chosenCategory)
            DrawDutyPicker(watch, chosenCategory);

        string keyword = keywordInputs.GetValueOrDefault(watch.Id, "");
        ImGui.SetNextItemWidth(280);
        bool add = ImGui.InputTextWithHint("##keyword-input", "Keyword or comma-separated phrases", ref keyword, 300,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.SmallButton("+##add-keyword") || add)
        {
            foreach (var term in keyword.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                         .Select(ListingWatch.Normalize))
            {
                if (!watch.Terms.Contains(term, StringComparer.OrdinalIgnoreCase))
                    watch.Terms.Add(term);
            }
            keyword = "";
            plugin.Save();
        }
        keywordInputs[watch.Id] = keyword;

        if (watch.Terms.Count == 0)
            ImGui.TextDisabled("Any listing in this scope");
        else
        {
            for (int index = 0; index < watch.Terms.Count; index++)
            {
                ImGui.PushID(index);
                ImGui.TextUnformatted(watch.Terms[index]);
                ImGui.SameLine();
                bool removeTerm = ImGui.SmallButton("Remove##keyword");
                ImGui.PopID();
                if (removeTerm)
                {
                    watch.Terms.RemoveAt(index);
                    plugin.Save();
                    break;
                }
            }
        }

        return ImGui.SmallButton("Delete watch");
    }

    private void SetCategory(ListingWatch watch, DutyCategory? category)
    {
        if (watch.Category == category)
            return;
        watch.Category = category;
        watch.Duties.Clear();
        dutyInputs.Remove(watch.Id);
        plugin.Save();
    }

    private void DrawDutyPicker(ListingWatch watch, DutyCategory category)
    {
        var options = plugin.Catalog.GetDuties(category).ToArray();
        string preview = watch.Duties.Count switch
        {
            0 => "Any duty in category",
            1 => options.FirstOrDefault(option => option.Id == watch.Duties[0]).Name ?? "1 duty selected",
            _ => $"{watch.Duties.Count} duties selected",
        };

        ImGui.SetNextItemWidth(300);
        if (!ImGui.BeginCombo("Duties", preview, ImGuiComboFlags.HeightLarge))
            return;

        string search = dutyInputs.GetValueOrDefault(watch.Id, "");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##duty-search", "Find a duty", ref search, 100);
        dutyInputs[watch.Id] = search;
        if (ImGui.Selectable("Any duty in category", watch.Duties.Count == 0))
        {
            watch.Duties.Clear();
            plugin.Save();
        }

        foreach (var option in options.Where(option => option.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
        {
            bool selected = watch.Duties.Contains(option.Id);
            ImGui.PushID((int)option.Id);
            if (ImGui.Selectable(option.Name, selected, ImGuiSelectableFlags.DontClosePopups))
            {
                if (selected)
                    watch.Duties.Remove(option.Id);
                else
                    watch.Duties.Add(option.Id);
                plugin.Save();
            }
            ImGui.PopID();
        }
        ImGui.EndCombo();
    }
}
