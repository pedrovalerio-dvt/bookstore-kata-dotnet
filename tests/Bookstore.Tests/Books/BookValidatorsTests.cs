using Bookstore.Core.Books;

namespace Bookstore.Tests.Books;

public sealed class BookValidatorsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Validate_MissingTitle_RejectsCreate(string? title)
    {
        var result = new CreateBookValidator().Validate(new CreateBookRequest(title));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(int.MaxValue, true)]
    public void Validate_Quantity_EnforcesNonnegativeStock(int quantity, bool valid)
    {
        var result = new CreateBookValidator().Validate(new CreateBookRequest("Book", Quantity: quantity));
        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Programming", true)]
    [InlineData("Fiction", true)]
    [InlineData("Non-Fiction", true)]
    [InlineData("Science", true)]
    [InlineData("Biography", true)]
    [InlineData("Children", true)]
    [InlineData("programming", false)]
    [InlineData("6", false)]
    [InlineData("", false)]
    [InlineData("Programming ", false)]
    public void Validate_Category_UsesExactNames(string? category, bool valid)
    {
        var result = new CreateBookValidator().Validate(new CreateBookRequest("Book", CategoryId: category));
        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Validate_InactiveDate_RequiresInactiveBook(bool active, bool valid)
    {
        var result = new CreateBookValidator().Validate(
            new CreateBookRequest("Book", Active: active, InactiveDate: DateTimeOffset.UtcNow));
        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-an-object-id")]
    [InlineData("zz7f1f77bcf86cd799439011")]
    public void Validate_InvalidId_RejectsUpdate(string? id)
    {
        Assert.False(new UpdateBookValidator().Validate(new UpdateBookRequest(id, 0, true)).IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, null)]
    [InlineData(-1, true)]
    public void Validate_MissingOrInvalidUpdateFields_RejectsUpdate(int? quantity, bool? active)
    {
        var result = new UpdateBookValidator().Validate(new UpdateBookRequest(TestBook.Id, quantity, active));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData(TestBook.Id, "Book", null, false)]
    [InlineData("invalid", null, null, false)]
    [InlineData(null, "", null, false)]
    [InlineData(null, " ", null, false)]
    [InlineData(null, "Book", "", false)]
    [InlineData(null, "Book", "Author", false)]
    [InlineData(TestBook.Id, null, null, true)]
    [InlineData("507F1F77BCF86CD799439011", null, null, true)]
    [InlineData(null, "Book", null, true)]
    public void Validate_Lookup_RequiresOneSupportedValidFilter(
        string? id, string? name, string? author, bool valid)
    {
        Assert.Equal(valid, new BookQueryValidator().Validate(new BookQuery(id, name, author)).IsValid);
    }
}
