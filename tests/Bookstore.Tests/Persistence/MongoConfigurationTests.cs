using Bookstore.Core.Books;
using Bookstore.Infrastructure;
using Bookstore.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Bookstore.Tests.Persistence;

public sealed class MongoConfigurationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-a-mongodb-uri")]
    [InlineData("https://localhost")]
    public void Resolve_InvalidConnectionString_RejectsConfiguration(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddBookstorePersistence(Configuration(connectionString));
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<MongoOptions>>().Value);
    }

    [Fact]
    public void Resolve_ValidConfiguration_ReusesClientAndScopesRepository()
    {
        var services = new ServiceCollection();
        services.AddBookstorePersistence(Configuration("mongodb://localhost:27017"));
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(first.ServiceProvider.GetRequiredService<IMongoClient>(),
            second.ServiceProvider.GetRequiredService<IMongoClient>());
        Assert.Equal("bookstore_config_test",
            provider.GetRequiredService<IMongoDatabase>().DatabaseNamespace.DatabaseName);
        Assert.Same(first.ServiceProvider.GetRequiredService<IBookRepository>(),
            first.ServiceProvider.GetRequiredService<IBookRepository>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<IBookRepository>(),
            second.ServiceProvider.GetRequiredService<IBookRepository>());
    }

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Mongo:ConnectionString"] = connectionString,
            ["Mongo:DatabaseName"] = "bookstore_config_test"
        }).Build();
}
