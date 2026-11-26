using Dalamud.Configuration;
using Dalamud.Game.Gui.PartyFinder.Types;

namespace PartyFinderWatch;

public sealed class WatchConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool AutomaticRefresh { get; set; } = true;
    public int RefreshSeconds { get; set; } = 30;
    public bool PlaySound { get; set; } = true;
    public bool FlashIcon { get; set; } = true;
    public float? IconX { get; set; }
    public float? IconY { get; set; }
    public List<ListingWatch> Watches { get; set; } = [];
}

public sealed class ListingWatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New watch";
    public bool Enabled { get; set; } = true;
    public DutyCategory? Category { get; set; }
    public List<uint> Duties { get; set; } = [];
    public List<string> Terms { get; set; } = [];

    internal bool Matches(Listing listing) => Enabled
        && (Category is null || Category == listing.Category)
        && (Duties.Count == 0 || Duties.Contains(listing.DutyId))
        && (Terms.Count == 0 || Terms.Any(term => HasTerm(listing.SearchableDescription, term)));

    internal static string Normalize(string value) => string.Join(' ',
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool HasTerm(string description, string term)
    {
        int start = 0;
        while (start < description.Length)
        {
            int position = description.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (position < 0)
                return false;

            int end = position + term.Length;
            bool startsWord = position == 0 || !char.IsLetterOrDigit(description[position - 1]);
            bool endsWord = end == description.Length || !char.IsLetterOrDigit(description[end]);
            if (startsWord && endsWord)
                return true;

            start = position + 1;
        }

        return false;
    }
}

internal sealed record Listing(
    ulong Id, DutyCategory Category, uint DutyId, string Duty, string World, string Recruiter,
    string Description, string SearchableDescription, byte SlotsFilled, byte TotalSlots,
    DateTimeOffset Expires, DateTimeOffset FirstSeen);
