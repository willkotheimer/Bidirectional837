using System.Reflection;
using System.Text.RegularExpressions;
using Translator.Domain.Entities;
using Translator.Domain.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Translator.Domain.Tests;

/// <summary>
/// Machine-checked guard over governance.txt Section 1 ("Naming Alignment") and Section 2
/// ("Mandatory Database Schema"). Every assertion here is an invariant, so every test is a
/// Theory driven by the governed field table rather than a hand-written example.
/// </summary>
public class SchemaContractTheories
{
    /// <summary>
    /// Governance Section 1: attribute names must reflect ASC X12 nomenclature. A name that
    /// does not carry a loop or segment token requires a documented mapping attribute, so an
    /// undecorated name such as total_amount must fail this Theory.
    /// </summary>
    private static readonly Regex X12ProvenanceToken = new(
        // PROVENANCE: FIND-006 - this guard first required a 2-3 character segment identifier and
        // so rejected the valid N3 and N4 segments.
        // Segment IDs are 1-3 characters in X12 (N3, N4, NM1, DMG), followed by the 2-3 digit
        // element position, optionally followed by the composite sub-element position.
        @"^(BHT\d{2}|Loop\d{4}[A-Z]{2}_[A-Z]{1,3}\d{2,3}|CLM\d{2}(_\d)?|HI\d{2}_\d|LX\d{2}|SV\d{3}(_\d)?|DTP\d{2})_",
        RegexOptions.Compiled);

    /// <summary>Structural keys carry no 837 counterpart; governance Section 2 declares them explicitly.</summary>
    private static readonly HashSet<string> StructuralProperties = ["Id", "ClaimHeaderId"];

    public static IEnumerable<object[]> GovernedEntityTypes() =>
    [
        [typeof(ClaimHeader)],
        [typeof(ClaimLineItem)],
    ];

