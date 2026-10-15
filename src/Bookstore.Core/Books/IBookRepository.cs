namespace Bookstore.Core.Books;

public interface IBookRepository
{
    Task<IReadOnlyList<Book>> ListAsync(int? categoryId, CancellationToken cancellationToken);
    Task<Book?> FindAsync(BookQuery query, CancellationToken cancellationToken);
    Task<Book> InsertAsync(Book book, CancellationToken cancellationToken);
    Task<Book?> UpdateInventoryAsync(
        string id, int quantity, bool active, DateTimeOffset? inactiveDate,
        CancellationToken cancellationToken);
}
