using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Gui.PartyFinder.Types;

namespace PartyFinderWatch;

internal sealed partial class WatchWindow(Plugin plugin)
{
    private const string SettingsTab = "Settings";
    private static readonly Vector4 Ink = new(0.92f, 0.94f, 0.97f, 1f);
    private static readonly Vector4 Muted = new(0.65f, 0.70f, 0.78f, 1f);
    private static readonly Vector4 Accent = new(0.93f, 0.77f, 0.48f, 1f);
    private static readonly Vector4 AccentInk = new(0.12f, 0.09f, 0.06f, 1f);
    private static readonly Vector4 Panel = new(0.075f, 0.10f, 0.15f, 0.98f);
    private static readonly Vector4 Surface = new(0.12f, 0.16f, 0.23f, 1f);
    private static readonly Vector4 Hover = new(0.20f, 0.26f, 0.35f, 1f);
    private static readonly Vector4 Line = new(0.22f, 0.28f, 0.36f, 1f);
    private static readonly Vector4 Selected = new(0.26f, 0.22f, 0.17f, 1f);
    private static readonly Vector4 AlternateRow = new(0.09f, 0.12f, 0.18f, 1f);
    private static readonly Vector4 MatchRow = new(0.15f, 0.14f, 0.14f, 1f);
    private static readonly (ImGuiCol Slot, Vector4 Color)[] Palette =
    [
        (ImGuiCol.Text, Ink), (ImGuiCol.TextDisabled, Muted),
        (ImGuiCol.WindowBg, Panel), (ImGuiCol.PopupBg, Panel), (ImGuiCol.Border, Line),
        (ImGuiCol.TitleBg, Surface), (ImGuiCol.TitleBgActive, Surface), (ImGuiCol.TitleBgCollapsed, Surface),
        (ImGuiCol.FrameBg, Surface), (ImGuiCol.FrameBgHovered, Hover), (ImGuiCol.FrameBgActive, Hover),
        (ImGuiCol.Button, Surface), (ImGuiCol.ButtonHovered, Hover), (ImGuiCol.ButtonActive, Selected),
        (ImGuiCol.CheckMark, Accent), (ImGuiCol.SliderGrab, Accent), (ImGuiCol.SliderGrabActive, Accent),
        (ImGuiCol.Header, Surface), (ImGuiCol.HeaderHovered, Hover), (ImGuiCol.HeaderActive, Selected),
        (ImGuiCol.Tab, Surface), (ImGuiCol.TabHovered, Hover), (ImGuiCol.TabActive, Selected),
        (ImGuiCol.Separator, Line), (ImGuiCol.TableHeaderBg, Surface), (ImGuiCol.TableBorderLight, Line),
        (ImGuiCol.TableRowBg, Panel), (ImGuiCol.TableRowBgAlt, AlternateRow),
        (ImGuiCol.ScrollbarBg, Panel), (ImGuiCol.ScrollbarGrab, Line),
        (ImGuiCol.ScrollbarGrabHovered, Muted), (ImGuiCol.ScrollbarGrabActive, Muted),
        (ImGuiCol.ResizeGrip, Line), (ImGuiCol.ResizeGripHovered, Accent), (ImGuiCol.ResizeGripActive, Accent),
    ];

    private readonly Dictionary<Guid, string> keywordInputs = [];
    private readonly Dictionary<Guid, string> dutyInputs = [];
    private bool isOpen = !plugin.Config.WatchSetupComplete;
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

        foreach (var (slot, color) in Palette)
            ImGui.PushStyleColor(slot, color);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(10, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 6f);

