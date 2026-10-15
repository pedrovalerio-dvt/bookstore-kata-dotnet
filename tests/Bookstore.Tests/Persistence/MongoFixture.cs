using System.Text;
using DotNet.Testcontainers.Configurations;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace Bookstore.Tests.Persistence;

public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7.0.28-jammy")
        .WithReplicaSet("rs0")
        .Build();
    private IMongoClient? _client;

    public string ConnectionString => new MongoUrlBuilder(_container.GetConnectionString())
    {
        DirectConnection = true,
        ReplicaSetName = "rs0"
    }.ToString();
    public IMongoDatabase CreateDatabase() =>
        (_client ?? throw new InvalidOperationException("MongoDB fixture is not initialized."))
        .GetDatabase($"bookstore_test_{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _client = new MongoClient(ConnectionString);
        var database = CreateDatabase();
        await database.CreateCollectionAsync("transaction_probe");
        using var session = await _client.StartSessionAsync();
        await session.WithTransactionAsync(async (transaction, cancellationToken) =>
        {
            await database.GetCollection<BsonDocument>("transaction_probe").InsertOneAsync(
                transaction, new BsonDocument("_id", ObjectId.GenerateNewId()), cancellationToken: cancellationToken);
            return true;
        }, new TransactionOptions(
            ReadConcern.Snapshot, ReadPreference.Primary, WriteConcern.WMajority));
        await _client.DropDatabaseAsync(database.DatabaseNamespace.DatabaseName);
    }

    public async Task<(long ExitCode, string Output)> RunLegacyMappingAsync(
        IMongoDatabase database, string manifest, bool apply = false, CancellationToken cancellationToken = default)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "scripts", "link-legacy-authors.js")))
        {
            root = root.Parent;
        }

        var scriptPath = Path.Combine(
            (root ?? throw new InvalidOperationException("Mapping script was not found.")).FullName,
            "scripts", "link-legacy-authors.js");
        var directory = $"/data/author-mapping-{Guid.NewGuid():N}";
        var created = await _container.ExecAsync(["mkdir", "-m", "700", directory], cancellationToken);
        if (created.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not create protected mapping directory.");
        }

        try
        {
            var scriptConnectionString = new MongoUrlBuilder(ConnectionString)
            {
                Server = new MongoServerAddress("127.0.0.1", 27017),
                DatabaseName = database.DatabaseNamespace.DatabaseName,
                AuthenticationSource = "admin"
            }.ToString();
            await _container.CopyAsync(Encoding.UTF8.GetBytes(manifest), $"{directory}/manifest.json",
                fileMode: UnixFileModes.UserRead | UnixFileModes.UserWrite, ct: cancellationToken);
            await _container.CopyAsync(await File.ReadAllBytesAsync(scriptPath, cancellationToken),
                $"{directory}/link-legacy-authors.js",
                fileMode: UnixFileModes.UserRead | UnixFileModes.UserWrite, ct: cancellationToken);
            var result = await _container.ExecAsync(
            [
                "env",
                $"BOOKSTORE_MAPPING_MANIFEST={directory}/manifest.json",
                $"BOOKSTORE_MAPPING_MODE={(apply ? "apply" : "dry-run")}",
                "mongosh",
                scriptConnectionString,
                "--quiet", "--file", $"{directory}/link-legacy-authors.js"
            ], cancellationToken);
            return (result.ExitCode ?? -1, result.Stdout + result.Stderr);
        }
        finally
        {
            await _container.ExecAsync(["rm", "-f", $"{directory}/manifest.json", $"{directory}/link-legacy-authors.js"]);
            await _container.ExecAsync(["rmdir", directory]);
        }
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        await _container.DisposeAsync();
    }
}
