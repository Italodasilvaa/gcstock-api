using Girus.Application.Audit;
using Girus.Application.Abstractions;
using Girus.Application.Common;
using Girus.Application.Purchasing;
using Girus.Domain.Entities;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class PurchaseService(GirusDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IPurchaseService
{
    public async Task<List<PurchaseEntryDto>> GetAllAsync(CancellationToken ct)
    {
        var purchases = await db.PurchaseEntries
            .Include(p => p.Items)
            .Where(p => p.CompanyId == currentUser.CompanyId)
            .OrderByDescending(p => p.EntryDate)
            .ToListAsync(ct);
        return purchases.Select(ToDto).ToList();
    }

    public async Task<PurchaseEntryDto> SaveAsync(SavePurchaseRequest request, CancellationToken ct)
    {
        var count = await db.PurchaseEntries.CountAsync(p => p.CompanyId == currentUser.CompanyId, ct);
        var now = DateTime.UtcNow;
        var rnd = Random.Shared;

        var saved = new PurchaseEntry
        {
            CompanyId = currentUser.CompanyId,
            OrderCode = "PED-2025-0" + (90 + count),
            NfeNumber = request.NfeNumber ?? "NF-e 000." + rnd.Next(10000, 100000),
            NfeSeries = request.NfeSeries ?? "Série 1",
            EntryDate = request.EntryDate ?? DateOnly.FromDateTime(now),
            EntryTime = now.ToString("HH:mm"),
            SupplierName = request.SupplierName ?? "Minerva Foods S.A.",
            SupplierCnpj = request.SupplierCnpj ?? "67.620.377/0001-47",
            SupplierCity = request.SupplierCity ?? "Barretos / SP",
            SupplierSif = request.SupplierSif ?? "SIF 421",
            ItemsCount = request.ItemsCount ?? request.Items?.Count ?? 3,
            TotalWeightKg = request.TotalWeightKg ?? 160,
            TotalAmount = request.TotalAmount ?? 5765.0m,
            ShippingCost = request.ShippingCost ?? 120.0m,
            PaymentCondition = request.PaymentCondition ?? "Boleto 30 dias",
            PaymentStatus = "pendente",
            PaymentMode = request.PaymentMode ?? "a_prazo",
            Items = request.Items?.Select(i => new PurchaseItem
            {
                Name = i.Name,
                Code = i.Code,
                Quantity = i.Quantity,
                Unit = i.Unit,
                UnitCost = i.UnitCost,
                Subtotal = i.Subtotal,
                ExpiryDate = i.ExpiryDate,
                LotNumber = i.LotNumber,
                IsPerishable = i.IsPerishable,
            }).ToList() ?? [],
        };
        db.PurchaseEntries.Add(saved);

        // Também cria automaticamente um título em Contas a Pagar (Módulo Compras).
        var dueDate = DateOnly.FromDateTime(now.AddDays(30));
        var payable = new AccountsPayable
        {
            CompanyId = currentUser.CompanyId,
            Supplier = saved.SupplierName,
            Description = $"Entrada NF-e {saved.NfeNumber} ({saved.ItemsCount} itens)",
            NfeReference = saved.NfeNumber,
            IssueDate = saved.EntryDate,
            DueDate = dueDate,
            DueLabel = DateLabelHelper.DueLabelFor(dueDate),
            Amount = saved.TotalAmount,
            PaymentMethod = saved.PaymentCondition,
            Origin = "Módulo Compras",
            Status = "a_vencer",
        };
        db.AccountsPayables.Add(payable);

        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Compra / Entrada", $"{saved.OrderCode} • {saved.SupplierName}", null, ct);
        return ToDto(saved);
    }

    private static PurchaseEntryDto ToDto(PurchaseEntry p) => new(
        p.Id, p.OrderCode, p.NfeNumber, p.NfeSeries, p.EntryDate, p.EntryTime, p.SupplierName, p.SupplierCnpj,
        p.SupplierCity, p.SupplierSif, p.ItemsCount, p.TotalWeightKg, p.TotalAmount, p.ShippingCost,
        p.PaymentCondition, p.PaymentStatus, p.PaymentMode,
        p.Items.Select(i => new PurchaseItemDto(
            i.Name, i.Code, i.Quantity, i.Unit, i.UnitCost, i.Subtotal, i.ExpiryDate, i.LotNumber, i.IsPerishable)).ToList());
}
