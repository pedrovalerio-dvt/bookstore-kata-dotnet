using Bookstore.Core.Authors;
using FluentValidation;

namespace Bookstore.Tests.Authors;

public sealed class AuthorTests
{
    [Fact]
    public void Deactivate_ActiveAuthor_ReturnsInactiveCopyWithNewTimestamp()
    {
        var author = Author.Create("id", new CreateAuthorRequest("Name"), TestBook.Now);
        var inactive = author.Deactivate(TestBook.Now.AddDays(1));
        Assert.True(author.Active);
        Assert.False(inactive.Active);
        Assert.Equal(author.CreatedAt, inactive.CreatedAt);
        Assert.Equal(TestBook.Now.AddDays(1), inactive.UpdatedAt);
    }

    [Fact]
    public void Deactivate_AlreadyInactive_PreservesUpdatedAt()
    {
        var author = Author.Create("id", new CreateAuthorRequest("Name", Active: false), TestBook.Now);
        Assert.Same(author, author.Deactivate(TestBook.Now.AddDays(1)));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(255, true)]
    [InlineData(256, false)]
    public void Create_NameBoundary_EnforcesLength(int length, bool valid)
    {
        var request = new CreateAuthorRequest(new string('x', length));
        if (valid)
            Assert.Equal(request.Name, Author.Create("id", request, TestBook.Now).Name);
        else
            Assert.Throws<ValidationException>(() => Author.Create("id", request, TestBook.Now));
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData("", null, null, false)]
    [InlineData(" ", null, null, false)]
    [InlineData("Name", "", null, false)]
    [InlineData("Name", null, " ", false)]
    [InlineData("Name", "invalid", null, false)]
    [InlineData("Name", "name@example.invalid", " Country ", true)]
    [InlineData("Name", null, null, true)]
    public async Task ValidateAsync_ProfileInput_EnforcesRulesWithoutEchoingValues(
        string? name, string? email, string? nationality, bool valid)
    {
        var request = new CreateAuthorRequest(name, Email: email, Nationality: nationality);
        var result = await new CreateAuthorValidator().ValidateAsync(request);
        Assert.Equal(valid, result.IsValid);
        Assert.All(result.Errors, error => Assert.DoesNotContain("invalid", error.ErrorMessage));
    }
}
