using Bookstore.Core.Authors;
using Bookstore.Core.Books;
using FluentValidation;
using Moq;

namespace Bookstore.Tests.Books;

public sealed class ManagedAuthorBookTests
{
    private readonly Mock<IBookRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IAuthorBookCoordinator> _coordinator = new(MockBehavior.Strict);
    private BookService Service => new(_repository.Object, new CreateBookValidator(),
        new UpdateBookValidator(), new BookQueryValidator(), TimeProvider.System, _coordinator.Object);

    [Fact]
    public async Task CreateAsync_ManagedAuthor_UsesCoordinatorWithCancellation()
    {
        using var source = new CancellationTokenSource();
        var expected = TestBook.Create() with { AuthorId = TestBook.Id };
        _coordinator.Setup(coordinator => coordinator.CreateLinkedAsync(
            It.Is<Book>(book => book.AuthorId == TestBook.Id && book.Author == null), source.Token))
            .ReturnsAsync(expected);
        Assert.Same(expected, await Service.CreateAsync(
            new CreateBookRequest("Title", AuthorId: TestBook.Id), source.Token));
        _coordinator.VerifyAll();
        _repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("bad", null)]
    [InlineData(TestBook.Id, "Text")]
    [InlineData(TestBook.Id, "")]
    public async Task CreateAsync_InvalidOrConflictingAuthor_RejectsWithoutWrites(string authorId, string? author)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service.CreateAsync(
            new CreateBookRequest("Title", author, AuthorId: authorId), default));
        _repository.VerifyNoOtherCalls();
        _coordinator.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_AuthorFailure_PropagatesSafeDomainError(bool inactive)
    {
        _coordinator.Setup(coordinator => coordinator.CreateLinkedAsync(It.IsAny<Book>(), default))
            .ThrowsAsync(inactive ? new AuthorInactiveException() : new AuthorNotFoundException());
        if (inactive)
            await Assert.ThrowsAsync<AuthorInactiveException>(() => Service.CreateAsync(
                new CreateBookRequest("Title", AuthorId: TestBook.Id), default));
        else
            await Assert.ThrowsAsync<AuthorNotFoundException>(() => Service.CreateAsync(
                new CreateBookRequest("Title", AuthorId: TestBook.Id), default));
    }

    [Fact]
    public async Task ListAsync_AllFilters_ForwardsConjunctionAndToken()
    {
        using var source = new CancellationTokenSource();
        _repository.Setup(repository => repository.ListByAuthorIdAsync(6, "Name", TestBook.Id, source.Token))
            .ReturnsAsync([]);
        Assert.Empty(await Service.ListAsync("Programming", "Name", source.Token, TestBook.Id));
        _repository.VerifyAll();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    public async Task ListAsync_InvalidAuthorId_RejectsBeforeDatabase(string id)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service.ListAsync(null, null, default, id));
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FindAsync_AuthorIdOnSingle_RejectsWithoutDatabase()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            Service.FindAsync(new BookQuery(TestBook.Id, null, AuthorId: TestBook.Id), default));
        _repository.VerifyNoOtherCalls();
    }
}
