using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Translator.TestSupport;

namespace Translator.Api.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - governance Feature 2, User Story 2.2: "As a User, I want to download
/// an .837 batch as a .zip archive via GET /api/v1/claims/export-zip, so that I can transport files
/// externally."
///
/// The unit-level packaging invariants live in Translator.Edi.Tests. These measure the governed
/// route: that it answers on the published contract, that what it returns is a ZIP a client can
/// actually open, and that the 837 files inside it carry the claims the store holds.
/// </summary>
public class ExportZipTheories : IClassFixture<GovernedApiFactory>
{
    private readonly GovernedApiFactory _factory;

    public ExportZipTheories(GovernedApiFactory factory) => _factory = factory;

    public static IEnumerable<object[]> Batches() =>
    [
        [1, "OH", new[] { "Cardiac" }],
        [4, "CA", new[] { "Anesthesia" }],
        [12, "NY", new[] { "PhysicalTherapy", "Cardiac" }],
    ];

    [Theory]
    [MemberData(nameof(Batches))]
    public async Task Export_answers_with_a_zip_a_client_can_open(int billCount, string state, string[] categories)
    {
        var client = _factory.CreateClient();
        await GenerateAsync(client, billCount, state, categories);

        var response = await client.GetAsync("/api/v1/claims/export-zip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);

        using var archive = await OpenAsync(response);
        Assert.NotEmpty(archive.Entries);
    }

    /// <summary>
    /// The response is a download, so it names the file it is. Without this a browser saves the
    /// route name, and the user story is about transporting a file externally.
    /// </summary>
    [Fact]
    public async Task Export_is_delivered_as_a_named_zip_attachment()
    {
        var client = _factory.CreateClient();
        await GenerateAsync(client, 2, "OH", ["Cardiac"]);

        var response = await client.GetAsync("/api/v1/claims/export-zip");
        var disposition = response.Content.Headers.ContentDisposition;

        Assert.NotNull(disposition);
        Assert.Equal("attachment", disposition.DispositionType);
        Assert.EndsWith(".zip", disposition.FileName?.Trim('"') ?? "", StringComparison.Ordinal);
    }

    /// <summary>
    /// Governance User Story 2.2: the archive must contain "valid individual or batched 837 files
    /// matching the database records". Every claim the dashboard route reports must appear in the
    /// archive, carrying the same governed values.
    /// </summary>
    [Theory]
    [MemberData(nameof(Batches))]
    public async Task Archive_carries_every_stored_claim_with_its_governed_values(
        int billCount, string state, string[] categories)
    {
        var client = _factory.CreateClient();
        await GenerateAsync(client, billCount, state, categories);

        var stored = JsonDocument.Parse(await client.GetStringAsync("/api/v1/claims")).RootElement;
        using var archive = await OpenAsync(await client.GetAsync("/api/v1/claims/export-zip"));

        var interchanges = archive.Entries.Select(ReadText).ToList();
        Assert.Equal(stored.GetArrayLength(), interchanges.Count);

        var byControlNumber = interchanges
            .ToLookup(edi => X12TestReader.Single(edi, "CLM")[1]);

        foreach (var claim in stored.EnumerateArray())
        {
            var controlNumber = claim.GetProperty("CLM01_ClaimControlNumber").GetString()!;
            var candidates = byControlNumber[controlNumber].ToList();

            Assert.NotEmpty(candidates);

            var edi = candidates.Single(text =>
                X12TestReader.Single(text, "BHT")[3] ==
                claim.GetProperty("BHT03_ClaimSubmitterTransactionId").GetString());

            Assert.Equal(
                claim.GetProperty("CLM02_TotalClaimChargeAmount").GetDecimal(),
                decimal.Parse(X12TestReader.Single(edi, "CLM")[2]));

            Assert.Equal(
                claim.GetProperty("Loop2010AA_NM109_BillingProviderNpi").GetString(),
                X12TestReader.All(edi, "NM1").Single(nm1 => nm1[1] == "85")[9]);

            Assert.Equal(
                claim.GetProperty("LineItems").GetArrayLength(),
                X12TestReader.All(edi, "SV1").Count);
        }
    }

    /// <summary>
    /// The contract publishes 200 as the only response, so an export of nothing is an empty
    /// archive rather than an error. A fresh host is used because the store is a singleton
    /// (ADR-015) and any other test in this assembly would otherwise have filled it.
    /// </summary>
    [Fact]
    public async Task Export_from_an_empty_store_is_an_empty_archive_not_an_error()
    {
        using var isolated = new GovernedApiFactory();
        var client = isolated.CreateClient();

        var response = await client.GetAsync("/api/v1/claims/export-zip");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var archive = await OpenAsync(response);
        Assert.Empty(archive.Entries);
    }

    private static async Task GenerateAsync(HttpClient client, int billCount, string state, string[] categories)
    {
        var response = await client.PostAsJsonAsync("/api/v1/bills/batch-generate", new
        {
            BillCount = billCount,
            JurisdictionState = state,
            MedicalCodeCategories = categories,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<ZipArchive> OpenAsync(HttpResponseMessage response) =>
        new(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
