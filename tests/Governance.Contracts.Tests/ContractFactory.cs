using System.Reflection;
using Governance.Contracts.DTOs;

namespace Governance.Contracts.Tests;

/// <summary>
/// Builds governed DTOs from a valid template, with any single field overridden by name.
/// Records are positional, so a test cannot use <c>with</c> against a property chosen at runtime;
/// this walks the primary constructor instead.
/// </summary>
public static class ContractFactory
{
    private static readonly Dictionary<string, object?> ValidClaimHeader = new()
    {
        ["Id"] = (Guid?)null,
        ["BHT03_ClaimSubmitterTransactionId"] = "BHT00000001",
        ["BHT04_TransactionSetCreationDate"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        ["Loop2010AA_NM103_BillingProviderLastNameOrOrg"] = "ACME MEDICAL GROUP",
        ["Loop2010AA_NM104_BillingProviderFirstName"] = "JANE",
        ["Loop2010AA_NM109_BillingProviderNpi"] = "1234567890",
        ["Loop2010AA_N301_BillingProviderAddressLine"] = "100 MAIN STREET",
        ["Loop2010AA_N401_BillingProviderCity"] = "COLUMBUS",
        ["Loop2010AA_N402_BillingProviderState"] = "OH",
        ["Loop2010AA_N403_BillingProviderZipCode"] = "43004",
        ["Loop2010BA_NM103_SubscriberLastName"] = "DOE",
        ["Loop2010BA_NM104_SubscriberFirstName"] = "JOHN",
        ["Loop2010BA_DMG02_SubscriberDob"] = "19800101",
        ["Loop2010BA_DMG03_SubscriberGender"] = "M",
        ["Loop2010BB_NM103_PayerName"] = "EXAMPLE PAYER",
        ["Loop2010BB_NM109_PayerId"] = "PAYER001",
        ["CLM01_ClaimControlNumber"] = "CLM0000000001",
        ["CLM02_TotalClaimChargeAmount"] = 125.00m,
        ["CLM05_1_PlaceOfServiceCode"] = "11",
        ["CLM05_3_ClaimFrequencyCode"] = "1",
        ["HI01_2_PrincipalDiagnosisCode"] = "E11.9",
        ["LineItems"] = new List<ClaimLineItemDto>(),
    };

    private static readonly Dictionary<string, object?> ValidClaimLineItem = new()
    {
        ["Id"] = (Guid?)null,
        ["LX01_AssignedLineNumber"] = 1,
        ["SV101_2_ProcedureCode"] = "99213",
        ["SV102_LineItemChargeAmount"] = 125.00m,
        ["SV104_ServiceUnitCount"] = 1.0000m,
        ["SV103_UnitOfMeasure"] = "UN",
        ["DTP03_ServiceDate"] = "20260101",
    };

    private static readonly Dictionary<string, object?> ValidBatchRequest = new()
    {
        ["BillCount"] = 10,
        ["JurisdictionState"] = "OH",
        ["MedicalCodeCategories"] = new List<string> { "Anesthesia" },
    };

    private static readonly Dictionary<Type, Dictionary<string, object?>> Templates = new()
    {
        [typeof(ClaimHeaderDto)] = ValidClaimHeader,
        [typeof(ClaimLineItemDto)] = ValidClaimLineItem,
        [typeof(BatchGenerationRequestDto)] = ValidBatchRequest,
    };

    public static object Valid(Type contractType) => Build(contractType, overrides: null);

    public static object With(Type contractType, string propertyName, object? value) =>
        Build(contractType, new Dictionary<string, object?> { [propertyName] = value });

    private static object Build(Type contractType, Dictionary<string, object?>? overrides)
    {
        var template = Templates[contractType];
        var constructor = contractType.GetConstructors().Single(c => c.GetParameters().Length == template.Count);

        var arguments = constructor.GetParameters()
            .Select(parameter => overrides is not null && overrides.TryGetValue(parameter.Name!, out var value)
                ? value
                : template[parameter.Name!])
            .ToArray();

        return constructor.Invoke(arguments);
    }

    /// <summary>A string of exactly <paramref name="length"/> characters.</summary>
    public static string StringOfLength(int length) => new('X', length);
}
