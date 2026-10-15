using System.Net;
using System.Net.Http.Json;
using Bookstore.Core.Books;
using Bookstore.Tests.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Bookstore.Tests.Persistence;

[Trait("Category", "Database")]
[Collection("MongoDB")]
public sealed class BookWorkflowTests(MongoFixture fixture)
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

    [Fact]
    public async Task SearchAsync_AuthorAndCategory_ReturnsIntersectionThroughRealHttpAndMongo()
    {
        await using var factory = new MongoApplication(fixture.ConnectionString, $"author_{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        CreateBookRequest[] requests =
        [
            new("Programming book", "Kent Beck", "Programming", 3),
            new("Inactive programming book", "KENT BECK", "Programming", 0, Active: false),
            new("Fiction book", "Kent Beck", "Fiction"),
            new("Another writer", "Other author", "Programming"),
            new("Authorless book", CategoryId: "Programming"),
            new("Literal symbols", "C++ (Jr.)", "Programming")
        ];
        foreach (var request in requests)
        {
            using var created = await client.PostAsJsonAsync("/api/books", request);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var allAuthorBooks = await client.GetFromJsonAsync<Book[]>("/api/books?author=eNt%20b");
        Assert.NotNull(allAuthorBooks);
        Assert.Equal(3, allAuthorBooks.Length);
        var programming = await client.GetFromJsonAsync<Book[]>("/api/books?author=BECK&category=Programming");
        Assert.NotNull(programming);
        Assert.Equal(2, programming.Length);
        Assert.All(programming, book => Assert.Equal(6, book.CategoryId));
        Assert.Contains(programming, book => !book.Active);
        Assert.Empty((await client.GetFromJsonAsync<Book[]>("/api/books?author=nobody"))!);
        Assert.Empty((await client.GetFromJsonAsync<Book[]>("/api/books?author=Kent&category=Science"))!);
        var literal = await client.GetFromJsonAsync<Book[]>("/api/books?author=C%2B%2B%20%28Jr.%29");
        Assert.Equal("Literal symbols", Assert.Single(literal!).Title);
        Assert.Equal(6, (await client.GetFromJsonAsync<Book[]>("/api/books"))!.Length);

        using var invalidAuthor = await client.GetAsync("/api/books?author=%20");
        await BookEndpointsTests.AssertProblemAsync(invalidAuthor, HttpStatusCode.BadRequest);
        using var invalidCategory = await client.GetAsync("/api/books?author=Kent&category=unknown");
        await BookEndpointsTests.AssertProblemAsync(invalidCategory, HttpStatusCode.BadRequest);
        using var single = await client.GetAsync("/api/books/single?author=Kent");
        await BookEndpointsTests.AssertProblemAsync(single, HttpStatusCode.BadRequest);
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
