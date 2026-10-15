using Bookstore.Core.Books;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Bookstore.Tests.Http;

public sealed class BookstoreFactory(string environment = "Development") : WebApplicationFactory<Program>
{
    public Mock<IBookRepository> Repository { get; } = new(MockBehavior.Strict);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IBookRepository>();
            services.AddScoped(_ => Repository.Object);
        });
    }
}
