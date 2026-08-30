namespace Governance.Edi;

/// <summary>
/// PROVENANCE: ADR-018 - the canonical rendering of a governed decimal as an X12 numeric element,
/// and its inverse.
/// </summary>
/// <remarks>NOT YET IMPLEMENTED - see Governance.Edi.Tests.</remarks>
public static class X12Number
{
    /// <summary>Renders a governed decimal as an X12 R-type element.</summary>
    public static string Render(decimal value) => throw new NotImplementedException(nameof(Render));

    /// <summary>Reads an X12 R-type element back at the governed scale it was declared with.</summary>
    public static decimal Parse(string element, int governedScale) => throw new NotImplementedException(nameof(Parse));
}
