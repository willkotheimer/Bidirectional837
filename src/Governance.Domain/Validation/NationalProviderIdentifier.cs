namespace Governance.Domain.Validation;

/// <summary>
/// The check-digit rule governing Loop2010AA_NM109_BillingProviderNpi.
/// NOT YET IMPLEMENTED - see Governance.Domain.Tests.NationalProviderIdentifierTheories.
/// </summary>
public static class NationalProviderIdentifier
{
    public const int Length = 10;

    public static bool IsValid(string? candidate)
        => throw new NotImplementedException(nameof(IsValid));

    public static string CheckDigitFor(string nineDigitBase)
        => throw new NotImplementedException(nameof(CheckDigitFor));
}
