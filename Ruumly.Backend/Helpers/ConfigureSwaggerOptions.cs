using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Generates one Swagger document per discovered API version.
/// Registered as IConfigureOptions&lt;SwaggerGenOptions&gt; so it runs after
/// IApiVersionDescriptionProvider is populated.
/// </summary>
public sealed class ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
    : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, new OpenApiInfo
            {
                Title   = "Ruumly API",
                Version = description.GroupName,
            });
        }
    }
}
