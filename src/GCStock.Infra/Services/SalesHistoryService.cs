using GCStock.Application.Abstractions;
using GCStock.Application.Finance;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class SalesHistoryService(GCStockDbContext db, ICurrentUserContext currentUser) : ISalesHistoryService
{
    public async Task<List<SaleRecordDto>> GetAllAsync(CancellationToken ct)
    {
        return await db.SaleRecords
            .Where(s => s.CompanyId == currentUser.CompanyId && s.Status != "cancelada")
            .OrderByDescending(s => s.Timestamp)
            .Select(s => new SaleRecordDto(
                s.Id, s.SaleId, s.Timestamp, s.Total, s.NetAmount, s.FeeAmount, s.PaymentMode, s.ClientName, s.SoldBy))
            .ToListAsync(ct);
    }
}
