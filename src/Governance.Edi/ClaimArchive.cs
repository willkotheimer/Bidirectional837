using System.IO.Compression;
using System.Text;
using Governance.Domain.Entities;

namespace Governance.Edi;

/// <summary>
/// Governance User Story 2.2: packages serialised 837 transactions into a ZIP archive for
/// transport.
/// </summary>
/// <remarks>
/// PROVENANCE: ADR-017 - one 837 file per claim, named for its governed claim control number.
/// </remarks>
public sealed class ClaimArchive
{
    /// <summary>
    /// PROVENANCE: ADR-014 - entry timestamps are fixed rather than taken from the clock, so the
    /// same claims package to the same bytes. An archive stamped with the wall clock could never be
    /// compared against an earlier export to show that nothing had changed.
    /// </summary>
    private static readonly DateTimeOffset FixedEntryTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A byte order mark would sit in front of the ISA segment, where a reader locating the
    /// delimiters by offset would read the wrong characters as delimiters.
    /// </summary>
    private static readonly UTF8Encoding EntryEncoding = new(encoderShouldEmitUTF8Identifier: false);

    public ClaimArchive(Edi837Serializer serializer) => Serializer = serializer;

    public Edi837Serializer Serializer { get; }

    /// <summary>The archive entry name for a claim.</summary>
    /// <remarks>
    /// The control number leads, because it is what a user opening the archive looks for. The
    /// storage identity follows it, because two claims may legitimately share a control number - a
    /// replacement claim carries the original's CLM01 under a different CLM05-3 - and neither may
    /// overwrite the other. Anything the control number carries that a filesystem would read as
    /// structure is replaced: an archive is extracted onto someone else's machine.
    /// </remarks>
    public static string EntryNameFor(ClaimHeader claim)
    {
        ArgumentNullException.ThrowIfNull(claim);

        var safe = new string(claim.CLM01_ClaimControlNumber
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '_')
            .ToArray());

        return $"{safe}-{claim.Id:N}.837";
    }

    /// <summary>
    /// True if the payload is a ZIP archive rather than a bare 837 file.
    /// </summary>
    /// <remarks>
    /// Read from the payload's own local file header signature, not from a filename or a declared
    /// content type. Governance User Story 3.1 accepts "an 837 file or a .zip of 837 files" through
    /// one route, and an uploaded file's name is whatever the client chose to call it.
    /// </remarks>
    public static bool LooksLikeZipArchive(ReadOnlySpan<byte> payload) =>
        payload.Length >= 4 && payload[0] == 0x50 && payload[1] == 0x4B && payload[2] == 0x03 && payload[3] == 0x04;

    /// <summary>The 837 text of every entry in a ZIP archive, in the order the archive holds them.</summary>
    public static IReadOnlyList<string> Unpack(byte[] package)
    {
        ArgumentNullException.ThrowIfNull(package);

        try
        {
            using var buffer = new MemoryStream(package, writable: false);
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

            return archive.Entries.Select(entry =>
            {
                using var reader = new StreamReader(entry.Open(), EntryEncoding);
                return reader.ReadToEnd();
            }).ToList();
        }
        catch (InvalidDataException failure)
        {
            throw new EdiFormatException(
                $"The payload could not be read as a ZIP archive: {failure.Message}", failure);
        }
    }

    /// <summary>The ZIP archive holding one 837 file per claim, in the order given.</summary>
    public byte[] Package(IReadOnlyList<ClaimHeader> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var claim in claims)
            {
                var entry = archive.CreateEntry(EntryNameFor(claim), CompressionLevel.Optimal);
                entry.LastWriteTime = FixedEntryTimestamp;

                using var stream = entry.Open();
                using var writer = new StreamWriter(stream, EntryEncoding);
                writer.Write(Serializer.Serialize(claim));
            }
        }

        return buffer.ToArray();
    }
}