        var viewport = ImGui.GetMainViewport();
        var maximumSize = Vector2.Max(new Vector2(1), viewport.WorkSize - new Vector2(28));
        var initialSize = Vector2.Min(new Vector2(880, 590), maximumSize);
        ImGui.SetNextWindowPos(viewport.WorkPos + (viewport.WorkSize - initialSize) * 0.5f, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(initialSize, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(Vector2.Min(new Vector2(560, 340), maximumSize), maximumSize);
        if (ImGui.Begin("Party Finder Watch", ref isOpen, ImGuiWindowFlags.NoCollapse))
        {
            if (setupPage == SetupPage.None)
                DrawTabs();
            else
                DrawSetup();
        }
        ImGui.End();
        ImGui.PopStyleVar(7);
        ImGui.PopStyleColor(Palette.Length);
    }

    private void DrawTabs()
    {
        if (!ImGui.BeginTabBar("##pfwatch-tabs"))
            return;

        if (ImGui.BeginTabItem("Party Finder", showListings ? ImGuiTabItemFlags.SetSelected : 0))
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
        showListings = false;
        ImGui.EndTabBar();
    }

    private void DrawIcon()
    {
        var viewport = ImGui.GetMainViewport();
        var defaultPosition = viewport.WorkPos + new Vector2(280, 75);
        var savedPosition = new Vector2(plugin.Config.IconX ?? defaultPosition.X, plugin.Config.IconY ?? defaultPosition.Y);
        var badgeSize = Vector2.Max(new Vector2(46, 38), ImGui.CalcTextSize("PF") + new Vector2(26, 20));
        var maximum = Vector2.Max(viewport.WorkPos, viewport.WorkPos + viewport.WorkSize - badgeSize - new Vector2(10));
        var badgePosition = Vector2.Clamp(savedPosition, viewport.WorkPos, maximum);
        ImGui.SetNextWindowPos(badgePosition, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 9f);

        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBackground;
        if (ImGui.Begin("##pfwatch-icon", flags))
        {
            int unread = plugin.UnreadCount;
            float pulse = plugin.IsFlashing ? 0.55f + 0.45f * (float)Math.Cos(ImGui.GetTime() * 9) : 0.65f;
            var fill = unread > 0 ? Vector4.Lerp(Panel, Selected, pulse) : Panel;
            ImGui.PushStyleColor(ImGuiCol.Button, fill);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Hover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Selected);
            ImGui.PushStyleColor(ImGuiCol.Text, Accent);
            bool click = ImGui.Button("PF###open", badgeSize);
            ImGui.PopStyleColor(4);
            DrawBadgeBorder(unread, pulse);

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                draggingIcon = true;
                var position = Vector2.Clamp(badgePosition + ImGui.GetIO().MouseDelta, viewport.WorkPos, maximum);
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
        ImGui.PopStyleVar(2);
    }

    private static void DrawBadgeBorder(int unread, float pulse)
    {
        var draw = ImGui.GetWindowDrawList();
        var topLeft = ImGui.GetItemRectMin();
        var bottomRight = ImGui.GetItemRectMax();
        var border = unread > 0 ? Vector4.Lerp(Line, Accent, pulse) : Line;
        draw.AddRect(topLeft, bottomRight, ImGui.GetColorU32(border), 9f, ImDrawFlags.None, 1.5f);

        if (unread == 0)
            return;

        float radius = MathF.Max(7f, ImGui.GetFontSize() * 0.58f);
        var center = new Vector2(bottomRight.X - radius + 1, topLeft.Y + radius - 1);
        draw.AddCircleFilled(center, radius, ImGui.GetColorU32(Accent));
        draw.AddText(center - ImGui.CalcTextSize("!") * 0.5f, ImGui.GetColorU32(AccentInk), "!");
    }

    private void DrawListings()
    {
        ImGui.Spacing();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Data center");
        ImGui.SameLine();
        DrawText(plugin.DataCenter, Accent);
        AlignRight("Refresh now");
        if (PrimaryButton("Refresh now"))
            plugin.RequestRefresh();
        DrawText(plugin.Status, Muted);
        DrawText(plugin.ScanSummary, Muted);
        ImGui.Spacing();

        if (FilterButton("All listings", !matchesOnly))
            matchesOnly = false;
        ImGui.SameLine();
        if (FilterButton("Matches", matchesOnly))
            matchesOnly = true;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##listings-search", "Search received listings", ref listingSearch, 160);

        const ImGuiTableFlags flags = ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg
            | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("##party-finder-entries", 3, flags, Vector2.Zero))
            return;

        ImGui.TableSetupColumn("Duty", ImGuiTableColumnFlags.WidthStretch, 0.34f);
        ImGui.TableSetupColumn("Recruitment", ImGuiTableColumnFlags.WidthStretch, 0.56f);
        ImGui.TableSetupColumn("Party", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("00 / 00").X + 8);
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        bool any = false;
        foreach (var listing in plugin.CurrentListings.OrderByDescending(item => item.FirstSeen))
        {
            string matches = plugin.MatchNames(listing);
            if ((matchesOnly && matches.Length == 0) || !ContainsSearch(listing))
                continue;

            any = true;
            DrawListingRow(listing, matches);
        }

        if (!any)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(1);
            DrawText(matchesOnly || listingSearch.Length > 0 ? "No matching listings." : "No listings to show.", Muted);
        }

