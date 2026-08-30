using Translator.Api.Mapping;
using Translator.Contracts.DTOs;
using Translator.Domain.Persistence;
using Translator.Generation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Translator.Api.Controllers;

/// <summary>
/// PROVENANCE: GOVERNANCE-4 - "API Contracts First: OpenAPI specifications (swagger.json) and C#
/// API controllers must be fully defined before building underlying service logic."
///
/// The route, request contract, and every response this operation can produce were published in
/// docs/api/swagger.json before the generation engine existed. The engine now stands behind it.
/// </summary>
/// <remarks>
/// PROVENANCE: FIND-016 - this controller does not carry a class-level [Produces]. That attribute
/// is a result filter, not documentation: it forces its media type onto every response including
/// the problem documents, which the published contract declares as application/problem+json. The
/// response content types are documented by [ProducesResponseType] instead, which describes without
/// overriding.
/// </remarks>
[ApiController]
[Route("api/v1/bills")]
public class BillsController : ControllerBase
{
    private readonly SyntheticClaimGenerator _generator;
    private readonly IMedicalCodeCatalog _codeCatalog;
    private readonly EphemeralClaimStore _store;

    public BillsController(
        SyntheticClaimGenerator generator,
        IMedicalCodeCatalog codeCatalog,
        EphemeralClaimStore store)
    {
        _generator = generator;
        _codeCatalog = codeCatalog;
        _store = store;
    }

    /// <summary>
    /// Generates a batch of synthetic claims (governance User Story 1.3).
    /// </summary>
    /// <remarks>
    /// PROVENANCE: ADR-010 - the governed ceiling of 500 is enforced by the Range annotation on
    /// BatchGenerationRequestDto.BillCount. [ApiController] turns a binding or validation failure
    /// into the 400 Bad Request that User Story 1.3 requires, before this method body is entered.
    /// </remarks>
    [HttpPost("batch-generate")]
    [ProducesResponseType(typeof(List<ClaimHeaderDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BatchGenerate(
        [FromBody] BatchGenerationRequestDto request,
        CancellationToken cancellationToken)
    {
        // A category with no codes behind it would silently yield claims with no service lines,
        // which would satisfy the CLM02 sum invariant while being worthless. Refuse it instead.
        var unknown = request.MedicalCodeCategories
            .Where(category => _codeCatalog.CodesIn(category).Count == 0)
            .ToList();

        if (unknown.Count > 0)
        {
            ModelState.AddModelError(
                nameof(request.MedicalCodeCategories),
                $"No medical codes are catalogued for: {string.Join(", ", unknown)}. " +
                $"Known categories are: {string.Join(", ", _codeCatalog.Categories)}.");

            return ValidationProblem(ModelState);
        }

        var claims = await _generator.GenerateAsync(
            new BatchGenerationRequest(
                request.BillCount,
                request.JurisdictionState,
                request.MedicalCodeCategories,
                Seed: Random.Shared.Next()),
            cancellationToken);

        await using (var context = _store.CreateContext())
        {
            // A governed batch is 500 claims carrying up to 2,500 service lines. Change detection
            // is quadratic in tracked entities, and these are all freshly constructed and known to
            // be new, so there is nothing for it to detect. Governance User Story 1.3 allows 3.0
            // seconds for the whole operation; leaving this on spends most of that budget here.
            context.ChangeTracker.AutoDetectChangesEnabled = false;

            context.Claims.AddRange(claims);
            await context.SaveChangesAsync(cancellationToken);
        }

        // PROVENANCE: FIND-022 - read back what was written, exactly as the import route does.
        //
        // Returning the generated objects made a claim's representation depend on which route
        // produced it: a service unit count of 2 here and 2.0000 from import, because only the store
        // applies the governed scale (FIND-014). The two-tab client compares generated claims against
        // imported ones, so that difference is not cosmetic.
        //
        // It is also the FIND-001 and FIND-002 argument. The store is the layer those defects were
        // found in, and a response assembled from what went in would report a mutation the store
        // introduced as though it had not happened.
        var identifiers = claims.Select(claim => claim.Id).ToList();

        await using (var context = _store.CreateContext())
        {
            var stored = await context.Claims
                .Include(claim => claim.LineItems)
                .AsNoTracking()
                .Where(claim => identifiers.Contains(claim.Id))
                .ToListAsync(cancellationToken);

            return StatusCode(StatusCodes.Status201Created, stored.Select(ClaimMapper.ToDto).ToList());
        }
    }
}
