namespace Bookstore.Core.Books;

public sealed record Book(
    string Id,
    string? Title,
    string? Author,
    int CategoryId,
    int Quantity,
    string? Description,
    string? Language,
    bool Active,
    DateTimeOffset? InactiveDate,
    string? Publisher,
    DateTimeOffset? PublisherDate);
