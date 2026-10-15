using FluentValidation;

namespace Bookstore.Core.Authors;

/// <summary>A catalogue Author; names are credits, not unique identities.</summary>
public sealed record Author
{
    public string Id { get; }
    public string Name { get; }
    public DateOnly? BirthDate { get; }
    public string? Nationality { get; }
    public string? Email { get; }
    public bool Active { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }

    public Author(string id, string name, DateOnly? birthDate, string? nationality,
        string? email, bool active, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        Name = name;
        BirthDate = birthDate;
        Nationality = nationality;
        Email = email;
        Active = active;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Validates profile invariants and assigns server-owned lifecycle timestamps.</summary>
    public static Author Create(string id, CreateAuthorRequest request, DateTimeOffset now)
    {
        new CreateAuthorValidator().ValidateAndThrow(request);
        return new(id, request.Name!, request.BirthDate, request.Nationality,
            request.Email, request.Active, now, now);
    }

    /// <summary>Retires the Author without changing an earlier retirement timestamp.</summary>
    public Author Deactivate(DateTimeOffset now) => !Active ? this :
        new(Id, Name, BirthDate, Nationality, Email, false, CreatedAt, now);
}
