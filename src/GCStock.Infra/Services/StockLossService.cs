using System.Globalization;
using GCStock.Application.Abstractions;
using GCStock.Application.Audit;
using GCStock.Application.Inventory;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class StockLossService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IStockLossService
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public async Task<List<StockLossDto>> GetAllAsync(CancellationToken ct)
    {
        var losses = await db.StockLosses
            .Where(l => l.CompanyId == currentUser.CompanyId)
            .OrderByDescending(l => l.Date)
            .ThenByDescending(l => l.CreatedAt)
            .Take(1000)
            .ToListAsync(ct);
        return losses.Select(ToDto).ToList();
    }

    public async Task<StockLossDto> CreateAsync(CreateStockLossRequest request, CancellationToken ct)
    {
        if (request.Qty <= 0) throw new ArgumentException("A quantidade da perda deve ser maior que zero.");
        if (!LossReasons.IsValid(request.Reason)) throw new ArgumentException("Motivo da perda inválido.");
        if (request.UnitCost is < 0) throw new ArgumentException("O custo unitário não pode ser negativo.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)); // dia em Brasília
        var date = request.Date ?? today;
        if (date > today) throw new ArgumentException("A data da perda não pode ser no futuro.");

        // Produto travado até o fim: uma venda simultânea não consegue vender a mesma quantidade que está sendo perdida.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Products\" WHERE \"Id\" = {request.ProductId} AND \"CompanyId\" = {currentUser.CompanyId} FOR UPDATE", ct);

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == request.ProductId && p.CompanyId == currentUser.CompanyId, ct)
            ?? throw new ArgumentException("Produto não encontrado.");

        if (request.Qty > product.Stock)
        {
            throw new InvalidOperationException(
                $"Estoque insuficiente: {product.Name} tem {Format(Math.Max(0, product.Stock))} {product.Unit} e a perda é de {Format(request.Qty)} {product.Unit}.");
        }

        // Lote perdido: o escolhido pelo usuário ou, sem escolha, o pedido mais antigo.
        var consumption = new List<(Guid LotId, decimal Qty)>();
        var lotNumber = string.Empty;
        if (request.StockBatchId is { } lotId)
        {
            var lot = await db.StockBatches.FirstOrDefaultAsync(
                    b => b.Id == lotId && b.CompanyId == currentUser.CompanyId && b.ProductId == product.Id, ct)
                ?? throw new ArgumentException("Lote não encontrado para este produto.");
            if (request.Qty > lot.AvailableKg)
            {
                throw new InvalidOperationException(
                    $"O lote {(string.IsNullOrEmpty(lot.LotNumber) ? "selecionado" : lot.LotNumber)} tem só {Format(lot.AvailableKg)} {product.Unit}.");
            }

            lot.AvailableKg -= request.Qty;
            consumption.Add((lot.Id, request.Qty));
            lotNumber = lot.LotNumber;
        }
        else
        {
            consumption = await StockLots.ConsumeAsync(db, currentUser.CompanyId, product.Id, request.Qty, ct);
        }

        // O custo médio do que sobra NÃO muda: o prejuízo é a quantidade vezes o custo informado (ou o médio atual).
        product.Stock -= request.Qty;
        var unitCost = request.UnitCost ?? product.CostPrice;
        var totalCost = Math.Round(request.Qty * unitCost, 2, MidpointRounding.AwayFromZero);

        var loss = new StockLoss
        {
            CompanyId = currentUser.CompanyId,
            Date = date,
            ProductId = product.Id,
            ProductName = product.Name,
            Qty = request.Qty,
            Unit = product.Unit,
            UnitCost = unitCost,
            TotalCost = totalCost,
            Reason = request.Reason,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            StockBatchId = request.StockBatchId,
            LotNumber = lotNumber,
            CreatedBy = currentUser.UserName,
            Lots = consumption.Select(c => new StockLossLot { StockBatchId = c.LotId, Qty = c.Qty }).ToList(),
        };
        db.StockLosses.Add(loss);

        db.StockMovements.Add(new StockMovement
        {
            CompanyId = currentUser.CompanyId,
            Timestamp = DateTime.UtcNow,
            Type = "perda_quebra",
            Title = $"Perda: {product.Name} ({LossReasons.Label(request.Reason)})",
            DocInfo = loss.Notes ?? $"Registrada por {currentUser.UserName}",
            AmountKg = -request.Qty,
            Unit = product.Unit,
            Description = $"Perda de {Format(request.Qty)} {product.Unit} • custo {unitCost.ToString("C", PtBr)} • prejuízo {totalCost.ToString("C", PtBr)}",
            BadgeLabel = "Perda",
            AuditLink = LossReasons.Label(request.Reason),
        });

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "registrou perda", "Perda de Estoque",
            $"{product.Name} • {Format(request.Qty)} {product.Unit} • {totalCost.ToString("C", PtBr)}",
            LossReasons.Label(request.Reason), ct);
        await transaction.CommitAsync(ct);

        return ToDto(loss);
    }

    public async Task<StockLossDto?> UndoAsync(Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"StockLosses\" WHERE \"Id\" = {id} AND \"CompanyId\" = {currentUser.CompanyId} FOR UPDATE", ct);

        var loss = await db.StockLosses
            .Include(l => l.Lots)
            .FirstOrDefaultAsync(l => l.Id == id && l.CompanyId == currentUser.CompanyId, ct);
        if (loss is null) return null;

        if (loss.Status == "desfeita") throw new InvalidOperationException("Esta perda já foi desfeita.");

        var now = DateTime.UtcNow;

        // A quantidade volta ao estoque do produto e ao MESMO lote de onde saiu (se o lote ainda existir).
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == loss.ProductId && p.CompanyId == currentUser.CompanyId, ct);
        if (product is not null) product.Stock += loss.Qty;

        var lotIds = loss.Lots.Select(l => l.StockBatchId).ToList();
        var lots = lotIds.Count > 0
            ? await db.StockBatches.Where(b => b.CompanyId == currentUser.CompanyId && lotIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct)
            : new Dictionary<Guid, StockBatch>();
        foreach (var consumed in loss.Lots)
        {
            if (lots.TryGetValue(consumed.StockBatchId, out var lot)) lot.AvailableKg += consumed.Qty;
        }

        db.StockMovements.Add(new StockMovement
        {
            CompanyId = currentUser.CompanyId,
            Timestamp = now,
            Type = "entrada",
            Title = $"Perda desfeita: {loss.ProductName}",
            DocInfo = $"Perda de {loss.Date:dd/MM/yyyy} desfeita por {currentUser.UserName}",
            AmountKg = loss.Qty,
            Unit = loss.Unit,
            Description = "Quantidade devolvida ao estoque pelo cancelamento do lançamento de perda",
            BadgeLabel = "Perda desfeita",
            AuditLink = LossReasons.Label(loss.Reason),
        });

        loss.Status = "desfeita";
        loss.UndoneAt = now;
        loss.UndoneBy = currentUser.UserName;

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(
            "desfez perda", "Perda de Estoque",
            $"{loss.ProductName} • {Format(loss.Qty)} {loss.Unit} • {loss.TotalCost.ToString("C", PtBr)}", null, ct);
        await transaction.CommitAsync(ct);

        return ToDto(loss);
    }

    private static string Format(decimal value) => value.ToString("0.###", PtBr);

    private static StockLossDto ToDto(StockLoss l) => new(
        l.Id, l.Date, l.CreatedAt, l.ProductId, l.ProductName, l.Qty, l.Unit, l.UnitCost, l.TotalCost, l.Reason,
        l.Notes, l.LotNumber, l.CreatedBy, l.Status, l.UndoneAt, l.UndoneBy);
}
