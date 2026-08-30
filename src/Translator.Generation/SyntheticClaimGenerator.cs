using Translator.Domain.Entities;

namespace Translator.Generation;

/// <summary>A request to generate a batch of synthetic claims.</summary>
public record BatchGenerationRequest(
    int BillCount,
    string JurisdictionState,
    IReadOnlyList<string> MedicalCodeCategories,
    int Seed);

/// <summary>
/// PROVENANCE: GOVERNANCE-5, ADR-014 - governance Feature 1, the synthetic bill batch generator.
/// </summary>
/// <remarks>
/// Generation is seeded and therefore reproducible. Governance Section 4 requires recorded,
/// repeatable test runs, and a claim that fails a downstream assertion is only investigable if the
/// batch that produced it can be regenerated exactly.
/// </remarks>
public sealed class SyntheticClaimGenerator
{
    private static readonly string[] FamilyNames =
        ["ANDERSON", "BAKER", "CHEN", "DIAZ", "ELLIS", "FOSTER", "GARCIA", "HAYES", "IVERSON",
         "JACKSON", "KOWALSKI", "LOPEZ", "MERCER", "NGUYEN", "OKAFOR", "PATEL", "QUINN", "REYES"];

    private static readonly string[] GivenNames =
        ["ALICE", "BRIAN", "CARMEN", "DEREK", "ELENA", "FELIX", "GRACE", "HAROLD", "IRENE",
         "JAMAL", "KAREN", "LUIS", "MARIA", "NOAH", "OLIVIA", "PRIYA"];

    private static readonly string[] Payers =
        ["MERIDIAN HEALTH PLAN", "NORTHSTAR INSURANCE", "UNITED REGIONAL BENEFITS", "CAPITOL MUTUAL HEALTH"];

    private static readonly string[] Genders = ["M", "F", "U"];

    /// <summary>Place of service: office, inpatient hospital, outpatient hospital, independent clinic.</summary>
    private static readonly string[] PlacesOfService = ["11", "21", "22", "49"];

    /// <summary>ICD-10-CM principal diagnoses drawn from the seed corpus.</summary>
    private static readonly string[] PrincipalDiagnoses = ["E11.9", "I10", "M54.5", "J44.9", "A00"];

    private const int MaximumLinesPerClaim = 5;

    public SyntheticClaimGenerator(
        IProviderDirectory providerDirectory,
        IMedicalCodeCatalog codeCatalog,
        IChargeSchedule chargeSchedule)
    {
        ProviderDirectory = providerDirectory;
        CodeCatalog = codeCatalog;
        ChargeSchedule = chargeSchedule;
    }

    public IProviderDirectory ProviderDirectory { get; }
    public IMedicalCodeCatalog CodeCatalog { get; }
    public IChargeSchedule ChargeSchedule { get; }

    public async Task<IReadOnlyList<ClaimHeader>> GenerateAsync(
        BatchGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var codes = request.MedicalCodeCategories
            .SelectMany(CodeCatalog.CodesIn)
            .ToList();

        if (codes.Count == 0)
        {
            throw new ArgumentException(
                $"No medical codes are catalogued for the requested categories: " +
                $"{string.Join(", ", request.MedicalCodeCategories)}.",
                nameof(request));
        }

        var random = new Random(request.Seed);
        var createdAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var claims = new List<ClaimHeader>(request.BillCount);

        for (var index = 0; index < request.BillCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var provider = await ProviderDirectory.ProviderForAsync(
                request.JurisdictionState, index, cancellationToken);

            claims.Add(BuildClaim(request, provider, codes, random, createdAt, index));
        }

        return claims;
    }

    private ClaimHeader BuildClaim(
        BatchGenerationRequest request,
        BillingProvider provider,
        IReadOnlyList<MedicalCode> codes,
        Random random,
        DateTime createdAt,
        int index)
    {
        var serviceDate = createdAt.AddDays(random.Next(0, 365));

        var claim = new ClaimHeader
        {
            BHT03_ClaimSubmitterTransactionId = $"BATCH{request.Seed:D6}-{index + 1:D5}",
            BHT04_TransactionSetCreationDate = createdAt,

            Loop2010AA_NM103_BillingProviderLastNameOrOrg = Truncate(provider.OrganisationOrLastName, 100),
            Loop2010AA_NM104_BillingProviderFirstName = provider.FirstName is null ? null : Truncate(provider.FirstName, 35),
            Loop2010AA_NM109_BillingProviderNpi = provider.Npi,
            Loop2010AA_N301_BillingProviderAddressLine = Truncate(provider.AddressLine, 55),
            Loop2010AA_N401_BillingProviderCity = Truncate(provider.City, 30),
            Loop2010AA_N402_BillingProviderState = provider.State,
            Loop2010AA_N403_BillingProviderZipCode = Truncate(provider.ZipCode, 15),

            Loop2010BA_NM103_SubscriberLastName = FamilyNames[random.Next(FamilyNames.Length)],
            Loop2010BA_NM104_SubscriberFirstName = GivenNames[random.Next(GivenNames.Length)],
            Loop2010BA_DMG02_SubscriberDob = BirthDate(random),
            Loop2010BA_DMG03_SubscriberGender = Genders[random.Next(Genders.Length)],

            Loop2010BB_NM103_PayerName = Payers[random.Next(Payers.Length)],
            Loop2010BB_NM109_PayerId = $"PAYER{random.Next(1000, 9999)}",

            // Unique within the batch by construction: the batch seed plus the claim ordinal.
            CLM01_ClaimControlNumber = $"CLM{request.Seed:D8}{index + 1:D6}",
            CLM05_1_PlaceOfServiceCode = PlacesOfService[random.Next(PlacesOfService.Length)],
            CLM05_3_ClaimFrequencyCode = "1",
            HI01_2_PrincipalDiagnosisCode = PrincipalDiagnoses[random.Next(PrincipalDiagnoses.Length)],
        };

        var lineCount = random.Next(1, MaximumLinesPerClaim + 1);

        for (var lineNumber = 1; lineNumber <= lineCount; lineNumber++)
        {
            var code = codes[random.Next(codes.Count)];
            var units = decimal.Round(random.Next(1, 9), 4);

            claim.LineItems.Add(new ClaimLineItem
            {
                LX01_AssignedLineNumber = lineNumber,
                SV101_2_ProcedureCode = code.Code,
                SV102_LineItemChargeAmount = decimal.Round(ChargeSchedule.ChargeFor(code.Code) * units, 2),
                SV103_UnitOfMeasure = "UN",
                SV104_ServiceUnitCount = units,
                DTP03_ServiceDate = serviceDate.ToString("yyyyMMdd"),
            });
        }

        // Governance User Story 1.2: CLM02 equals the sum of the SV102 line amounts. Computed from
        // the lines rather than alongside them, so the two cannot drift apart.
        claim.CLM02_TotalClaimChargeAmount = claim.LineItems.Sum(line => line.SV102_LineItemChargeAmount);

        return claim;
    }

    private static string BirthDate(Random random)
    {
        var year = random.Next(1935, 2010);
        var month = random.Next(1, 13);
        var day = random.Next(1, DateTime.DaysInMonth(year, month) + 1);

        return $"{year:D4}{month:D2}{day:D2}";
    }

    /// <summary>
    /// Holds a value inside its governed column length. Registry-sourced names and addresses are
    /// real data of unbounded length, and ADR-005 records that the store will not refuse an
    /// over-length value, so the generator must not hand it one.
    /// </summary>
    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
