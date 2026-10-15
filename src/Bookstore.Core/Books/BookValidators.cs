using FluentValidation;

namespace Bookstore.Core.Books;

public sealed class CreateBookValidator : AbstractValidator<CreateBookRequest>
{
    public CreateBookValidator()
    {
        RuleFor(request => request.Title).NotEmpty();
        RuleFor(request => request.Quantity).GreaterThanOrEqualTo(0);
        RuleFor(request => request.AuthorId).Must(BookIdentifier.IsValid)
            .When(request => request.AuthorId is not null)
            .WithMessage("AuthorId must contain exactly 24 hexadecimal characters.");
        RuleFor(request => request).Must(request => request.AuthorId is null || request.Author is null)
            .WithMessage("Supply authorId or author text, not both.");
        RuleFor(request => request.CategoryId)
            .Must(category => category is null || Categories.FindId(category) is not null)
            .WithMessage("CategoryId must be an exact category name or null.");
        RuleFor(request => request.InactiveDate)
            .Null().When(request => request.Active)
            .WithMessage("An active book cannot have an inactive date.");
    }
}

public sealed class UpdateBookValidator : AbstractValidator<UpdateBookRequest>
{
    public UpdateBookValidator()
    {
        RuleFor(request => request.Id).Must(BookIdentifier.IsValid)
            .WithMessage("Id must contain exactly 24 hexadecimal characters.");
        RuleFor(request => request.Quantity).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(request => request.Active).NotNull();
    }
}

public sealed class BookQueryValidator : AbstractValidator<BookQuery>
{
    public BookQueryValidator()
    {
        RuleFor(request => request)
            .Must(query => (query.Id is null) != (query.Name is null))
            .WithMessage("Supply exactly one of id or name.");
        RuleFor(request => request.Id).Must(BookIdentifier.IsValid)
            .When(request => request.Id is not null)
            .WithMessage("Id must contain exactly 24 hexadecimal characters.");
        RuleFor(request => request.Name).NotEmpty().When(request => request.Name is not null);
        RuleFor(request => request.Author).Null()
            .WithMessage("Use the books collection endpoint to search by author.");
        RuleFor(request => request.AuthorId).Null()
            .WithMessage("Use the books collection endpoint to filter by authorId.");
    }
}

public static class BookIdentifier
{
    public static bool IsValid(string? value) =>
        value is { Length: 24 } && value.All(char.IsAsciiHexDigit);
}
