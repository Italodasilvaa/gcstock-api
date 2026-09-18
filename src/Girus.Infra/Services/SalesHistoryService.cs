using Girus.Application.Abstractions;
using Girus.Application.Finance;
using Girus.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace Girus.Infra.Services;

public class SalesHistoryService(GirusDbContext db, ICurrentUserContext currentUser) : ISalesHistoryService
{
    public async Task<List<SaleRecordDto>> GetAllAsync(CancellationToken ct)
    {
        return await db.SaleRecords
            .Where(s => s.CompanyId == currentUser.CompanyId)
            .OrderByDescending(s => s.Timestamp)
            .Select(s => new SaleRecordDto(
                s.Id, s.SaleId, s.Timestamp, s.Total, s.NetAmount, s.FeeAmount, s.PaymentMode, s.ClientName, s.SoldBy))
            .ToListAsync(ct);
    }
}
