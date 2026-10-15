namespace Bookstore.Core.Books;

/// <summary>Creates a book only if its Author remains active in the ordered transaction.</summary>
public interface IAuthorBookCoordinator
{
    Task<Book> CreateLinkedAsync(Book book, CancellationToken cancellationToken);
}
