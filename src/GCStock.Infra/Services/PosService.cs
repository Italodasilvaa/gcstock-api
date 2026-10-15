using GCStock.Application.Audit;
using System.Globalization;
using GCStock.Application.Abstractions;
using GCStock.Application.Access;
using GCStock.Application.Common;
using GCStock.Application.Pdv;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class PosService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit, IPermissionChecker permissions) : IPosService
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>
    /// Preço negativo é inválido. Vender por um preço diferente do cadastro (produto: preço de venda; kit: preço
    /// sugerido) exige a permissão "vendas.alterar-preco" — o front esconde a edição, mas a regra vale aqui também.
    /// </summary>
    private async Task EnsurePricesAllowedAsync(
        FinalizeSaleRequest request, Dictionary<Guid, Product> products, Dictionary<Guid, Kit> kits, CancellationToken ct)
    {
        if (request.Items.Any(i => i.Price < 0))
        {
            throw new ArgumentException("O preço de um item não pode ser negativo.");
        }

        var changed = request.Items.Any(i =>
            (i.ProductId.HasValue && products.TryGetValue(i.ProductId.Value, out var product) && Math.Abs(i.Price - product.SellingPrice) > 0.005m)
            || (i.KitId.HasValue && kits.TryGetValue(i.KitId.Value, out var kit) && Math.Abs(i.Price - kit.SuggestedPrice) > 0.005m));

        if (changed && !await permissions.HasAsync(Permissions.SalesEditPrice, ct))
        {
            throw new UnauthorizedAccessException("Você não tem permissão para alterar o preço na venda.");
        }
    }

    /// <summary>
    /// Bloqueia a venda acima do estoque: soma o que cada produto precisa (avulso + componentes dos kits vendidos) e
    /// recusa se algum passar do saldo, para o estoque nunca ficar negativo.
    /// </summary>
    private static void EnsureStockAvailable(FinalizeSaleRequest request, Dictionary<Guid, Product> products, Dictionary<Guid, Kit> kits)
    {
        var required = new Dictionary<Guid, decimal>();
        void Add(Guid productId, decimal qty) => required[productId] = required.GetValueOrDefault(productId) + qty;

        foreach (var item in request.Items)
        {
            if (item.ProductId.HasValue && products.ContainsKey(item.ProductId.Value))
            {
                Add(item.ProductId.Value, item.Qty);
            }
            else if (item.KitId.HasValue && kits.TryGetValue(item.KitId.Value, out var kit))
            {
                foreach (var component in kit.Components) Add(component.ProductId, component.Quantity * item.Qty);
            }
        }

        var shortages = required
            .Where(r => products.TryGetValue(r.Key, out var p) && r.Value > p.Stock)
            .Select(r =>
            {
                var p = products[r.Key];
                return $"{p.Name} (disponível {Format(Math.Max(0, p.Stock))} {p.Unit}, pedido {Format(r.Value)} {p.Unit})";
            })
            .ToList();

        if (shortages.Count > 0)
        {
            throw new InvalidOperationException("Estoque insuficiente: " + string.Join("; ", shortages) + ".");
        }

        static string Format(decimal value) => value.ToString("0.###", PtBr);
    }

    /// <summary>Consome os lotes do produto (vence primeiro, sai primeiro) e guarda de onde saiu, para devolver no cancelamento.</summary>
    private async Task ConsumeLotsAsync(SaleItem saleItem, Guid productId, decimal quantity, CancellationToken ct)
    {
        foreach (var (lotId, qty) in await StockLots.ConsumeAsync(db, currentUser.CompanyId, productId, quantity, ct))
        {
            saleItem.Lots.Add(new SaleItemLot { StockBatchId = lotId, ProductId = productId, Qty = qty });
        }
    }

    public async Task<FinalizeSaleResult> FinalizeSaleAsync(FinalizeSaleRequest request, CancellationToken ct)
    {
        if (request.Items.Count == 0 || request.Items.Any(i => i.Qty <= 0))
        {
            throw new ArgumentException("A venda precisa ter ao menos um item e todas as quantidades devem ser maiores que zero.");
        }

        var rnd = Random.Shared;
        var saleId = Guid.CreateVersion7();
        var receiptNumber = "NFCe-" + rnd.Next(100000, 1000000);
        var now = DateTime.UtcNow;

        // Tudo-ou-nada, e os produtos ficam travados até o fim: duas vendas simultâneas do mesmo produto não
        // conseguem, juntas, passar do estoque.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Baixa automática de estoque: produto avulso desconta ele mesmo; kit desconta cada componente
        // proporcionalmente à quantidade de kits vendidos.
        var kitIds = request.Items.Where(i => i.KitId.HasValue).Select(i => i.KitId!.Value).Distinct().ToList();
        var kits = kitIds.Count > 0
            ? await db.Kits.Include(k => k.Components).Where(k => k.CompanyId == currentUser.CompanyId && kitIds.Contains(k.Id)).ToDictionaryAsync(k => k.Id, ct)
            : new Dictionary<Guid, Kit>();

        var productIds = request.Items.Where(i => i.ProductId.HasValue).Select(i => i.ProductId!.Value)
            .Concat(kits.Values.SelectMany(k => k.Components.Select(c => c.ProductId)))
            .Distinct()
            .ToArray();

        if (productIds.Length > 0)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Products\" WHERE \"Id\" = ANY({productIds}) AND \"CompanyId\" = {currentUser.CompanyId} FOR UPDATE", ct);
        }

        var products = productIds.Length > 0
            ? await db.Products.Where(p => p.CompanyId == currentUser.CompanyId && productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<Guid, Product>();

        await EnsurePricesAllowedAsync(request, products, kits, ct);
        EnsureStockAvailable(request, products, kits);

        var saleItems = new List<SaleItem>();

        foreach (var line in request.Items)
        {
            // Unidade real do cadastro do produto (kg, pct, un...); kit é vendido por "un".
            var lineUnit = line.ProductId.HasValue && products.TryGetValue(line.ProductId.Value, out var lineProduct)
                ? lineProduct.Unit
                : line.KitId.HasValue ? "un" : line.Unit;

            // Custo médio do estoque NO MOMENTO da venda (a venda não altera o custo médio); kit = soma dos componentes.
            var unitCost = line.ProductId.HasValue && products.TryGetValue(line.ProductId.Value, out var costProduct)
                ? costProduct.CostPrice
                : line.KitId.HasValue && kits.TryGetValue(line.KitId.Value, out var costKit)
                    ? costKit.Components.Sum(c => products.TryGetValue(c.ProductId, out var cp) ? c.Quantity * cp.CostPrice : 0m)
                    : 0m;

            saleItems.Add(new SaleItem
            {
                ProductId = line.ProductId,
                KitId = line.KitId,
                Name = line.Name,
                Qty = line.Qty,
                Unit = lineUnit,
                UnitPrice = line.Price,
                Subtotal = Math.Round(line.Qty * line.Price, 2, MidpointRounding.AwayFromZero),
                PriceKnown = true,
                UnitCost = unitCost,
                TotalCost = Math.Round(line.Qty * unitCost, 2, MidpointRounding.AwayFromZero),
            });

            db.StockMovements.Add(new StockMovement
            {
                CompanyId = currentUser.CompanyId,
                Timestamp = now,
                Type = "baixa_pdv",
                Title = $"Venda {receiptNumber}: {line.Name}",
                DocInfo = $"Tíquete Balcão 01 • {request.PaymentMode} • {request.Total.ToString("C", PtBr)}",
                AmountKg = -line.Qty,
                Unit = lineUnit,
                Description = $"Baixa realizada no PDV: {line.Qty}x {line.Name}",
                BadgeLabel = "PDV Finalizado",
                AuditLink = "Cupom SAT",
            });
        }

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            var saleItem = saleItems[index];

            if (item.ProductId.HasValue && products.TryGetValue(item.ProductId.Value, out var product))
            {
                product.Stock -= item.Qty;
                await ConsumeLotsAsync(saleItem, product.Id, item.Qty, ct);
                continue;
            }

            if (item.KitId.HasValue && kits.TryGetValue(item.KitId.Value, out var kit))
            {
                kit.StockAvailable = Math.Max(0, kit.StockAvailable - item.Qty);
                foreach (var component in kit.Components)
                {
                    if (products.TryGetValue(component.ProductId, out var componentProduct))
                    {
                        var used = component.Quantity * item.Qty;
                        componentProduct.Stock -= used;
                        await ConsumeLotsAsync(saleItem, componentProduct.Id, used, ct);
                    }
                }
            }
        }

        // As vendas baixam o estoque do produto; os lotes acompanham (consome primeiro o que vence antes).
        await StockLots.ReconcileAsync(db, currentUser.CompanyId, [.. products.Values], ct);

        // Pagamento a crédito/parcelado gera um recebível de adquirente para D+30.
        if (request.PaymentMode is "credito_vista" or "parcelado")
        {
            var dueDate = DateOnly.FromDateTime(now.AddDays(30));
            db.AccountsReceivables.Add(new AccountsReceivable
            {
                CompanyId = currentUser.CompanyId,
                ClientOrOrigin = $"Adquirente {request.CardFlag ?? "Mastercard"} - PDV Balcão 01",
                SubCategory = "Venda Crédito à Vista (TEF Balcão)",
                Reference = receiptNumber,
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
                Reference = receiptNumber,
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

            if (request.ClientId.HasValue)
            {
                var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId, ct);
                if (client is not null)
                {
                    client.TotalSpent += request.Total;
                }
            }
        }

        var subtotal = saleItems.Sum(i => i.Subtotal);
        db.SaleRecords.Add(new SaleRecord
        {
            CompanyId = currentUser.CompanyId,
            SaleId = saleId,
            ReceiptNumber = receiptNumber,
            Timestamp = now,
            Total = request.Total,
            NetAmount = request.NetAmount,
            FeeAmount = request.FeeAmount,
            DiscountAmount = Math.Max(0, subtotal - request.Total),
            PaymentMode = request.PaymentMode,
            CardFlag = request.CardFlag,
            ClientId = request.ClientId,
            ClientName = request.ClientName,
            SoldBy = currentUser.UserName,
            Items = saleItems,
        });

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "vendeu", "Venda PDV",
            $"{receiptNumber} • {request.Total.ToString("C", PtBr)}",
            request.ClientName ?? request.PaymentMode, ct);
        await transaction.CommitAsync(ct);

        return new FinalizeSaleResult(saleId, receiptNumber);
    }
}
