using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bookstore.Core.Authors;
using Bookstore.Core.Books;
using Bookstore.Tests.Authors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Bookstore.Tests.Http;

public sealed class AuthorEndpointsTests
{
    [Fact]
    public async Task CreateAsync_ValidAuthor_ReturnsProfileAndLocation()
    {
        await using var factory = new Factory();
        factory.Authors.Setup(repository => repository.InsertAsync(It.IsAny<Author>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorServiceTests.Example);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/authors",
            new CreateAuthorRequest("Name", new DateOnly(1980, 1, 2)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/authors/{TestBook.Id}", response.Headers.Location!.ToString());
        Assert.Equal(AuthorServiceTests.Example, await response.Content.ReadFromJsonAsync<Author>());
    }

    [Theory]
    [InlineData("/api/authors", false)]
    [InlineData("/api/authors?includeInactive=false", false)]
    [InlineData("/api/authors?includeInactive=true", true)]
    public async Task ListAsync_ValidQuery_ForwardsVisibility(string path, bool includeInactive)
    {
        await using var factory = new Factory();
        factory.Authors.Setup(repository => repository.ListAsync(includeInactive, It.IsAny<CancellationToken>()))
            .ReturnsAsync([AuthorServiceTests.Example]);
        using var client = factory.CreateClient();
        Assert.Equal(AuthorServiceTests.Example,
            Assert.Single((await client.GetFromJsonAsync<Author[]>(path))!));
        factory.Authors.VerifyAll();
    }

    [Theory]
    [InlineData("/api/authors/bad")]
    [InlineData("/api/authors?includeInactive=")]
    [InlineData("/api/authors?includeInactive=not-boolean")]
    public async Task GetAsync_InvalidInput_ReturnsCorrelatedProblem(string path)
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        await BookEndpointsTests.AssertProblemAsync(response, HttpStatusCode.BadRequest);
        factory.Authors.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FindAsync_KnownOrMissing_ReturnsExpectedStatus(bool found)
    {
        await using var factory = new Factory();
        factory.Authors.Setup(repository => repository.FindAsync(TestBook.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(found ? AuthorServiceTests.Example.Deactivate(TestBook.Now) : null);
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/authors/{TestBook.Id}");
        if (found)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await response.Content.ReadFromJsonAsync<Author>())!.Active);
        }
        else
            await BookEndpointsTests.AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(DeactivateAuthorResult.Deactivated, HttpStatusCode.NoContent)]
    [InlineData(DeactivateAuthorResult.AlreadyInactive, HttpStatusCode.NoContent)]
    [InlineData(DeactivateAuthorResult.Missing, HttpStatusCode.NotFound)]
    public async Task DeactivateAsync_Result_UsesExpectedStatus(
        DeactivateAuthorResult result, HttpStatusCode status)
    {
        await using var factory = new Factory();
        factory.Authors.Setup(repository => repository.DeactivateAsync(
            TestBook.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(result);
        using var client = factory.CreateClient();
        using var response = await client.DeleteAsync($"/api/authors/{TestBook.Id}");
        if (status == HttpStatusCode.NoContent)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsStringAsync());
        }
        else
            await BookEndpointsTests.AssertProblemAsync(response, status);
    }

    [Theory]
    [InlineData("{\"name\":\" \"}", HttpStatusCode.BadRequest)]
    [InlineData("{\"name\":\"Name\",\"email\":\"private-invalid-value\"}", HttpStatusCode.BadRequest)]
    [InlineData("{\"name\":\"Name\",\"birthDate\":\"not-a-date\"}", HttpStatusCode.BadRequest)]
    [InlineData("{\"name\":\"Name\",\"birthDate\":\"2000-01-01T00:00:00Z\"}", HttpStatusCode.BadRequest)]
    [InlineData("{\"name\":\"Name\",\"active\":null}", HttpStatusCode.BadRequest)]
    [InlineData("{", HttpStatusCode.BadRequest)]
    public async Task CreateAsync_InvalidBody_ReturnsSafeProblem(string json, HttpStatusCode status)
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/authors", new StringContent(json, Encoding.UTF8, "application/json"));
        await BookEndpointsTests.AssertProblemAsync(response, status);
        Assert.DoesNotContain("private-invalid-value", await response.Content.ReadAsStringAsync());
        factory.Authors.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateAsync_WrongMediaType_ReturnsUnsupportedMediaType()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/authors", new StringContent("Name"));
        await BookEndpointsTests.AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task ListAsync_RepositoryFailure_ReturnsRedactedProblem()
    {
        await using var factory = new Factory();
        factory.Authors.Setup(repository => repository.ListAsync(false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sensitive-database"));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/authors");
        await BookEndpointsTests.AssertProblemAsync(response, HttpStatusCode.InternalServerError);
        Assert.DoesNotContain("sensitive-database", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false, HttpStatusCode.NotFound)]
    [InlineData(true, HttpStatusCode.Conflict)]
    public async Task CreateBookAsync_ReferencedAuthorFailure_ReturnsDomainProblem(bool inactive, HttpStatusCode status)
    {
        await using var factory = new Factory();
        factory.Coordinator.Setup(coordinator => coordinator.CreateLinkedAsync(
            It.IsAny<Book>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(inactive ? new AuthorInactiveException() : new AuthorNotFoundException());
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/books",
            new CreateBookRequest("Book", AuthorId: TestBook.Id));
        await BookEndpointsTests.AssertProblemAsync(response, status);
    }

    [Fact]
    public async Task OpenApiAsync_ManagedAuthorContracts_DescribesRequiredFieldsAndStatuses()
    {
        await using var factory = new Factory();
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var root = json.RootElement;
        Assert.Equal("1.2.0", root.GetProperty("info").GetProperty("version").GetString());
        var paths = root.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/authors").GetProperty("post").GetProperty("responses")
            .GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _));
        Assert.True(paths.GetProperty("/api/authors/{id}").GetProperty("delete")
            .GetProperty("responses").TryGetProperty("204", out _));
        var schemas = root.GetProperty("components").GetProperty("schemas");
        var required = schemas.GetProperty("Author").GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).ToArray();
        Assert.Equal(new[] { "active", "createdAt", "id", "name", "updatedAt" }, required.Order().ToArray());
        Assert.Equal("date", schemas.GetProperty("Author").GetProperty("properties")
            .GetProperty("birthDate").GetProperty("format").GetString());
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public Mock<IAuthorRepository> Authors { get; } = new(MockBehavior.Strict);
        public Mock<IAuthorBookCoordinator> Coordinator { get; } = new(MockBehavior.Strict);
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAuthorRepository>();
                services.RemoveAll<IAuthorBookCoordinator>();
                services.AddScoped(_ => Authors.Object);
                services.AddScoped(_ => Coordinator.Object);
            });
    }
}
