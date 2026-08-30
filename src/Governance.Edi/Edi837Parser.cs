using Governance.Domain.Entities;

namespace Governance.Edi;

/// <summary>
/// Raised when an interchange cannot be read as a governed 837 Professional claim.
/// </summary>
/// <remarks>
/// It carries a message naming what was wrong rather than where the reader gave up, because
/// governance User Story 3.1 requires malformed files to be handled and the person handling one
/// needs to know which segment to look at.
/// </remarks>
public sealed class EdiFormatException : Exception
{
    public EdiFormatException(string message) : base(message) { }

    public EdiFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Governance User Story 3.1: reads an ASC X12 837 Professional interchange back into the governed
/// <see cref="ClaimHeader"/> entity, mapping each element to the Section 2 column named for it.
/// </summary>
/// <remarks>
/// PROVENANCE: ADR-021 - the reader refuses anything it cannot map exactly, rather than salvaging
/// what it can. A partially read claim reaches the store and re-exports as a well-formed 837 that
/// says something the sender never said, which is a Zero-Mutation breach introduced by the import
/// itself.
/// </remarks>
public sealed class Edi837Parser
{
    /// <summary>ISA is fixed width: 105 characters before its terminator.</summary>
    private const int InterchangeHeaderLength = 105;

    /// <summary>Offsets the standard fixes so the delimiters can be found before they are known.</summary>
    private const int ElementSeparatorOffset = 3;
    private const int RepetitionSeparatorOffset = 82;
    private const int ComponentSeparatorOffset = 104;

    /// <summary>The claim carried by one interchange.</summary>
    public ClaimHeader Parse(string interchange)
    {
        var delimiters = ReadDelimiters(interchange);
        var segments = ReadSegments(interchange, delimiters);

        RequireEnvelopeBalances(segments);
        RequireProfessionalClaim(segments);

        var claim = ReadClaim(segments, delimiters);

        RequireTotalMatchesItsLines(claim);

        return claim;
    }

    /// <summary>
    /// The delimiters an interchange declares, read positionally out of its own ISA segment. The
    /// standard fixes these offsets precisely so that a reader can find them before it knows them,
    /// which is why a truncated ISA cannot be read at all rather than read cautiously.
    /// </summary>
    private static X12Delimiters ReadDelimiters(string interchange)
    {
        if (string.IsNullOrWhiteSpace(interchange))
        {
            throw new EdiFormatException("The payload is empty; an 837 interchange begins with an ISA segment.");
        }

        if (!interchange.StartsWith("ISA", StringComparison.Ordinal))
        {
            throw new EdiFormatException(
                "The payload does not begin with an ISA segment, so it is not an X12 interchange.");
        }

        if (interchange.Length <= InterchangeHeaderLength)
        {
            throw new EdiFormatException(
                $"The ISA segment is {interchange.Length} characters; the standard fixes it at " +
                $"{InterchangeHeaderLength} plus its terminator. A truncated header cannot declare its delimiters.");
        }

        return new X12Delimiters(
            Element: interchange[ElementSeparatorOffset],
            Component: interchange[ComponentSeparatorOffset],
            Repetition: interchange[RepetitionSeparatorOffset],
            Segment: interchange[InterchangeHeaderLength]);
    }

    /// <summary>
    /// Splits the interchange into segments. Whitespace between a terminator and the next segment
    /// identifier is discarded: a writer may wrap segments onto separate lines for readability, and
    /// a file that has crossed platforms may have had its line endings rewritten in transit.
    /// </summary>
    private static List<ParsedSegment> ReadSegments(string interchange, X12Delimiters delimiters)
    {
        var segments = new List<ParsedSegment>();

        foreach (var raw in interchange.Split(delimiters.Segment))
        {
            var text = raw.Trim('\r', '\n', ' ');
            if (text.Length == 0) continue;

            var elements = text.Split(delimiters.Element);
            segments.Add(new ParsedSegment(elements[0], elements[1..], delimiters));
        }

        return segments;
    }

