namespace TenExCards.E2E;

/// <summary>
/// One application for the whole suite. AppUnderTest binds a FIXED port, and xUnit runs separate
/// collections in parallel — so a per-class fixture means two processes racing for one port the
/// moment a second test class exists. Sharing the fixture serialises the classes instead.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppUnderTest>
{
    public const string Name = "app under test";
}
