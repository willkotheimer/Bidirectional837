using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Translator.Edi;
using Translator.TestSupport;

namespace Translator.Api.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-4 - the Roundtrip Reversibility Test Standard, performed end to end
/// through the published API rather than against the engines beneath it.
///
/// PROVENANCE: GOVERNANCE-1 - the Zero-Mutation Rule, in both directions.
///
/// The unit suite already proves the round trip over the corpus, and the API suite already asks each
/// imported claim whether it round-trips. Neither closes the loop these two Theories close.
///
/// The existing API check asks the *receiving* host whether its own stored record survives a round
/// trip. It never compares that record back to the claim the *sending* host generated, so a value
/// lost identically on the way out and the way in would satisfy it. And nothing at the API boundary
/// has ever compared an uploaded file against the file the system would emit from what it stored,
/// which is the governed standard stated literally.
/// </summary>
public class RoundTripJourneyTheories
{
    /// <summary>Storage identity has no 837 counterpart, so it cannot survive and is not compared.</summary>
    private static readonly HashSet<string> NotCarriedBy837 = new(StringComparer.Ordinal) { "Id" };

    public static IEnumerable<object[]> BatchSizes() => [[1], [5], [20]];

    /// <summary>
    /// Bill to 837 and back. Generated on one host, exported, imported into a second, and every
    /// governed column compared against the claim that started the journey.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchSizes))]
    public async Task Bill_survives_the_journey_to_837_and_back_into_a_bill(int billCount)
    {
        using var origin = new GovernedApiFactory();
        var sender = origin.CreateClient();

        var generated = await sender.PostAsJsonAsync("/api/v1/bills/batch-generate", new
        {
            BillCount = billCount,
            JurisdictionState = "OH",
            MedicalCodeCategories = new[] { "Cardiac", "Anesthesia" },
        });
        Assert.Equal(HttpStatusCode.Created, generated.StatusCode);

        var before = JsonDocument.Parse(await generated.Content.ReadAsStringAsync()).RootElement.Clone();
        var archive = await sender.GetByteArrayAsync("/api/v1/claims/export-zip");

        using var destination = new GovernedApiFactory();
        var receiver = destination.CreateClient();

        var imported = await UploadAsync(receiver, archive, "batch.zip");
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);

        var after = JsonDocument.Parse(await imported.Content.ReadAsStringAsync()).RootElement.Clone();

        Assert.Equal(before.GetArrayLength(), after.GetArrayLength());

        foreach (var original in before.EnumerateArray())
        {
            var control = original.GetProperty("CLM01_ClaimControlNumber").GetString();
            var rebuilt = after.EnumerateArray()
                .Single(claim => claim.GetProperty("CLM01_ClaimControlNumber").GetString() == control);

            AssertGovernedFieldsMatch(original, rebuilt, $"claim {control}");

            var originalLines = original.GetProperty("LineItems").EnumerateArray().ToList();
            var rebuiltLines = rebuilt.GetProperty("LineItems").EnumerateArray().ToList();

            Assert.Equal(originalLines.Count, rebuiltLines.Count);

            foreach (var line in originalLines)
            {
                var number = line.GetProperty("LX01_AssignedLineNumber").GetInt32();
                var counterpart = rebuiltLines
                    .Single(candidate => candidate.GetProperty("LX01_AssignedLineNumber").GetInt32() == number);

                AssertGovernedFieldsMatch(line, counterpart, $"claim {control} line {number}");
            }
        }
    }

    /// <summary>
    /// 837 to bill and back, which is the governed standard stated literally:
    /// Import(837) then Export() reproduces the file that went in.
    /// </summary>
    /// <remarks>
    /// The interchange is produced by the serializer rather than hand-written, because the standard
    /// is about a file this system emits. A file from elsewhere can preserve every governed field
    /// and still differ in delimiters or line endings, which is a different claim and one the
    /// import table is careful not to make.
    /// </remarks>
    [Theory]
    [InlineData(1, 3)]
    [InlineData(4, 1)]
    [InlineData(9, 5)]
    public async Task Interchange_survives_the_journey_into_a_bill_and_back_out(int index, int lineCount)
    {
        using var host = new GovernedApiFactory();
        var client = host.CreateClient();

        var original = new Edi837Serializer().Serialize(GovernedClaimCorpus.Build(index, lineCount));

        var imported = await UploadAsync(client, Encoding.UTF8.GetBytes(original), "claim.837");
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);

        var archive = await client.GetByteArrayAsync("/api/v1/claims/export-zip");
        var regenerated = ClaimArchive.Unpack(archive);

        Assert.Single(regenerated);
        Assert.Equal(original, regenerated[0]);
    }

    /// <summary>
    /// Every property the contract publishes, compared. Reading the field list from the contract
    /// rather than naming fields here means a column added to the governed schema is compared
    /// automatically, instead of being silently exempt until someone remembers to add it.
    /// </summary>
    private static void AssertGovernedFieldsMatch(JsonElement before, JsonElement after, string what)
    {
        foreach (var property in before.EnumerateObject())
        {
            if (NotCarriedBy837.Contains(property.Name)) continue;
            if (property.Name == "LineItems") continue;

            Assert.True(after.TryGetProperty(property.Name, out var counterpart),
                $"{what}: '{property.Name}' is missing after the round trip.");

            Assert.Equal(property.Value.ToString(), counterpart.ToString());
        }
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] payload, string fileName)
    {
        var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        return await client.PostAsync("/api/v1/claims/import", new MultipartFormDataContent { { content, "file", fileName } });
    }
}
