using Bookstore.Core.Authors;
using Bookstore.Infrastructure.Persistence;
using Bookstore.Tests.Authors;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace Bookstore.Tests.Persistence;

[Trait("Category", "Database")]
[Collection("MongoDB")]
public sealed class AuthorBookConcurrencyTests(MongoFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateLinkedAsync_CompetingRetirement_CommitsOnlyInStrictOrder(bool deletionFirst)
    {
        var database = fixture.CreateDatabase();
        var author = await new MongoAuthorRepository(database).InsertAsync(AuthorServiceTests.Example, default);
        // Both collections must exist before competing transactions begin.
        await database.CreateCollectionAsync("books");
        using var release = new ManualResetEventSlim();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var conflicted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = MongoClientSettings.FromConnectionString(fixture.ConnectionString);
        settings.ClusterConfigurator = cluster =>
        {
            cluster.Subscribe<CommandStartedEvent>(command =>
            {
                var shouldHold = deletionFirst ? command.CommandName == "commitTransaction" :
                    command.CommandName == "insert" && command.Command.GetValue("insert", "") == "books";
                if (!shouldHold)
                    return;
                held.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Test did not release its held transaction.");
            });
        };
        using var firstClient = new MongoClient(settings);
        var firstDatabase = firstClient.GetDatabase(database.DatabaseNamespace.DatabaseName);
        var contenderSettings = MongoClientSettings.FromConnectionString(fixture.ConnectionString);
        contenderSettings.ClusterConfigurator = cluster =>
        {
            cluster.Subscribe<CommandFailedEvent>(command =>
            {
                if (command.Failure is MongoCommandException { Code: 112 })
                    conflicted.TrySetResult();
            });
            cluster.Subscribe<CommandSucceededEvent>(command =>
            {
                if (command.Reply.TryGetValue("writeErrors", out var errors) && errors.AsBsonArray.Any(
                    error => error.AsBsonDocument.GetValue("code", 0).ToInt32() == 112))
                    conflicted.TrySetResult();
            });
        };
        using var contenderClient = new MongoClient(contenderSettings);
        var contenderDatabase = contenderClient.GetDatabase(database.DatabaseNamespace.DatabaseName);
        var book = TestBook.Create() with { AuthorId = author.Id };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        Task first;
        Task second = Task.CompletedTask;
        if (deletionFirst)
            first = Task.Run(() => new MongoAuthorRepository(firstDatabase)
                .DeactivateAsync(author.Id, TestBook.Now.AddDays(1), timeout.Token));
        else
            first = Task.Run(() => new MongoAuthorBookCoordinator(firstDatabase).CreateLinkedAsync(book, timeout.Token));
        try
        {
            await held.Task.WaitAsync(TimeSpan.FromSeconds(20));
            second = deletionFirst
                ? Task.Run(() => new MongoAuthorBookCoordinator(contenderDatabase).CreateLinkedAsync(book, timeout.Token))
                : Task.Run(() => new MongoAuthorRepository(contenderDatabase)
                    .DeactivateAsync(author.Id, TestBook.Now.AddDays(1), timeout.Token));
            // Wait for actual MongoDB write contention before allowing the first commit.
            await conflicted.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            release.Set();
        }

        await first;
        if (deletionFirst)
            await Assert.ThrowsAsync<AuthorInactiveException>(async () => await second);
        else
            await second;
        var books = await new MongoBookRepository(database).ListByAuthorIdAsync(null, null, author.Id, default);
        Assert.Equal(deletionFirst ? 0 : 1, books.Count);
        Assert.False((await new MongoAuthorRepository(database).FindAsync(author.Id, default))!.Active);
        var stored = await database.GetCollection<BsonDocument>("authors")
            .Find(new BsonDocument("_id", ObjectId.Parse(author.Id))).SingleAsync();
        Assert.Equal(deletionFirst ? 0L : 1L, stored["linkRevision"].AsInt64);
    }
}
