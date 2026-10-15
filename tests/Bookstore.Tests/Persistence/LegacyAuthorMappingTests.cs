using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Bookstore.Tests.Persistence;

[Trait("Category", "Database")]
[Collection("MongoDB")]
public sealed class LegacyAuthorMappingTests(MongoFixture fixture)
{
    [Theory]
    [InlineData("Historical spelling")]
    [InlineData(null)]
    public async Task RunMapping_DefaultMode_ValidatesWithoutChangingDocuments(string? credit)
    {
        var (database, book, author) = await SeedAsync(credit);

        var result = await fixture.RunLegacyMappingAsync(database, Manifest(book, author));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("\"status\":\"validated\"", result.Output);
        Assert.Equal(book, await FindAsync(database, "books", book["_id"]));
        Assert.Equal(author, await FindAsync(database, "authors", author["_id"]));
        Assert.DoesNotContain("private@example.invalid", result.Output);
        Assert.DoesNotContain("Historical spelling", result.Output);
    }

    [Fact]
    public async Task RunMapping_Apply_PreservesCreditAndMetadataAndWritesGuard()
    {
        var (database, book, author) = await SeedAsync();

        var result = await fixture.RunLegacyMappingAsync(database, Manifest(book, author), apply: true);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("\"status\":\"linked\"", result.Output);
        book["authorId"] = author["_id"];
        author["linkRevision"] = 1L;
        Assert.Equal(book, await FindAsync(database, "books", book["_id"]));
        Assert.Equal(author, await FindAsync(database, "authors", author["_id"]));
        Assert.DoesNotContain("private@example.invalid", result.Output);
    }

