using System.Text;
using Governance.Domain.Entities;
using Governance.TestSupport;

namespace Governance.Edi.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - governance Feature 3, User Story 3.1: the ingestion engine accepts
/// "an incoming 837 file or a .zip of 837 files". Reading the archive back is the inverse of the
/// User Story 2.2 packaging, and the two are asserted against each other here rather than
/// separately.
/// </summary>
public class ArchiveIngestionTheories
{
    private static readonly Edi837Serializer Serializer = new();
    private static readonly Edi837Parser Parser = new();
    private static readonly ClaimArchive Archive = new(Serializer);

    public static IEnumerable<object[]> BatchSizes() => [[1], [2], [7], [25]];

    private static IReadOnlyList<ClaimHeader> Batch(int size) =>
        Enumerable.Range(1, size).Select(index => GovernedClaimCorpus.Build(index, 1 + (index % 5))).ToList();

    [Theory]
    [MemberData(nameof(BatchSizes))]
    public void Every_packaged_interchange_comes_back_out_of_the_archive(int size)
    {
        var claims = Batch(size);

        var unpacked = ClaimArchive.Unpack(Archive.Package(claims));

        Assert.Equal(size, unpacked.Count);
        Assert.Equal(
            claims.Select(Serializer.Serialize).OrderBy(text => text, StringComparer.Ordinal),
            unpacked.OrderBy(text => text, StringComparer.Ordinal));
    }

    /// <summary>
    /// The whole loop at the archive level: package a batch, unpack it, parse each file, and find
    /// the claims that went in. This is governance User Story 2.2 and User Story 3.1 meeting.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchSizes))]
    public void Claims_survive_packaging_and_ingestion_as_a_batch(int size)
    {
        var claims = Batch(size);

        var recovered = ClaimArchive.Unpack(Archive.Package(claims)).Select(Parser.Parse).ToList();

        Assert.Equal(size, recovered.Count);

        foreach (var original in claims)
        {
            var match = recovered.Single(claim =>
                claim.BHT03_ClaimSubmitterTransactionId == original.BHT03_ClaimSubmitterTransactionId);

            Assert.Empty(ReversibilityVerifier.Differences(original, match));
        }
    }

    /// <summary>
    /// A payload is recognised as an archive by its own local file header signature, not by a
    /// filename or a declared content type. The route accepts both shapes, and a client's choice
    /// of filename is not evidence of anything.
    /// </summary>
    [Fact]
    public void Archive_is_recognised_by_its_signature_and_a_bare_interchange_is_not()
    {
        var package = Archive.Package(Batch(2));
        var bare = Encoding.UTF8.GetBytes(Serializer.Serialize(GovernedClaimCorpus.Build(1, 3)));

        Assert.True(ClaimArchive.LooksLikeZipArchive(package));
        Assert.False(ClaimArchive.LooksLikeZipArchive(bare));
        Assert.False(ClaimArchive.LooksLikeZipArchive(ReadOnlySpan<byte>.Empty));
        Assert.False(ClaimArchive.LooksLikeZipArchive("PK"u8));
    }

    [Fact]
    public void Payload_that_is_not_a_readable_archive_is_refused()
    {
        var notAnArchive = Encoding.UTF8.GetBytes("PK and then nothing that follows the format");

        Assert.Throws<EdiFormatException>(() => ClaimArchive.Unpack(notAnArchive));
    }
}
