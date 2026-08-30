using System.Text;
using Governance.Api.Mapping;
using Governance.Contracts.DTOs;
using Governance.Domain.Entities;
using Governance.Domain.Persistence;
using Governance.Edi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Governance.Api.Controllers;

/// <summary>
/// PROVENANCE: GOVERNANCE-4 - contracts and controllers are fully defined before the service logic
/// beneath them. Every operation below published its route, parameters and response codes before
/// the engine beneath it existed, and answered 501 rather than 404 until that engine arrived. None
/// answers 501 any longer: export arrived with governance Feature 2, import and reversibility
/// verification with Feature 3.
///
/// PROVENANCE: ADR-009 - governance names only the export route in this controller. The list,
/// fetch, import and verify routes are additions recorded in the register; no governed route is
/// changed, renamed or removed.
/// </summary>
/// <remarks>
/// PROVENANCE: FIND-016 - this controller does not carry a class-level [Produces]. That attribute
/// is a result filter, not documentation: it forces its media type onto every response including
/// the problem documents, which the published contract declares as application/problem+json. The
/// response content types are documented by [ProducesResponseType] instead, which describes without
/// overriding.
/// </remarks>
[ApiController]
[Route("api/v1/claims")]
public class ClaimsController : ControllerBase
{
    private readonly EphemeralClaimStore _store;
    private readonly ClaimArchive _archive;
    private readonly Edi837Parser _parser;
    private readonly ReversibilityVerifier _verifier;

    public ClaimsController(
        EphemeralClaimStore store,
        ClaimArchive archive,
        Edi837Parser parser,
        ReversibilityVerifier verifier)
    {
        _store = store;
        _archive = archive;
        _parser = parser;
        _verifier = verifier;
    }

    /// <summary>Lists stored claims for the Imported Bills Dashboard (governance User Story 3.1).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ClaimHeaderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListClaims(CancellationToken cancellationToken)
    {
        await using var context = _store.CreateContext();

        var claims = await context.Claims
            .Include(claim => claim.LineItems)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return Ok(claims.Select(ClaimMapper.ToDto).ToList());
    }

    /// <summary>Retrieves a single stored claim.</summary>
    /// <remarks>
    /// The guid route constraint keeps this template from capturing the literal "export-zip"
    /// segment, so both routes can sit directly beneath /api/v1/claims as the contract publishes.
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ClaimHeaderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClaim(Guid id, CancellationToken cancellationToken)
    {
        await using var context = _store.CreateContext();

        var claim = await context.Claims
            .Include(header => header.LineItems)
            .AsNoTracking()
            .SingleOrDefaultAsync(header => header.Id == id, cancellationToken);

        return claim is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: $"No claim {id} is held in the store.")
            : Ok(ClaimMapper.ToDto(claim));
    }

    /// <summary>
    /// Downloads stored claims as 837 files in a ZIP archive (governance User Story 2.2).
    /// </summary>
    /// <remarks>
    /// PROVENANCE: ADR-017 - the archive holds one 837 file per claim. Claims are ordered by their
    /// governed control number so that the same stored claims export to the same archive whatever
    /// order the store returns them in.
    ///
    /// The contract publishes 200 as this operation's only response, so an export of an empty store
    /// is an empty archive rather than an error: nothing has gone wrong, there is simply nothing
    /// held yet.
    /// </remarks>
    [HttpGet("export-zip")]
    [Produces("application/zip")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportZip(CancellationToken cancellationToken)
    {
        await using var context = _store.CreateContext();

        var claims = await context.Claims
            .Include(claim => claim.LineItems)
            .AsNoTracking()
            .OrderBy(claim => claim.CLM01_ClaimControlNumber)
            .ThenBy(claim => claim.Id)
            .ToListAsync(cancellationToken);

        return File(_archive.Package(claims), "application/zip", "claims-837.zip");
    }

    /// <summary>
    /// Ingests an 837 file, or a ZIP archive of them (governance User Story 3.1).
    /// </summary>
    /// <remarks>
    /// PROVENANCE: ADR-022 - the whole payload is read and parsed before anything is stored, and a
    /// single unreadable file rejects the upload entire. A partially applied batch is the worst
    /// outcome available: the store would hold claims the sender never successfully sent, and they
    /// would export as valid 837 files indistinguishable from the rest.
    /// </remarks>
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(List<ClaimHeaderDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: "No file was uploaded. Send one 837 file, or a ZIP archive of them, as 'file'.");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var payload = buffer.ToArray();

        List<ClaimHeader> claims;
        try
        {
            var interchanges = ClaimArchive.LooksLikeZipArchive(payload)
                ? ClaimArchive.Unpack(payload)
                : [Encoding.UTF8.GetString(payload)];

            claims = interchanges.Select(_parser.Parse).ToList();
        }
        catch (EdiFormatException refusal)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: refusal.Message);
        }

        if (claims.Count == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                detail: "The uploaded archive contains no 837 files.");
        }

        await using var context = _store.CreateContext();
        context.Claims.AddRange(claims);
        await context.SaveChangesAsync(cancellationToken);

        // Read back what was written rather than returning the parsed objects. The store is where
        // FIND-001 and FIND-002 were found, and a response built from the objects that went in
        // would report a mutation the store introduced as though it had not happened.
        var stored = await context.Claims
            .Include(claim => claim.LineItems)
            .AsNoTracking()
            .Where(claim => claims.Select(created => created.Id).Contains(claim.Id))
            .ToListAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, stored.Select(ClaimMapper.ToDto).ToList());
    }

    /// <summary>
    /// Proves zero-mutation round-tripping for a stored claim (governance User Story 3.2).
    /// </summary>
    /// <remarks>
    /// PROVENANCE: GOVERNANCE-1 - the Zero-Mutation Rule, answered for one claim. The claim is read
    /// from the store rather than taken from the request, so the round trip measured is the one the
    /// guarantee is about: what the database holds, re-exported and read back.
    /// </remarks>
    [HttpPost("{id:guid}/verify-reversibility")]
    [ProducesResponseType(typeof(ReversibilityReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyReversibility(Guid id, CancellationToken cancellationToken)
    {
        await using var context = _store.CreateContext();

        var claim = await context.Claims
            .Include(header => header.LineItems)
            .AsNoTracking()
            .SingleOrDefaultAsync(header => header.Id == id, cancellationToken);

        if (claim is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound,
                detail: $"No claim {id} is held in the store.");
        }

        var verdict = _verifier.Verify(claim);

        return Ok(new ReversibilityReportDto(
            claim.Id,
            verdict.EdiTextIsIdentical,
            verdict.RecordIsIdentical,
            verdict.Differences.ToList()));
    }
}