        ImGui.EndTable();
    }

    private static void DrawListingRow(Listing listing, string matches)
    {
        ImGui.TableNextRow();
        if (matches.Length > 0)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(MatchRow));

        ImGui.TableSetColumnIndex(0);
        DrawText(listing.Duty, Ink);
        var category = DutyCatalog.CategoryName(listing.Category);
        if (listing.Duty != category)
            DrawText(category, Muted);

        ImGui.TableSetColumnIndex(1);
        if (matches.Length > 0)
            DrawText(matches, Accent);
        if (!string.IsNullOrEmpty(listing.Description))
            DrawText(listing.Description, Ink);
        DrawText($"{listing.Recruiter} · {listing.World}", Muted);

        ImGui.TableSetColumnIndex(2);
        ImGui.TextUnformatted($"{listing.SlotsFilled} / {listing.TotalSlots}");
    }

    private static bool FilterButton(string text, bool selected)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? Selected : Surface);
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? Accent : Ink);
        bool clicked = ImGui.Button(text);
        ImGui.PopStyleColor(2);
        return clicked;
    }

    private static bool PrimaryButton(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1f, 0.85f, 0.58f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Accent);
        ImGui.PushStyleColor(ImGuiCol.Text, AccentInk);
        bool clicked = ImGui.Button(text);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static void AlignRight(string buttonText) => AlignRight(ButtonWidth(buttonText));

    private static float ButtonWidth(string text) => ImGui.CalcTextSize(text).X + ImGui.GetStyle().FramePadding.X * 2;

    private static void AlignRight(float width)
    {
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - width));
    }

    private static void DrawText(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushTextWrapPos();
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
    }

    private bool ContainsSearch(Listing listing) => listingSearch.Length == 0
        || listing.Duty.Contains(listingSearch, StringComparison.OrdinalIgnoreCase)
        || listing.Description.Contains(listingSearch, StringComparison.OrdinalIgnoreCase)
        || DutyCatalog.CategoryName(listing.Category).Contains(listingSearch, StringComparison.OrdinalIgnoreCase);

    private void DrawSettings()
    {
        ImGui.Spacing();
        if (ImGui.BeginChild("##settings-scroll", Vector2.Zero))
        {
            float columnThreshold = Math.Max(650, ImGui.CalcTextSize("Refresh with native Party Finder closed").X * 2 + 80);
            if (ImGui.GetContentRegionAvail().X >= columnThreshold && ImGui.BeginTable("##settings-controls", 2))
            {
                ImGui.TableNextColumn();
                DrawRefreshSettings();
                ImGui.TableNextColumn();
                DrawAlertSettings();
                ImGui.EndTable();
            }
            else
            {
                DrawRefreshSettings();
                ImGui.Spacing();
                DrawAlertSettings();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawWatches();
        }
        ImGui.EndChild();
    }

    private void DrawRefreshSettings()
    {
        DrawText("Refresh", Accent);
        var config = plugin.Config;
        bool automaticRefresh = config.AutomaticRefresh;
        if (ImGui.Checkbox("Automatic refresh", ref automaticRefresh))
        {
            config.AutomaticRefresh = automaticRefresh;
            if (automaticRefresh)
                plugin.RequestRefresh();
            plugin.Save();
        }

        bool backgroundRefresh = config.RefreshWithPartyFinderClosed;
        if (ImGui.Checkbox("Refresh with native Party Finder closed", ref backgroundRefresh))
        {
            config.RefreshWithPartyFinderClosed = backgroundRefresh;
            if (backgroundRefresh && config.AutomaticRefresh)
                plugin.RequestRefresh();
            plugin.Save();
        }

        var interval = config.RefreshSeconds;
        ImGui.SetNextItemWidth(Math.Min(190, ImGui.GetContentRegionAvail().X * 0.50f));
        if (ImGui.SliderInt("Refresh interval", ref interval, 10, 600, "%d sec", ImGuiSliderFlags.AlwaysClamp))
        {
            config.RefreshSeconds = interval;
            plugin.ResetTimer();
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
            plugin.Save();
    }

    private void DrawAlertSettings()
    {
        DrawText("Alerts", Accent);
        var config = plugin.Config;
        bool sound = config.PlaySound;
        if (ImGui.Checkbox("Play a sound for new matches", ref sound))
        {
            config.PlaySound = sound;
            plugin.Save();
        }

        int alertSound = Math.Clamp(config.AlertSound, WatchConfiguration.FirstAlertSound, WatchConfiguration.LastAlertSound);
        ImGui.SetNextItemWidth(Math.Min(205, ImGui.GetContentRegionAvail().X * 0.54f));
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
        if (ImGui.Button("Preview sound"))
            plugin.PreviewAlertSound();

        bool flash = config.FlashIcon;
        if (ImGui.Checkbox("Flash the minimized icon", ref flash))
        {
            config.FlashIcon = flash;
            plugin.Save();
        }
    }

    private void DrawWatches()
    {
        var watches = plugin.Config.Watches;
        ImGui.AlignTextToFramePadding();
        DrawText("Saved watches", Accent);
        AlignRight(ButtonWidth("Quick tour") + ButtonWidth("Add presets") + ButtonWidth("+ Add watch")
            + ImGui.GetStyle().ItemSpacing.X * 2);
        if (ImGui.Button("Quick tour"))
            setupPage = SetupPage.Tour;
        ImGui.SameLine();
        if (ImGui.Button("Add presets"))
            OpenPresetPicker();
        ImGui.SameLine();
        if (ImGui.Button("+ Add watch"))
        {
            watches.Add(new ListingWatch { Enabled = false });
            plugin.Save();
        }

        if (watches.Count == 0)
            DrawText("No saved watches.", Muted);

        ListingWatch? remove = null;
        foreach (var watch in watches)
        {
            ImGui.PushID(watch.Id.ToString());
            var heading = watch.Enabled ? watch.Name : $"{watch.Name}  ·  Off";
            if (ImGui.CollapsingHeader($"{heading}###watch-header", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent(10);
                if (DrawWatch(watch))
                    remove = watch;
                ImGui.Unindent(10);
                ImGui.Spacing();
            }
            ImGui.PopID();
        }

        if (remove is not null)
        {
            watches.Remove(remove);
            keywordInputs.Remove(remove.Id);
            dutyInputs.Remove(remove.Id);
            plugin.Save();
        }
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
        ImGui.SetNextItemWidth(Math.Min(245, ImGui.GetContentRegionAvail().X * 0.61f));
        if (ImGui.InputText("Watch name", ref name, 70))
            watch.Name = name;
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            watch.Name = string.IsNullOrWhiteSpace(watch.Name) ? "New watch" : watch.Name.Trim();
            plugin.Save();
        }

        bool filtersFitOnOneLine = ImGui.GetContentRegionAvail().X >= Math.Max(650, ImGui.GetFontSize() * 43);
        ImGui.SetNextItemWidth(Math.Min(230, ImGui.GetContentRegionAvail().X * 0.66f));
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
        {
            if (filtersFitOnOneLine)
                ImGui.SameLine();
            DrawDutyPicker(watch, chosenCategory);
        }

        bool requireGilOffer = watch.RequireGilOffer;
        if (ImGui.Checkbox("Require a gil offer", ref requireGilOffer))
        {
            watch.RequireGilOffer = requireGilOffer;
            plugin.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Looks for amounts like 1m, 2 mil, and 500k, or phrases like 'will pay'.");

        string keyword = keywordInputs.GetValueOrDefault(watch.Id, "");
        ImGui.SetNextItemWidth(Math.Min(315, ImGui.GetContentRegionAvail().X * 0.84f));
        bool add = ImGui.InputTextWithHint("##keyword-input", "Keyword or comma-separated phrases", ref keyword, 300,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        bool addButton = ImGui.Button("+##add-keyword");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Add keywords");
        if (addButton || add)
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
            DrawText(watch.RequireGilOffer ? "Any gil offer in this scope" : "Any listing in this scope", Muted);
        else
            DrawKeywordChips(watch);

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.69f, 0.65f, 1f));
        bool removeWatch = ImGui.SmallButton("Delete watch");
        ImGui.PopStyleColor();
        return removeWatch;
    }

    private void DrawKeywordChips(ListingWatch watch)
    {
        var style = ImGui.GetStyle();
        float rightEdge = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        for (int index = 0; index < watch.Terms.Count; index++)
        {
            string term = watch.Terms[index];
            string label = term.Length > 28 ? term[..26] + "…" : term;
            label += "  ×";
            float width = ImGui.CalcTextSize(label).X + style.FramePadding.X * 2;
            if (index > 0 && ImGui.GetItemRectMax().X + style.ItemSpacing.X + width < rightEdge)
                ImGui.SameLine();

            ImGui.PushID(index);
            bool removeTerm = FilterButton(label + "###keyword", true);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"Remove keyword: {term}");
                ImGui.EndTooltip();
            }
            ImGui.PopID();
            if (removeTerm)
            {
                watch.Terms.RemoveAt(index);
                plugin.Save();
                break;
            }
        }
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

        ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X * 0.75f));
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
