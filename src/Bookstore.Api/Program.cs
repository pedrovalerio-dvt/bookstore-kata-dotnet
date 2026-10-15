using System.Diagnostics;
using System.Text.Json.Serialization;
using Bookstore.Api.Books;
using Bookstore.Api.Errors;
using Bookstore.Api.OpenApi;
using Bookstore.Core.Books;
using Bookstore.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Http.Json;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddSingleton<IProblemDetailsWriter, JsonProblemDetailsWriter>();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
    context.ProblemDetails.Instance = $"urn:trace:{traceId}";
    context.ProblemDetails.Extensions["traceId"] = traceId;
    context.ProblemDetails.Detail ??= "The request could not be completed.";
    context.ProblemDetails.Type ??= "about:blank";
    context.ProblemDetails.Status ??= context.HttpContext.Response.StatusCode;
    context.ProblemDetails.Title ??= Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(
        context.ProblemDetails.Status.Value);
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddOpenApi(options =>
{
    options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
    options.AddSchemaTransformer<BookSchemaTransformer>();
    options.AddOperationTransformer((operation, _, _) =>
    {
        foreach (var (status, response) in operation.Responses ?? [])
        {
            if (response is Microsoft.OpenApi.OpenApiResponse concrete)
            {
                concrete.Headers ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiHeader>();
                concrete.Headers["X-Trace-Id"] = new Microsoft.OpenApi.OpenApiHeader
                {
                    Description = "Trace identifier correlating the response, Problem Details, and JSON logs.",
                    Schema = new Microsoft.OpenApi.OpenApiSchema { Type = Microsoft.OpenApi.JsonSchemaType.String }
                };
                if (operation.OperationId == "CreateBook" && status == "201")
                {
                    concrete.Headers["Location"] = new Microsoft.OpenApi.OpenApiHeader
                    {
                        Description = "Lookup URL for the created book.",
                        Schema = new Microsoft.OpenApi.OpenApiSchema { Type = Microsoft.OpenApi.JsonSchemaType.String }
                    };
                }
            }
        }
        return Task.CompletedTask;
    });
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Bookstore API";
        document.Info.Version = "1.0.0";
        document.Info.Description =
            "MongoDB-backed .NET 10 API. No application authentication is configured. " +
            "Errors use Problem Details with a trace instance and X-Trace-Id header. " +
            "Dates are ISO 8601 date-times. Null response properties are omitted. " +
            "Category names are case-sensitive: Fiction, Non-Fiction, Science, Biography, Children, Programming.";
        document.Servers = [new Microsoft.OpenApi.OpenApiServer { Url = "/" }];
        return Task.CompletedTask;
    });
});
builder.Services.AddBookstorePersistence(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IValidator<CreateBookRequest>, CreateBookValidator>();
builder.Services.AddScoped<IValidator<UpdateBookRequest>, UpdateBookValidator>();
builder.Services.AddScoped<IValidator<BookQuery>, BookQueryValidator>();
builder.Services.AddScoped<BookService>();

var app = builder.Build();
app.Use(async (context, next) =>
{
    var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Trace-Id"] = traceId;
        return Task.CompletedTask;
    });
    using (Serilog.Context.LogContext.PushProperty("TraceId", traceId))
    {
        await next(context);
    }
});
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapBookEndpoints();
app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}
app.Run();

public partial class Program;
