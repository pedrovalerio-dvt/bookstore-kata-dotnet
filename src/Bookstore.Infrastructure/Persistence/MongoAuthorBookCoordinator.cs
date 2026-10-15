using Bookstore.Core.Authors;
using Bookstore.Core.Books;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Bookstore.Infrastructure.Persistence;

/// <summary>Commits the active-Author guard and new Book together, with driver-managed transaction retries.</summary>
public sealed class MongoAuthorBookCoordinator(IMongoDatabase database) : IAuthorBookCoordinator
{
    public async Task<Book> CreateLinkedAsync(Book book, CancellationToken cancellationToken)
    {
        var authorId = ObjectId.Parse(book.AuthorId!);
        var id = ObjectId.GenerateNewId();
        var authors = database.GetCollection<BsonDocument>("authors");
        var books = database.GetCollection<BsonDocument>("books");
        using var session = await database.Client.StartSessionAsync(cancellationToken: cancellationToken);
        return await session.WithTransactionAsync(async (transaction, token) =>
        {
            // Snapshot reads alone allow write skew. Incrementing the same document retirement
            // writes forces one transaction to retry against the committed active state.
            var author = await authors.FindOneAndUpdateAsync(transaction,
                Builders<BsonDocument>.Filter.Eq("_id", authorId) &
                Builders<BsonDocument>.Filter.Eq("active", true),
                Builders<BsonDocument>.Update.Inc("linkRevision", 1L),
                new FindOneAndUpdateOptions<BsonDocument> { ReturnDocument = ReturnDocument.After }, token);
            if (author is null)
            {
                var exists = await authors.Find(transaction,
                    Builders<BsonDocument>.Filter.Eq("_id", authorId)).AnyAsync(token);
                if (!exists)
                    throw new AuthorNotFoundException();
                throw new AuthorInactiveException();
            }

            var document = MongoBookRepository.CreateDocument(
                book with { AuthorId = authorId.ToString(), Author = author["name"].AsString }, id);
            await books.InsertOneAsync(transaction, document, cancellationToken: token);
            return MongoBookRepository.Map(document);
        }, MongoAuthorRepository.TransactionOptions, cancellationToken);
    }
}
