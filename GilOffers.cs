using System.Text.RegularExpressions;

namespace PartyFinderWatch;

internal static partial class GilOffers
{
    private static readonly string[] OfferTerms =
    [
        "pay", "paying", "paid", "payment", "merc", "mercs", "mercenary",
        "gil for", "gil per", "gil each", "gil on clear",
    ];

    private static readonly string[] ExcludedTerms =
    [
        "no pay", "no payment", "no gil", "unpaid", "not paying", "free clear",
        "wts", "selling clears", "selling a clear", "selling clear", "selling carries", "selling a carry",
    ];

    internal static bool Matches(string description) =>
        !ExcludedTerms.Any(term => ListingWatch.HasTerm(description, term))
        && (GilAmount().IsMatch(description) || OfferTerms.Any(term => ListingWatch.HasTerm(description, term)));

    // Amounts can be abbreviated, written with separators, or followed by “gil”.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:\d{1,3}(?:,\d{3})+|\d+)(?:[.,]\d+)?\s*(?:(?:millions?|mill|mil|thousands?|[mk])(?:\s*gil)?|gil)(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GilAmount();
}