    /// <summary>
    /// The checks a clearinghouse makes before it reads a claim field. A file that fails one of
    /// these has been truncated, concatenated or edited in transit, and reading it as though it
    /// were whole is how half a file becomes a whole claim.
    /// </summary>
    private static void RequireEnvelopeBalances(List<ParsedSegment> segments)
    {
        var isa = Only(segments, "ISA");
        var gs = Only(segments, "GS");
        var st = Only(segments, "ST");
        var se = Only(segments, "SE");
        var ge = Only(segments, "GE");
        var iea = Only(segments, "IEA");

        Require(isa[13] == iea[2],
            $"IEA02 is '{iea[2]}' but ISA13 is '{isa[13]}'; the interchange control numbers disagree.");
        Require(gs[6] == ge[2],
            $"GE02 is '{ge[2]}' but GS06 is '{gs[6]}'; the group control numbers disagree.");
        Require(st[2] == se[2],
            $"SE02 is '{se[2]}' but ST02 is '{st[2]}'; the transaction control numbers disagree.");

        var declaredGroups = Number(iea[1], "IEA01");
        var actualGroups = segments.Count(segment => segment.Id == "GS");
        Require(declaredGroups == actualGroups,
            $"IEA01 declares {declaredGroups} functional group(s); the interchange carries {actualGroups}.");

        var declaredSets = Number(ge[1], "GE01");
        var actualSets = segments.Count(segment => segment.Id == "ST");
        Require(declaredSets == actualSets,
            $"GE01 declares {declaredSets} transaction set(s); the group carries {actualSets}.");

        var start = segments.FindIndex(segment => segment.Id == "ST");
        var end = segments.FindIndex(segment => segment.Id == "SE");
        Require(end > start, "The SE segment precedes the ST segment it is meant to close.");

        var declaredSegments = Number(se[1], "SE01");
        var actualSegments = end - start + 1;
        Require(declaredSegments == actualSegments,
            $"SE01 declares {declaredSegments} segments; the transaction set contains {actualSegments}.");
    }

    private static void RequireProfessionalClaim(List<ParsedSegment> segments)
    {
        var gs = Only(segments, "GS");
        var st = Only(segments, "ST");

        Require(gs[1] == "HC",
            $"GS01 is '{gs[1]}'; a health care claim interchange declares HC.");
        Require(st[1] == "837",
            $"ST01 is '{st[1]}'; this reader handles transaction set 837 only.");
        Require(st[3] == Edi837Serializer.ImplementationGuide,
            $"ST03 is '{st[3]}'; this reader handles {Edi837Serializer.ImplementationGuide} only.");
    }

