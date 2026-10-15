using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Bookstore.Api.Errors;

public sealed class JsonProblemDetailsWriter(
    IOptions<ProblemDetailsOptions> problemOptions,
    IOptions<JsonOptions> jsonOptions) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public async ValueTask WriteAsync(ProblemDetailsContext context)
    {
        problemOptions.Value.CustomizeProblemDetails?.Invoke(context);
        await context.HttpContext.Response.WriteAsJsonAsync(
            context.ProblemDetails, jsonOptions.Value.SerializerOptions,
            "application/problem+json", context.HttpContext.RequestAborted);
    }
}
