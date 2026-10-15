using GCStock.Application.Access;

namespace GCStock.IntegrationTests;

public class PermissionCatalogTests
{
    [Fact]
    public void Catalog_Contains_Purchase_Edit()
    {
        var permission = Assert.Single(Permissions.All, p => p.Key == Permissions.PurchasesEdit);
        Assert.Equal("compras.editar", permission.Key);
        Assert.Equal("Editar pedidos de compra", permission.Label);
    }

    [Theory]
    [InlineData("compras.editar", true)]
    [InlineData("COMPRAS.EDITAR", false)]
    [InlineData("inexistente", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_Catalog_Keys_Are_Valid(string? key, bool expected)
    {
        Assert.Equal(expected, Permissions.IsValid(key));
    }

    [Fact]
    public void Catalog_Keys_Are_Unique()
    {
        Assert.Equal(Permissions.All.Count, Permissions.AllKeys.Distinct().Count());
    }
}
