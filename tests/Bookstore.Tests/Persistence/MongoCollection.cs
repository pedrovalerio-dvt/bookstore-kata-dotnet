namespace Bookstore.Tests.Persistence;

// Share one replica set while retaining a unique database per test.
[CollectionDefinition("MongoDB")]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>;