    [Fact]
    public async Task RunMapping_AlreadyLinkedToRetiredAuthor_SkipsWithoutWriting()
    {
        var (database, book, author) = await SeedAsync();
        var manifest = Manifest(book, author);
        Assert.Equal(0, (await fixture.RunLegacyMappingAsync(database, manifest, apply: true)).ExitCode);
        await database.GetCollection<BsonDocument>("authors").UpdateOneAsync(
            new BsonDocument("_id", author["_id"]), new BsonDocument("$set", new BsonDocument("active", false)));
        var before = await FindAsync(database, "authors", author["_id"]);

        var result = await fixture.RunLegacyMappingAsync(database, manifest, apply: true);

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("\"status\":\"skipped\"", result.Output);
        Assert.Equal(before, await FindAsync(database, "authors", author["_id"]));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("""[{"bookId":"bad","expectedAuthor":null,"expectedAuthorId":null,"authorId":"507f1f77bcf86cd799439011"}]""")]
    [InlineData("""[{"bookId":"507f1f77bcf86cd799439012","expectedAuthorId":null,"authorId":"507f1f77bcf86cd799439011"}]""")]
    [InlineData("""[{"bookId":"507f1f77bcf86cd799439012","expectedAuthor":7,"expectedAuthorId":null,"authorId":"507f1f77bcf86cd799439011"}]""")]
    [InlineData("""[{"bookId":"507f1f77bcf86cd799439012","expectedAuthor":null,"expectedAuthorId":"507f1f77bcf86cd799439011","authorId":"507f1f77bcf86cd799439011"}]""")]
    public async Task RunMapping_MalformedManifest_FailsWithoutWriting(string manifest)
    {
        var (database, book, author) = await SeedAsync();

        var result = await fixture.RunLegacyMappingAsync(database, manifest, apply: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("\"status\":\"failed\"", result.Output);
        Assert.Equal(book, await FindAsync(database, "books", book["_id"]));
        Assert.Equal(author, await FindAsync(database, "authors", author["_id"]));
    }

    [Fact]
    public async Task RunMapping_DuplicateBookEntries_RejectsWholeManifestBeforeWriting()
    {
        var (database, book, author) = await SeedAsync();
        var entry = Entry(book, author);

        var result = await fixture.RunLegacyMappingAsync(database, JsonSerializer.Serialize(new[] { entry, entry }), apply: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("DUPLICATE_BOOK", result.Output);
        Assert.Equal(book, await FindAsync(database, "books", book["_id"]));
        Assert.Equal(author, await FindAsync(database, "authors", author["_id"]));
    }

    [Theory]
    [InlineData("STALE_CREDIT")]
    [InlineData("ASSOCIATION_CONFLICT")]
    [InlineData("BOOK_NOT_FOUND")]
    [InlineData("AUTHOR_NOT_FOUND")]
    [InlineData("AUTHOR_INACTIVE")]
    public async Task RunMapping_StaleOrUnknownOrInactiveInput_FailsWithoutWriting(string expectedCode)
    {
        var (database, book, author) = await SeedAsync();
        var entry = Entry(book, author);
        if (expectedCode == "STALE_CREDIT")
        {
            entry["expectedAuthor"] = "Stale private credit";
        }
        else if (expectedCode == "ASSOCIATION_CONFLICT")
        {
            book["authorId"] = ObjectId.GenerateNewId();
            await database.GetCollection<BsonDocument>("books").ReplaceOneAsync(new BsonDocument("_id", book["_id"]), book);
        }
        else if (expectedCode == "BOOK_NOT_FOUND")
        {
            entry["bookId"] = ObjectId.GenerateNewId().ToString();
        }
        else if (expectedCode == "AUTHOR_NOT_FOUND")
        {
            entry["authorId"] = ObjectId.GenerateNewId().ToString();
        }
        else
        {
            author["active"] = false;
            await database.GetCollection<BsonDocument>("authors").ReplaceOneAsync(new BsonDocument("_id", author["_id"]), author);
        }

        var result = await fixture.RunLegacyMappingAsync(database, JsonSerializer.Serialize(new[] { entry }), apply: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedCode, result.Output);
        Assert.DoesNotContain("Stale private credit", result.Output);
        Assert.Equal(book, await FindAsync(database, "books", book["_id"]));
        Assert.Equal(author, await FindAsync(database, "authors", author["_id"]));
    }

    [Fact]
    public async Task RunMapping_BookWriteRejected_RollsBackAuthorGuard()
    {
        var (database, book, author) = await SeedAsync();
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            ["collMod"] = "books",
            ["validator"] = new BsonDocument("authorId", new BsonDocument("$exists", false)),
            ["validationLevel"] = "strict",
            ["validationAction"] = "error"
        });

        var result = await fixture.RunLegacyMappingAsync(database, Manifest(book, author), apply: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("MAPPING_FAILED", result.Output);
        Assert.Equal(book, await FindAsync(database, "books", book["_id"]));
        Assert.Equal(author, await FindAsync(database, "authors", author["_id"]));
    }

    [Fact]
    public async Task RunMapping_PartialFailureThenResume_SkipsCommittedEntryAndLinksRemainingBook()
    {
        var (database, firstBook, author) = await SeedAsync();
        var secondBook = firstBook.DeepClone().AsBsonDocument;
        secondBook["_id"] = ObjectId.GenerateNewId();
        secondBook["author"] = "Changed credit";
        await database.GetCollection<BsonDocument>("books").InsertOneAsync(secondBook);
        var secondEntry = Entry(secondBook, author);
        secondEntry["expectedAuthor"] = "Original credit";
        var manifest = JsonSerializer.Serialize(new[] { Entry(firstBook, author), secondEntry });

        var failed = await fixture.RunLegacyMappingAsync(database, manifest, apply: true);
        Assert.NotEqual(0, failed.ExitCode);
        Assert.Contains("\"status\":\"linked\"", failed.Output);
        Assert.Contains("STALE_CREDIT", failed.Output);
        Assert.Equal(author["_id"], (await FindAsync(database, "books", firstBook["_id"]))["authorId"]);
        Assert.False((await FindAsync(database, "books", secondBook["_id"])).Contains("authorId"));
        await database.GetCollection<BsonDocument>("books").UpdateOneAsync(
            new BsonDocument("_id", secondBook["_id"]), new BsonDocument("$set", new BsonDocument("author", "Original credit")));

        var resumed = await fixture.RunLegacyMappingAsync(database, manifest, apply: true);

        Assert.Equal(0, resumed.ExitCode);
        Assert.Contains("\"status\":\"skipped\"", resumed.Output);
        Assert.Contains("\"status\":\"linked\"", resumed.Output);
        Assert.Equal(author["_id"], (await FindAsync(database, "books", secondBook["_id"]))["authorId"]);
        Assert.Equal(2, (await FindAsync(database, "authors", author["_id"]))["linkRevision"].ToInt64());
    }

    private async Task<(IMongoDatabase Database, BsonDocument Book, BsonDocument Author)> SeedAsync(string? credit = "Historical spelling")
    {
        var database = fixture.CreateDatabase();
        var now = new BsonDateTime(new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc));
        var author = new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["name"] = "Managed name",
            ["email"] = "private@example.invalid",
            ["active"] = true,
            ["createdAt"] = now,
            ["updatedAt"] = now,
            ["linkRevision"] = 0L
        };
        var book = new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["title"] = "Legacy title",
            ["author"] = credit is null ? BsonNull.Value : credit,
            ["quantity"] = 7,
            ["categoryId"] = 6,
            ["active"] = false,
            ["createdAt"] = now
        };
        await database.GetCollection<BsonDocument>("authors").InsertOneAsync(author);
        await database.GetCollection<BsonDocument>("books").InsertOneAsync(book);
        return (database, book, author);
    }

    private static Dictionary<string, object?> Entry(BsonDocument book, BsonDocument author) => new()
    {
        ["bookId"] = book["_id"].ToString(),
        ["expectedAuthor"] = book["author"].IsBsonNull ? null : book["author"].AsString,
        ["expectedAuthorId"] = null,
        ["authorId"] = author["_id"].ToString()
    };

    private static string Manifest(BsonDocument book, BsonDocument author) =>
        JsonSerializer.Serialize(new[] { Entry(book, author) });

    private static async Task<BsonDocument> FindAsync(IMongoDatabase database, string collection, BsonValue id) =>
        await database.GetCollection<BsonDocument>(collection).Find(new BsonDocument("_id", id)).SingleAsync();
}
