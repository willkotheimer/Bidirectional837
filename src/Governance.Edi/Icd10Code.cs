using System.Text.RegularExpressions;

namespace Governance.Edi;

/// <summary>
/// PROVENANCE: ADR-019 - the ICD-10-CM decimal point is carried in the database and forbidden in
/// the X12 element, so the two forms are converted rather than stored twice.
/// </summary>
/// <remarks>
/// The conversion is only invertible for a code held in canonical form (FIND-011). The
/// point always follows the third character, so restoring it is deterministic; but a code stored as
/// "E119" would be emitted unchanged and read back as "E11.9", silently changing a clinical field
/// while producing a perfectly valid file. A code that is not canonical is therefore refused.
/// </remarks>
public static partial class Icd10Code
{
    /// <summary>The form written into an HI segment: no decimal point.</summary>
    public static string ToX12(string storedCode)
    {
        if (storedCode is null || !StoredForm().IsMatch(storedCode))
        {
            throw new FormatException(
                $"'{storedCode}' is not an ICD-10-CM code in the canonical form the governed " +
                "HI01_2_PrincipalDiagnosisCode column holds: three characters, then a decimal " +
                "point and up to four more.");
        }

        return storedCode.Replace(".", string.Empty);
    }

    /// <summary>The form held in HI01_2_PrincipalDiagnosisCode: decimal point restored.</summary>
    public static string FromX12(string ediCode)
    {
        if (ediCode is null || !EmittedForm().IsMatch(ediCode))
        {
            throw new FormatException($"'{ediCode}' is not an ICD-10-CM code as an HI segment carries it.");
        }

        return ediCode.Length <= 3 ? ediCode : ediCode[..3] + "." + ediCode[3..];
    }

    /// <summary>A category of three characters, optionally extended after a decimal point.</summary>
    [GeneratedRegex(@"^[A-Z][0-9][0-9A-Z](\.[0-9A-Z]{1,4})?$")]
    private static partial Regex StoredForm();

    /// <summary>The same code with the point removed, which is how X12 carries it.</summary>
    [GeneratedRegex(@"^[A-Z][0-9][0-9A-Z][0-9A-Z]{0,4}$")]
    private static partial Regex EmittedForm();
}
