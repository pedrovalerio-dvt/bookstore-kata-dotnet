using Bookstore.Core.Authors;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Bookstore.Infrastructure.Persistence;

/// <summary>Maps managed profiles to BSON and orders retirement against transactional book links.</summary>
public sealed class MongoAuthorRepository(IMongoDatabase database) : IAuthorRepository
{
    private readonly IMongoCollection<BsonDocument> _authors = database.GetCollection<BsonDocument>("authors");

    internal static TransactionOptions TransactionOptions { get; } = new(
        readConcern: ReadConcern.Snapshot,
        readPreference: ReadPreference.Primary,
        writeConcern: WriteConcern.WMajority);

    public async Task<Author> InsertAsync(Author author, CancellationToken cancellationToken)
    {
        var document = new BsonDocument
        {
            ["_id"] = ObjectId.GenerateNewId(),
            ["name"] = author.Name,
            ["active"] = author.Active,
            ["createdAt"] = new BsonDateTime(author.CreatedAt.UtcDateTime),
            ["updatedAt"] = new BsonDateTime(author.UpdatedAt.UtcDateTime),
            ["linkRevision"] = 0L
        };
        if (author.BirthDate is { } birthDate)
            document["birthDate"] = new BsonDateTime(birthDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        if (author.Nationality is not null)
            document["nationality"] = author.Nationality;
        if (author.Email is not null)
            document["email"] = author.Email;
        await _authors.InsertOneAsync(document, cancellationToken: cancellationToken);
        return Map(document);
    }

    public async Task<IReadOnlyList<Author>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var filter = includeInactive ? Builders<BsonDocument>.Filter.Empty :
            Builders<BsonDocument>.Filter.Eq("active", true);
        var documents = await _authors.Find(filter).ToListAsync(cancellationToken);
        return documents.Select(Map).ToArray();
    }

    public async Task<Author?> FindAsync(string id, CancellationToken cancellationToken)
    {
        var document = await _authors.Find(IdFilter(id)).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : Map(document);
    }

    public async Task<DeactivateAuthorResult> DeactivateAsync(
        string id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var session = await database.Client.StartSessionAsync(cancellationToken: cancellationToken);
        return await session.WithTransactionAsync(async (transaction, token) =>
        {
            var document = await _authors.Find(transaction, IdFilter(id)).FirstOrDefaultAsync(token);
            if (document is null)
                return DeactivateAuthorResult.Missing;
            var author = Map(document);
            var inactive = author.Deactivate(now);
            if (ReferenceEquals(author, inactive))
                return DeactivateAuthorResult.AlreadyInactive;

            // A real write to the guarded Author serializes retirement against linked creation.
            await _authors.UpdateOneAsync(transaction, IdFilter(id),
                Builders<BsonDocument>.Update.Set("active", false)
                    .Set("updatedAt", new BsonDateTime(inactive.UpdatedAt.UtcDateTime)),
                cancellationToken: token);
            return DeactivateAuthorResult.Deactivated;
        }, TransactionOptions, cancellationToken);
    }

    private static FilterDefinition<BsonDocument> IdFilter(string id) =>
        Builders<BsonDocument>.Filter.Eq("_id", ObjectId.Parse(id));

    private static Author Map(BsonDocument document) => new(
        document["_id"].AsObjectId.ToString(), document["name"].AsString,
        document.TryGetValue("birthDate", out var date) && !date.IsBsonNull
            ? DateOnly.FromDateTime(date.ToUniversalTime()) : null,
        Text(document, "nationality"), Text(document, "email"), document["active"].AsBoolean,
        new DateTimeOffset(document["createdAt"].ToUniversalTime()),
        new DateTimeOffset(document["updatedAt"].ToUniversalTime()));

    private static string? Text(BsonDocument document, string name) =>
        document.TryGetValue(name, out var value) && !value.IsBsonNull ? value.AsString : null;
}
