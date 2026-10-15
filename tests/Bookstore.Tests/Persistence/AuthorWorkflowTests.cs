using System.Net;
using System.Net.Http.Json;
using Bookstore.Core.Authors;
using Bookstore.Core.Books;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Bookstore.Tests.Persistence;

[Trait("Category", "Database")]
[Collection("MongoDB")]
public sealed class AuthorWorkflowTests(MongoFixture fixture)
{
    [Fact]
    public async Task WorkflowAsync_ManagedAndLegacyAuthors_PreservesBooksAfterRetirement()
    {
        await using var factory = new Application(fixture.ConnectionString, $"authors_{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        using var firstResponse = await client.PostAsJsonAsync("/api/authors", new CreateAuthorRequest("Same Name"));
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = (await firstResponse.Content.ReadFromJsonAsync<Author>())!;
        using var secondResponse = await client.PostAsJsonAsync("/api/authors", new CreateAuthorRequest("Same Name"));
        var second = (await secondResponse.Content.ReadFromJsonAsync<Author>())!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first, await client.GetFromJsonAsync<Author>(firstResponse.Headers.Location));
        foreach (var id in new[] { first.Id, second.Id })
        {
            using var created = await client.PostAsJsonAsync("/api/books",
                new CreateBookRequest("Linked", CategoryId: "Programming", AuthorId: id));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var book = (await created.Content.ReadFromJsonAsync<Book>())!;
            Assert.Equal(id, book.AuthorId);
            Assert.Equal("Same Name", book.Author);
        }
        using var legacy = await client.PostAsJsonAsync("/api/books", new CreateBookRequest("Legacy", "Same Name"));
        Assert.Equal(HttpStatusCode.Created, legacy.StatusCode);
        Assert.Null((await legacy.Content.ReadFromJsonAsync<Book>())!.AuthorId);
        Assert.Single((await client.GetFromJsonAsync<Book[]>($"/api/books?authorId={first.Id}&author=sAmE&category=Programming"))!);
        Assert.Equal(3, (await client.GetFromJsonAsync<Book[]>("/api/books?author=same"))!.Length);
        Assert.Empty((await client.GetFromJsonAsync<Book[]>($"/api/books?authorId={first.Id}&category=Fiction"))!);
        using var deleted = await client.DeleteAsync($"/api/authors/{first.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var inactive = (await client.GetFromJsonAsync<Author>($"/api/authors/{first.Id}"))!;
        Assert.False(inactive.Active);
        Assert.Equal(second.Id, Assert.Single((await client.GetFromJsonAsync<Author[]>("/api/authors"))!).Id);
        Assert.Equal(2, (await client.GetFromJsonAsync<Author[]>("/api/authors?includeInactive=true"))!.Length);
        using var repeat = await client.DeleteAsync($"/api/authors/{first.Id}");
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
        Assert.Equal(inactive, await client.GetFromJsonAsync<Author>($"/api/authors/{first.Id}"));
        Assert.Single((await client.GetFromJsonAsync<Book[]>($"/api/books?authorId={first.Id}"))!);
        using var rejected = await client.PostAsJsonAsync("/api/books", new CreateBookRequest("No link", AuthorId: first.Id));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(3, (await client.GetFromJsonAsync<Book[]>("/api/books"))!.Length);
        using var invalidSingle = await client.GetAsync($"/api/books/single?name=Linked&authorId={first.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, invalidSingle.StatusCode);
    }

    private sealed class Application(string connectionString, string database) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Mongo:ConnectionString"] = connectionString,
                    ["Mongo:DatabaseName"] = database
                }));
    }
}
