namespace Bookstore.Core.Books;

public interface IBookRepository
{
    /// <summary>Lists books satisfying both optional category and literal author-fragment filters.</summary>
    /// <param name="categoryId">Numeric category filter, or null for all categories.</param>
    /// <param name="author">Case-insensitive literal substring, or null for no author filter.</param>
    /// <param name="cancellationToken">Cancellation propagated to database I/O.</param>
    /// <returns>All matching books; an empty collection when no books match.</returns>
    Task<IReadOnlyList<Book>> ListAsync(int? categoryId, string? author, CancellationToken cancellationToken);
    Task<Book?> FindAsync(BookQuery query, CancellationToken cancellationToken);
    Task<Book> InsertAsync(Book book, CancellationToken cancellationToken);
    Task<Book?> UpdateInventoryAsync(
        string id, int quantity, bool active, DateTimeOffset? inactiveDate,
        CancellationToken cancellationToken);
}
