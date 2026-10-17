using GCStock.Application.Audit;
using GCStock.Application.Abstractions;
using GCStock.Application.Clients;
using GCStock.Domain.Entities;
using GCStock.Infra.Database;
using Microsoft.EntityFrameworkCore;

namespace GCStock.Infra.Services;

public class ClientService(GCStockDbContext db, ICurrentUserContext currentUser, IAuditRecorder audit) : IClientService
{
    public async Task<List<ClientDto>> GetAllAsync(CancellationToken ct)
    {
        return await db.Clients
            .Where(c => c.CompanyId == currentUser.CompanyId)
            .Select(c => ToDto(c))
            .ToListAsync(ct);
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
                if (request.TotalSpent is not null) existing.TotalSpent = request.TotalSpent.Value;
                if (request.FavoriteCuts is not null) existing.FavoriteCuts = request.FavoriteCuts;
                if (request.Notes is not null) existing.Notes = request.Notes;

                await db.SaveChangesAsync(ct);
                await audit.RecordAsync("editou", "Cliente", existing.Name, null, ct);
                return ToDto(existing);
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
            TotalSpent = request.TotalSpent ?? 0,
            FavoriteCuts = request.FavoriteCuts ?? "Cortes Variados",
            Notes = request.Notes ?? string.Empty,
        };
        db.Clients.Add(saved);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync("criou", "Cliente", saved.Name, null, ct);
        return ToDto(saved);
    }

    private static ClientDto ToDto(Client c) => new(
        c.Id, c.Name, c.Document, c.Phone, c.Email, c.Type, c.FidelityPoints, c.CreditLimit, c.TotalSpent,
        c.FavoriteCuts, c.Notes);
}
