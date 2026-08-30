using Governance.Contracts.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace Governance.Api.Controllers;

/// <summary>
/// PROVENANCE: ADR-025 - two read-only routes governance does not name, published so the generation
/// form can be built against the same catalogue the generator draws from rather than against a copy
/// of it that would drift.
///
/// PROVENANCE: GOVERNANCE-4 - published in docs/api/swagger.json before this controller existed,
/// and answering 501 until the service behind it does.
/// </summary>
[ApiController]
[Route("api/v1")]
public class CatalogController : ControllerBase
{
    /// <summary>Lists the catalogued medical codes (governance User Story 1.2).</summary>
    [HttpGet("codes")]
    [ProducesResponseType(typeof(List<MedicalCodeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult ListMedicalCodes([FromQuery] string? category)
        => StatusCode(StatusCodes.Status501NotImplemented);

    /// <summary>Lists the jurisdictions a provider can be sourced for (governance User Story 1.1).</summary>
    [HttpGet("jurisdictions")]
    [ProducesResponseType(typeof(List<JurisdictionDto>), StatusCodes.Status200OK)]
    public IActionResult ListJurisdictions()
        => StatusCode(StatusCodes.Status501NotImplemented);
}
