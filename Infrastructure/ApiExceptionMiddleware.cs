using System.Text.Json;

namespace CandidatePortal.Api.Infrastructure;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException error)
        {
            await WriteError(context, error.StatusCode, error.Detail);
        }
        catch (Exception error)
        {
            logger.LogError(error, "Unhandled API request failure");
            if (!context.Response.HasStarted)
            {
                await WriteError(context, StatusCodes.Status500InternalServerError, "An unexpected server error occurred");
            }
        }
    }

    private static Task WriteError(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { detail }));
    }
}
