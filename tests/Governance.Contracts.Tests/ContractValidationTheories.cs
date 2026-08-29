using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Governance.Contracts.DTOs;
using Governance.Domain.Entities;

namespace Governance.Contracts.Tests;

/// <summary>
/// PROVENANCE: ADR-010 - the compensating control ADR-005 made the API layer responsible for.
///
/// The ephemeral store cannot enforce the governance Section 2 StringLength limits, so the contract
/// boundary must. These Theories walk the governed field table rather than sampling it: every limit
/// governance states is proven to be mirrored on the DTO and proven to be enforced.
/// </summary>
public class ContractValidationTheories
{
    /// <summary>
    /// The governed string fields and their Section 2 maximum lengths, paired with the DTO that
    /// carries them. Derived from the entity model so that the table cannot drift from Section 2.
    /// </summary>
    public static IEnumerable<object[]> GovernedStringFields()
    {
        foreach (var (entityType, contractType) in EntityToContract)
        {
            foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(string)) continue;

                var stringLength = property.GetCustomAttribute<StringLengthAttribute>();
                if (stringLength is null) continue;

                yield return [contractType, property.Name, stringLength.MaximumLength];
            }
        }
    }

    /// <summary>Governed string fields that Section 2 also marks [Required].</summary>
    public static IEnumerable<object[]> GovernedRequiredStringFields()
    {
        foreach (var (entityType, contractType) in EntityToContract)
        {
            foreach (var property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(string)) continue;
                if (property.GetCustomAttribute<RequiredAttribute>() is null) continue;

                yield return [contractType, property.Name];
            }
        }
    }

    private static readonly (Type Entity, Type Contract)[] EntityToContract =
    [
        (typeof(ClaimHeader), typeof(ClaimHeaderDto)),
        (typeof(ClaimLineItem), typeof(ClaimLineItemDto)),
    ];

    [Theory]
    [MemberData(nameof(GovernedStringFields))]
    public void Contract_mirrors_the_governed_maximum_length(Type contractType, string propertyName, int governedMaxLength)
    {
        var contractProperty = contractType.GetProperty(propertyName);

        Assert.NotNull(contractProperty);
        var declared = contractProperty.GetCustomAttribute<StringLengthAttribute>();

        Assert.True(declared is not null,
            $"{contractType.Name}.{propertyName} declares no StringLength. ADR-005 makes the contract " +
            "boundary responsible for the governed limits, because the store cannot enforce them.");
        Assert.Equal(governedMaxLength, declared!.MaximumLength);
    }

    [Theory]
    [MemberData(nameof(GovernedStringFields))]
    public void Contract_rejects_a_value_one_character_over_the_governed_limit(Type contractType, string propertyName, int governedMaxLength)
    {
        var candidate = ContractFactory.With(contractType, propertyName, ContractFactory.StringOfLength(governedMaxLength + 1));

        var failures = Validate(candidate);

        Assert.Contains(failures, failure => failure.MemberNames.Contains(propertyName));
    }

    [Theory]
    [MemberData(nameof(GovernedStringFields))]
    public void Contract_accepts_a_value_at_exactly_the_governed_limit(Type contractType, string propertyName, int governedMaxLength)
    {
        var candidate = ContractFactory.With(contractType, propertyName, ContractFactory.StringOfLength(governedMaxLength));

        var failures = Validate(candidate);

        Assert.DoesNotContain(failures, failure => failure.MemberNames.Contains(propertyName));
    }

    [Theory]
    [MemberData(nameof(GovernedRequiredStringFields))]
    public void Contract_rejects_a_missing_value_for_a_governed_required_field(Type contractType, string propertyName)
    {
        var candidate = ContractFactory.With(contractType, propertyName, null);

        var failures = Validate(candidate);

        Assert.Contains(failures, failure => failure.MemberNames.Contains(propertyName));
    }

    /// <summary>
    /// Governance User Story 1.3: a request above 500 bills is rejected. The lower bound of 1 is an
    /// addition recorded in ADR-010.
    /// </summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(250, true)]
    [InlineData(499, true)]
    [InlineData(500, true)]
    [InlineData(501, false)]
    [InlineData(1000, false)]
    [InlineData(int.MaxValue, false)]
    public void Batch_request_honours_the_governed_bill_count_ceiling(int billCount, bool expectedValid)
    {
        var candidate = ContractFactory.With(typeof(BatchGenerationRequestDto), "BillCount", billCount);

        var failures = Validate(candidate);

        Assert.Equal(expectedValid, !failures.Any(f => f.MemberNames.Contains("BillCount")));
    }

    [Theory]
    [InlineData("O", false)]
    [InlineData("OH", true)]
    [InlineData("OHI", false)]
    [InlineData("", false)]
    public void Batch_request_constrains_jurisdiction_state_to_two_characters(string state, bool expectedValid)
    {
        var candidate = ContractFactory.With(typeof(BatchGenerationRequestDto), "JurisdictionState", state);

        var failures = Validate(candidate);

        Assert.Equal(expectedValid, !failures.Any(f => f.MemberNames.Contains("JurisdictionState")));
    }

    private static List<ValidationResult> Validate(object candidate)
    {
        var failures = new List<ValidationResult>();
        Validator.TryValidateObject(candidate, new ValidationContext(candidate), failures, validateAllProperties: true);
        return failures;
    }
}
