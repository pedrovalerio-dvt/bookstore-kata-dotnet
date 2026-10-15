using Bookstore.Core.Authors;
using Bookstore.Core.Books;
using Bookstore.Infrastructure.Persistence;
using Bookstore.Tests.Authors;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Bookstore.Tests.Persistence;

[Trait("Category", "Database")]
[Collection("MongoDB")]
public sealed class MongoAuthorRepositoryTests(MongoFixture fixture)
{
    [Fact]
    public async Task InsertAndFindAsync_Profile_RoundTripsBsonAndNamesakes()
    {
        var database = fixture.CreateDatabase();
        var repository = new MongoAuthorRepository(database);
        var input = AuthorServiceTests.Example;
        var first = await repository.InsertAsync(input, default);
        var second = await repository.InsertAsync(input, default);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first, await repository.FindAsync(first.Id.ToUpperInvariant(), default));
        Assert.Equal(input.Name, first.Name);
        Assert.Equal(input.Email, first.Email);
        Assert.Equal(input.Nationality, first.Nationality);
        Assert.Equal(input.BirthDate, first.BirthDate);
        var document = await database.GetCollection<BsonDocument>("authors")
            .Find(new BsonDocument("_id", ObjectId.Parse(first.Id))).SingleAsync();
        Assert.True(document["_id"].IsObjectId);
        Assert.False(document.Contains("id"));
        Assert.True(document["birthDate"].IsValidDateTime);
        Assert.Equal(0, document["linkRevision"].AsInt64);
        Assert.Equal(2, (await repository.ListAsync(false, default)).Count);
        Assert.Null(await repository.FindAsync(ObjectId.GenerateNewId().ToString(), default));
    }

    [Fact]
    public async Task DeactivateAsync_ExistingAuthor_PreservesProfileAndIsIdempotent()
    {
        var database = fixture.CreateDatabase();
        var repository = new MongoAuthorRepository(database);
        var author = await repository.InsertAsync(AuthorServiceTests.Example, default);
        var now = TestBook.Now.AddDays(1);
        Assert.Equal(DeactivateAuthorResult.Deactivated, await repository.DeactivateAsync(author.Id, now, default));
        var inactive = await repository.FindAsync(author.Id, default);
        Assert.Equal(author.Deactivate(now), inactive);
        Assert.Empty(await repository.ListAsync(false, default));
        Assert.Equal(inactive, Assert.Single(await repository.ListAsync(true, default)));
        Assert.Equal(DeactivateAuthorResult.AlreadyInactive,
            await repository.DeactivateAsync(author.Id, now.AddDays(1), default));
        Assert.Equal(inactive, await repository.FindAsync(author.Id, default));
        Assert.Equal(DeactivateAuthorResult.Missing,
            await repository.DeactivateAsync(ObjectId.GenerateNewId().ToString(), now, default));
    }

    [Fact]
    public async Task InsertAsync_MinimalInactiveAuthor_OmitsOptionalMetadata()
    {
        var database = fixture.CreateDatabase();
        var repository = new MongoAuthorRepository(database);
        var author = await repository.InsertAsync(
            Author.Create(string.Empty, new CreateAuthorRequest("Name", Active: false), TestBook.Now), default);
        Assert.Null(author.BirthDate);
        Assert.Null(author.Email);
        Assert.Null(author.Nationality);
        Assert.Empty(await repository.ListAsync(false, default));
        Assert.Equal(author, Assert.Single(await repository.ListAsync(true, default)));
    }

    [Fact]
    public async Task CreateLinkedAsync_ActiveAuthor_PersistsIdentityAndConjunctiveFilters()
    {
        var database = fixture.CreateDatabase();
        var authors = new MongoAuthorRepository(database);
        var books = new MongoBookRepository(database);
        var coordinator = new MongoAuthorBookCoordinator(database);
        var author = await authors.InsertAsync(AuthorServiceTests.Example, default);
        var namesake = await authors.InsertAsync(AuthorServiceTests.Example, default);
        var linked = await coordinator.CreateLinkedAsync(
            TestBook.Create() with { Id = "", AuthorId = author.Id.ToUpperInvariant(), Author = null }, default);
        await coordinator.CreateLinkedAsync(TestBook.Create() with { AuthorId = namesake.Id }, default);
        await books.InsertAsync(TestBook.Create() with { Author = author.Name }, default);
        Assert.Equal(author.Id, linked.AuthorId);
        Assert.Equal(author.Name, linked.Author);
        Assert.Equal(linked, Assert.Single(await books.ListByAuthorIdAsync(6, "nAm", author.Id, default)));
        Assert.Empty(await books.ListByAuthorIdAsync(1, null, author.Id, default));
        Assert.Empty(await books.ListByAuthorIdAsync(null, "other", author.Id, default));
        Assert.Empty(await books.ListByAuthorIdAsync(null, null, ObjectId.GenerateNewId().ToString(), default));
        var updated = await books.UpdateInventoryAsync(linked.Id, 7, false, TestBook.Now, default);
        Assert.Equal(author.Id, updated!.AuthorId);
        Assert.Equal(author.Name, updated.Author);
        await authors.DeactivateAsync(author.Id, TestBook.Now.AddDays(1), default);
        Assert.Equal(updated, Assert.Single(await books.ListByAuthorIdAsync(null, null, author.Id, default)));
        var storedAuthor = await database.GetCollection<BsonDocument>("authors")
            .Find(new BsonDocument("_id", ObjectId.Parse(author.Id))).SingleAsync();
        Assert.Equal(1, storedAuthor["linkRevision"].AsInt64);
    }

    [Fact]
    public async Task CreateLinkedAsync_MissingOrInactiveAuthor_DoesNotInsertBook()
    {
        var database = fixture.CreateDatabase();
        var coordinator = new MongoAuthorBookCoordinator(database);
        await Assert.ThrowsAsync<AuthorNotFoundException>(() =>
            coordinator.CreateLinkedAsync(TestBook.Create() with { AuthorId = TestBook.Id }, default));
        var author = await new MongoAuthorRepository(database).InsertAsync(
            Author.Create("", new CreateAuthorRequest("Inactive", Active: false), TestBook.Now), default);
        await Assert.ThrowsAsync<AuthorInactiveException>(() =>
            coordinator.CreateLinkedAsync(TestBook.Create() with { AuthorId = author.Id }, default));
        Assert.Empty(await new MongoBookRepository(database).ListAsync(null, null, default));
        var stored = await database.GetCollection<BsonDocument>("authors").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal(0, stored["linkRevision"].AsInt64);
    }

    [Fact]
    public async Task CreateLinkedAsync_InsertFailure_RollsBackGuardedAuthorWrite()
    {
        var database = fixture.CreateDatabase();
        await database.CreateCollectionAsync("books", new CreateCollectionOptions<BsonDocument>
        {
            Validator = new BsonDocument("title", new BsonDocument("$eq", "Only accepted title"))
        });
        var author = await new MongoAuthorRepository(database).InsertAsync(AuthorServiceTests.Example, default);
        await Assert.ThrowsAsync<MongoWriteException>(() =>
            new MongoAuthorBookCoordinator(database).CreateLinkedAsync(
                TestBook.Create() with { AuthorId = author.Id }, default));
        var stored = await database.GetCollection<BsonDocument>("authors").Find(FilterDefinition<BsonDocument>.Empty).SingleAsync();
        Assert.Equal(0, stored["linkRevision"].AsInt64);
        Assert.Empty(await new MongoBookRepository(database).ListAsync(null, null, default));
    }

    [Fact]
    public async Task ListAsync_CancelledRequest_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MongoAuthorRepository(fixture.CreateDatabase()).ListAsync(true, source.Token));
    }
}
