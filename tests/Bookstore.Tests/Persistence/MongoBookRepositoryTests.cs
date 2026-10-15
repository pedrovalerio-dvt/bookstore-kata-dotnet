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

        Assert.Equal(programming, Assert.Single(await repository.ListAsync(6, null, default)));
        Assert.Equal(2, (await repository.ListAsync(null, null, default)).Count);
        Assert.Empty(await repository.ListAsync(3, null, default));
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
        Assert.Single(await repository.ListAsync(null, null, default));
    }

    [Fact]
    public async Task UpdateInventoryAsync_MissingBook_DoesNotInsert()
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        Assert.Null(await repository.UpdateInventoryAsync(TestBook.Id, 1, true, null, default));
        Assert.Empty(await repository.ListAsync(null, null, default));
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

        var books = await new MongoBookRepository(database).ListAsync(null, null, default);

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
            new MongoBookRepository(database).ListAsync(null, null, default));
    }

    [Fact]
    public async Task ListAsync_CancelledToken_PropagatesCancellation()
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.ListAsync(null, null, source.Token));
    }

    [Fact]
    public async Task ListAsync_PartialAuthor_ReturnsEveryCaseInsensitiveMatchIncludingInactive()
    {
        var database = fixture.CreateDatabase();
        var repository = new MongoBookRepository(database);
        var first = await repository.InsertAsync(TestBook.Create(), default);
        var second = await repository.InsertAsync(TestBook.Create() with
        {
            Title = "Another book",
            Author = "KENT BECK",
            CategoryId = 1,
            Active = false
        }, default);
        await repository.InsertAsync(TestBook.Create() with { Author = "Other writer" }, default);
        await repository.InsertAsync(TestBook.Create() with { Author = null }, default);
        await database.GetCollection<BsonDocument>("books").InsertOneAsync(
            new BsonDocument { ["_id"] = ObjectId.GenerateNewId(), ["title"] = "Missing author" });

        var matches = await repository.ListAsync(null, "eNt b", default);

        Assert.Equal(2, matches.Count);
        Assert.Contains(first, matches);
        Assert.Contains(second, matches);
        Assert.Equal(first, Assert.Single(await repository.ListAsync(6, "eNt b", default)));
        Assert.Empty(await repository.ListAsync(3, "eNt b", default));
        Assert.Empty(await repository.ListAsync(null, "nobody", default));
        Assert.Equal(5, (await repository.ListAsync(null, null, default)).Count);
    }

    [Theory]
    [InlineData(".*")]
    [InlineData("C++ (Jr.)")]
    [InlineData("[Beck]")]
    [InlineData(@"A\B")]
    [InlineData("^writer$")]
    [InlineData(" Kent ")]
    [InlineData("O'Neil & Sons")]
    public async Task ListAsync_RegexSymbolsAndWhitespace_TreatsFragmentLiterally(string fragment)
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        var expected = await repository.InsertAsync(
            TestBook.Create() with { Author = $"prefix{fragment}suffix" }, default);
        await repository.InsertAsync(TestBook.Create() with { Author = "Unrelated writer Kent" }, default);

        var result = await repository.ListAsync(null, fragment, default);

        Assert.Equal(expected, Assert.Single(result));
    }

    [Fact]
    public async Task ListAsync_AccentedAuthor_HandlesCaseWithoutAccentFolding()
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        var expected = await repository.InsertAsync(
            TestBook.Create() with { Author = "Ren\u00e9 Writer" }, default);

        Assert.Equal(expected, Assert.Single(await repository.ListAsync(null, "REN\u00c9", default)));
        Assert.Empty(await repository.ListAsync(null, "Rene", default));
    }

    [Fact]
    public async Task ListAsync_AuthorQueryCancelled_PropagatesCancellation()
    {
        var repository = new MongoBookRepository(fixture.CreateDatabase());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.ListAsync(6, "Kent", source.Token));
    }
}