    public static IEnumerable<object[]> GovernedScalarProperties()
    {
        foreach (var entity in new[] { typeof(ClaimHeader), typeof(ClaimLineItem) })
        {
            foreach (var property in entity.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType == typeof(List<ClaimLineItem>)) continue;
                if (property.PropertyType == typeof(ClaimHeader)) continue;
                yield return [entity, property.Name];
            }
        }
    }

    public static IEnumerable<object[]> GovernedX12Properties() =>
        GovernedScalarProperties().Where(row => !StructuralProperties.Contains((string)row[1]));

    [Theory]
    [InlineData(typeof(ClaimHeader), "Claims")]
    [InlineData(typeof(ClaimLineItem), "ClaimLineItems")]
    public void Entity_maps_to_the_governed_table_name(Type entityType, string expectedTableName)
    {
        using var store = new EphemeralClaimStore();
        using var context = store.CreateContext();

        var entity = context.Model.FindEntityType(entityType);

        Assert.NotNull(entity);
        Assert.Equal(expectedTableName, entity.GetTableName());
    }

    [Theory]
    [MemberData(nameof(GovernedX12Properties))]
    public void Governed_property_name_carries_an_x12_provenance_token(Type entityType, string propertyName)
    {
        Assert.True(
            X12ProvenanceToken.IsMatch(propertyName),
            entityType.Name + "." + propertyName + " does not encode its 837 loop/segment origin. " +
            "Governance Section 1 requires ASC X12 nomenclature or a documented mapping attribute.");
    }

    [Theory]
    [MemberData(nameof(GovernedScalarProperties))]
    public void Governed_property_maps_to_a_column_of_the_same_name(Type entityType, string propertyName)
    {
        using var store = new EphemeralClaimStore();
        using var context = store.CreateContext();

        var property = PropertyOf(context.Model, entityType, propertyName);
        var storeObject = StoreObjectIdentifier.Create(property.DeclaringType, StoreObjectType.Table);

        Assert.Equal(propertyName, property.GetColumnName(storeObject!.Value));
    }

    [Theory]
    // ClaimHeader - governance Section 2 [Required] fields
    [InlineData(typeof(ClaimHeader), "BHT03_ClaimSubmitterTransactionId")]
    [InlineData(typeof(ClaimHeader), "BHT04_TransactionSetCreationDate")]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_NM103_BillingProviderLastNameOrOrg")]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_NM109_BillingProviderNpi")]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_N301_BillingProviderAddressLine")]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_N401_BillingProviderCity")]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_N402_BillingProviderState")]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_N403_BillingProviderZipCode")]
    [InlineData(typeof(ClaimHeader), "Loop2010BA_NM103_SubscriberLastName")]
    [InlineData(typeof(ClaimHeader), "Loop2010BA_NM104_SubscriberFirstName")]
    [InlineData(typeof(ClaimHeader), "Loop2010BA_DMG02_SubscriberDob")]
    [InlineData(typeof(ClaimHeader), "Loop2010BA_DMG03_SubscriberGender")]
    [InlineData(typeof(ClaimHeader), "Loop2010BB_NM103_PayerName")]
    [InlineData(typeof(ClaimHeader), "Loop2010BB_NM109_PayerId")]
    [InlineData(typeof(ClaimHeader), "CLM01_ClaimControlNumber")]
    [InlineData(typeof(ClaimHeader), "CLM05_1_PlaceOfServiceCode")]
    [InlineData(typeof(ClaimHeader), "CLM05_3_ClaimFrequencyCode")]
    [InlineData(typeof(ClaimHeader), "HI01_2_PrincipalDiagnosisCode")]
    // ClaimLineItem - governance Section 2 [Required] fields
    [InlineData(typeof(ClaimLineItem), "LX01_AssignedLineNumber")]
    [InlineData(typeof(ClaimLineItem), "SV101_2_ProcedureCode")]
    [InlineData(typeof(ClaimLineItem), "SV103_UnitOfMeasure")]
    [InlineData(typeof(ClaimLineItem), "DTP03_ServiceDate")]
    public void Governed_required_field_is_not_nullable_in_the_store(Type entityType, string propertyName)
    {
        using var store = new EphemeralClaimStore();
        using var context = store.CreateContext();

        Assert.False(PropertyOf(context.Model, entityType, propertyName).IsNullable);
    }

    [Theory]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_NM104_BillingProviderFirstName")]
    public void Governed_optional_field_is_nullable_in_the_store(Type entityType, string propertyName)
    {
        using var store = new EphemeralClaimStore();
        using var context = store.CreateContext();

        Assert.True(PropertyOf(context.Model, entityType, propertyName).IsNullable);
    }

    [Theory]
    [InlineData(typeof(ClaimHeader), "BHT03_ClaimSubmitterTransactionId", 50)]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_NM103_BillingProviderLastNameOrOrg", 100)]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_NM104_BillingProviderFirstName", 35)]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_NM109_BillingProviderNpi", 10)]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_N301_BillingProviderAddressLine", 55)]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_N401_BillingProviderCity", 30)]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_N402_BillingProviderState", 2)]
    [InlineData(typeof(ClaimHeader), "Loop2010AA_N403_BillingProviderZipCode", 15)]
    [InlineData(typeof(ClaimHeader), "Loop2010BA_NM103_SubscriberLastName", 60)]
    [InlineData(typeof(ClaimHeader), "Loop2010BA_NM104_SubscriberFirstName", 35)]
    [InlineData(typeof(ClaimHeader), "Loop2010BA_DMG02_SubscriberDob", 8)]
    [InlineData(typeof(ClaimHeader), "Loop2010BA_DMG03_SubscriberGender", 1)]
    [InlineData(typeof(ClaimHeader), "Loop2010BB_NM103_PayerName", 60)]
    [InlineData(typeof(ClaimHeader), "Loop2010BB_NM109_PayerId", 80)]
    [InlineData(typeof(ClaimHeader), "CLM01_ClaimControlNumber", 38)]
    [InlineData(typeof(ClaimHeader), "CLM05_1_PlaceOfServiceCode", 2)]
    [InlineData(typeof(ClaimHeader), "CLM05_3_ClaimFrequencyCode", 1)]
    [InlineData(typeof(ClaimHeader), "HI01_2_PrincipalDiagnosisCode", 10)]
    [InlineData(typeof(ClaimLineItem), "SV101_2_ProcedureCode", 5)]
    [InlineData(typeof(ClaimLineItem), "SV103_UnitOfMeasure", 2)]
    [InlineData(typeof(ClaimLineItem), "DTP03_ServiceDate", 8)]
    public void Governed_string_field_declares_the_governed_max_length(Type entityType, string propertyName, int expectedMaxLength)
    {
        using var store = new EphemeralClaimStore();
        using var context = store.CreateContext();

        Assert.Equal(expectedMaxLength, PropertyOf(context.Model, entityType, propertyName).GetMaxLength());
    }

    [Theory]
    [InlineData(typeof(ClaimHeader), "CLM02_TotalClaimChargeAmount", "decimal(18,2)")]
    [InlineData(typeof(ClaimLineItem), "SV102_LineItemChargeAmount", "decimal(18,2)")]
    [InlineData(typeof(ClaimLineItem), "SV104_ServiceUnitCount", "decimal(18,4)")]
    public void Governed_numeric_field_declares_the_governed_precision(Type entityType, string propertyName, string expectedColumnType)
    {
        using var store = new EphemeralClaimStore();
        using var context = store.CreateContext();

        Assert.Equal(expectedColumnType, PropertyOf(context.Model, entityType, propertyName).GetColumnType());
    }

    [Theory]
    [MemberData(nameof(GovernedEntityTypes))]
    public void Governed_entity_has_a_single_guid_primary_key_named_Id(Type entityType)
    {
        using var store = new EphemeralClaimStore();
        using var context = store.CreateContext();

        var key = context.Model.FindEntityType(entityType)!.FindPrimaryKey();

        Assert.NotNull(key);
        var keyProperty = Assert.Single(key.Properties);
        Assert.Equal("Id", keyProperty.Name);
        Assert.Equal(typeof(Guid), keyProperty.ClrType);
    }

    private static IProperty PropertyOf(IModel model, Type entityType, string propertyName)
    {
        var entity = model.FindEntityType(entityType);
        Assert.NotNull(entity);
        var property = entity.FindProperty(propertyName);
        Assert.NotNull(property);
        return property;
    }
}
