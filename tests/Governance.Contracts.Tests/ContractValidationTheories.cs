using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Governance.Contracts.DTOs;
using Governance.Domain.Entities;

namespace Governance.Contracts.Tests;

/// <summary>
/// PROVENANCE: ADR-005, ADR-010 - the compensating control ADR-005 made the API layer responsible
/// for. The ephemeral store cannot enforce the governance Section 2 StringLength limits, so the
/// contract boundary must. These Theories walk the governed field table rather than sampling it:
/// every limit governance states is proven to be mirrored on the DTO, and proven to reject.
/// </summary>
/// <remarks>
/// Metadata is read from the primary constructor parameters rather than from the generated
/// properties, because that is where ASP.NET Core reads it for a record type. It rejects a record
/// whose validation metadata sits on the property instead, and would do so at runtime rather than
/// at compile time - a failure this suite exists to prevent reaching the deployed API.
/// </remarks>
public class ContractValidationTheories
{
    private static readonly (Type Entity, Type Contract)[] EntityToContract =
    [
        (typeof(ClaimHeader), typeof(ClaimHeaderDto)),
        (typeof(ClaimLineItem), typeof(ClaimLineItemDto)),
    ];

    /// <summary>Governed string fields and their Section 2 maximum lengths, read from the entity model.</summary>
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

    [Theory]
    [MemberData(nameof(GovernedStringFields))]
    public void Contract_mirrors_the_governed_maximum_length(Type contractType, string fieldName, int governedMaxLength)
    {
        var declared = ValidationAttributesOf(contractType, fieldName).OfType<StringLengthAttribute>().SingleOrDefault();

        Assert.True(declared is not null,
            $"{contractType.Name}.{fieldName} declares no StringLength. ADR-005 makes the contract " +
            "boundary responsible for the governed limits, because the store cannot enforce them.");
        Assert.Equal(governedMaxLength, declared!.MaximumLength);
    }

    [Theory]
    [MemberData(nameof(GovernedStringFields))]
    public void Contract_rejects_a_value_one_character_over_the_governed_limit(Type contractType, string fieldName, int governedMaxLength)
        => Assert.False(IsAccepted(contractType, fieldName, new string('X', governedMaxLength + 1)));

    [Theory]
    [MemberData(nameof(GovernedStringFields))]
    public void Contract_accepts_a_value_at_exactly_the_governed_limit(Type contractType, string fieldName, int governedMaxLength)
        => Assert.True(IsAccepted(contractType, fieldName, new string('X', governedMaxLength)));

    [Theory]
    [MemberData(nameof(GovernedRequiredStringFields))]
    public void Contract_rejects_a_missing_value_for_a_governed_required_field(Type contractType, string fieldName)
        => Assert.False(IsAccepted(contractType, fieldName, null));

    /// <summary>
    /// Governance User Story 1.3: a request above 500 bills is rejected. The floor of 1 is an
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
    public void Batch_request_honours_the_governed_bill_count_ceiling(int billCount, bool expectedAccepted)
        => Assert.Equal(expectedAccepted, IsAccepted(typeof(BatchGenerationRequestDto), "BillCount", billCount));

    [Theory]
    [InlineData("O", false)]
    [InlineData("OH", true)]
    [InlineData("OHI", false)]
    [InlineData("", false)]
    public void Batch_request_constrains_jurisdiction_state_to_two_characters(string state, bool expectedAccepted)
        => Assert.Equal(expectedAccepted, IsAccepted(typeof(BatchGenerationRequestDto), "JurisdictionState", state));

    /// <summary>
    /// Guards the framework constraint this suite was extended to catch: ASP.NET Core throws for a
    /// record carrying validation metadata on a generated property rather than on its constructor
    /// parameter, so an annotation in the wrong place disables validation for the whole request.
    /// </summary>
    [Theory]
    [InlineData(typeof(ClaimHeaderDto))]
    [InlineData(typeof(ClaimLineItemDto))]
    [InlineData(typeof(BatchGenerationRequestDto))]
    public void Contract_declares_no_validation_metadata_on_a_generated_property(Type contractType)
    {
        var misplaced = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttributes<ValidationAttribute>(inherit: true).Any())
            .Select(property => property.Name)
            .ToList();

        Assert.True(misplaced.Count == 0,
            $"{contractType.Name} carries validation metadata on the generated propert(ies) " +
            $"{string.Join(", ", misplaced)}. ASP.NET Core reads record validation metadata from the " +
            "primary constructor parameter and throws when it is found on the property instead.");
    }

    private static IEnumerable<ValidationAttribute> ValidationAttributesOf(Type contractType, string fieldName)
    {
        var parameter = PrimaryConstructor(contractType).GetParameters()
            .SingleOrDefault(p => p.Name == fieldName);

        Assert.True(parameter is not null,
            $"{contractType.Name} has no constructor parameter named {fieldName}.");

        return parameter!.GetCustomAttributes<ValidationAttribute>(inherit: true);
    }

    /// <summary>Evaluates the governed constraints on one field exactly as the framework will.</summary>
    private static bool IsAccepted(Type contractType, string fieldName, object? value) =>
        ValidationAttributesOf(contractType, fieldName).All(attribute => attribute.IsValid(value));

    private static ConstructorInfo PrimaryConstructor(Type contractType) =>
        contractType.GetConstructors().MaxBy(c => c.GetParameters().Length)!;
}
