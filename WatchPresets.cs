using Dalamud.Game.Gui.PartyFinder.Types;

namespace PartyFinderWatch;

internal static class WatchPresets
{
    internal static IReadOnlyList<PresetGroup> Groups { get; } =
    [
        new("Merc parties", "Paid clears and paid prog or lockouts in High-end Duty.",
        [
            new("paid-clears", "Paid clears", DutyCategory.HighEndDuty,
                ["c41", "c4 1", "c4-1", "clear for 1", "clear for one", "a2c", "supp to clear", "clear", "reclear"],
                RequireGilOffer: true),
            new("paid-prog", "Paid prog and lockouts", DutyCategory.HighEndDuty,
                ["prog", "progging", "speedprog", "progression", "cleanup", "clean up", "lockout", "lockouts", "practice"],
                RequireGilOffer: true),
        ]),
        new("Treasure hunts", "Any listing in Treasure Hunts, including posts without keywords.",
        [
            new("treasure-hunts", "Treasure hunts", DutyCategory.TreasureHunts, []),
        ]),
        new("Social venues", "Find cafés, taverns, DJ nights, and other venues listed under Other.",
        [
            new("social-venues", "Social venues", DutyCategory.None,
                ["rp venue", "nightclub", "live dj", "grand opening", "cafe", "café", "tavern", "open stage", "bathhouse", "bath house"]),
        ]),
        new("Venue hiring", "Find venue and event staff recruitment listed under Other.",
        [
            new("venue-hiring", "Venue hiring", DutyCategory.None,
                ["hiring", "job openings", "job open", "looking for staff", "seeking staff", "recruiting staff", "staff applications", "positions open"]),
        ]),
    ];

    internal static int AddTo(List<ListingWatch> destination, IEnumerable<PresetGroup> groups)
    {
        var existingKeys = destination.Select(watch => watch.PresetKey).ToHashSet(StringComparer.Ordinal);
        int added = 0;
        foreach (var preset in groups.SelectMany(group => group.Watches))
        {
            if (!existingKeys.Add(preset.Key))
                continue;

            destination.Add(new ListingWatch
            {
                PresetKey = preset.Key,
                Name = preset.Name,
                Category = preset.Category,
                Terms = [.. preset.Terms],
                RequireGilOffer = preset.RequireGilOffer,
            });
            added++;
        }

        return added;
    }
}

internal sealed record PresetGroup(string Name, string Description, IReadOnlyList<WatchPreset> Watches);
internal sealed record WatchPreset(string Key, string Name, DutyCategory Category, IReadOnlyList<string> Terms, bool RequireGilOffer = false);
