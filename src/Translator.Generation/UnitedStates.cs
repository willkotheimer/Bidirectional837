namespace Translator.Generation;

/// <summary>
/// The two-letter jurisdiction codes the governed <c>Loop2010AA_N402</c> column carries, and their
/// names.
/// </summary>
/// <remarks>
/// PROVENANCE: ADR-025 - a selector needs a label, and "OH" is a code rather than a label. This is a
/// fixed list rather than a package dependency: it changes on a timescale of decades, it is fifty
/// states plus the District of Columbia and Puerto Rico, and a dependency would be a larger surface
/// than the data it carries.
///
/// A code with no entry is returned unchanged rather than replaced with a placeholder, so an
/// unexpected jurisdiction is still usable and still obviously itself.
/// </remarks>
public static class UnitedStates
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona", ["AR"] = "Arkansas",
        ["CA"] = "California", ["CO"] = "Colorado", ["CT"] = "Connecticut", ["DE"] = "Delaware",
        ["DC"] = "District of Columbia", ["FL"] = "Florida", ["GA"] = "Georgia", ["HI"] = "Hawaii",
        ["ID"] = "Idaho", ["IL"] = "Illinois", ["IN"] = "Indiana", ["IA"] = "Iowa",
        ["KS"] = "Kansas", ["KY"] = "Kentucky", ["LA"] = "Louisiana", ["ME"] = "Maine",
        ["MD"] = "Maryland", ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota",
        ["MS"] = "Mississippi", ["MO"] = "Missouri", ["MT"] = "Montana", ["NE"] = "Nebraska",
        ["NV"] = "Nevada", ["NH"] = "New Hampshire", ["NJ"] = "New Jersey", ["NM"] = "New Mexico",
        ["NY"] = "New York", ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio",
        ["OK"] = "Oklahoma", ["OR"] = "Oregon", ["PA"] = "Pennsylvania", ["PR"] = "Puerto Rico",
        ["RI"] = "Rhode Island", ["SC"] = "South Carolina", ["SD"] = "South Dakota",
        ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah", ["VT"] = "Vermont",
        ["VA"] = "Virginia", ["WA"] = "Washington", ["WV"] = "West Virginia", ["WI"] = "Wisconsin",
        ["WY"] = "Wyoming",
    };

    /// <summary>The jurisdiction's name, or the code itself if it is not one this list knows.</summary>
    public static string NameOf(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return Names.TryGetValue(code, out var name) ? name : code.ToUpperInvariant();
    }
}
