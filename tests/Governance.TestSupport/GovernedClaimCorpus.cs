using Governance.Domain.Entities;

namespace Governance.TestSupport;

/// <summary>
/// Deterministic corpus of governed claims used as Theory data. Deterministic by design:
/// governance Section 4 requires a recorded failing run, and a randomised corpus would make
/// that recorded RED run unauditable.
/// </summary>
public static class GovernedClaimCorpus
{
    private static readonly string[] States = ["OH", "CA", "NY", "TX", "FL"];
    private static readonly string[] Genders = ["M", "F", "U"];
    private static readonly string[] PlaceOfService = ["11", "21", "22", "81"];
    private static readonly string[] ProcedureCodes = ["G0008", "A0425", "J0120", "99213", "97110"];
    private static readonly string[] DiagnosisCodes = ["A00", "E11.9", "I10", "M54.5"];
    private static readonly string[] UnitsOfMeasure = ["UN", "MJ"];

    /// <summary>Builds claim number <paramref name="index"/> of the corpus. Same index, same claim.</summary>
    public static ClaimHeader Build(int index, int lineItemCount = 3)
    {
        var rng = new Random(index * 7919);
        var header = new ClaimHeader
        {
            Id = DeterministicGuid(index, 0),
            BHT03_ClaimSubmitterTransactionId = $"BHT{index:D8}",
            BHT04_TransactionSetCreationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index),
            Loop2010AA_NM103_BillingProviderLastNameOrOrg = $"BILLING PROVIDER ORG {index}",
            Loop2010AA_NM104_BillingProviderFirstName = index % 3 == 0 ? null : $"FIRST{index}",
            Loop2010AA_NM109_BillingProviderNpi = $"1{index % 1000000000:D9}",
            Loop2010AA_N301_BillingProviderAddressLine = $"{100 + index} MAIN STREET SUITE {index % 500}",
            Loop2010AA_N401_BillingProviderCity = $"CITY{index}",
            Loop2010AA_N402_BillingProviderState = States[index % States.Length],
            Loop2010AA_N403_BillingProviderZipCode = $"{43000 + (index % 1000):D5}",
            Loop2010BA_NM103_SubscriberLastName = $"SUBSCRIBERLAST{index}",
            Loop2010BA_NM104_SubscriberFirstName = $"SUBFIRST{index}",
            Loop2010BA_DMG02_SubscriberDob = $"{1940 + (index % 70):D4}{1 + (index % 12):D2}{1 + (index % 28):D2}",
            Loop2010BA_DMG03_SubscriberGender = Genders[index % Genders.Length],
            Loop2010BB_NM103_PayerName = $"PAYER ORGANIZATION {index}",
            Loop2010BB_NM109_PayerId = $"PAYERID{index:D6}",
            CLM01_ClaimControlNumber = $"CLM{index:D10}",
            CLM05_1_PlaceOfServiceCode = PlaceOfService[index % PlaceOfService.Length],
            CLM05_3_ClaimFrequencyCode = "1",
            HI01_2_PrincipalDiagnosisCode = DiagnosisCodes[index % DiagnosisCodes.Length],
        };

        for (var line = 1; line <= lineItemCount; line++)
        {
            header.LineItems.Add(new ClaimLineItem
            {
                Id = DeterministicGuid(index, line),
                LX01_AssignedLineNumber = line,
                SV101_2_ProcedureCode = ProcedureCodes[(index + line) % ProcedureCodes.Length],
                SV102_LineItemChargeAmount = decimal.Round(rng.Next(1, 50_000) / 100m, 2),
                SV103_UnitOfMeasure = UnitsOfMeasure[(index + line) % UnitsOfMeasure.Length],
                SV104_ServiceUnitCount = decimal.Round(rng.Next(1, 40) / 4m, 4),
                DTP03_ServiceDate = $"2026{1 + (index % 12):D2}{1 + (line % 28):D2}",
            });
        }

        // Governance Feature 1 / User Story 1.2: CLM02 equals the sum of its SV102 amounts.
        header.CLM02_TotalClaimChargeAmount = header.LineItems.Sum(l => l.SV102_LineItemChargeAmount);
        return header;
    }

    /// <summary>Theory data: claim indices spanning line-item cardinalities 1..8.</summary>
    public static IEnumerable<object[]> ClaimIndices()
    {
        for (var index = 1; index <= 12; index++)
        {
            yield return [index, 1 + (index % 8)];
        }
    }

    private static Guid DeterministicGuid(int index, int line)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, index);
        BitConverter.TryWriteBytes(bytes[4..], line);
        BitConverter.TryWriteBytes(bytes[8..], 0x08371000 + index);
        BitConverter.TryWriteBytes(bytes[12..], line * 31 + 17);
        return new Guid(bytes);
    }
}