    /// <summary>Maps the segments onto the governed Section 2 columns, one for one.</summary>
    private static ClaimHeader ReadClaim(List<ParsedSegment> segments, X12Delimiters delimiters)
    {
        var bht = Only(segments, "BHT");
        var clm = Only(segments, "CLM");
        var hi = Only(segments, "HI");
        var dmg = Only(segments, "DMG");
        var n3 = Only(segments, "N3");
        var n4 = Only(segments, "N4");

        var levels = segments.Where(segment => segment.Id == "HL").ToList();
        Require(levels.Count >= 2,
            $"The transaction carries {levels.Count} HL segment(s); an 837 claim declares a billing " +
            "provider level and a subscriber level beneath it.");

        var billing = Party(segments, "85");
        var subscriber = Party(segments, "IL");
        var payer = Party(segments, "PR");

        var claim = new ClaimHeader
        {
            BHT03_ClaimSubmitterTransactionId = bht[3],
            BHT04_TransactionSetCreationDate = ReadCreationDate(bht),

            Loop2010AA_NM103_BillingProviderLastNameOrOrg = billing[3],
            // An absent element and an empty one are the same absence in X12, and the governed
            // column is nullable, so both become null.
            Loop2010AA_NM104_BillingProviderFirstName = billing[4].Length == 0 ? null : billing[4],
            Loop2010AA_NM109_BillingProviderNpi = billing[9],
            Loop2010AA_N301_BillingProviderAddressLine = n3[1],
            Loop2010AA_N401_BillingProviderCity = n4[1],
            Loop2010AA_N402_BillingProviderState = n4[2],
            Loop2010AA_N403_BillingProviderZipCode = n4[3],

            Loop2010BA_NM103_SubscriberLastName = subscriber[3],
            Loop2010BA_NM104_SubscriberFirstName = subscriber[4],
            Loop2010BA_DMG02_SubscriberDob = ReadBirthDate(dmg),
            Loop2010BA_DMG03_SubscriberGender = dmg[3],

            Loop2010BB_NM103_PayerName = payer[3],
            Loop2010BB_NM109_PayerId = payer[9],

            CLM01_ClaimControlNumber = clm[1],
            CLM02_TotalClaimChargeAmount = Amount(clm[2], 2, "CLM02"),
            CLM05_1_PlaceOfServiceCode = clm.Component(5, 1),
            CLM05_3_ClaimFrequencyCode = clm.Component(5, 3),
            HI01_2_PrincipalDiagnosisCode = ReadDiagnosis(hi),
        };

        ReadServiceLines(segments, claim);

        return claim;
    }

    /// <summary>
    /// The service line trio is read positionally, and each LX must be answered by exactly one SV1
    /// and one DTP. Reading the three segment types as independent lists and zipping them would
    /// pair line 2's charge with line 3's date in any file where one segment is missing, producing
    /// a claim that is internally consistent and wrong.
    /// </summary>
    private static void ReadServiceLines(List<ParsedSegment> segments, ClaimHeader claim)
    {
        for (var index = 0; index < segments.Count; index++)
        {
            if (segments[index].Id != "LX") continue;

            var lx = segments[index];
            Require(index + 2 < segments.Count && segments[index + 1].Id == "SV1" && segments[index + 2].Id == "DTP",
                $"The service line at LX01 '{lx[1]}' is not followed by an SV1 and a DTP segment.");

            var sv1 = segments[index + 1];
            var dtp = segments[index + 2];

            Require(dtp[1] == "472", $"DTP01 is '{dtp[1]}'; a service line carries date qualifier 472.");
            Require(dtp[2] == "D8", $"DTP02 is '{dtp[2]}'; the governed service date column holds CCYYMMDD, which is D8.");

            var unitOfMeasure = sv1[3];
            Require(unitOfMeasure.Length is > 0 and <= 2,
                $"SV103 is '{unitOfMeasure}'; the governed unit of measure column holds two characters.");

            claim.LineItems.Add(new ClaimLineItem
            {
                LX01_AssignedLineNumber = Number(lx[1], "LX01"),
                SV101_2_ProcedureCode = sv1.Component(1, 2),
                SV102_LineItemChargeAmount = Amount(sv1[2], 2, "SV102"),
                SV103_UnitOfMeasure = unitOfMeasure,
                SV104_ServiceUnitCount = Amount(sv1[4], 4, "SV104"),
                DTP03_ServiceDate = ReadServiceDate(dtp),
            });
        }

        Require(claim.LineItems.Count > 0,
            "The claim carries no service lines. A claim with no LX segment bills nothing, and its " +
            "CLM02 total would satisfy the sum invariant only vacuously.");
    }

    /// <summary>
    /// PROVENANCE: GOVERNANCE-1 - CLM02 and the SV102 amounts state the same fact twice, and
    /// governance User Story 1.2 requires them to agree. A file in which they disagree cannot be
    /// stored without choosing which of the two to believe, and either choice is a mutation the
    /// import introduced rather than found.
    /// </summary>
    private static void RequireTotalMatchesItsLines(ClaimHeader claim)
    {
        var lineSum = claim.LineItems.Sum(line => line.SV102_LineItemChargeAmount);

        Require(claim.CLM02_TotalClaimChargeAmount == lineSum,
            $"CLM02 is {claim.CLM02_TotalClaimChargeAmount} but the SV102 line amounts sum to {lineSum}. " +
            "The file contradicts itself.");
    }

