using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bookstore.Core.Books;

public sealed record CreateBookRequest(
    [property: Required, Description("Nonblank title. Duplicate titles remain allowed.")] string? Title,
    string? Author = null,
    [property: Description("Exact category NAME, not numeric ID. Omitted/null maps to category 0.")]
    string? CategoryId = null,
    [property: Range(0, int.MaxValue), Description("Absolute stock quantity; defaults to zero.")]
    int Quantity = 0,
    string? Description = null,
    string? Language = null,
    [property: Description("Defaults to true. Active books cannot have an inactiveDate.")] bool Active = true,
    [property: Description("UTC date-time; inactive creation defaults to server time if omitted.")]
    DateTimeOffset? InactiveDate = null,
    string? Publisher = null,
    DateTimeOffset? PublisherDate = null,
    [property: RegularExpression("^[0-9a-fA-F]{24}$"),
        Description("Optional managed Author ID; cannot be supplied with author text.")]
    string? AuthorId = null);

public sealed record UpdateBookRequest(
    [property: Required, RegularExpression("^[0-9a-fA-F]{24}$")] string? Id,
    [property: Required, Range(0, int.MaxValue)] int? Quantity,
    [property: Required, Description("False stamps inactiveDate with server time; true clears it.")] bool? Active);

public sealed record BookQuery(string? Id, string? Name, string? Author = null, string? AuthorId = null);

public sealed record Category(int Id, string Name);

public static class Categories
{
    public static IReadOnlyList<Category> All { get; } = Array.AsReadOnly<Category>(
    [
        new(1, "Fiction"),
        new(2, "Non-Fiction"),
        new(3, "Science"),
        new(4, "Biography"),
        new(5, "Children"),
        new(6, "Programming")
    ]);

    public static int? FindId(string name) =>
        All.FirstOrDefault(category => category.Name == name)?.Id;
}
