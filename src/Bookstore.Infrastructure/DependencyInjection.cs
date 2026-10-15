using Bookstore.Core.Authors;
using Bookstore.Core.Books;
using Bookstore.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Bookstore.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBookstorePersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MongoOptions>().Bind(configuration.GetSection(MongoOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options =>
            {
                try
                {
                    _ = new MongoUrl(options.ConnectionString);
                    return true;
                }
                catch (Exception exception) when (
                    exception is FormatException or MongoConfigurationException or ArgumentException)
                {
                    return false;
                }
            }, "Mongo:ConnectionString must be a valid MongoDB connection string.")
            .ValidateOnStart();
        services.AddSingleton<IMongoClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MongoOptions>>().Value;
            var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(settings);
        });
        services.AddSingleton(provider => provider.GetRequiredService<IMongoClient>().GetDatabase(
            provider.GetRequiredService<IOptions<MongoOptions>>().Value.DatabaseName));
        services.AddScoped<IBookRepository, MongoBookRepository>();
        services.AddScoped<IAuthorRepository, MongoAuthorRepository>();
        services.AddScoped<IAuthorBookCoordinator, MongoAuthorBookCoordinator>();
        return services;
    }
}
