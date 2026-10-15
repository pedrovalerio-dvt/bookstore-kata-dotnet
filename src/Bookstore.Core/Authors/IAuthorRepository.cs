namespace Bookstore.Core.Authors;

public interface IAuthorRepository
{
    Task<Author> InsertAsync(Author author, CancellationToken cancellationToken);
    Task<IReadOnlyList<Author>> ListAsync(bool includeInactive, CancellationToken cancellationToken);
    Task<Author?> FindAsync(string id, CancellationToken cancellationToken);
    Task<DeactivateAuthorResult> DeactivateAsync(
        string id, DateTimeOffset now, CancellationToken cancellationToken);
}
