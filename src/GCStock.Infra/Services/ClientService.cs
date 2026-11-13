using GCStock.Application.Audit;
using GCStock.Application.Abstractions;
using GCStock.Application.Clients;
using GCStock.Domain.Entities;
using GCStock.Domain.Services;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class ClientService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IClientService
{
    // Horário de Brasília (UTC-3): o dia da compra é o dia local.
    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

    public async Task<List<ClientDto>> GetAllAsync(CancellationToken ct)
    {
        var clients = await db.Clients.Where(c => c.CompanyId == currentUser.CompanyId).ToListAsync(ct);
        var stats = await BuildStatsAsync(null, ct);
        return clients.Select(c => ToDto(c, stats.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<ClientHistoryDto?> GetHistoryAsync(Guid id, CancellationToken ct)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == currentUser.CompanyId, ct);
        if (client is null) return null;

        var stats = await BuildStatsAsync([id], ct);

        var sales = await db.SaleRecords
            .Include(s => s.Items)
            .Where(s => s.CompanyId == currentUser.CompanyId && s.ClientId == id)
            .OrderByDescending(s => s.Timestamp)
            .ToListAsync(ct);
        var saleKeys = sales.Select(s => s.Id).ToList();
        var returns = await db.SaleReturns
            .Include(r => r.Items)
            .Where(r => r.CompanyId == currentUser.CompanyId && saleKeys.Contains(r.SaleRecordId))
            .ToListAsync(ct);
        var returnsBySale = returns.GroupBy(r => r.SaleRecordId).ToDictionary(g => g.Key, g => g.ToList());

        var open = await db.AccountsReceivables
            .Where(r => r.CompanyId == currentUser.CompanyId && r.ClientId == id && r.Status != "recebido")
            .Select(r => new { r.SaleId, r.Reference, r.GrossAmount })
            .ToListAsync(ct);

        var purchases = sales.Select(s =>
        {
            var saleReturns = returnsBySale.GetValueOrDefault(s.Id) ?? [];
            var returnedByItem = saleReturns.SelectMany(r => r.Items).GroupBy(i => i.SaleItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
            var openAmount = open.Where(o => o.SaleId == s.SaleId || (o.SaleId == null && o.Reference == s.ReceiptNumber)).Sum(o => o.GrossAmount);
            return new ClientPurchaseDto(
                s.SaleId, s.ReceiptNumber, s.Timestamp, s.Total, saleReturns.Sum(r => r.RefundAmount), s.PaymentMode, s.Status,
                s.Status == "cancelada" ? 0m : openAmount,
                s.Items.OrderBy(i => i.Name)
                    .Select(i => new ClientPurchaseItemDto(i.Name, i.Qty, i.Unit, i.Subtotal, returnedByItem.GetValueOrDefault(i.Id)))
                    .ToList());
        }).ToList();

        // O que mais compra: produtos avulsos e kits, pela quantidade líquida (vendas canceladas fora, devolvido descontado).
        List<ClientTopItemDto> Top(bool kits) => sales
            .Where(s => s.Status != "cancelada")
            .SelectMany(s =>
            {
                var returnedByItem = (returnsBySale.GetValueOrDefault(s.Id) ?? [])
                    .SelectMany(r => r.Items).GroupBy(i => i.SaleItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
                return s.Items.Where(i => i.KitId.HasValue == kits)
                    .Select(i => (i.Name, i.Unit, Qty: i.Qty - returnedByItem.GetValueOrDefault(i.Id)));
            })
            .GroupBy(x => (x.Name, x.Unit))
            .Select(g => new ClientTopItemDto(g.Key.Name, g.Key.Unit, g.Sum(x => x.Qty)))
            .Where(x => x.Qty > 0)
            .OrderByDescending(x => x.Qty)
            .Take(5)
            .ToList();

        return new ClientHistoryDto(ToDto(client, stats.GetValueOrDefault(id)), purchases, Top(false), Top(true));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == currentUser.CompanyId, ct);
        if (client is null) return false;

        var open = await db.AccountsReceivables.AnyAsync(
            r => r.CompanyId == currentUser.CompanyId && r.ClientId == id && r.Status != "recebido", ct);
        if (open)
        {
            throw new InvalidOperationException("Este cliente tem conta a receber em aberto. Dê baixa ou exclua o título antes de apagar o cliente.");
        }

        db.Clients.Remove(client);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("excluiu", "Cliente", client.Name, null, ct);
        return true;
    }

    public async Task<ClientDto> SaveAsync(SaveClientRequest request, CancellationToken ct)
    {
        if (request.CreditLimit is < 0) throw new ArgumentException("O limite de crédito não pode ser negativo.");

        if (request.Id.HasValue)
        {
            var existing = await db.Clients.FirstOrDefaultAsync(c => c.Id == request.Id && c.CompanyId == currentUser.CompanyId, ct);
            if (existing is not null)
            {
                if (request.Name is not null) existing.Name = request.Name;
                if (request.Document is not null) existing.Document = request.Document;
                if (request.Phone is not null) existing.Phone = request.Phone;
                if (request.Email is not null) existing.Email = request.Email;
                if (request.Type is not null) existing.Type = request.Type;
                if (request.FidelityPoints is not null) existing.FidelityPoints = request.FidelityPoints.Value;
                if (request.CreditLimit is not null) existing.CreditLimit = request.CreditLimit.Value;
                if (request.FavoriteCuts is not null) existing.FavoriteCuts = request.FavoriteCuts;
                if (request.Notes is not null) existing.Notes = request.Notes;

                await db.SaveChangesAsync(ct);
                await audit.RecordAsync("editou", "Cliente", existing.Name, null, ct);
                return ToDto(existing, (await BuildStatsAsync([existing.Id], ct)).GetValueOrDefault(existing.Id));
            }
        }

        var saved = new Client
        {
            CompanyId = currentUser.CompanyId,
            Name = request.Name ?? "Novo Cliente",
            Document = request.Document ?? "000.000.000-00",
            Phone = request.Phone ?? "(11) 90000-0000",
            Email = request.Email ?? string.Empty,
            Type = request.Type ?? "pf",
            FidelityPoints = request.FidelityPoints ?? 0,
            CreditLimit = request.CreditLimit ?? 0,
            FavoriteCuts = request.FavoriteCuts ?? "Cortes Variados",
            Notes = request.Notes ?? string.Empty,
        };
        db.Clients.Add(saved);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Cliente", saved.Name, null, ct);
        return ToDto(saved, null);
    }

    /// <summary>Indicadores de um cliente (ou de todos, com <paramref name="clientIds"/> nulo), lidos das vendas e dos títulos a receber.</summary>
    private sealed record ClientStats(ClientMetrics.Result Metrics, decimal OpenBalance, decimal TotalPaid);

    private async Task<Dictionary<Guid, ClientStats>> BuildStatsAsync(List<Guid>? clientIds, CancellationToken ct)
    {
        var companyId = currentUser.CompanyId;

        var sales = await db.SaleRecords
            .Where(s => s.CompanyId == companyId && s.ClientId != null && s.Status != "cancelada"
                && (clientIds == null || clientIds.Contains(s.ClientId.Value)))
            .Select(s => new { s.Id, s.SaleId, s.ReceiptNumber, ClientId = s.ClientId!.Value, s.Timestamp, s.Total })
            .ToListAsync(ct);
        var saleKeys = sales.Select(s => s.Id).ToList();
        var refunded = (await db.SaleReturns
                .Where(r => r.CompanyId == companyId && saleKeys.Contains(r.SaleRecordId))
                .Select(r => new { r.SaleRecordId, r.RefundAmount })
                .ToListAsync(ct))
            .GroupBy(r => r.SaleRecordId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.RefundAmount));

        var open = await db.AccountsReceivables
            .Where(r => r.CompanyId == companyId && r.ClientId != null && r.Status != "recebido"
                && (clientIds == null || clientIds.Contains(r.ClientId.Value)))
            .Select(r => new { ClientId = r.ClientId!.Value, r.SaleId, r.Reference, r.GrossAmount })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, ClientStats>();
        foreach (var clientId in sales.Select(s => s.ClientId).Concat(open.Select(o => o.ClientId)).Distinct())
        {
            var clientSales = sales.Where(s => s.ClientId == clientId).ToList();
            var metrics = ClientMetrics.Compute(clientSales.Select(s =>
                new ClientMetrics.SaleFact(DateOnly.FromDateTime(s.Timestamp + BrazilOffset), s.Total, refunded.GetValueOrDefault(s.Id))));

            var clientOpen = open.Where(o => o.ClientId == clientId).ToList();
            var openBalance = clientOpen.Sum(o => o.GrossAmount);
            // Pago = comprado menos o que ainda falta pagar dessas compras (títulos manuais, sem venda, ficam só no saldo em aberto).
            var saleIdSet = clientSales.Select(s => s.SaleId).ToHashSet();
            var receiptSet = clientSales.Select(s => s.ReceiptNumber).ToHashSet();
            var openFromSales = clientOpen
                .Where(o => (o.SaleId.HasValue && saleIdSet.Contains(o.SaleId.Value)) || (o.SaleId == null && receiptSet.Contains(o.Reference)))
                .Sum(o => o.GrossAmount);

            result[clientId] = new ClientStats(metrics, openBalance, Math.Max(0m, metrics.TotalPurchased - openFromSales));
        }

        return result;
    }

    private static ClientDto ToDto(Client c, ClientStats? stats)
    {
        var metrics = stats?.Metrics ?? ClientMetrics.Compute([]);
        var open = stats?.OpenBalance ?? 0m;
        return new ClientDto(
            c.Id, c.Name, c.Document, c.Phone, c.Email, c.Type, c.FidelityPoints, c.CreditLimit,
            // Total comprado vem das vendas (o TotalSpent gravado só contava vendas a prazo e deixou de ser mantido).
            metrics.TotalPurchased,
            c.FavoriteCuts, c.Notes,
            stats?.TotalPaid ?? 0m, open, metrics.PurchaseCount, metrics.FirstPurchase, metrics.LastPurchase, metrics.AverageTicket,
            metrics.FrequencyDays, ClientMetrics.CreditAvailable(c.CreditLimit, open), ClientMetrics.ExceedsLimit(c.CreditLimit, open, 0m));
    }
}
