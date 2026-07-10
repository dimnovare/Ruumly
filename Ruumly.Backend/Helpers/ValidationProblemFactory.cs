using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Ruumly.Backend.Helpers;

/// <summary>
/// Builds the app's sanitized 400 body for BOTH model-validation failures and
/// malformed-JSON deserialization failures (both surface through
/// <c>ApiBehaviorOptions.InvalidModelStateResponseFactory</c>).
///
/// The framework default (ProblemDetailsInvalidModelStateResponse) leaks the
/// internal .NET target type name ("...could not be converted to
/// Ruumly.Backend.DTOs.Requests.ChooseOptionRequest"), JSON byte offsets
/// ("BytePositionInLine"/"LineNumber") and a traceId to anonymous callers. This
/// emits a stable { error, message, statusCode, validationErrors } envelope with
/// generic field-level messages and no traceId.
/// </summary>
public static class ValidationProblemFactory
{
    private const string GenericFieldMessage = "The value is invalid.";

    public static IActionResult Build(ActionContext context)
    {
        var validationErrors = new Dictionary<string, string[]>();

        foreach (var (rawKey, entry) in context.ModelState)
        {
            if (entry.Errors.Count == 0) continue;

            var field = CleanKey(rawKey);
            var messages = entry.Errors
                .Select(e => Sanitize(e.ErrorMessage))
                .Distinct()
                .ToArray();

            // Several raw ModelState keys can normalize to the same field name.
            validationErrors[field] = validationErrors.TryGetValue(field, out var existing)
                ? existing.Concat(messages).Distinct().ToArray()
                : messages;
        }

        var message = validationErrors.Count > 0
            ? string.Join(" ", validationErrors.SelectMany(kv => kv.Value).Distinct())
            : "Validation failed.";

        return new BadRequestObjectResult(new
        {
            error      = "Bad Request",
            message,
            statusCode = 400,
            validationErrors,
        });
    }

    /// <summary>
    /// System.Text.Json deserialization errors surface as ModelState error messages
    /// carrying the target .NET type and byte offsets. Replace anything that smells
    /// like a framework/exception dump with a generic message; pass through
    /// human-authored FluentValidation / DataAnnotations messages (safe + useful).
    /// </summary>
    private static string Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return GenericFieldMessage;

        var leaks =
            message.Contains("Ruumly.Backend", StringComparison.Ordinal)      ||
            message.Contains("System.", StringComparison.Ordinal)             ||
            message.Contains("BytePositionInLine", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("LineNumber", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Path: $", StringComparison.Ordinal)             ||
            message.Contains("could not be converted", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("JSON", StringComparison.OrdinalIgnoreCase);

        return leaks ? GenericFieldMessage : message;
    }

    /// <summary>
    /// Normalizes a ModelState key into a client-facing field name:
    /// "$.optionId" → "optionId", "$[0].title" → "title", "$" / "" → "request".
    /// </summary>
    private static string CleanKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "request";

        var k = key;
        if (k.StartsWith("$.", StringComparison.Ordinal)) k = k[2..];
        else if (k.StartsWith("$", StringComparison.Ordinal)) k = k[1..];

        // Drop any leading path punctuation ("[0].", ".", "]") left behind.
        k = k.TrimStart('.', '[', ']');
        // Collapse an array-index prefix like "0].title" down to the trailing member.
        var lastDot = k.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < k.Length - 1) k = k[(lastDot + 1)..];

        return k.Length == 0 ? "request" : k;
    }
}

/// <summary>
/// Wires <see cref="ValidationProblemFactory"/> as the API 400 factory. MUST be
/// chained on the <see cref="IMvcBuilder"/> returned by AddControllers so it runs
/// AFTER the framework's ApiBehaviorOptionsSetup — registering the config earlier
/// (the previous bug) let the framework default silently overwrite it, so 400s fell
/// back to the leaky ProblemDetails shape.
/// </summary>
public static class MvcValidationExtensions
{
    public static IMvcBuilder AddSanitizedValidationErrors(this IMvcBuilder builder)
    {
        builder.ConfigureApiBehaviorOptions(options =>
            options.InvalidModelStateResponseFactory = ValidationProblemFactory.Build);
        return builder;
    }
}
