using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Api.Errors;

public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            ValidationException validation => (400, "Validation failed", validation.Message),
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Invalid HTTP request",
                "The request body or parameters could not be read."),
            _ => (500, "Internal server error", "An unexpected error occurred.")
        };
        if (status >= 500)
        {
            logger.LogError(exception, "Request failed with status {StatusCode}", status);
        }
        else
        {
            logger.LogWarning("Request rejected with status {StatusCode}: {Detail}", status, detail);
        }
        httpContext.Response.StatusCode = status;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        });
        return true;
    }
}
