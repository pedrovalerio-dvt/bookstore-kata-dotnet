using Bookstore.Core.Books;
using FluentValidation;
using Moq;

namespace Bookstore.Tests.Books;

public sealed class BookServiceTests
{
    private readonly Mock<IBookRepository> _repository = new(MockBehavior.Strict);

    private BookService CreateService() => new(
        _repository.Object, new CreateBookValidator(), new UpdateBookValidator(),
        new BookQueryValidator(), new FixedTimeProvider());

    [Theory]
    [InlineData(null, null)]
    [InlineData("Programming", 6)]
    [InlineData("Fiction", 1)]
    public async Task ListAsync_ValidCategory_PassesDatabaseFilter(string? category, int? categoryId)
    {
        using var source = new CancellationTokenSource();
        IReadOnlyList<Book> expected = [TestBook.Create()];
        _repository.Setup(repository => repository.ListAsync(categoryId, source.Token)).ReturnsAsync(expected);

        var actual = await CreateService().ListAsync(category, source.Token);

        Assert.Same(expected, actual);
        _repository.VerifyAll();
    }

    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("programming")]
    public async Task ListAsync_UnknownCategory_RejectsBeforeDatabaseAccess(string category)
    {
        await Assert.ThrowsAsync<ValidationException>(() => CreateService().ListAsync(category, default));
        _repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_ValidRequest_ReturnsPersistedBook(bool active)
    {
        Book? captured = null;
        var expected = TestBook.Create();
        _repository.Setup(repository => repository.InsertAsync(It.IsAny<Book>(), default))
            .Callback<Book, CancellationToken>((book, _) => captured = book).ReturnsAsync(expected);
        var request = new CreateBookRequest("Book", "Author", "Programming", 4,
            "Description", "English", active, Publisher: "Publisher", PublisherDate: TestBook.Now);

        var result = await CreateService().CreateAsync(request, default);

        Assert.Same(expected, result);
        Assert.NotNull(captured);
        Assert.Equal("Book", captured.Title);
        Assert.Equal("Author", captured.Author);
        Assert.Equal(6, captured.CategoryId);
        Assert.Equal(4, captured.Quantity);
        Assert.Equal("Description", captured.Description);
        Assert.Equal("English", captured.Language);
        Assert.Equal("Publisher", captured.Publisher);
        Assert.Equal(TestBook.Now, captured.PublisherDate);
        Assert.Equal(active ? null : TestBook.Now, captured.InactiveDate);
        Assert.Equal(active, captured.Active);
    }

    [Fact]
    public async Task CreateAsync_OmittedCategoryAndExplicitInactiveDate_PreservesDefaultsAndDate()
    {
        var date = TestBook.Now.AddDays(-1);
        _repository.Setup(repository => repository.InsertAsync(
                It.Is<Book>(book => book.CategoryId == 0 && book.InactiveDate == date && !book.Active), default))
            .ReturnsAsync(TestBook.Create());
        await CreateService().CreateAsync(new CreateBookRequest("Book", Active: false, InactiveDate: date), default);
        _repository.VerifyAll();
    }

    [Fact]
    public async Task CreateAsync_InvalidRequest_DoesNotWrite()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().CreateAsync(new CreateBookRequest(" "), default));
        _repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpdateAsync_ValidRequest_SetsTimestampAndReturnsCompleteBook(bool active)
    {
        var expected = TestBook.Create();
        _repository.Setup(repository => repository.UpdateInventoryAsync(
                TestBook.Id, 5, active, active ? null : TestBook.Now, default))
            .ReturnsAsync(expected);

        var result = await CreateService().UpdateAsync(new UpdateBookRequest(TestBook.Id, 5, active), default);

        Assert.Same(expected, result);
        Assert.NotNull(result);
        Assert.Equal("Test Driven Development", result.Title);
        Assert.Equal(6, result.CategoryId);
        _repository.VerifyAll();
    }

    [Fact]
    public async Task UpdateAsync_MissingBook_ReturnsNull()
    {
        _repository.Setup(repository => repository.UpdateInventoryAsync(TestBook.Id, 0, true, null, default))
            .ReturnsAsync((Book?)null);
        Assert.Null(await CreateService().UpdateAsync(new UpdateBookRequest(TestBook.Id, 0, true), default));
    }

    [Fact]
    public async Task UpdateAsync_OmittedFields_DoesNotOverwriteStock()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().UpdateAsync(new UpdateBookRequest(TestBook.Id, null, null), default));
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FindAsync_ValidQuery_UsesRepositoryAndCancellationToken()
    {
        using var source = new CancellationTokenSource();
        var query = new BookQuery(TestBook.Id, null);
        var expected = TestBook.Create();
        _repository.Setup(repository => repository.FindAsync(query, source.Token)).ReturnsAsync(expected);

        Assert.Same(expected, await CreateService().FindAsync(query, source.Token));
        _repository.VerifyAll();
    }

    [Fact]
    public async Task FindAsync_InvalidQuery_DoesNotReadDatabase()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService().FindAsync(new BookQuery(null, null), default));
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListAsync_RepositoryFailure_PropagatesFailure()
    {
        _repository.Setup(repository => repository.ListAsync(null, default))
            .ThrowsAsync(new InvalidOperationException("Database unavailable"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().ListAsync(null, default));
    }

    [Fact]
    public async Task CreateAsync_CancelledToken_DoesNotWrite()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService().CreateAsync(new CreateBookRequest("Book"), source.Token));
        _repository.VerifyNoOtherCalls();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TestBook.Now;
    }
}
