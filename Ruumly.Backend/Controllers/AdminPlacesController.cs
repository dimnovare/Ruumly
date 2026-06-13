using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ruumly.Backend.Helpers;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

/// <summary>
/// Admin-only Google Places proxy used for onboarding prefill. Calls Google with
/// the server-side GooglePlaces:ApiKey (never exposing it to the browser) and
/// degrades gracefully — autocomplete returns [] and details returns 204 when the
/// feature is disabled (no API key configured).
/// </summary>
[ApiController]
[Route("api/admin/places")]
[Authorize(Roles = "Admin")]
public class AdminPlacesController(IPlacesService places, IHttpContextAccessor http) : ControllerBase
{
    private string Lang => http.HttpContext?.Request.GetLang() ?? "et";

    [HttpGet("autocomplete")]
    public async Task<IActionResult> Autocomplete([FromQuery] string? q)
    {
        var results = await places.AutocompleteAsync(q ?? string.Empty, Lang, HttpContext.RequestAborted);
        return Ok(results);
    }

    [HttpGet("details")]
    public async Task<IActionResult> Details([FromQuery] string? placeId)
    {
        var prefill = await places.GetPrefillAsync(placeId ?? string.Empty, Lang, HttpContext.RequestAborted);
        return prefill is null ? NoContent() : Ok(prefill);
    }
}
