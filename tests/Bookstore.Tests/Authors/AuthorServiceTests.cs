using Bookstore.Core.Authors;
using FluentValidation;
using Moq;

namespace Bookstore.Tests.Authors;

public sealed class AuthorServiceTests
{
    private readonly Mock<IAuthorRepository> _repository = new(MockBehavior.Strict);
    private AuthorService Service => new(_repository.Object, new FixedTime());
    internal static Author Example => Author.Create(TestBook.Id,
        new CreateAuthorRequest("Name", new DateOnly(1980, 1, 2), "Country", "name@example.invalid"), TestBook.Now);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_ValidInput_ForwardsServerTimesAndToken(bool active)
    {
        using var source = new CancellationTokenSource();
        var expected = Example;
        _repository.Setup(repository => repository.InsertAsync(
            It.Is<Author>(author => author.Id == string.Empty && author.Active == active &&
                author.CreatedAt == TestBook.Now && author.UpdatedAt == TestBook.Now), source.Token))
            .ReturnsAsync(expected);
        Assert.Same(expected, await Service.CreateAsync(new CreateAuthorRequest("Name", Active: active), source.Token));
        _repository.VerifyAll();
    }

    [Fact]
    public async Task CreateAsync_InvalidOrCancelled_DoesNotWrite()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service.CreateAsync(new CreateAuthorRequest(" "), default));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service.CreateAsync(new CreateAuthorRequest("Name"), source.Token));
        _repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ListAsync_Visibility_ForwardsFilterAndToken(bool includeInactive)
    {
        using var source = new CancellationTokenSource();
        _repository.Setup(repository => repository.ListAsync(includeInactive, source.Token)).ReturnsAsync([Example]);
        Assert.Equal(Example, Assert.Single(await Service.ListAsync(includeInactive, source.Token)));
        _repository.VerifyAll();
    }

    [Fact]
    public async Task FindAsync_ValidId_ForwardsTokenAndMissingResult()
    {
        using var source = new CancellationTokenSource();
        _repository.Setup(repository => repository.FindAsync(TestBook.Id, source.Token)).ReturnsAsync((Author?)null);
        Assert.Null(await Service.FindAsync(TestBook.Id, source.Token));
        _repository.VerifyAll();
    }

    [Theory]
    [InlineData(DeactivateAuthorResult.Deactivated)]
    [InlineData(DeactivateAuthorResult.AlreadyInactive)]
    [InlineData(DeactivateAuthorResult.Missing)]
    public async Task DeactivateAsync_RepositoryOutcome_ForwardsTimeAndToken(DeactivateAuthorResult result)
    {
        using var source = new CancellationTokenSource();
        _repository.Setup(repository => repository.DeactivateAsync(TestBook.Id, TestBook.Now, source.Token))
            .ReturnsAsync(result);
        Assert.Equal(result, await Service.DeactivateAsync(TestBook.Id, source.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzz")]
    public async Task FindAndDeactivateAsync_InvalidId_RejectsBeforeDatabase(string id)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Service.FindAsync(id, default));
        await Assert.ThrowsAsync<ValidationException>(() => Service.DeactivateAsync(id, default));
        _repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListAsync_Failure_IsNotSwallowed()
    {
        _repository.Setup(repository => repository.ListAsync(false, default))
            .ThrowsAsync(new InvalidOperationException("database error"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service.ListAsync(false, default));
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TestBook.Now;
    }
}
