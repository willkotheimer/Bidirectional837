using System.Globalization;
using System.Text.RegularExpressions;

namespace Governance.Edi;

/// <summary>
/// PROVENANCE: ADR-018 - the canonical rendering of a governed decimal as an X12 numeric element,
/// and its exact inverse.
/// </summary>
/// <remarks>
/// X12 carries no scale: a numeric element is written without insignificant trailing zeros, so the
/// text alone cannot say whether 1 was a quantity of one or an amount of 1.0000. The scale is a
/// property of the governed Section 2 column, and is restored from there on the way back in. The
/// two directions are inverse by construction, which is what the Section 1 Reversibility Guarantee
/// needs of them.
/// </remarks>
public static partial class X12Number
{
    /// <summary>Renders a governed decimal as an X12 R-type element.</summary>
    public static string Render(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);

        if (!text.Contains('.')) return text;

        text = text.TrimEnd('0').TrimEnd('.');

        // Trimming every digit of -0.00 or 0.00 leaves a sign or nothing at all.
        return text.Length == 0 || text == "-" ? "0" : text;
    }

    /// <summary>Reads an X12 R-type element back at the governed scale it was declared with.</summary>
    /// <exception cref="FormatException">
    /// The element is not a number, or carries more precision than the governed column can hold.
    /// Rounding it instead would store an amount the file does not state, which is the corruption
    /// FIND-001 recorded arriving through a different door.
    /// </exception>
    public static decimal Parse(string element, int governedScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(governedScale);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(governedScale, 28);

        if (element is null || !NumericElement().IsMatch(element))
        {
            throw new FormatException($"'{element}' is not an X12 numeric element.");
        }

        var point = element.IndexOf('.');
        var suppliedScale = point < 0 ? 0 : element.Length - point - 1;

        if (suppliedScale > governedScale)
        {
            throw new FormatException(
                $"'{element}' carries {suppliedScale} decimal places; the governed column holds " +
                $"{governedScale}. Reading it would silently change the value.");
        }

        // Addition takes the larger of the two scales, so adding a zero of the governed scale
        // restores the trailing zeros rendering suppressed. Rounding would not: decimal.Round(1m, 2)
        // is 1, not 1.00, and the difference is exactly what FIND-002 recorded in the store.
        return decimal.Parse(element, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture)
               + new decimal(0, 0, 0, false, (byte)governedScale);
    }

    [GeneratedRegex(@"^-?\d+(\.\d+)?$")]
    private static partial Regex NumericElement();
}
