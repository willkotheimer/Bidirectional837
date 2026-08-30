namespace Governance.Edi;

/// <summary>
/// PROVENANCE: ADR-019 - the ICD-10-CM decimal point is carried in the database and forbidden in
/// the X12 element, so the two forms are converted rather than stored twice.
/// </summary>
/// <remarks>NOT YET IMPLEMENTED - see Governance.Edi.Tests.</remarks>
public static class Icd10Code
{
    /// <summary>The form written into an HI segment: no decimal point.</summary>
    public static string ToX12(string storedCode) => throw new NotImplementedException(nameof(ToX12));

    /// <summary>The form held in HI01_2_PrincipalDiagnosisCode: decimal point restored.</summary>
    public static string FromX12(string ediCode) => throw new NotImplementedException(nameof(FromX12));
}
