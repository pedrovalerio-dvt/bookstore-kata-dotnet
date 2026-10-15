using System.Net;
using System.Net.Http.Json;
using Bookstore.Core.Books;
using Bookstore.Tests.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Bookstore.Tests.Persistence;

[Trait("Category", "Database")]
public sealed class BookWorkflowTests(MongoFixture fixture) : IClassFixture<MongoFixture>
{
    [Fact]
    public async Task WorkflowAsync_RealMongoDatabase_CreatesFindsFiltersAndUpdatesBook()
    {
        await using var factory = new MongoApplication(fixture.ConnectionString, $"workflow_{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        using var created = await client.PostAsJsonAsync("/api/books",
            new CreateBookRequest("Book", "Author", "Programming", 3, PublisherDate: TestBook.Now));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var book = await created.Content.ReadFromJsonAsync<Book>();
        Assert.NotNull(book);
        Assert.True(book.Active);
        Assert.Null(book.InactiveDate);

        var found = await client.GetFromJsonAsync<Book>(created.Headers.Location);
        Assert.Equal(book, found);
        Assert.Equal(book, await client.GetFromJsonAsync<Book>("/api/books/single?name=Book"));
        Assert.Equal(book, Assert.Single((await client.GetFromJsonAsync<Book[]>("/api/books?category=Programming"))!));
        Assert.Empty((await client.GetFromJsonAsync<Book[]>("/api/books?category=Fiction"))!);

        using var updated = await client.PutAsJsonAsync("/api/books", new UpdateBookRequest(book.Id, 9, false));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var inactive = await updated.Content.ReadFromJsonAsync<Book>();
        Assert.NotNull(inactive);
        Assert.Equal(book.Title, inactive.Title);
        Assert.Equal(6, inactive.CategoryId);
        Assert.Equal(9, inactive.Quantity);
        Assert.False(inactive.Active);
        Assert.NotNull(inactive.InactiveDate);
        Assert.Equal(inactive, await client.GetFromJsonAsync<Book>(created.Headers.Location));

        using var missing = await client.PutAsJsonAsync("/api/books", new UpdateBookRequest(TestBook.Id, 1, true));
        await BookEndpointsTests.AssertProblemAsync(missing, HttpStatusCode.NotFound);
    }

    private sealed class MongoApplication(string connectionString, string databaseName) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Mongo:ConnectionString"] = connectionString,
                    ["Mongo:DatabaseName"] = databaseName
                }));
        }
    }
}
