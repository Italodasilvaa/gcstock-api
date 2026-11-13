using GCStock.Application.Audit;
using System.Globalization;
using GCStock.Application.Abstractions;
using GCStock.Application.Access;
using GCStock.Application.Common;
using GCStock.Application.Pdv;
using GCStock.Domain.Entities;
using GCStock.Domain.Services;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class PosService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit, IPermissionChecker permissions) : IPosService
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    /// <summary>
    /// Cliente, produtos e kits recebidos por ID precisam pertencer à empresa do usuário. Antes, um ID de outra empresa
    /// era ignorado em silêncio (venda sem baixa de estoque) ou, no cliente, alterava o cadastro alheio.
    /// </summary>
    private async Task EnsureReferencesBelongToCompanyAsync(
        FinalizeSaleRequest request, Dictionary<Guid, Product> products, Dictionary<Guid, Kit> kits, CancellationToken ct)
    {
        if (request.Items.Any(i => i.ProductId.HasValue && !products.ContainsKey(i.ProductId.Value)))
        {
            throw new ArgumentException("Um dos produtos da venda não foi encontrado.");
        }

        if (request.Items.Any(i => i.KitId.HasValue && !kits.ContainsKey(i.KitId.Value)))
        {
            throw new ArgumentException("Um dos kits da venda não foi encontrado.");
        }

        if (request.ClientId.HasValue
            && !await db.Clients.AnyAsync(c => c.Id == request.ClientId && c.CompanyId == currentUser.CompanyId, ct))
        {
            throw new ArgumentException("Cliente não encontrado.");
        }
    }

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
        var required = RequiredByProduct(request, products, kits);

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

    /// <summary>Quanto de cada produto a venda precisa: itens avulsos + componentes dos kits vendidos.</summary>
    private static Dictionary<Guid, decimal> RequiredByProduct(
        FinalizeSaleRequest request, Dictionary<Guid, Product> products, Dictionary<Guid, Kit> kits)
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

        return required;
    }

    /// <summary>
    /// Produto vencido não pode ser vendido: a parte do estoque que está em lotes vencidos não conta como vendável.
    /// Recusa se o que a venda pede passa do estoque válido (estoque total menos o vencido).
    /// </summary>
    private async Task EnsureNotExpiredAsync(
        FinalizeSaleRequest request, Dictionary<Guid, Product> products, Dictionary<Guid, Kit> kits, CancellationToken ct)
    {
        var required = RequiredByProduct(request, products, kits);
        if (required.Count == 0) return;

        var today = ExpiryRules.Today(DateTime.UtcNow);
        var ids = required.Keys.ToList();
        var expiredLots = await db.StockBatches
            .Where(b => b.CompanyId == currentUser.CompanyId && ids.Contains(b.ProductId) && b.AvailableKg > 0 && b.ExpiryDate < today)
            .ToListAsync(ct);

        var problems = new List<string>();
        foreach (var group in expiredLots.GroupBy(b => b.ProductId))
        {
            if (!products.TryGetValue(group.Key, out var product)) continue;

            var expiredQty = group.Sum(b => b.AvailableKg);
            var sellable = Math.Max(0, product.Stock - expiredQty);
            if (required[group.Key] > sellable + 0.0005m)
            {
                var lots = string.Join(", ", group.Select(b => string.IsNullOrWhiteSpace(b.LotNumber) ? "sem número" : b.LotNumber));
                problems.Add(
                    $"{product.Name}: {expiredQty.ToString("0.###", PtBr)} {product.Unit} vencido(s) (lote {lots}); " +
                    $"vendável {sellable.ToString("0.###", PtBr)} {product.Unit}, pedido {required[group.Key].ToString("0.###", PtBr)} {product.Unit}");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Produto vencido não pode ser vendido: " + string.Join("; ", problems) + ". Registre a perda do lote vencido em Perdas.");
        }
    }

    /// <summary>Consome os lotes do produto (FEFO: vence antes, sai antes; vencido nunca sai) e guarda de onde saiu, para devolver no cancelamento.</summary>
    private async Task ConsumeLotsAsync(SaleItem saleItem, Guid productId, decimal quantity, CancellationToken ct)
    {
        foreach (var (lotId, qty) in await StockLots.ConsumeAsync(db, currentUser.CompanyId, productId, quantity, ct))
        {
            saleItem.Lots.Add(new SaleItemLot { StockBatchId = lotId, ProductId = productId, Qty = qty });
        }
    }

    /// <summary>
    /// Lotes escolhidos na venda: precisam existir, ser do produto da linha e ter saldo para tudo o que a venda tira deles.
    /// </summary>
    private async Task<Dictionary<Guid, StockBatch>> LoadChosenLotsAsync(
        FinalizeSaleRequest request, Dictionary<Guid, Product> products, CancellationToken ct)
    {
        var chosen = request.Items.Where(i => i.StockBatchId.HasValue).ToList();
        if (chosen.Count == 0) return [];

        if (chosen.Any(i => !i.ProductId.HasValue || !products.ContainsKey(i.ProductId.Value)))
        {
            throw new ArgumentException("Só é possível escolher o lote de um produto avulso (kits saem do pedido mais antigo).");
        }

        var ids = chosen.Select(i => i.StockBatchId!.Value).Distinct().ToList();
        var lots = await db.StockBatches
            .Where(b => b.CompanyId == currentUser.CompanyId && ids.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);

        var today = ExpiryRules.Today(DateTime.UtcNow);
        foreach (var item in chosen)
        {
            if (!lots.TryGetValue(item.StockBatchId!.Value, out var lot) || lot.ProductId != item.ProductId)
            {
                throw new ArgumentException($"O lote escolhido para {item.Name} não existe ou não é desse produto.");
            }

            if (ExpiryRules.IsExpired(lot.ExpiryDate, today))
            {
                throw new InvalidOperationException(
                    $"O lote {lot.LotNumber} de {item.Name} está vencido ({lot.ExpiryDate:dd/MM/yyyy}) e não pode ser vendido.");
            }
        }

        foreach (var group in chosen.GroupBy(i => i.StockBatchId!.Value))
        {
            var lot = lots[group.Key];
            var wanted = group.Sum(i => i.Qty);
            if (wanted > lot.AvailableKg)
            {
                throw new InvalidOperationException(
                    $"Estoque insuficiente no lote {lot.LotNumber} de {lot.ProductName} (disponível {lot.AvailableKg.ToString("0.###", PtBr)} {lot.Unit}, pedido {wanted.ToString("0.###", PtBr)} {lot.Unit}).");
            }
        }

        return lots;
    }

    public async Task<FinalizeSaleResult> FinalizeSaleAsync(FinalizeSaleRequest request, CancellationToken ct)
    {
        if (request.Items.Count == 0 || request.Items.Any(i => i.Qty <= 0))
        {
            throw new ArgumentException("A venda precisa ter ao menos um item e todas as quantidades devem ser maiores que zero.");
        }

        var saleId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        // Tudo-ou-nada, e os produtos ficam travados até o fim: duas vendas simultâneas do mesmo produto não
        // conseguem, juntas, passar do estoque.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // O lock por empresa serializa a numeração: duas vendas ao mesmo tempo nunca recebem o mesmo número.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({currentUser.CompanyId.ToString() + ":vendas"}, 0))", ct);
        var receiptNumber = await NextReceiptNumberAsync(now, ct);

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

        await EnsureReferencesBelongToCompanyAsync(request, products, kits, ct);
        await EnsurePricesAllowedAsync(request, products, kits, ct);
        EnsureStockAvailable(request, products, kits);
        await EnsureNotExpiredAsync(request, products, kits, ct);
        var chosenLots = await LoadChosenLotsAsync(request, products, ct);

        // Valores definitivos: o servidor calcula desconto, taxa, total cobrado e líquido a partir dos itens e da forma de
        // pagamento cadastrada. O que a tela enviou só é conferido: se não bate, a venda é recusada em vez de cobrar errado.
        var payment = await ResolvePaymentAsync(request, ct);
        var itemsSubtotal = request.Items.Sum(i => Math.Round(i.Qty * i.Price, 2, MidpointRounding.AwayFromZero));
        var pricing = SalePricing.Calculate(itemsSubtotal, request.Discount, payment.FeePercent, payment.FixedFee, request.PassFeeToCustomer);
        if (Math.Abs(pricing.Total - request.Total) > 0.01m)
        {
            throw new InvalidOperationException(
                $"O valor da venda mudou: a tela mostrava {request.Total.ToString("C", PtBr)} e o sistema calculou {pricing.Total.ToString("C", PtBr)} " +
                "(preço ou taxa de pagamento atualizados). Atualize a tela e confira antes de finalizar.");
        }
        var installments = ValidInstallments(payment.Mode, request.Installments);

        var saleItems = new List<SaleItem>();

        foreach (var line in request.Items)
        {
            // Unidade real do cadastro do produto (kg, pct, un...); kit é vendido por "un".
            var lineUnit = line.ProductId.HasValue && products.TryGetValue(line.ProductId.Value, out var lineProduct)
                ? lineProduct.Unit
                : line.KitId.HasValue ? "un" : line.Unit;

            // Lote escolhido: custo do lote (lucro real). Senão, custo médio do estoque NO MOMENTO da venda (a venda não
            // altera o custo médio); kit = soma dos componentes.
            var chosenLot = line.StockBatchId.HasValue ? chosenLots[line.StockBatchId.Value] : null;
            var unitCost = chosenLot is { UnitCost: > 0 }
                ? chosenLot.UnitCost
                : line.ProductId.HasValue && products.TryGetValue(line.ProductId.Value, out var costProduct)
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
                DocInfo = $"Tíquete Balcão 01 • {payment.Mode} • {pricing.Total.ToString("C", PtBr)}",
                AmountKg = -line.Qty,
                Unit = lineUnit,
                Description = $"Baixa realizada no PDV: {line.Qty}x {line.Name}" + (chosenLot is null ? "" : $" • Lote {chosenLot.LotNumber}"),
                BadgeLabel = "PDV Finalizado",
                AuditLink = "Cupom SAT",
            });
        }

        // Quem escolheu lote é atendido primeiro, para a ordem automática das outras linhas não esvaziar o lote escolhido.
        foreach (var index in Enumerable.Range(0, request.Items.Count).OrderBy(i => request.Items[i].StockBatchId.HasValue ? 0 : 1))
        {
            var item = request.Items[index];
            var saleItem = saleItems[index];

            if (item.ProductId.HasValue && products.TryGetValue(item.ProductId.Value, out var product))
            {
                product.Stock -= item.Qty;
                if (item.StockBatchId.HasValue)
                {
                    chosenLots[item.StockBatchId.Value].AvailableKg -= item.Qty;
                    saleItem.Lots.Add(new SaleItemLot { StockBatchId = item.StockBatchId.Value, ProductId = product.Id, Qty = item.Qty });
                }
                else
                {
                    await ConsumeLotsAsync(saleItem, product.Id, item.Qty, ct);
                }
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

        // As vendas baixam o estoque do produto; os lotes acompanham (consome primeiro o pedido mais antigo).
        await StockLots.ReconcileAsync(db, currentUser.CompanyId, [.. products.Values], ct);

        var cardFlag = request.CardFlag ?? "Mastercard";

        // Cartão: crédito à vista = 1 recebível de adquirente em D+30; parcelado = 1 recebível por parcela (30, 60, 90... dias).
        // A soma das parcelas fecha exatamente o total cobrado, a taxa e o líquido (diferença de centavos vai para a última).
        if (payment.Mode is "credito_vista" or "parcelado")
        {
            var grossParts = SalePricing.SplitInstallments(pricing.Total, installments);
            var feeParts = SalePricing.SplitInstallments(pricing.Fee, installments);
            for (var n = 1; n <= installments; n++)
            {
                var dueDate = DateOnly.FromDateTime(now.AddDays(30 * n));
                var suffix = installments > 1 ? $" • parcela {n}/{installments}" : string.Empty;
                db.AccountsReceivables.Add(new AccountsReceivable
                {
                    CompanyId = currentUser.CompanyId,
                    SaleId = saleId,
                    ClientOrOrigin = $"Adquirente {cardFlag} - PDV Balcão 01",
                    SubCategory = (installments > 1 ? "Venda Crédito Parcelado (TEF Balcão)" : "Venda Crédito à Vista (TEF Balcão)") + suffix,
                    Reference = receiptNumber,
                    DueDate = dueDate,
                    DueLabel = DateLabelHelper.DueLabelFor(dueDate),
                    GrossAmount = grossParts[n - 1],
                    FeeRetention = feeParts[n - 1],
                    NetAmount = grossParts[n - 1] - feeParts[n - 1],
                    Status = "a_vencer",
                    Type = "adquirente",
                    PaymentModeDesc = $"{cardFlag} D+{30 * n}{suffix}",
                    InstallmentNumber = n,
                    InstallmentCount = installments,
                });
            }
        }

        // Venda a prazo: gera os títulos a receber vinculados ao cliente escolhido (1 por parcela, de mês em mês a partir
        // do primeiro vencimento), para cobrança posterior.
        if (payment.Mode == "a_prazo")
        {
            var firstDue = request.DueDate ?? DateOnly.FromDateTime(now.AddDays(30));
            var parts = SalePricing.SplitInstallments(pricing.Total, installments);
            for (var n = 1; n <= installments; n++)
            {
                var dueDate = firstDue.AddMonths(n - 1);
                var suffix = installments > 1 ? $" • parcela {n}/{installments}" : string.Empty;
                db.AccountsReceivables.Add(new AccountsReceivable
                {
                    CompanyId = currentUser.CompanyId,
                    SaleId = saleId,
                    ClientOrOrigin = request.ClientName ?? "Cliente Avulso (A Prazo)",
                    SubCategory = "Venda a Prazo • PDV Balcão 01" + suffix,
                    Reference = receiptNumber,
                    DueDate = dueDate,
                    DueLabel = DateLabelHelper.DueLabelFor(dueDate),
                    GrossAmount = parts[n - 1],
                    FeeRetention = 0,
                    NetAmount = parts[n - 1],
                    Status = "a_vencer",
                    Type = "b2b",
                    PaymentModeDesc = "Fiado / A Prazo" + suffix,
                    ClientId = request.ClientId,
                    InstallmentNumber = n,
                    InstallmentCount = installments,
                });
            }

            if (request.ClientId.HasValue)
            {
                var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId && c.CompanyId == currentUser.CompanyId, ct);
                if (client is not null)
                {
                    client.TotalSpent += pricing.Total;
                }
            }
        }

        db.SaleRecords.Add(new SaleRecord
        {
            CompanyId = currentUser.CompanyId,
            SaleId = saleId,
            ReceiptNumber = receiptNumber,
            Timestamp = now,
            Total = pricing.Total,
            NetAmount = pricing.Net,
            FeeAmount = pricing.Fee,
            DiscountAmount = pricing.Discount,
            PaymentMode = payment.Mode,
            CardFlag = request.CardFlag,
            ClientId = request.ClientId,
            ClientName = request.ClientName,
            SoldBy = currentUser.UserName,
            Items = saleItems,
        });

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "vendeu", "Venda PDV",
            $"{receiptNumber} • {pricing.Total.ToString("C", PtBr)}",
            request.ClientName ?? payment.Mode, ct);
        await transaction.CommitAsync(ct);

        return new FinalizeSaleResult(saleId, receiptNumber, pricing.Total, pricing.Net, pricing.Fee, pricing.Discount, installments);
    }

    /// <summary>VEN-{ano}-{sequência de 6 dígitos}, único por empresa (o lock da venda garante que ninguém pega o mesmo).</summary>
    private async Task<string> NextReceiptNumberAsync(DateTime now, CancellationToken ct)
    {
        var prefix = $"VEN-{ExpiryRules.Today(now).Year}-";
        var existing = await db.SaleRecords
            .Where(s => s.CompanyId == currentUser.CompanyId && s.ReceiptNumber.StartsWith(prefix))
            .Select(s => s.ReceiptNumber)
            .ToListAsync(ct);

        var next = existing
            .Select(code => int.TryParse(code[prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"{prefix}{next:D6}";
    }

    private sealed record PaymentRule(string Mode, decimal FeePercent, decimal FixedFee);

    /// <summary>
    /// Forma de pagamento e taxa vêm do cadastro da empresa (Configurações), nunca do que a tela mandou. Sem id de forma de
    /// pagamento, usa a primeira ativa do tipo informado; sem nenhuma, a venda segue sem taxa.
    /// </summary>
    private async Task<PaymentRule> ResolvePaymentAsync(FinalizeSaleRequest request, CancellationToken ct)
    {
        if (request.PaymentOptionId.HasValue)
        {
            var option = await db.PaymentFeeSettings.FirstOrDefaultAsync(
                f => f.Id == request.PaymentOptionId && f.CompanyId == currentUser.CompanyId && f.Active, ct)
                ?? throw new ArgumentException("A forma de pagamento escolhida não foi encontrada ou está inativa.");
            return new PaymentRule(SalePricing.InferMode(option.Method), option.FeePercent, option.FixedFee ?? 0m);
        }

        if (!SalePricing.Modes.Contains(request.PaymentMode))
        {
            throw new ArgumentException($"Forma de pagamento inválida: {request.PaymentMode}.");
        }

        var options = await db.PaymentFeeSettings
            .Where(f => f.CompanyId == currentUser.CompanyId && f.Active)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);
        var match = options.FirstOrDefault(f => SalePricing.InferMode(f.Method) == request.PaymentMode);
        return new PaymentRule(request.PaymentMode, match?.FeePercent ?? 0m, match?.FixedFee ?? 0m);
    }

    /// <summary>Parcelas só existem em cartão parcelado e venda a prazo (1 a 12); nos demais pagamentos é sempre 1.</summary>
    private static int ValidInstallments(string mode, int requested)
    {
        if (mode is not ("parcelado" or "a_prazo")) return 1;
        if (requested < 1 || requested > SalePricing.MaxInstallments)
        {
            throw new ArgumentException($"O número de parcelas deve ficar entre 1 e {SalePricing.MaxInstallments}.");
        }
        return requested;
    }
}