    private static DateTime ReadCreationDate(ParsedSegment bht)
    {
        var date = Date(bht[4], "BHT04");
        var time = bht[5];

        Require(time.Length == 4 && time.All(char.IsAsciiDigit), $"BHT05 is '{time}'; the standard writes HHMM.");
        Require(int.Parse(time[..2]) < 24 && int.Parse(time[2..]) < 60, $"BHT05 is '{time}', which is not a time.");

        return date.AddHours(int.Parse(time[..2])).AddMinutes(int.Parse(time[2..]));
    }

    private static string ReadBirthDate(ParsedSegment dmg)
    {
        Require(dmg[1] == "D8",
            $"DMG01 is '{dmg[1]}'; the governed subscriber date of birth column holds CCYYMMDD, which is D8.");

        Date(dmg[2], "DMG02");
        return dmg[2];
    }

    private static string ReadServiceDate(ParsedSegment dtp)
    {
        Date(dtp[3], "DTP03");
        return dtp[3];
    }

    private static string ReadDiagnosis(ParsedSegment hi)
    {
        Require(hi.Component(1, 1) == "ABK",
            $"HI01-1 is '{hi.Component(1, 1)}'; the principal ICD-10 diagnosis carries qualifier ABK.");

        try
        {
            return Icd10Code.FromX12(hi.Component(1, 2));
        }
        catch (FormatException failure)
        {
            throw new EdiFormatException($"HI01-2 is not an ICD-10-CM code: {failure.Message}", failure);
        }
    }

    /// <summary>The single NM1 segment for an entity identifier code.</summary>
    private static ParsedSegment Party(List<ParsedSegment> segments, string entityIdentifier)
    {
        var matches = segments.Where(segment => segment.Id == "NM1" && segment[1] == entityIdentifier).ToList();

        Require(matches.Count == 1,
            $"The transaction carries {matches.Count} NM1 segment(s) for entity identifier " +
            $"'{entityIdentifier}'; exactly one is required.");

        return matches[0];
    }

    private static ParsedSegment Only(List<ParsedSegment> segments, string segmentId)
    {
        var matches = segments.Where(segment => segment.Id == segmentId).ToList();

        Require(matches.Count == 1,
            $"The interchange carries {matches.Count} {segmentId} segment(s); exactly one is required.");

        return matches[0];
    }

    private static decimal Amount(string element, int governedScale, string position)
    {
        try
        {
            return X12Number.Parse(element, governedScale);
        }
        catch (FormatException failure)
        {
            throw new EdiFormatException($"{position} cannot be read as a governed amount: {failure.Message}", failure);
        }
    }

    private static int Number(string element, string position)
    {
        Require(int.TryParse(element, out var value), $"{position} is '{element}', which is not a whole number.");
        return int.Parse(element);
    }

    /// <summary>Validates a CCYYMMDD element and returns it as a date.</summary>
    private static DateTime Date(string element, string position)
    {
        Require(
            DateTime.TryParseExact(element, "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var value),
            $"{position} is '{element}', which is not a CCYYMMDD date.");

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static void Require(bool condition, string explanation)
    {
        if (!condition) throw new EdiFormatException(explanation);
    }

    /// <summary>One segment of the interchange being read.</summary>
    private sealed record ParsedSegment(string Id, string[] Elements, X12Delimiters Delimiters)
    {
        /// <summary>Element by its one-based X12 position; an element past the end is absent, not an error.</summary>
        public string this[int position] =>
            position >= 1 && position <= Elements.Length ? Elements[position - 1] : string.Empty;

        public string Component(int position, int component)
        {
            var parts = this[position].Split(Delimiters.Component);
            return component >= 1 && component <= parts.Length ? parts[component - 1] : string.Empty;
        }
    }
}
