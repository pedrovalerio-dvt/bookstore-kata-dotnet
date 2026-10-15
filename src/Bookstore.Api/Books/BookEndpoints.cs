using System.ComponentModel;
using Bookstore.Core.Books;
using Microsoft.AspNetCore.Mvc;

namespace Bookstore.Api.Books;

public static class BookEndpoints
{
    public static IEndpointRouteBuilder MapBookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/books").WithTags("Books");
        group.MapGet("/", ListAsync).WithName("ListBooks")
            .WithSummary("List books by optional category, author text, and Author ID.")
            .WithDescription("authorId is exact identity; author is a case-insensitive literal substring and is not trimmed. All supplied filters combine with AND. Invalid IDs, blank author text, and unknown/empty category return 400. Missing filters list all books, including inactive ones. Null/missing authors do not match an author text search. No matches returns an empty array. Inactive Authors' books remain visible. No pagination or guaranteed ordering.")
            .Produces<IReadOnlyList<Book>>().ProducesProblem(400).ProducesProblem(500);
        group.MapGet("/single", FindAsync).WithName("GetBook")
            .WithSummary("Find a book by exactly one of id or exact title.")
            .WithDescription("Supply exactly one of id or name. Author text or authorId filtering on this single-book route returns 400; use the collection endpoint for author searches.")
            .Produces<Book>().ProducesProblem(400).ProducesProblem(404).ProducesProblem(500);
        group.MapPost("/", CreateAsync).WithName("CreateBook")
            .WithSummary("Create a book; categoryId accepts an exact category name.")
            .WithDescription("Requires a nonblank title and nonnegative quantity. Supply author text or authorId, not both. Managed links snapshot the active Author name transactionally; unknown Author returns 404 and inactive Author returns 409. Unknown categories return 400. Inactive creation without a date uses server UTC time.")
            .Produces<Book>(201).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(415).ProducesProblem(500);
        group.MapPut("/", UpdateAsync).WithName("UpdateBook")
            .WithSummary("Set quantity and active status, returning the complete persisted book.")
            .WithDescription("Id, quantity, and active must all be supplied. An unmatched ID returns 404; this does not insert a book. Deactivation stamps inactiveDate and activation clears it.")
            .Produces<Book>().ProducesProblem(400).ProducesProblem(404)
            .ProducesProblem(415).ProducesProblem(500);
        group.MapGet("/health", () => TypedResults.Ok())
            .WithName("BooksHealth").WithSummary("HTTP liveness only; does not query MongoDB.")
            .Produces(200);
        return endpoints;
    }

    /// <summary>Returns the collection matching the supplied category and literal author fragment.</summary>
    private static async Task<IResult> ListAsync(
        [Description("Exact category name; combined with author using AND.")] string? category,
        [Description("Optional nonblank, case-insensitive literal author substring. Whitespace is not trimmed.")]
        string? author,
        [Description("Optional exact managed Author ID; all supplied filters combine with AND.")]
        string? authorId, BookService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.ListAsync(category, author, cancellationToken, authorId));

    private static async Task<IResult> FindAsync(
        string? id, string? name, string? author, string? authorId,
        BookService service, CancellationToken cancellationToken)
    {
        var book = await service.FindAsync(new BookQuery(id, name, author, authorId), cancellationToken);
        return book is null ? NotFound() : TypedResults.Ok(book);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateBookRequest request, BookService service, CancellationToken cancellationToken)
    {
        var book = await service.CreateAsync(request, cancellationToken);
        return TypedResults.Created($"/api/books/single?id={book.Id}", book);
    }

    private static async Task<IResult> UpdateAsync(
        [FromBody] UpdateBookRequest request, BookService service, CancellationToken cancellationToken)
    {
        var book = await service.UpdateAsync(request, cancellationToken);
        return book is null ? NotFound() : TypedResults.Ok(book);
    }

    private static IResult NotFound() => TypedResults.Problem(
        statusCode: 404, title: "Book not found", detail: "No book matched the supplied identifier or title.");
}
