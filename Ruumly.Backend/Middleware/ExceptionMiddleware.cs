using System.Net;
using System.Text.Json;
using Ruumly.Backend.Helpers;

namespace Ruumly.Backend.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var (statusCodeForLog, _) = ex switch
            {
                ConflictException           => (409, ""),
                ForbiddenException          => (403, ""),
                UnauthorizedAccessException => (401, ""),
                KeyNotFoundException        => (404, ""),
                NotFoundException           => (404, ""),
                ArgumentException           => (400, ""),
                _                           => (500, ""),
            };
            logger.LogError(ex,
                "Unhandled {Method} {Path} → {StatusCode}: {Message}",
                context.Request.Method,
                context.Request.Path,
                statusCodeForLog,
                ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, error) = exception switch
        {
            ConflictException           => (HttpStatusCode.Conflict,             "Conflict"),
            ForbiddenException          => (HttpStatusCode.Forbidden,            "Forbidden"),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized,         "Unauthorized"),
            KeyNotFoundException        => (HttpStatusCode.NotFound,             "Not Found"),
            NotFoundException           => (HttpStatusCode.NotFound,             "Not Found"),
            ArgumentException           => (HttpStatusCode.BadRequest,           "Bad Request"),
            _                           => (HttpStatusCode.InternalServerError,  "Internal Server Error")
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode  = (int)statusCode;

        var payload = JsonSerializer.Serialize(new
        {
            error,
            message    = exception.Message,
            statusCode = (int)statusCode
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return context.Response.WriteAsync(payload);
    }
}
