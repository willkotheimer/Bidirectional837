using System.Net.Http.Json;
using System.Text.Json;

namespace Translator.Api.Tests;

/// <summary>
/// PROVENANCE: FIND-020 - the payload must carry the field names the contract publishes.
///
/// PROVENANCE: GOVERNANCE-1 - "Naming Alignment: Attribute names across the database, DTOs, and
/// React forms must reflect ASC X12 nomenclature (e.g. Loop2000A_BillingProvider,
/// CLM02_TotalClaimChargeAmount)."
///
/// docs/api/swagger.json declares those names. The application serves CLM02_TotalClaimChargeAmount,
/// BHT03_ClaimSubmitterTransactionId and Loop2010AA_NM103_BillingProviderLastNameOrOrg, because the
/// default camelCase policy lowercases the leading character of an acronym. Those are not ASC X12
/// nomenclature; they are a mangling of it.
///
/// The API conformance suite has checked routes since Section 2, status codes since Section 2 and
/// media types since FIND-016. It has never checked a field name, and the existing Theories read the
/// mangled names - which makes them a record of the defect rather than a guard against it.
/// </summary>
public class ContractNamingTheories : IClassFixture<GovernedApiFactory>
{
    private readonly GovernedApiFactory _factory;

    public ContractNamingTheories(GovernedApiFactory factory) => _factory = factory;

    /// <summary>The property names docs/api/swagger.json declares for a claim.</summary>
    private static List<string> PublishedClaimProperties() =>
        PublishedContract.Schema("ClaimHeaderDto").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name).ToList();

    public static IEnumerable<object[]> PublishedProperties() =>
        PublishedClaimProperties().Select(name => new object[] { name });

    /// <summary>
    /// Every governed column name the contract publishes appears verbatim in a served claim. Asserted
    /// per property, so a failure names the field that was mangled rather than reporting that two
    /// documents differ somewhere.
    /// </summary>
    [Theory]
    [MemberData(nameof(PublishedProperties))]
    public async Task Served_claim_carries_the_property_name_the_contract_publishes(string published)
    {
        var claim = await GenerateOneAsync();

        Assert.True(claim.TryGetProperty(published, out _),
            $"The contract publishes '{published}'. The served claim carries " +
            $"[{string.Join(", ", claim.EnumerateObject().Select(p => p.Name).Take(6))}, ...] instead.");
    }

    /// <summary>
    /// The other direction: a served claim must not carry a name the contract does not publish. This
    /// is what catches the mangling as an addition rather than only as an absence.
    /// </summary>
    [Fact]
    public async Task Served_claim_carries_no_property_the_contract_does_not_publish()
    {
        var claim = await GenerateOneAsync();
        var published = PublishedClaimProperties().ToHashSet(StringComparer.Ordinal);

        var unpublished = claim.EnumerateObject()
            .Select(property => property.Name)
            .Where(name => !published.Contains(name))
            .ToList();

        Assert.True(unpublished.Count == 0,
            $"The served claim carries {string.Join(", ", unpublished)}, which the contract does not publish.");
    }

    /// <summary>
    /// The governed names are the point, so they are asserted by name rather than only by parity
    /// with the contract. A contract edited to match a mangled payload would satisfy the Theories
    /// above and still breach governance Section 1.
    /// </summary>
    [Theory]
    [InlineData("CLM01_ClaimControlNumber")]
    [InlineData("CLM02_TotalClaimChargeAmount")]
    [InlineData("BHT03_ClaimSubmitterTransactionId")]
    [InlineData("HI01_2_PrincipalDiagnosisCode")]
    [InlineData("Loop2010AA_NM109_BillingProviderNpi")]
    [InlineData("Loop2010BA_NM103_SubscriberLastName")]
    public async Task Governed_column_name_reaches_the_client_unmangled(string governedName)
    {
        var claim = await GenerateOneAsync();

        Assert.True(claim.TryGetProperty(governedName, out _),
            $"Governance Section 1 requires ASC X12 nomenclature to reach the client. " +
            $"'{governedName}' does not appear in the served claim.");
    }

    /// <summary>Service lines carry governed names too; they are a separate schema.</summary>
    [Theory]
    [InlineData("SV101_2_ProcedureCode")]
    [InlineData("SV102_LineItemChargeAmount")]
    [InlineData("LX01_AssignedLineNumber")]
    [InlineData("DTP03_ServiceDate")]
    public async Task Governed_service_line_name_reaches_the_client_unmangled(string governedName)
    {
        var claim = await GenerateOneAsync();
        var lines = claim.GetProperty(
            claim.TryGetProperty("LineItems", out _) ? "LineItems" : "LineItems");

        Assert.True(lines[0].TryGetProperty(governedName, out _),
            $"'{governedName}' does not appear in the served service line.");
    }

    private async Task<JsonElement> GenerateOneAsync()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/bills/batch-generate", new
        {
            BillCount = 1,
            JurisdictionState = "OH",
            MedicalCodeCategories = new[] { "Cardiac" },
        });

        var claims = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return claims[0].Clone();
    }
}
