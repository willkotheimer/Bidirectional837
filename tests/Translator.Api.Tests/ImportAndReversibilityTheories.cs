using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Translator.TestSupport;

namespace Translator.Api.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - governance Feature 3 at the API boundary. User Story 3.1 ingests an
/// 837 file or a ZIP of them so that they display on the Imported Bills Dashboard; User Story 3.2
/// re-exports an imported bill and re-imports it to verify no field mutation occurred.
///
/// The unit-level round trip lives in Translator.Edi.Tests. What is proven here is the loop a user
/// actually performs: generate a batch, download the archive, upload it back, and ask the system
/// whether anything moved.
/// </summary>
public class ImportAndReversibilityTheories : IClassFixture<GovernedApiFactory>
{
    private readonly GovernedApiFactory _factory;

    public ImportAndReversibilityTheories(GovernedApiFactory factory) => _factory = factory;

    public static IEnumerable<object[]> BatchSizes() => [[1], [3], [9]];

    [Fact]
    public async Task Uploading_a_single_837_file_creates_the_claim_it_describes()
    {
        using var isolated = new GovernedApiFactory();
        var client = isolated.CreateClient();

        var claim = GovernedClaimCorpus.Build(1, 3);
        var interchange = new Translator.Edi.Edi837Serializer().Serialize(claim);

        var response = await UploadAsync(client, Encoding.UTF8.GetBytes(interchange), "claim.837");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, created.GetArrayLength());

