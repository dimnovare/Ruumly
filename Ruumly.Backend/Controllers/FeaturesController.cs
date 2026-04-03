using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Ruumly.Backend.Data;

namespace Ruumly.Backend.Controllers;

[ApiController]
public class FeaturesController(RuumlyDbContext db) : ControllerBase
{
    [HttpGet("/api/features")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFeatureDefinitions()
    {
        var settings = await db.PlatformSettings
            .Where(s => s.Key.StartsWith("features."))
            .ToDictionaryAsync(s => s.Key.Replace("features.", ""), s => s.Value);

        var result = new Dictionary<string, List<object>>();
        foreach (var (type, json) in settings)
        {
            try
            {
                var features = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
                result[type] = features?.Cast<object>().ToList() ?? [];
            }
            catch
            {
                result[type] = [];
            }
        }

        return Ok(result);
    }
}
