namespace Governance.Domain.Validation;

/// <summary>
/// The check-digit rule governing <c>Loop2010AA_NM109_BillingProviderNpi</c>.
/// </summary>
/// <remarks>
/// An NPI is ten digits: a nine-digit identifier followed by a check digit. The check digit is a
/// Luhn checksum computed not over the identifier alone but over the identifier prefixed with
/// 80840, the ANSI issuer identifier assigned to CMS for health care providers. A ten-digit string
/// that fails this check is not an NPI, and a claim carrying one would be rejected downstream.
/// </remarks>
public static class NationalProviderIdentifier
{
    public const int Length = 10;

    private const int BaseLength = 9;

    /// <summary>The ANSI issuer prefix the checksum is computed over, per the NPI specification.</summary>
    private const string IssuerPrefix = "80840";

    public static bool IsValid(string? candidate)
    {
        if (candidate is null || candidate.Length != Length) return false;
        if (!candidate.All(char.IsAsciiDigit)) return false;

        return CheckDigitFor(candidate[..BaseLength]) == candidate[BaseLength..];
    }

    /// <summary>
    /// Computes the check digit that makes <paramref name="nineDigitBase"/> a valid NPI.
    /// </summary>
    public static string CheckDigitFor(string nineDigitBase)
    {
        ArgumentNullException.ThrowIfNull(nineDigitBase);

        if (nineDigitBase.Length != BaseLength || !nineDigitBase.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                $"An NPI base is exactly {BaseLength} digits; received '{nineDigitBase}'.",
                nameof(nineDigitBase));
        }

        var checksumSource = IssuerPrefix + nineDigitBase;
        var total = 0;

        // Luhn, doubling every second digit counting from the right of the base number. The check
        // digit is the amount that would carry the total to the next multiple of ten.
        for (var offset = 0; offset < checksumSource.Length; offset++)
        {
            var digit = checksumSource[^(offset + 1)] - '0';

            if (offset % 2 == 0)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }

            total += digit;
        }

        return ((10 - (total % 10)) % 10).ToString();
    }
}
