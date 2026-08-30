using System.IO.Compression;
using System.Text;
using Governance.Domain.Entities;
using Governance.TestSupport;

namespace Governance.Edi.Tests;

/// <summary>
/// PROVENANCE: GOVERNANCE-5 - governance Feature 2, User Story 2.2: "As a User, I want to download
/// an .837 batch as a .zip archive ... so that I can transport files externally." Acceptance
/// criterion: "Zip file contains valid individual or batched 837 files matching the database
/// records."
///
/// "Matching the database records" is read strictly: an entry must be byte-identical to what the
/// serializer produces for the record it names. An archive that merely resembles the records would
/// satisfy a looser reading and defeat the Section 1 Reversibility Guarantee, since the archive is
/// the artefact that actually leaves the system.
/// </summary>
public class ClaimArchiveTheories
{
    private static readonly Edi837Serializer Serializer = new();
    private static readonly ClaimArchive Archive = new(Serializer);

    /// <summary>Batch sizes spanning the empty archive, one claim, and a many-claim batch.</summary>
    public static IEnumerable<object[]> BatchSizes() => [[0], [1], [2], [7], [25]];

    private static IReadOnlyList<ClaimHeader> Batch(int size) =>
        Enumerable.Range(1, size).Select(index => GovernedClaimCorpus.Build(index, 1 + (index % 5))).ToList();

    [Theory]
    [MemberData(nameof(BatchSizes))]
    public void Archive_holds_one_entry_for_every_claim(int size)
    {
        var claims = Batch(size);

        using var archive = Open(Archive.Package(claims));

        Assert.Equal(size, archive.Entries.Count);
        Assert.Equal(size, archive.Entries.Select(entry => entry.FullName).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(BatchSizes))]
    public void Every_entry_is_byte_identical_to_the_serialised_record_it_names(int size)
    {
        var claims = Batch(size);

        using var archive = Open(Archive.Package(claims));

        foreach (var claim in claims)
        {
            var entry = archive.GetEntry(ClaimArchive.EntryNameFor(claim));

            Assert.NotNull(entry);
            Assert.Equal(Serializer.Serialize(claim), ReadText(entry));
        }
    }

    /// <summary>
    /// Entry names are derived from the governed claim control number, so a user opening the
    /// archive can find the claim they are looking for, and carry the .837 extension the user
    /// story names.
    /// </summary>
    [Theory]
    [MemberData(nameof(ClaimIndices))]
    public void Entry_name_carries_the_governed_claim_control_number_and_the_837_extension(
        int index, int lineCount)
    {
        var claim = GovernedClaimCorpus.Build(index, lineCount);
        var name = ClaimArchive.EntryNameFor(claim);

        Assert.EndsWith(".837", name, StringComparison.Ordinal);
        Assert.Contains(claim.CLM01_ClaimControlNumber, name, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> ClaimIndices() => GovernedClaimCorpus.ClaimIndices();

    /// <summary>
    /// An archive is extracted onto someone else's filesystem. A control number carrying a path
    /// separator, a traversal, or a character the filesystem reserves must not become a path.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("A/B")]
    [InlineData("C:WINDOWS")]
    [InlineData("claim?name")]
    [InlineData("..")]
    public void Entry_name_is_a_plain_file_name_whatever_the_control_number_carries(string controlNumber)
    {
        var claim = GovernedClaimCorpus.Build(1);
        claim.CLM01_ClaimControlNumber = controlNumber;

        var name = ClaimArchive.EntryNameFor(claim);

        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain(':', name);
        Assert.False(name.StartsWith('.'), $"'{name}' would extract as a hidden or relative path.");
        Assert.Equal(name, Path.GetFileName(name));
        Assert.All(Path.GetInvalidFileNameChars(), invalid => Assert.DoesNotContain(invalid, name));
    }

    /// <summary>
    /// Two claims may legitimately share a control number: a replacement claim carries the
    /// original's CLM01 under a different CLM05-3 frequency code. Neither may overwrite the other
    /// in the archive.
    /// </summary>
    [Fact]
    public void Claims_sharing_a_control_number_still_get_distinct_entries()
    {
        var original = GovernedClaimCorpus.Build(1);
        var replacement = GovernedClaimCorpus.Build(2);
        replacement.CLM01_ClaimControlNumber = original.CLM01_ClaimControlNumber;
        replacement.CLM05_3_ClaimFrequencyCode = "7";

        using var archive = Open(Archive.Package([original, replacement]));

        Assert.Equal(2, archive.Entries.Count);
        Assert.NotEqual(ClaimArchive.EntryNameFor(original), ClaimArchive.EntryNameFor(replacement));
        Assert.Equal(Serializer.Serialize(original), ReadText(archive.GetEntry(ClaimArchive.EntryNameFor(original))!));
        Assert.Equal(Serializer.Serialize(replacement), ReadText(archive.GetEntry(ClaimArchive.EntryNameFor(replacement))!));
    }

    /// <summary>
    /// PROVENANCE: ADR-014 - the reproducibility the generator promises has to survive packaging.
    /// A ZIP that stamped the wall clock into its entries would differ on every export of the same
    /// claims, so an archive could never be compared against an earlier one to show that nothing
    /// changed.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchSizes))]
    public void Packaging_the_same_claims_twice_yields_the_same_bytes(int size)
    {
        var claims = Batch(size);

        Assert.Equal(Archive.Package(claims), Archive.Package(claims));
    }

    /// <summary>
    /// Entries are UTF-8 without a byte order mark. A BOM would sit in front of the ISA segment,
    /// where a reader expecting a fixed-width header finds the delimiters by offset and would read
    /// the wrong characters as delimiters.
    /// </summary>
    [Theory]
    [MemberData(nameof(BatchSizes))]
    public void Entries_carry_no_byte_order_mark_before_the_ISA_segment(int size)
    {
        using var archive = Open(Archive.Package(Batch(size)));

        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);

            var bytes = memory.ToArray();
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                $"{entry.FullName} begins with a UTF-8 byte order mark.");
            Assert.Equal("ISA", Encoding.UTF8.GetString(bytes, 0, 3));
        }
    }

    private static ZipArchive Open(byte[] package) =>
        new(new MemoryStream(package), ZipArchiveMode.Read);

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
