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
