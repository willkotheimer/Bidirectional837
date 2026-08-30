using Governance.Api.Mapping;
using Governance.Contracts.DTOs;
using Governance.Domain.Persistence;
using Governance.Edi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Governance.Api.Controllers;

/// <summary>
/// PROVENANCE: GOVERNANCE-4 - contracts and controllers are fully defined before the service logic
/// beneath them. Every operation below published its route, parameters and response codes before
/// the engine beneath it existed. The export engine has since arrived with governance Feature 2;
/// import and reversibility verification are Feature 3 deliverables and answer 501 until then,
/// never 404, because the contract exists from the section that published it onward.
///
/// PROVENANCE: ADR-009 - governance names only the export route in this controller. The list,
/// fetch, import and verify routes are additions recorded in the register; no governed route is
/// changed, renamed or removed.
/// </summary>
[ApiController]
[Route("api/v1/claims")]
[Produces("application/json")]
public class ClaimsController : ControllerBase
{
    private readonly EphemeralClaimStore _store;
    private readonly ClaimArchive _archive;

    public ClaimsController(EphemeralClaimStore store, ClaimArchive archive)
    {
        _store = store;
        _archive = archive;
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
    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(List<ClaimHeaderDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Import(IFormFile file)
        => StatusCode(StatusCodes.Status501NotImplemented);

    /// <summary>
    /// Proves zero-mutation round-tripping for a stored claim (governance User Story 3.2).
    /// </summary>
    [HttpPost("{id:guid}/verify-reversibility")]
    [ProducesResponseType(typeof(ReversibilityReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult VerifyReversibility(Guid id)
        => StatusCode(StatusCodes.Status501NotImplemented);
}
