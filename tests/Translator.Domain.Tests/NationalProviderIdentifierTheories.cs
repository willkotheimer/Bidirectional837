using Translator.Domain.Validation;

namespace Translator.Domain.Tests;

/// <summary>
/// Governance User Story 1.1 requires that the generator populate a *valid* NPI. An NPI is not
/// merely ten digits: it carries a Luhn check digit computed over the identifier prefixed with the
/// NPI issuer prefix 80840. A ten-digit string that fails that check is not an NPI, and a
/// clearinghouse would reject the claim carrying it.
/// </summary>
public class NationalProviderIdentifierTheories
{
    /// <summary>
    /// Identifiers whose check digit is correct. Each is the nine-digit base with the check digit
    /// this implementation must compute appended.
    /// </summary>
    [Theory]
    [InlineData("1234567893")]
    [InlineData("1245319599")]
    [InlineData("1679576722")]
    [InlineData("1841293990")]
    [InlineData("1497758544")]
    public void Correctly_checked_identifier_is_accepted(string npi)
        => Assert.True(NationalProviderIdentifier.IsValid(npi));

    /// <summary>
    /// The same identifiers with a deliberately wrong final digit. A validator that ignores the
    /// check digit would accept all of these, which is the failure this Theory exists to prevent.
    /// </summary>
    [Theory]
    [InlineData("1234567890")]
    [InlineData("1234567891")]
    [InlineData("1245319598")]
    [InlineData("1679576720")]
    [InlineData("1841293991")]
    [InlineData("1497758545")]
    public void Identifier_with_a_wrong_check_digit_is_rejected(string npi)
        => Assert.False(NationalProviderIdentifier.IsValid(npi));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123456789")]      // nine digits
    [InlineData("12345678931")]    // eleven digits
    [InlineData("123456789A")]     // non-numeric
    [InlineData("A234567893")]
    [InlineData("12345 67893")]
    public void Malformed_identifier_is_rejected(string? npi)
        => Assert.False(NationalProviderIdentifier.IsValid(npi));

    /// <summary>
    /// Appending the computed check digit to any nine-digit base must produce a valid identifier.
    /// This is the invariant the generator depends on to mint provider identifiers that pass.
    /// </summary>
    [Theory]
    [InlineData("123456789")]
    [InlineData("000000000")]
    [InlineData("999999999")]
    [InlineData("124531959")]
    [InlineData("167957672")]
    [InlineData("184129399")]
    [InlineData("149775854")]
    [InlineData("500000001")]
    public void Base_with_its_computed_check_digit_is_always_valid(string nineDigitBase)
    {
        var npi = nineDigitBase + NationalProviderIdentifier.CheckDigitFor(nineDigitBase);

        Assert.Equal(NationalProviderIdentifier.Length, npi.Length);
        Assert.True(NationalProviderIdentifier.IsValid(npi));
    }

    [Theory]
    [InlineData("123456789", "3")]
    public void Check_digit_matches_the_published_worked_example(string nineDigitBase, string expected)
        => Assert.Equal(expected, NationalProviderIdentifier.CheckDigitFor(nineDigitBase));
}
