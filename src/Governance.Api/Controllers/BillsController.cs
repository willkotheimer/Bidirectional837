using Governance.Contracts.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace Governance.Api.Controllers;

/// <summary>
/// PROVENANCE: GOVERNANCE-4 - "API Contracts First: OpenAPI specifications (swagger.json) and C#
/// API controllers must be fully defined before building underlying service logic."
///
/// The route, request contract, and every response this operation can produce are defined here and
/// published in docs/api/swagger.json. The generation engine beneath it is Feature 1's deliverable,
/// so the operation answers 501 until that section lands. It never answers 404: the contract exists
/// from this section onward, and only its implementation is outstanding.
/// </summary>
[ApiController]
[Route("api/v1/bills")]
[Produces("application/json")]
public class BillsController : ControllerBase
{
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
    public IActionResult BatchGenerate([FromBody] BatchGenerationRequestDto request)
        => StatusCode(StatusCodes.Status501NotImplemented);
}
