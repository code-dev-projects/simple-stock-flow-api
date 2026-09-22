using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace SimpleStockFlow.Adapters.Rest.Binding;

/// <summary>
/// The from/to edges of GET /api/sales and GET /api/reports/sales. Section 1.3 of the contract
/// gives the two endpoints one set of rules, so they read the edges through one reader: while
/// each controller carried its own copy, a defect in one of them was a defect in both and a fix
/// to one of them was a divergence nobody would notice until the two endpoints disagreed about
/// what a range means.
/// </summary>
internal static class RangeEdges
{
    /// <summary>
    /// D-C3 in full. Not "it carries an offset" but "it is ISO 8601 and it carries an offset":
    /// the framework's own parser accepts 01/06/2026 00:00:00Z as the 6th of January, offset and
    /// all, so a reader that only checks for the offset still answers for another month in
    /// silence. Matching an exact set of spellings is what leaves neither the host's zone nor
    /// the current culture a say in which instant the caller meant.
    /// </summary>
    private static readonly string[] WithWrittenOffset =
    {
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mmzzz",
    };

    /// <summary>
    /// The Z spellings, kept apart because Z is a literal here and not an offset the parser can
    /// read: AssumeUniversal is what turns it into +00:00 instead of the machine's own zone.
    /// </summary>
    private static readonly string[] WithZulu =
    {
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
        "yyyy-MM-dd'T'HH:mm'Z'",
    };

    /// <summary>
    /// Reads one edge, or names it in ModelState and returns null. The caller reads both before
    /// judging either, so whoever wrote both of them wrong hears about both at once.
    /// </summary>
    internal static DateTimeOffset? ReadRangeEdge(this ControllerBase controller, string? text, string parameter)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            controller.ModelState.AddModelError(parameter, $"The {parameter} field is required.");
            return null;
        }

        if (!TryReadInstant(text, out var instant))
        {
            controller.ModelState.AddModelError(
                parameter,
                $"The value '{text}' is not an ISO 8601 instant with an explicit offset.");
            return null;
        }

        return instant;
    }

    private static bool TryReadInstant(string text, out DateTimeOffset instant) =>
        DateTimeOffset.TryParseExact(
            text,
            WithWrittenOffset,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out instant)
        || DateTimeOffset.TryParseExact(
            text,
            WithZulu,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out instant);
}
