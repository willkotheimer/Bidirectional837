using Translator.Contracts.DTOs;
using Translator.Generation;
using Microsoft.AspNetCore.Mvc;

namespace Translator.Api.Controllers;

/// <summary>
/// PROVENANCE: ADR-025 - two read-only routes governance does not name, published so the generation
/// form can be built against the same catalogue the generator draws from rather than against a copy
/// of it that would drift.
///
/// PROVENANCE: GOVERNANCE-4 - published in docs/api/swagger.json before this controller existed.
/// </summary>
[ApiController]
[Route("api/v1")]
public class CatalogController : ControllerBase
{
    private readonly IMedicalCodeCatalog _catalog;
    private readonly IChargeSchedule _charges;
    private readonly SeedProviderDirectory _providers;

    public CatalogController(
        IMedicalCodeCatalog catalog, IChargeSchedule charges, SeedProviderDirectory providers)
    {
        _catalog = catalog;
        _charges = charges;
        _providers = providers;
    }

    /// <summary>Lists the catalogued medical codes (governance User Story 1.2).</summary>
    /// <remarks>
    /// PROVENANCE: ADR-024 - the charge served here is the charge the generator will bill for the
    /// code, read from the same schedule, so a client cannot advertise one number and receive
    /// another. It is example data derived from a published CMS fee schedule, not a price.
    ///
    /// An unrecognised category is a 404 rather than an empty list. An empty list would let a client
    /// render a working-looking selector for a category that batch generation then rejects with a
    /// 400, which moves the failure away from its cause.
    /// </remarks>
    [HttpGet("codes")]
    [ProducesResponseType(typeof(List<MedicalCodeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult ListMedicalCodes([FromQuery] string? category)
    {
        if (!string.IsNullOrWhiteSpace(category) && _catalog.CodesIn(category).Count == 0)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: $"No medical codes are catalogued for '{category}'. " +
                        $"Known categories are: {string.Join(", ", _catalog.Categories)}.");
        }

        var categories = string.IsNullOrWhiteSpace(category) ? _catalog.Categories : [category];

        var codes = categories
            .SelectMany(_catalog.CodesIn)
            .Select(code => new MedicalCodeDto(
                code.Code, code.Category, code.Description, _charges.ChargeFor(code.Code)))
            .OrderBy(code => code.Category, StringComparer.Ordinal)
            .ThenBy(code => code.Code, StringComparer.Ordinal)
            .ToList();

        return Ok(codes);
    }

    /// <summary>Lists the jurisdictions a provider can be sourced for (governance User Story 1.1).</summary>
    /// <remarks>
    /// Only jurisdictions the provider snapshot actually carries are served, so the selector cannot
    /// offer a state the generator would have to fall back to a synthetic provider for.
    /// </remarks>
    [HttpGet("jurisdictions")]
    [ProducesResponseType(typeof(List<JurisdictionDto>), StatusCodes.Status200OK)]
    public IActionResult ListJurisdictions()
    {
        var jurisdictions = _providers.Jurisdictions
            .Select(code => new JurisdictionDto(
                code, UnitedStates.NameOf(code), _providers.ProvidersIn(code).Count))
            .OrderBy(jurisdiction => jurisdiction.Name, StringComparer.Ordinal)
            .ToList();

        return Ok(jurisdictions);
    }
}
