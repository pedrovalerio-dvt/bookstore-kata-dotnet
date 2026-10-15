using FluentValidation;

namespace Bookstore.Core.Books;

public sealed class BookService(
    IBookRepository repository,
    IValidator<CreateBookRequest> createValidator,
    IValidator<UpdateBookRequest> updateValidator,
    IValidator<BookQuery> queryValidator,
    TimeProvider timeProvider)
{
    /// <summary>Validates collection filters and retrieves books without changing stored records.</summary>
    /// <param name="category">Exact category name, or null to omit category filtering.</param>
    /// <param name="author">Nonblank, case-insensitive literal author fragment, preserved without trimming.</param>
    /// <param name="cancellationToken">Cancellation propagated to repository reads.</param>
    /// <returns>Books matching every supplied filter.</returns>
    /// <exception cref="ValidationException">A supplied category is unknown or author is blank.</exception>
    public async Task<IReadOnlyList<Book>> ListAsync(
        string? category, string? author, CancellationToken cancellationToken)
    {
        if (author is not null && string.IsNullOrWhiteSpace(author))
        {
            throw new ValidationException("Author must contain non-whitespace text.");
        }

        int? categoryId = null;
        if (category is not null)
        {
            categoryId = Categories.FindId(category);
            if (categoryId is null)
            {
                throw new ValidationException("Category must be an exact category name.");
            }
        }

        return await repository.ListAsync(categoryId, author, cancellationToken);
    }

    public async Task<Book?> FindAsync(BookQuery query, CancellationToken cancellationToken)
    {
        await queryValidator.ValidateAndThrowAsync(query, cancellationToken);
        return await repository.FindAsync(query, cancellationToken);
    }

    public async Task<Book> CreateAsync(CreateBookRequest request, CancellationToken cancellationToken)
    {
        await createValidator.ValidateAndThrowAsync(request, cancellationToken);
        var book = new Book(
            string.Empty, request.Title, request.Author,
            request.CategoryId is null ? 0 : Categories.FindId(request.CategoryId)!.Value,
            request.Quantity, request.Description, request.Language, request.Active,
            request.Active ? null : request.InactiveDate ?? timeProvider.GetUtcNow(),
            request.Publisher, request.PublisherDate);
        return await repository.InsertAsync(book, cancellationToken);
    }

    public async Task<Book?> UpdateAsync(UpdateBookRequest request, CancellationToken cancellationToken)
    {
        await updateValidator.ValidateAndThrowAsync(request, cancellationToken);
        return await repository.UpdateInventoryAsync(
            request.Id!, request.Quantity!.Value, request.Active!.Value,
            request.Active.Value ? null : timeProvider.GetUtcNow(), cancellationToken);
    }
}
