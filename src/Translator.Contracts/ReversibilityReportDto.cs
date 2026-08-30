// PROVENANCE: ADR-009 - a response contract with no counterpart in governance Section 3. It carries
// no claim field; it reports a verdict about a claim. User Story 3.2 specifies the assertion to be
// made without specifying how the result is surfaced, so the shape is recorded in the register.

namespace Translator.Contracts.DTOs;

/// <summary>
/// The verdict of a zero-mutation round trip: export the stored claim to 837, re-import the result,
/// and compare both the emitted text and the reconstructed record against the originals.
/// </summary>
public record ReversibilityReportDto(
    Guid ClaimId,
    bool EdiTextIsIdentical,
    bool RecordIsIdentical,
    List<string> Differences
);
