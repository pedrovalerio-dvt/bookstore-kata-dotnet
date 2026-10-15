using Bookstore.Core.Books;
using Bookstore.Infrastructure.Persistence;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Bookstore.Tests.Persistence;

[Trait("Category", "Database")]
public sealed class MongoBookRepositoryTests(MongoFixture fixture) : IClassFixture<MongoFixture>
{
    [Fact]
    public async Task InsertAsync_AllFields_RoundTripsBsonDatesAndGeneratedId()
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        var input = TestBook.Create() with { Id = "", Active = false, InactiveDate = TestBook.Now };

        var inserted = await repository.InsertAsync(input, default);
        var found = await repository.FindAsync(new BookQuery(inserted.Id, null), default);

        Assert.True(BookIdentifier.IsValid(inserted.Id));
        Assert.Equal(input with { Id = inserted.Id }, found);
    }

    [Fact]
    public async Task FindAsync_ExactTitleAndId_UsesDatabaseEquality()
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        var inserted = await repository.InsertAsync(TestBook.Create(), default);
        var distinctString = new string(inserted.Id.ToCharArray());

        Assert.Equal(inserted, await repository.FindAsync(new BookQuery(distinctString.ToUpperInvariant(), null), default));
        Assert.Equal(inserted, await repository.FindAsync(new BookQuery(null, inserted.Title), default));
        Assert.Null(await repository.FindAsync(new BookQuery(null, "test driven development"), default));
        Assert.Null(await repository.FindAsync(new BookQuery(TestBook.Id, null), default));
    }

    [Fact]
    public async Task ListAsync_CategoryFilter_ExcludesOtherCategoriesButIncludesInactiveBooks()
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        var programming = await repository.InsertAsync(TestBook.Create() with { Active = false }, default);
        await repository.InsertAsync(TestBook.Create() with { CategoryId = 1 }, default);

        Assert.Equal(programming, Assert.Single(await repository.ListAsync(6, default)));
        Assert.Equal(2, (await repository.ListAsync(null, default)).Count);
        Assert.Empty(await repository.ListAsync(3, default));
    }

    [Fact]
    public async Task UpdateInventoryAsync_ExistingBook_PreservesMetadataAndReturnsPersistedState()
    {
        var database = fixture.CreateDatabase();
        var repository = new MongoBookRepository(database);
        var inserted = await repository.InsertAsync(TestBook.Create(), default);

        var inactive = await repository.UpdateInventoryAsync(inserted.Id, 8, false, TestBook.Now, default);
        Assert.Equal(inserted with { Quantity = 8, Active = false, InactiveDate = TestBook.Now }, inactive);
        Assert.Equal(inactive, await repository.FindAsync(new BookQuery(inserted.Id, null), default));

        var reactivated = await repository.UpdateInventoryAsync(inserted.Id, 2, true, null, default);
        Assert.Equal(inserted with { Quantity = 2 }, reactivated);
        Assert.Equal(reactivated, await repository.FindAsync(new BookQuery(inserted.Id, null), default));
        Assert.Single(await repository.ListAsync(null, default));
    }

    [Fact]
    public async Task UpdateInventoryAsync_MissingBook_DoesNotInsert()
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        Assert.Null(await repository.UpdateInventoryAsync(TestBook.Id, 1, true, null, default));
        Assert.Empty(await repository.ListAsync(null, default));
    }

    [Fact]
    public async Task ListAsync_LegacyMissingAndNullFields_AppliesCompatibleDefaults()
    {
        var database = fixture.CreateDatabase();
        await database.GetCollection<BsonDocument>("books").InsertManyAsync(
        [
            new BsonDocument("_id", ObjectId.GenerateNewId()),
            new BsonDocument
            {
                ["_id"] = ObjectId.GenerateNewId(),
                ["title"] = BsonNull.Value,
                ["categoryId"] = BsonNull.Value,
                ["quantity"] = BsonNull.Value,
                ["active"] = BsonNull.Value,
                ["inactiveDate"] = BsonNull.Value,
                ["publisherDate"] = BsonNull.Value
            }
        ]);

        var books = await new MongoBookRepository(database).ListAsync(null, default);

        Assert.Equal(2, books.Count);
        Assert.All(books, book =>
        {
            Assert.Null(book.Title);
            Assert.Equal(0, book.CategoryId);
            Assert.Equal(0, book.Quantity);
            Assert.False(book.Active);
            Assert.Null(book.InactiveDate);
            Assert.Null(book.PublisherDate);
        });
    }

    [Fact]
    public async Task ListAsync_InvalidStoredType_PropagatesMappingFailure()
    {
        var database = fixture.CreateDatabase();
        await database.GetCollection<BsonDocument>("books").InsertOneAsync(
            new BsonDocument { ["_id"] = ObjectId.GenerateNewId(), ["quantity"] = "invalid" });
        await Assert.ThrowsAsync<InvalidCastException>(() =>
            new MongoBookRepository(database).ListAsync(null, default));
    }

    [Fact]
    public async Task ListAsync_CancelledToken_PropagatesCancellation()
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ListAsync(null, source.Token));
    }
}
