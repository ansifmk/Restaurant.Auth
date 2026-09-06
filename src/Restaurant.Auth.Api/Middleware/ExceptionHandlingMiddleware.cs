using System.Net;
using System.Text.Json;
using FluentValidation;
using Restaurant.Auth.Api.Models.Responses;

namespace Restaurant.Auth.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            await HandleValidationException(context, ex);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            _logger.LogWarning(ex, "Domain exception: {Message}", ex.Message);
            await HandleException(context, HttpStatusCode.BadRequest, "An invalid request was processed.", "DOMAIN_ERROR");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access: {Message}", ex.Message);
            await HandleException(context, HttpStatusCode.Unauthorized, "Authentication is required.", "UNAUTHORIZED");
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Resource not found: {Message}", ex.Message);
            await HandleException(context, HttpStatusCode.NotFound, "The requested resource was not found.", "NOT_FOUND");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred.");
            await HandleException(context, HttpStatusCode.InternalServerError, "An internal server error occurred.", "INTERNAL_SERVER_ERROR");
        }
    }

    private static async Task HandleValidationException(HttpContext context, ValidationException ex)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;

        var errors = ex.Errors
            .GroupBy(e => e.PropertyName)
            .SelectMany(g => g.Select(e => new ApiError
            {
                Code = "VALIDATION_ERROR",
                Field = g.Key,
                Message = e.ErrorMessage
            }))
            .ToList();

        var response = ApiResponse<object>.Fail("Validation failed.", errors);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new DateTimeUtcJsonConverter());
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }

    private static async Task HandleException(HttpContext context, HttpStatusCode statusCode, string message, string errorCode)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = ApiResponse<object>.Fail(message, new List<ApiError>
        {
            new() { Code = errorCode, Message = message }
        });

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new DateTimeUtcJsonConverter());
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
    }
}
