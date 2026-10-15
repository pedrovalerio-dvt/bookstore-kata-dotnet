using FluentValidation;

namespace Bookstore.Core.Books;

public sealed class BookService(
    IBookRepository repository,
    IValidator<CreateBookRequest> createValidator,
    IValidator<UpdateBookRequest> updateValidator,
    IValidator<BookQuery> queryValidator,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<Book>> ListAsync(string? category, CancellationToken cancellationToken)
    {
        int? categoryId = null;
        if (category is not null)
        {
            categoryId = Categories.FindId(category);
            if (categoryId is null)
            {
                throw new ValidationException("Category must be an exact category name.");
            }
        }

        return await repository.ListAsync(categoryId, cancellationToken);
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
