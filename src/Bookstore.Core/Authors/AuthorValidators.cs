using FluentValidation;

namespace Bookstore.Core.Authors;

public sealed class CreateAuthorValidator : AbstractValidator<CreateAuthorRequest>
{
    public CreateAuthorValidator()
    {
        // Messages are also logged by the API; never interpolate profile values.
        RuleFor(request => request.Name).NotEmpty().WithMessage("Name is required.")
            .MaximumLength(255).WithMessage("Name must contain at most 255 characters.");
        RuleFor(request => request.Nationality).NotEmpty()
            .When(request => request.Nationality is not null)
            .WithMessage("Nationality must contain non-whitespace text.");
        RuleFor(request => request.Email).NotEmpty().WithMessage("Email must not be blank.")
            .EmailAddress().WithMessage("Email must have a valid email format.")
            .When(request => request.Email is not null);
    }
}
