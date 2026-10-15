using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Bookstore.Tests.Persistence;

public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7.0.28-jammy").Build();
    private IMongoClient? _client;

    public string ConnectionString => _container.GetConnectionString();
    public IMongoDatabase CreateDatabase() =>
        (_client ?? throw new InvalidOperationException("MongoDB fixture is not initialized."))
        .GetDatabase($"bookstore_test_{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _client = new MongoClient(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        await _container.DisposeAsync();
    }
}
