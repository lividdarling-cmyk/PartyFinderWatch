using System.Text.RegularExpressions;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace PartyFinderWatch;

internal sealed class DutyCatalog
{
    internal static readonly DutyCategory[] Categories = Enum.GetValues<DutyCategory>();

    private readonly Dictionary<DutyCategory, Dictionary<uint, string>> duties = [];

    internal DutyCatalog(IDataManager dataManager) : this(dataManager.GetExcelSheet<ContentFinderCondition>()) { }

    internal DutyCatalog(IEnumerable<ContentFinderCondition> conditions)
    {
        foreach (var row in conditions)
        {
            string name = ListingWatch.Normalize(row.Name.ToString());
            if (row.RowId == 0 || string.IsNullOrWhiteSpace(name))
                continue;

            var category = row.HighEndDuty || row.ContentType.RowId == 28
                ? DutyCategory.HighEndDuty
                : GetCategory(row.ContentType.RowId);
            if (category is { } found)
                Observe(found, row.RowId, name);
        }
    }

    internal IEnumerable<DutyOption> GetDuties(DutyCategory category) => duties.TryGetValue(category, out var options)
        ? options.Select(option => new DutyOption(option.Key, option.Value)).OrderBy(option => option.Name)
        : [];

    internal void Observe(DutyCategory category, uint id, string name)
    {
        if (!duties.TryGetValue(category, out var options))
            duties[category] = options = [];
        options[id] = name;
    }

    internal static string CategoryName(DutyCategory category) => category.ToString() switch
    {
        "None" => "Other",
        "GuildQuests" => "Guildhests",
        "GoldSaucer" => "Gold Saucer",
        "HighEndDuty" => "High-end Duty",
        "PvP" => "PvP",
        "FATEs" => "FATEs",
        "VCDungeonFinder" or "VariantCriterionDungeon" or "VariantCriterionDungeons" => "Variant & Criterion Dungeons",
        var name => Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " "),
    };

    // IDs from the game's ContentType sheet. Observed listings also supply their actual PF category.
    private static DutyCategory? GetCategory(uint contentType) => contentType switch
    {
        1 => DutyCategory.Roulette,
        2 => DutyCategory.Dungeons,
        3 => DutyCategory.GuildQuests,
        4 => DutyCategory.Trials,
        5 or 37 => DutyCategory.Raids,
        6 => DutyCategory.PvP,
        8 => DutyCategory.FATEs,
        9 => DutyCategory.TreasureHunts,
        19 or 32 or 35 => DutyCategory.GoldSaucer,
        21 => DutyCategory.DeepDungeons,
        26 or 29 or 38 => DutyCategory.FieldOperations,
        30 => DutyCategory.VCDungeonFinder,
        31 => DutyCategory.GatheringForays,
        33 => DutyCategory.TheHunt,
        _ => null,
    };
}

internal readonly record struct DutyOption(uint Id, string Name);
