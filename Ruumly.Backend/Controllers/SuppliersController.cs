using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Ruumly.Backend.Services.Interfaces;

namespace Ruumly.Backend.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController(IFeaturedPartnersService featuredPartnersService) : ControllerBase
{
    /// <summary>
    /// Returns up to 6 featured partners (verified Standard/Premium suppliers),
    /// rotating daily. Public — no auth required.
    /// </summary>
    [HttpGet("featured")]
    [EnableRateLimiting("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeatured()
    {
        var result = await featuredPartnersService.GetFeaturedAsync();
        return Ok(result);
    }
}
