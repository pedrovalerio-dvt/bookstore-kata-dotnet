using Bookstore.Core.Books;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Bookstore.Infrastructure.Persistence;

public sealed class MongoBookRepository(IMongoDatabase database) : IBookRepository
{
    private readonly IMongoCollection<BsonDocument> _books = database.GetCollection<BsonDocument>("books");

    public async Task<IReadOnlyList<Book>> ListAsync(int? categoryId, CancellationToken cancellationToken)
    {
        var filter = categoryId is null
            ? Builders<BsonDocument>.Filter.Empty
            : Builders<BsonDocument>.Filter.Eq("categoryId", categoryId.Value);
        var documents = await _books.Find(filter).ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    public async Task<Book?> FindAsync(BookQuery query, CancellationToken cancellationToken)
    {
        var filter = query.Id is not null
            ? Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(query.Id))
            : Builders<BsonDocument>.Filter.Eq("title", query.Name);
        var document = await _books.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<Book> InsertAsync(Book book, CancellationToken cancellationToken)
    {
        var id = ObjectId.GenerateNewId();
        var document = new BsonDocument
        {
            ["_id"] = id,
            ["title"] = Text(book.Title),
            ["author"] = Text(book.Author),
            ["categoryId"] = book.CategoryId,
            ["quantity"] = book.Quantity,
            ["description"] = Text(book.Description),
            ["language"] = Text(book.Language),
            ["active"] = book.Active,
            ["inactiveDate"] = Date(book.InactiveDate),
            ["publisher"] = Text(book.Publisher),
            ["publisherDate"] = Date(book.PublisherDate)
        };
        await _books.InsertOneAsync(document, cancellationToken: cancellationToken);
        return Map(document);
    }

    public async Task<Book?> UpdateInventoryAsync(
        string id, int quantity, bool active, DateTimeOffset? inactiveDate,
        CancellationToken cancellationToken)
    {
        var update = Builders<BsonDocument>.Update
            .Set("quantity", quantity).Set("active", active).Set("inactiveDate", Date(inactiveDate));
        var document = await _books.FindOneAndUpdateAsync(
            Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(id)), update,
            new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
        return document is null ? null : Map(document);
    }

    private static Book Map(BsonDocument document) => new(
        document["_id"].AsObjectId.ToString(),
        ReadText(document, "title"), ReadText(document, "author"),
        document.GetValue("categoryId", 0).IsBsonNull ? 0 : document.GetValue("categoryId", 0).AsInt32,
        document.GetValue("quantity", 0).IsBsonNull ? 0 : document.GetValue("quantity", 0).AsInt32,
        ReadText(document, "description"), ReadText(document, "language"),
        !document.GetValue("active", false).IsBsonNull && document.GetValue("active", false).AsBoolean,
        ReadDate(document, "inactiveDate"), ReadText(document, "publisher"),
        ReadDate(document, "publisherDate"));

    private static string? ReadText(BsonDocument document, string key) =>
        document.TryGetValue(key, out var value) && !value.IsBsonNull ? value.AsString : null;

    private static DateTimeOffset? ReadDate(BsonDocument document, string key) =>
        document.TryGetValue(key, out var value) && !value.IsBsonNull
            ? new DateTimeOffset(value.ToUniversalTime()) : null;

    private static BsonValue Text(string? value) => value is null ? BsonNull.Value : new BsonString(value);

    private static BsonValue Date(DateTimeOffset? value) =>
        value is null ? BsonNull.Value : new BsonDateTime(value.Value.UtcDateTime);
}
