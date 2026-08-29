using System.Reflection;
using Governance.Contracts.DTOs;
using Governance.Domain.Entities;

namespace Governance.Contracts.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-3 - "The DTO must match the schema and 837 mappings directly. Custom
/// field additions or deviations require explicit documentation and architect approval."
///
/// These Theories hold the transcription to that clause in both directions: no governed column may
/// go unrepresented on the contract, and no contract field may appear that the schema does not have.
/// Like the Section 1 naming theories, they guard a transcription rather than drive an
/// implementation, so they are expected to pass from the outset (ADR-003).
/// </summary>
public class ContractSchemaParityTheories
{
    private static readonly (Type Entity, Type Contract)[] GovernedPairs =
    [
        (typeof(ClaimHeader), typeof(ClaimHeaderDto)),
        (typeof(ClaimLineItem), typeof(ClaimLineItemDto)),
    ];

    /// <summary>Navigation properties model the object graph; they are not 837 fields.</summary>
    private static readonly HashSet<string> Navigations = ["ClaimHeader", "ClaimHeaderId"];

    public static IEnumerable<object[]> GovernedColumns()
    {
        foreach (var (entity, contract) in GovernedPairs)
        {
            foreach (var property in entity.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (Navigations.Contains(property.Name)) continue;
                yield return [entity, contract, property.Name];
            }
        }
    }

    public static IEnumerable<object[]> ContractFields()
    {
        foreach (var (entity, contract) in GovernedPairs)
        {
            foreach (var property in contract.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name == "EqualityContract") continue;
                yield return [entity, contract, property.Name];
            }
        }
    }

    [Theory]
    [MemberData(nameof(GovernedColumns))]
    public void Every_governed_column_is_represented_on_the_contract(Type entityType, Type contractType, string propertyName)
    {
        Assert.True(contractType.GetProperty(propertyName) is not null,
            $"Governed column {entityType.Name}.{propertyName} has no counterpart on {contractType.Name}. " +
            "Governance Section 3 requires the DTO to match the schema directly.");
    }

    [Theory]
    [MemberData(nameof(ContractFields))]
    public void Every_contract_field_traces_back_to_a_governed_column(Type entityType, Type contractType, string propertyName)
    {
        Assert.True(entityType.GetProperty(propertyName) is not null,
            $"{contractType.Name}.{propertyName} has no counterpart in the governed schema. " +
            "Governance Section 3 requires custom field additions to carry explicit documentation " +
            "and architect approval; record one in docs/DECISIONS.md if this field is intended.");
    }

    [Theory]
    [MemberData(nameof(GovernedColumns))]
    public void Contract_field_carries_the_governed_type(Type entityType, Type contractType, string propertyName)
    {
        var governed = entityType.GetProperty(propertyName)!.PropertyType;
        var contract = contractType.GetProperty(propertyName)!.PropertyType;

        Assert.Equal(Normalise(governed), Normalise(contract));
    }

    /// <summary>
    /// Reduces the two shapes to what governance actually constrains: the identifier is optional on
    /// the contract because a claim is created without one, and the line item collection holds the
    /// contract's own line type rather than the entity's.
    /// </summary>
    private static string Normalise(Type type)
    {
        if (type == typeof(Guid?)) return nameof(Guid);
        if (type == typeof(List<ClaimLineItem>) || type == typeof(List<ClaimLineItemDto>)) return "LineItems";
        return type.Name;
    }
}
