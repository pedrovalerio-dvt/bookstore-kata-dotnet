using Bookstore.Core.Books;
using FluentValidation;

namespace Bookstore.Core.Authors;

/// <summary>Coordinates validated Author operations without exposing persistence details.</summary>
public sealed class AuthorService(IAuthorRepository repository, TimeProvider timeProvider)
{
    public Task<Author> CreateAsync(CreateAuthorRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return repository.InsertAsync(Author.Create(string.Empty, request, timeProvider.GetUtcNow()), cancellationToken);
    }

    public Task<IReadOnlyList<Author>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        repository.ListAsync(includeInactive, cancellationToken);

    public Task<Author?> FindAsync(string id, CancellationToken cancellationToken)
    {
        ValidateId(id);
        return repository.FindAsync(id, cancellationToken);
    }

    public Task<DeactivateAuthorResult> DeactivateAsync(string id, CancellationToken cancellationToken)
    {
        ValidateId(id);
        return repository.DeactivateAsync(id, timeProvider.GetUtcNow(), cancellationToken);
    }

    private static void ValidateId(string id)
    {
        if (!BookIdentifier.IsValid(id))
            throw new ValidationException("Author id must contain exactly 24 hexadecimal characters.");
    }
}
