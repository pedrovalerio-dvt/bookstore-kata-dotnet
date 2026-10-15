using Bookstore.Core.Books;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Bookstore.Api.OpenApi;

public sealed class BookSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(Book))
        {
            schema.Required = new HashSet<string> { "id", "categoryId", "quantity", "active" };
        }
        else if (context.JsonTypeInfo.Type == typeof(CreateBookRequest))
        {
            SetNonNullable(schema, "title");
        }
        else if (context.JsonTypeInfo.Type == typeof(UpdateBookRequest))
        {
            SetNonNullable(schema, "id");
            SetNonNullable(schema, "quantity");
            SetNonNullable(schema, "active");
        }
        else if (context.JsonTypeInfo.Type == typeof(ProblemDetails))
        {
            schema.Required = new HashSet<string> { "type", "title", "status", "detail", "instance", "traceId" };
            schema.Properties ??= new Dictionary<string, IOpenApiSchema>();
            schema.Properties["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String };
            foreach (var name in schema.Required)
            {
                SetNonNullable(schema, name);
            }
        }

        return Task.CompletedTask;
    }

    private static void SetNonNullable(OpenApiSchema schema, string name)
    {
        if (schema.Properties is not null &&
            schema.Properties.TryGetValue(name, out var value) && value is OpenApiSchema property)
        {
            property.Type &= ~JsonSchemaType.Null;
        }
    }
}
