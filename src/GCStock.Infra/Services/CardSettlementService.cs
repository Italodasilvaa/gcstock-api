using GCStock.Application.Audit;
using GCStock.Application.Abstractions;
using GCStock.Application.Finance;
using GCStock.Domain.Entities;
using GCStock.Domain.Services;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

/// <summary>
/// Liquidação de cartão: visão dos títulos a receber de adquirente (as vendas de crédito de verdade, por parcela) e a
/// antecipação deles. Antes isso lia uma tabela separada que nunca recebia as vendas; agora é o mesmo dado de Contas a Receber.
/// </summary>
public class CardSettlementService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : ICardSettlementService
{
    /// <summary>Taxa de antecipação usada quando o título não tem forma de pagamento de origem (título antigo).</summary>
    private const decimal DefaultAnticipationPercent = 2.5m;

    private static readonly TimeSpan BrazilOffset = TimeSpan.FromHours(-3);

    public async Task<List<CardSettlementDto>> GetAllAsync(CancellationToken ct)
    {
        var titles = await db.AccountsReceivables
            .Where(r => r.CompanyId == currentUser.CompanyId && r.Type == "adquirente")
            .OrderBy(r => r.DueDate)
            .ToListAsync(ct);

        // Dia da venda (Brasília): o da venda que originou o título; título antigo sem vínculo usa vencimento - 30 dias.
        var saleIds = titles.Where(t => t.SaleId.HasValue).Select(t => t.SaleId!.Value).Distinct().ToList();
        var saleDays = saleIds.Count > 0
            ? (await db.SaleRecords
                    .Where(s => s.CompanyId == currentUser.CompanyId && saleIds.Contains(s.SaleId))
                    .Select(s => new { s.SaleId, s.Timestamp })
                    .ToListAsync(ct))
                .ToDictionary(s => s.SaleId, s => DateOnly.FromDateTime(s.Timestamp + BrazilOffset))
            : [];

        return titles.Select(t => ToDto(t, t.SaleId.HasValue && saleDays.TryGetValue(t.SaleId.Value, out var day) ? day : t.DueDate.AddDays(-30))).ToList();
    }

    public async Task AnticipateAsync(Guid id, CancellationToken ct)
    {
        var title = await db.AccountsReceivables.FirstOrDefaultAsync(
            r => r.Id == id && r.CompanyId == currentUser.CompanyId && r.Type == "adquirente", ct);
        if (title is null) return;

        if (title.Status == "recebido")
        {
            throw new InvalidOperationException(title.AnticipatedAt.HasValue
                ? "Este título já foi antecipado."
                : "Este título já foi recebido.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow + BrazilOffset);
        if (title.DueDate <= today)
        {
            throw new InvalidOperationException("Este título já venceu: use Liquidar em Contas a Receber, não há o que antecipar.");
        }

        // Taxa da modalidade (Configurações); título antigo, sem modalidade de origem, usa o padrão.
        var percent = DefaultAnticipationPercent;
        if (title.PaymentFeeSettingId.HasValue)
        {
            var configured = await db.PaymentFeeSettings
                .Where(f => f.Id == title.PaymentFeeSettingId && f.CompanyId == currentUser.CompanyId)
                .Select(f => (decimal?)f.AnticipationFeePercent)
                .FirstOrDefaultAsync(ct);
            percent = configured ?? DefaultAnticipationPercent;
        }

        // Antecipar é receber hoje o líquido da parcela menos o custo da antecipação (despesa financeira, não desconto de venda).
        title.AnticipationFee = SalePricing.AnticipationFee(title.NetAmount, percent);
        title.AnticipatedAt = today;
        title.Status = "recebido";
        title.ReceivedDate = today;
        title.DueLabel = "Antecipado";

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "antecipou", "Liquidação de Cartão", $"{title.ClientOrOrigin} • {title.Reference}",
            $"Líquido {title.NetAmount:C}, taxa de antecipação {percent}% = {title.AnticipationFee:C}", ct);
    }

    private static CardSettlementDto ToDto(AccountsReceivable t, DateOnly saleDay)
    {
        var anticipated = t.AnticipatedAt.HasValue;
        return new CardSettlementDto(
            t.Id,
            t.ClientOrOrigin,
            t.PaymentModeDesc,
            saleDay,
            anticipated ? "Hoje (Antecipado)" : t.Status == "recebido" ? "Liquidado" : t.DueDate.ToString("dd/MM/yyyy"),
            t.GrossAmount,
            t.FeeRetention,
            // O que realmente entra: líquido menos o custo da antecipação (se houve).
            t.NetAmount - t.AnticipationFee,
            anticipated ? "antecipado" : t.Status == "recebido" ? "liquidado" : "a_liquidar",
            anticipated ? t.AnticipationFee : null);
    }
}
