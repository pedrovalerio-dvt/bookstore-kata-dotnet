using Bookstore.Core.Authors;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Api.Authors;

/// <summary>Author HTTP adapters; transition logs are emitted only after persistence commits.</summary>
public static class AuthorEndpoints
{
    public static IEndpointRouteBuilder MapAuthorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/authors").WithTags("Authors");
        group.MapPost("/", CreateAsync).WithName("CreateAuthor")
            .WithSummary("Create a managed Author; duplicate names are allowed.")
            .WithDescription("Requires a nonblank name of at most 255 characters. Profile access is deployment-protected; the application does not configure authentication.")
            .Produces<Author>(201).ProducesProblem(400).ProducesProblem(415).ProducesProblem(500);
        group.MapGet("/", ListAsync).WithName("ListAuthors")
            .WithSummary("List active Authors; optionally include inactive profiles.")
            .WithDescription("includeInactive defaults to false. No pagination or ordering guarantee. Empty results return [].")
            .Produces<IReadOnlyList<Author>>().ProducesProblem(400).ProducesProblem(500);
        group.MapGet("/{id}", FindAsync).WithName("GetAuthor")
            .WithSummary("Get an Author by ID, including inactive profiles.")
            .Produces<Author>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(500);
        group.MapDelete("/{id}", DeactivateAsync).WithName("DeactivateAuthor")
            .WithSummary("Soft-delete an Author without changing linked books.")
            .WithDescription("Repeated retirement returns 204 without changing updatedAt. New links are rejected transactionally; this is not privacy erasure.")
            .Produces(204).ProducesProblem(400).ProducesProblem(404).ProducesProblem(500);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateAuthorRequest request, AuthorService service, ILogger<AuthorService> logger,
        CancellationToken cancellationToken)
    {
        var author = await service.CreateAsync(request, cancellationToken);
        logger.LogInformation("Author {AuthorId} created", author.Id);
        return TypedResults.Created($"/api/authors/{author.Id}", author);
    }

    private static async Task<IResult> ListAsync(
        bool? includeInactive, AuthorService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(includeInactive ?? false, cancellationToken));

    private static async Task<IResult> FindAsync(
        string id, AuthorService service, CancellationToken cancellationToken)
    {
        var author = await service.FindAsync(id, cancellationToken);
        return author is null ? NotFound() : TypedResults.Ok(author);
    }

    private static async Task<IResult> DeactivateAsync(
        string id, AuthorService service, ILogger<AuthorService> logger, CancellationToken cancellationToken)
    {
        var result = await service.DeactivateAsync(id, cancellationToken);
        if (result == DeactivateAuthorResult.Missing)
            return NotFound();
        if (result == DeactivateAuthorResult.Deactivated)
            logger.LogInformation("Author {AuthorId} deactivated", id);
        return TypedResults.NoContent();
    }

    private static IResult NotFound() => TypedResults.Problem(
        statusCode: 404, title: "Author not found", detail: "No Author matched the supplied identifier.");
}
