using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace PartyFinderWatch;

internal sealed partial class WatchWindow
{
    private SetupPage setupPage = plugin.Config.WatchSetupComplete ? SetupPage.None : SetupPage.Presets;
    private readonly HashSet<PresetGroup> selectedGroups = [];
    private bool showListings;

    private void OpenPresetPicker()
    {
        selectedGroups.Clear();
        setupPage = SetupPage.Presets;
    }

    private void DrawSetup()
    {
        if (ImGui.BeginChild("##picker-scroll", Vector2.Zero))
        {
            if (setupPage == SetupPage.Presets)
                DrawPresetPicker();
            else
                DrawTour();
        }
        ImGui.EndChild();
    }

    private void DrawPresetPicker()
    {
        bool firstRun = !plugin.Config.WatchSetupComplete;
        DrawText(firstRun ? "What do you look for in Party Finder?" : "Add watch presets", Accent);
        DrawText("Pick any that fit. You can edit each watch in Settings.", Muted);
        ImGui.Spacing();

        var existing = plugin.Config.Watches.Select(watch => watch.PresetKey).ToHashSet(StringComparer.Ordinal);
        int columns = ImGui.GetContentRegionAvail().X >= Math.Max(540, ImGui.GetFontSize() * 34) ? 2 : 1;
        const ImGuiTableFlags flags = ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInner;
        if (ImGui.BeginTable("##preset-choices", columns, flags))
        {
            foreach (var group in WatchPresets.Groups)
            {
                ImGui.TableNextColumn();
                bool alreadyAdded = group.Watches.All(preset => existing.Contains(preset.Key));
                bool selected = selectedGroups.Contains(group);
                if (selected)
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.CellBg, ImGui.GetColorU32(MatchRow));

                ImGui.BeginDisabled(alreadyAdded);
                if (ImGui.Checkbox(group.Name, ref selected))
                {
                    if (selected)
                        selectedGroups.Add(group);
                    else
                        selectedGroups.Remove(group);
                }
                ImGui.EndDisabled();
                DrawText(alreadyAdded ? "Already added" : group.Description, Muted);
            }
            ImGui.EndTable();
        }

        ImGui.Spacing();
        if (ImGui.Button(firstRun ? "Start empty" : "Cancel"))
            FinishPresetPicker(false);
        AlignRight("Add selected watches");
        ImGui.BeginDisabled(selectedGroups.Count == 0);
        if (PrimaryButton("Add selected watches"))
            FinishPresetPicker(true);
        ImGui.EndDisabled();
    }

    private void FinishPresetPicker(bool addSelected)
    {
        bool firstRun = !plugin.Config.WatchSetupComplete;
        int added = addSelected ? WatchPresets.AddTo(plugin.Config.Watches, WatchPresets.Groups.Where(selectedGroups.Contains)) : 0;
        selectedGroups.Clear();

        plugin.Config.WatchSetupComplete = true;
        if (firstRun || added > 0)
            plugin.Save();

        setupPage = firstRun ? SetupPage.Tour : SetupPage.None;
        showSettings = !firstRun;
    }

    private void DrawTour()
    {
        DrawText("Quick tour", Accent);
        ImGui.Separator();
        DrawTourStep("Edit a watch", "In Settings, pick a category and duties. Add keywords with +. With no keywords, the watch uses just the scope and any gil requirement.");
        DrawTourStep("See your matches", "Use All listings to browse Party Finder and Matches to see what your watches caught. Refresh now requests a new set of listings.");
        DrawTourStep("Keep watching", "Close the window to its draggable Party Finder Watch badge. An exclamation mark signals a new match. Choose or mute the sound in Settings.");

        ImGui.Spacing();
        if (ImGui.Button("View listings"))
        {
            setupPage = SetupPage.None;
            showListings = true;
        }
        AlignRight("Go to watches");
        if (PrimaryButton("Go to watches"))
        {
            setupPage = SetupPage.None;
            showSettings = true;
        }
    }

    private static void DrawTourStep(string title, string detail)
    {
        ImGui.Spacing();
        DrawText(title, Ink);
        DrawText(detail, Muted);
    }

    private enum SetupPage { None, Presets, Tour }
}
