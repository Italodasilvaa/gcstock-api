using Girus.Application.Audit;
using System.Globalization;
using Girus.Application.Abstractions;
using Girus.Application.Common;
using Girus.Application.Pdv;
using Girus.Domain.Entities;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class PosService(GirusDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IPosService
{
    public async Task<FinalizeSaleResult> FinalizeSaleAsync(FinalizeSaleRequest request, CancellationToken ct)
    {
        var rnd = Random.Shared;
        var saleId = "SALE-" + rnd.Next(1000, 10000);
        var receiptNumber = "NFCe-" + rnd.Next(100000, 1000000);
        var now = DateTime.UtcNow;

        var totalKg = request.Items.Sum(i => i.Unit == "kg" ? i.Qty : 0.5m * i.Qty);
        db.StockMovements.Add(new StockMovement
        {
            CompanyId = currentUser.CompanyId,
            Timestamp = now,
            Type = "baixa_pdv",
            Title = $"Venda {saleId} ({request.PaymentMode})",
            DocInfo = $"Tíquete Balcão 01 • {request.Total.ToString("C", new CultureInfo("pt-BR"))}",
            AmountKg = -totalKg,
            Description = $"Baixa realizada no PDV: {string.Join(", ", request.Items.Select(i => $"{i.Qty}x {i.Name}"))}",
            BadgeLabel = "PDV Finalizado",
            AuditLink = "Cupom SAT",
        });

        // Baixa automática de estoque: produto avulso desconta ele mesmo; kit desconta cada componente
        // proporcionalmente à quantidade de kits vendidos.
        var productIds = request.Items.Where(i => !string.IsNullOrEmpty(i.ProductId)).Select(i => i.ProductId!).Distinct().ToList();
        var kitIds = request.Items.Where(i => !string.IsNullOrEmpty(i.KitId)).Select(i => i.KitId!).Distinct().ToList();

        var products = productIds.Count > 0
            ? await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<string, Product>();
        var kits = kitIds.Count > 0
            ? await db.Kits.Include(k => k.Components).Where(k => k.CompanyId == currentUser.CompanyId && kitIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, ct)
            : new Dictionary<string, Kit>();

        foreach (var item in request.Items)
        {
            if (!string.IsNullOrEmpty(item.ProductId) && products.TryGetValue(item.ProductId, out var product))
            {
                product.Stock -= item.Qty;
                continue;
            }

            if (!string.IsNullOrEmpty(item.KitId) && kits.TryGetValue(item.KitId, out var kit))
            {
                kit.StockAvailable = Math.Max(0, kit.StockAvailable - item.Qty);
                foreach (var component in kit.Components)
                {
                    if (products.TryGetValue(component.ProductId, out var componentProduct))
                    {
                        componentProduct.Stock -= component.Quantity * item.Qty;
                        continue;
                    }

                    var loaded = await db.Products.FirstOrDefaultAsync(p => p.Id == component.ProductId && p.CompanyId == currentUser.CompanyId, ct);
                    if (loaded is not null)
                    {
                        loaded.Stock -= component.Quantity * item.Qty;
                        products[loaded.Id] = loaded;
                    }
                }
            }
        }

        // Pagamento a crédito/parcelado gera um recebível de adquirente para D+30.
        if (request.PaymentMode is "credito_vista" or "parcelado")
        {
            var dueDate = DateOnly.FromDateTime(now.AddDays(30));
            db.AccountsReceivables.Add(new AccountsReceivable
            {
                CompanyId = currentUser.CompanyId,
                ClientOrOrigin = $"Adquirente {request.CardFlag ?? "Mastercard"} - PDV Balcão 01",
                SubCategory = "Venda Crédito à Vista (TEF Balcão)",
                Reference = saleId,
                DueDate = dueDate,
                DueLabel = "Em 30 dias",
                GrossAmount = request.Total,
                FeeRetention = request.FeeAmount,
                NetAmount = request.NetAmount,
                Status = "a_vencer",
                Type = "adquirente",
                PaymentModeDesc = $"{request.CardFlag ?? "Mastercard"} D+30",
            });
        }

        // Venda a prazo: gera um título a receber vinculado ao cliente escolhido, para cobrança posterior.
        if (request.PaymentMode == "a_prazo")
        {
            var dueDate = request.DueDate ?? DateOnly.FromDateTime(now.AddDays(30));
            db.AccountsReceivables.Add(new AccountsReceivable
            {
                CompanyId = currentUser.CompanyId,
                ClientOrOrigin = request.ClientName ?? "Cliente Avulso (A Prazo)",
                SubCategory = "Venda a Prazo • PDV Balcão 01",
                Reference = saleId,
                DueDate = dueDate,
                DueLabel = DateLabelHelper.DueLabelFor(dueDate),
                GrossAmount = request.Total,
                FeeRetention = 0,
                NetAmount = request.Total,
                Status = "a_vencer",
                Type = "b2b",
                PaymentModeDesc = "Fiado / A Prazo (D+30)",
                ClientId = request.ClientId,
            });

            if (!string.IsNullOrEmpty(request.ClientId))
            {
                var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
                if (client is not null)
                {
                    client.TotalSpent += request.Total;
                }
            }
        }

        db.SaleRecords.Add(new SaleRecord
        {
            CompanyId = currentUser.CompanyId,
            SaleId = saleId,
            Timestamp = now,
            Total = request.Total,
            NetAmount = request.NetAmount,
            FeeAmount = request.FeeAmount,
            PaymentMode = request.PaymentMode,
            ClientName = request.ClientName,
            SoldBy = currentUser.UserName,
        });

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "vendeu", "Venda PDV",
            $"{saleId} • {request.Total.ToString("C", new CultureInfo("pt-BR"))}",
            request.ClientName ?? request.PaymentMode, ct);

        return new FinalizeSaleResult(saleId, receiptNumber);
    }
}
