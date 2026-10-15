using System.Text.Json;
using GCStock.Application.Purchasing;

namespace GCStock.IntegrationTests;

/// <summary>Garante o contrato do corpo de POST /compras/entrada com o que o front-end (gcstock-ui) envia.</summary>
public class PurchaseRequestBindingTests
{
    // Mesmas opções que o minimal API usa para ler JSON.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private const string ProductId = "0b2f6f3e-6a4b-4d0a-9d52-0f1d8a1c2e33";

    private static string Body(string itemExtras) => $$"""
        {
          "entryDate": "2026-10-14",
          "supplierName": "Frigorífico Sul",
          "supplierCnpj": "12.345.678/0001-90",
          "supplierCity": "São Paulo",
          "supplierSif": "SIF 123",
          "nfeNumber": "1001",
          "nfeSeries": "Série 1",
          "itemsCount": 1,
          "totalWeightKg": 20,
          "totalAmount": 200,
          "shippingCost": 0,
          "paymentCondition": "30 dias",
          "paymentMode": "a_prazo",
          "items": [
            {
              {{itemExtras}}
              "productId": "{{ProductId}}",
              "name": "Picanha",
              "code": "PIC-01",
              "quantity": 20,
              "unit": "kg",
              "unitCost": 10,
              "subtotal": 200,
              "expiryDate": "2026-12-01",
              "lotNumber": "L1",
              "isPerishable": true
            }
          ]
        }
        """;

    [Fact]
    public void Front_Payload_Without_Item_Id_Binds()
    {
        var request = JsonSerializer.Deserialize<SavePurchaseRequest>(Body(string.Empty), Options)!;

        var item = Assert.Single(request.Items!);
        Assert.Null(item.Id);
        Assert.Equal(Guid.Parse(ProductId), item.ProductId);
        Assert.Equal(new DateOnly(2026, 12, 1), item.ExpiryDate);
        Assert.Equal(new DateOnly(2026, 10, 14), request.EntryDate);
    }

    [Fact]
    public void Empty_String_Item_Id_Is_Rejected()
    {
        // A API é estrita de propósito: o front não deve enviar "" no lugar de um GUID.
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SavePurchaseRequest>(Body("\"id\": \"\","), Options));
    }
}
