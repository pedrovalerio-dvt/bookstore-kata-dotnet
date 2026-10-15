using Bookstore.Core.Books;

namespace Bookstore.Tests;

internal static class TestBook
{
    public const string Id = "507f1f77bcf86cd799439011";
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    public static Book Create() =>
        new(Id, "Test Driven Development", "Kent Beck", 6, 3,
            "A book about TDD", "English", true, null, "Addison-Wesley", Now.AddYears(-20));
}
