using Xunit;

namespace RoslynMcpServer.Tests.Testing.Il;

/// <summary>Shares one compiled fixture solution between the IL graph test classes.</summary>
[CollectionDefinition(Name)]
public sealed class IlShopCollection : ICollectionFixture<IlShopFixture>
{
    internal const string Name = "IlShop";
}
