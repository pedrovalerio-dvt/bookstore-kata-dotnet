using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bookstore.Core.Books;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Bookstore.Tests.Http;

public sealed class BookEndpointsTests
{
    [Theory]
    [InlineData("/api/books/health")]
    [InlineData("/api/books/health/")]
    public async Task HealthAsync_ApplicationRunning_ReturnsEmptyOk(string route)
    {
        await using var factory = new BookstoreFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.Contains("X-Trace-Id"));
        factory.Repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/api/books", null)]
    [InlineData("/api/books?category=Programming", 6)]
    public async Task ListAsync_ValidFilter_ReturnsBooks(string route, int? categoryId)
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.ListAsync(categoryId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([TestBook.Create()]);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var books = await response.Content.ReadFromJsonAsync<Book[]>();
        Assert.Equal(TestBook.Create(), Assert.Single(Assert.IsType<Book[]>(books)));
    }

    [Theory]
    [InlineData("/api/books?author=kEnT", null, "kEnT")]
    [InlineData("/api/books?author=Beck&category=Programming", 6, "Beck")]
    [InlineData("/api/books?author=C%2B%2B%20%28Jr.%29", null, "C++ (Jr.)")]
    [InlineData("/api/books?author=%20Kent%20", null, " Kent ")]
    public async Task ListAsync_AuthorQuery_ReturnsAllRepositoryMatches(
        string route, int? categoryId, string author)
    {
        await using var factory = new BookstoreFactory();
        Book[] expected =
        [
            TestBook.Create(),
            TestBook.Create() with { Id = "507f1f77bcf86cd799439012", Title = "Another book", Active = false }
        ];
        factory.Repository.Setup(repository => repository.ListAsync(categoryId, author, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, await response.Content.ReadFromJsonAsync<Book[]>());
        factory.Repository.VerifyAll();
    }

    [Fact]
    public async Task ListAsync_UnknownAuthor_ReturnsEmptyOk()
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.ListAsync(null, "nobody", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/books?author=nobody");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<Book[]>())!);
    }

    [Fact]
    public async Task ListAsync_AuthorSearchFailure_ReturnsSafeProblemDetails()
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.ListAsync(null, "Kent", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive query diagnostics"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/books?author=Kent");

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("sensitive", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ListAsync_EmptyCatalogue_ReturnsEmptyArray()
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        using var client = factory.CreateClient();
        Assert.Empty((await client.GetFromJsonAsync<Book[]>("/api/books"))!);
    }

    [Theory]
    [InlineData("/api/books?category=unknown")]
    [InlineData("/api/books?category=")]
    [InlineData("/api/books?author=")]
    [InlineData("/api/books?author=%20%20")]
    [InlineData("/api/books?author=%09%0A")]
    [InlineData("/api/books?author=Kent&category=unknown")]
    [InlineData("/api/books/single")]
    [InlineData("/api/books/single?id=bad")]
    [InlineData("/api/books/single?name=Book&author=Author")]
    [InlineData("/api/books/single?name=Book&author=")]
    [InlineData("/api/books/single?name=Book&id=507f1f77bcf86cd799439011")]
    public async Task GetAsync_InvalidFilter_ReturnsProblemDetails(string route)
    {
        await using var factory = new BookstoreFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(route);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        factory.Repository.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/api/books/single?id=507f1f77bcf86cd799439011", TestBook.Id, null)]
    [InlineData("/api/books/single?name=Test%20Driven%20Development", null, "Test Driven Development")]
    public async Task FindAsync_ExistingBook_ReturnsCompleteBook(string route, string? id, string? name)
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.FindAsync(new BookQuery(id, name), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestBook.Create());
        using var client = factory.CreateClient();
        Assert.Equal(TestBook.Create(), await client.GetFromJsonAsync<Book>(route));
    }

    [Fact]
    public async Task FindAsync_MissingBook_ReturnsNotFoundProblem()
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.FindAsync(It.IsAny<BookQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Book?)null);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/books/single?id={TestBook.Id}");
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateAsync_ValidJson_ReturnsCreatedAndLocation()
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.InsertAsync(
                It.Is<Book>(book => book.Active && book.CategoryId == 6 && book.Quantity == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestBook.Create());
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/books",
            new { title = "Book", categoryId = "Programming" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/books/single?id={TestBook.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(TestBook.Create(), await response.Content.ReadFromJsonAsync<Book>());
        factory.Repository.VerifyAll();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"title\":\" \"}")]
    [InlineData("{\"title\":\"Book\",\"quantity\":-1}")]
    [InlineData("{\"title\":\"Book\",\"categoryId\":\"6\"}")]
    [InlineData("{\"title\":\"Book\",\"active\":true,\"inactiveDate\":\"2026-10-15T09:00:00Z\"}")]
    [InlineData("{\"title\":\"Book\",\"publisherDate\":\"bad-date\"}")]
    [InlineData("{\"title\":\"Book\",\"quantity\":\"not-an-integer\"}")]
    [InlineData("{\"title\":\"Book\",\"quantity\":\"3\"}")]
    [InlineData("null")]
    [InlineData("{")]
    public async Task CreateAsync_InvalidBody_ReturnsBadRequestProblem(string body)
    {
        await using var factory = new BookstoreFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/books", content);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        factory.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_UnsupportedMediaType_ReturnsProblem()
    {
        await using var factory = new BookstoreFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent("Book", Encoding.UTF8, "text/plain");
        using var response = await client.PostAsync("/api/books", content);
        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task UpdateAsync_ExistingBook_ReturnsOkAndCompleteBook()
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.UpdateInventoryAsync(
                TestBook.Id, 5, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(TestBook.Create() with { Quantity = 5 });
        using var client = factory.CreateClient();

        using var response = await client.PutAsJsonAsync("/api/books", new { id = TestBook.Id, quantity = 5, active = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var book = await response.Content.ReadFromJsonAsync<Book>();
        Assert.NotNull(book);
        Assert.Equal(6, book.CategoryId);
        Assert.Equal("Test Driven Development", book.Title);
        Assert.Equal(5, book.Quantity);
    }

    [Fact]
    public async Task UpdateAsync_MissingBook_ReturnsNotFoundProblem()
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.UpdateInventoryAsync(
                TestBook.Id, 0, true, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Book?)null);
        using var client = factory.CreateClient();
        using var response = await client.PutAsJsonAsync("/api/books", new { id = TestBook.Id, quantity = 0, active = true });
        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"507f1f77bcf86cd799439011\"}")]
    [InlineData("{\"id\":\"bad\",\"quantity\":0,\"active\":true}")]
    [InlineData("{\"id\":\"507f1f77bcf86cd799439011\",\"quantity\":-1,\"active\":true}")]
    public async Task UpdateAsync_InvalidBody_ReturnsBadRequestProblem(string body)
    {
        await using var factory = new BookstoreFactory();
        using var client = factory.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PutAsync("/api/books", content);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
        factory.Repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListAsync_UnexpectedFailure_DoesNotDiscloseException()
    {
        await using var factory = new BookstoreFactory();
        factory.Repository.Setup(repository => repository.ListAsync(null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive database diagnostics"));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/books");
        await AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("sensitive", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/books?category=unknown", HttpStatusCode.BadRequest)]
    [InlineData("/unknown", HttpStatusCode.NotFound)]
    public async Task GetAsync_JsonExcludedFromAccept_ReturnsProblemDetailsForInvalidInput(
        string route, HttpStatusCode expected)
    {
        await using var factory = new BookstoreFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");
        using var response = await client.GetAsync(route);
        await AssertProblemAsync(response, expected);
    }

    [Theory]
    [InlineData("/unknown", "GET", HttpStatusCode.NotFound)]
    [InlineData("/api/books", "DELETE", HttpStatusCode.MethodNotAllowed)]
    public async Task SendAsync_RoutingFailure_ReturnsProblem(string route, string method, HttpStatusCode expected)
    {
        await using var factory = new BookstoreFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        using var response = await client.SendAsync(request);
        await AssertProblemAsync(response, expected);
    }

    [Fact]
    public async Task OpenApiAsync_ApplicationRunning_DescribesAllOperations()
    {
        await using var factory = new BookstoreFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;
        Assert.StartsWith("3.", root.GetProperty("openapi").GetString());
        Assert.Equal("1.1.0", root.GetProperty("info").GetProperty("version").GetString());
        var paths = root.GetProperty("paths");
        var books = paths.EnumerateObject().Single(path => path.Name.TrimEnd('/') == "/api/books").Value;
        Assert.True(books.TryGetProperty("get", out _));
        Assert.True(books.TryGetProperty("post", out _));
        Assert.True(books.TryGetProperty("put", out _));
        var author = books.GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "author");
        Assert.Equal("query", author.GetProperty("in").GetString());
        Assert.False(author.TryGetProperty("required", out var required) && required.GetBoolean());
        Assert.Equal("string", author.GetProperty("schema").GetProperty("type").GetString());
        Assert.Contains("case-insensitive", author.GetProperty("description").GetString());
        Assert.True(books.GetProperty("post").GetProperty("responses").GetProperty("201")
            .GetProperty("headers").TryGetProperty("Location", out _));
        Assert.True(books.GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("headers").TryGetProperty("X-Trace-Id", out _));
        Assert.True(paths.GetProperty("/api/books/single").TryGetProperty("get", out _));
        Assert.True(paths.GetProperty("/api/books/health").TryGetProperty("get", out _));
        Assert.Equal("/", Assert.Single(root.GetProperty("servers").EnumerateArray()).GetProperty("url").GetString());
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var bookRequired = schemas.GetProperty("Book").GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        Assert.Equal(new string?[] { "id", "categoryId", "quantity", "active" }, bookRequired);
        Assert.False(schemas.GetProperty("CreateBookRequest").GetProperty("properties")
            .GetProperty("title").TryGetProperty("nullable", out var titleNullable) && titleNullable.GetBoolean());
        var update = schemas.GetProperty("UpdateBookRequest");
        Assert.Equal(3, update.GetProperty("required").GetArrayLength());
        foreach (var name in new[] { "id", "quantity", "active" })
        {
            Assert.False(update.GetProperty("properties").GetProperty(name)
                .TryGetProperty("nullable", out var nullable) && nullable.GetBoolean());
        }
        Assert.Equal("integer", update.GetProperty("properties").GetProperty("quantity").GetProperty("type").GetString());
        Assert.Equal(6, schemas.GetProperty("ProblemDetails").GetProperty("required").GetArrayLength());
        Assert.True(schemas.GetProperty("ProblemDetails").GetProperty("properties").TryGetProperty("traceId", out _));
    }

    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    public async Task ScalarAsync_Environment_GatesDocumentationUi(string environment, HttpStatusCode expected)
    {
        await using var factory = new BookstoreFactory(environment);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/scalar/v1");
        Assert.Equal(expected, response.StatusCode);
    }

    internal static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)expected, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Type));
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
        var traceId = Assert.Single(response.Headers.GetValues("X-Trace-Id"));
        Assert.Equal($"urn:trace:{traceId}", problem.Instance);
        Assert.Equal(traceId, ((JsonElement)problem.Extensions["traceId"]!).GetString());
    }
}