        var imported = created[0];
        Assert.Equal(claim.CLM01_ClaimControlNumber, imported.GetProperty("CLM01_ClaimControlNumber").GetString());
        Assert.Equal(claim.CLM02_TotalClaimChargeAmount, imported.GetProperty("CLM02_TotalClaimChargeAmount").GetDecimal());
        Assert.Equal(claim.HI01_2_PrincipalDiagnosisCode, imported.GetProperty("HI01_2_PrincipalDiagnosisCode").GetString());
        Assert.Equal(claim.LineItems.Count, imported.GetProperty("LineItems").GetArrayLength());
    }

    /// <summary>
    /// The imported claim reaches the store, which is what governance User Story 3.1 means by
    /// displaying on the Imported Bills Dashboard: the list route is the dashboard's source.
    /// </summary>
    [Fact]
    public async Task Imported_claim_appears_on_the_dashboard_route()
    {
        using var isolated = new GovernedApiFactory();
        var client = isolated.CreateClient();

        var claim = GovernedClaimCorpus.Build(4, 2);
        var interchange = new Translator.Edi.Edi837Serializer().Serialize(claim);
        await UploadAsync(client, Encoding.UTF8.GetBytes(interchange), "claim.837");

        var listed = JsonDocument.Parse(await client.GetStringAsync("/api/v1/claims")).RootElement;

        Assert.Equal(1, listed.GetArrayLength());
        Assert.Equal(claim.CLM01_ClaimControlNumber, listed[0].GetProperty("CLM01_ClaimControlNumber").GetString());
    }

    /// <summary>
    /// The whole governed loop over HTTP: generate, export, re-import, and verify. This is the
    /// journey governance Feature 3 describes, performed end to end through the published contract
    /// rather than against the engines beneath it.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchSizes))]
    public async Task Generated_batch_survives_export_and_re_import_without_mutation(int billCount)
    {
        using var isolated = new GovernedApiFactory();
        var client = isolated.CreateClient();

        var generated = await client.PostAsJsonAsync("/api/v1/bills/batch-generate", new
        {
            BillCount = billCount,
            JurisdictionState = "OH",
            MedicalCodeCategories = new[] { "Cardiac", "Anesthesia" },
        });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);

        var archive = await client.GetByteArrayAsync("/api/v1/claims/export-zip");

        using var receiving = new GovernedApiFactory();
        var receiver = receiving.CreateClient();

        var imported = await UploadAsync(receiver, archive, "batch.zip");
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);

        var claims = JsonDocument.Parse(await imported.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(billCount, claims.GetArrayLength());

        foreach (var claim in claims.EnumerateArray())
        {
            var id = claim.GetProperty("Id").GetString();
            var verdict = await receiver.PostAsync($"/api/v1/claims/{id}/verify-reversibility", null);

            Assert.Equal(HttpStatusCode.OK, verdict.StatusCode);

            var report = JsonDocument.Parse(await verdict.Content.ReadAsStringAsync()).RootElement;

            Assert.True(report.GetProperty("EdiTextIsIdentical").GetBoolean());
            Assert.True(report.GetProperty("RecordIsIdentical").GetBoolean(),
                report.GetProperty("Differences").ToString());
            Assert.Equal(0, report.GetProperty("Differences").GetArrayLength());
        }
    }

    /// <summary>
    /// The claim a client gets back from an import must be the claim the store holds. A response
    /// built from the parsed object rather than from the stored record would hide any mutation the
    /// persistence layer introduced, which is the layer FIND-001 and FIND-002 were found in.
    /// </summary>
    [Fact]
    public async Task Imported_claim_is_returned_as_the_store_holds_it()
    {
        using var isolated = new GovernedApiFactory();
        var client = isolated.CreateClient();

        var claim = GovernedClaimCorpus.Build(7, 4);
        var interchange = new Translator.Edi.Edi837Serializer().Serialize(claim);

        var response = await UploadAsync(client, Encoding.UTF8.GetBytes(interchange), "claim.837");
        var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement[0];
        var id = created.GetProperty("Id").GetString();

        var fetched = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/claims/{id}")).RootElement;

        Assert.Equal(created.ToString(), fetched.ToString());
    }

    public static IEnumerable<object[]> RejectedUploads() =>
    [
        [Array.Empty<byte>(), "empty.837"],
        [Encoding.UTF8.GetBytes("   "), "blank.837"],
        [Encoding.UTF8.GetBytes("not an interchange"), "garbage.837"],
        [Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><claim/>"), "claim.xml"],
        [new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00 }, "truncated.zip"],
    ];

    [Theory]
    [MemberData(nameof(RejectedUploads))]
    public async Task Payload_that_is_not_a_governed_837_is_rejected_with_a_problem_document(
        byte[] payload, string fileName)
    {
        using var isolated = new GovernedApiFactory();
        var client = isolated.CreateClient();

        var response = await UploadAsync(client, payload, fileName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("problem+json", response.Content.Headers.ContentType?.MediaType ?? "",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// PROVENANCE: GOVERNANCE-1 - a rejected import must leave nothing behind. A partially applied
    /// batch is the worst outcome available: the store would hold claims the sender never
    /// successfully sent, and they would export as valid 837 files.
    /// </summary>
    [Fact]
    public async Task Archive_containing_one_bad_file_imports_nothing_at_all()
    {
        using var isolated = new GovernedApiFactory();
        var client = isolated.CreateClient();

        var serializer = new Translator.Edi.Edi837Serializer();
        var archive = new Translator.Edi.ClaimArchive(serializer);
        var package = archive.Package([GovernedClaimCorpus.Build(1, 2), GovernedClaimCorpus.Build(2, 2)]);

        var damaged = Corrupt(package);

        var response = await UploadAsync(client, damaged, "batch.zip");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var listed = JsonDocument.Parse(await client.GetStringAsync("/api/v1/claims")).RootElement;
        Assert.Equal(0, listed.GetArrayLength());
    }

    [Fact]
    public async Task Upload_with_no_file_at_all_is_rejected()
    {
        using var isolated = new GovernedApiFactory();
        var client = isolated.CreateClient();

        var response = await client.PostAsync("/api/v1/claims/import", new MultipartFormDataContent());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Verifying_a_claim_the_store_does_not_hold_is_a_not_found()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/v1/claims/{Guid.NewGuid()}/verify-reversibility", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Replaces the CLM segment of one archived file with something unreadable.</summary>
    private static byte[] Corrupt(byte[] package)
    {
        using var source = new MemoryStream(package);
        using var reading = new System.IO.Compression.ZipArchive(source, System.IO.Compression.ZipArchiveMode.Read);

        var texts = reading.Entries.Select(entry =>
        {
            using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false));
            return (entry.FullName, Text: reader.ReadToEnd());
        }).ToList();

        texts[1] = (texts[1].FullName, "this file is not an interchange");

        using var buffer = new MemoryStream();
        using (var writing = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            foreach (var (name, text) in texts)
            {
                using var stream = writing.CreateEntry(name).Open();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] payload, string fileName)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var form = new MultipartFormDataContent { { content, "file", fileName } };

        return await client.PostAsync("/api/v1/claims/import", form);
    }
}
