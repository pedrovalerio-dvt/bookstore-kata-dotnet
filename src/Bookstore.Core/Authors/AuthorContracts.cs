using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Bookstore.Core.Authors;

public sealed record CreateAuthorRequest(
    [property: Required, MaxLength(255), Description("Nonblank name; names need not be unique.")]
    string? Name,
    [property: Description("Calendar date in YYYY-MM-DD format.")] DateOnly? BirthDate = null,
    string? Nationality = null,
    [property: EmailAddress] string? Email = null,
    bool Active = true);

/// <summary>Safe application outcomes; persistence-specific exceptions never cross this boundary.</summary>
public enum DeactivateAuthorResult { Missing, AlreadyInactive, Deactivated }

public sealed class AuthorNotFoundException() : Exception("The referenced Author does not exist.");
public sealed class AuthorInactiveException() : Exception("The referenced Author is inactive.");
