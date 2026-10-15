using GCStock.Application.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GCStock.IntegrationTests;

/// <summary>
/// Garante que toda rota que escreve dados (POST/PUT/PATCH/DELETE) exige uma permissão do catálogo,
/// salvo as exceções abaixo. Ao criar uma rota de escrita nova, o teste falha até alguém protegê-la.
/// </summary>
public class WriteRoutesPermissionTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly HashSet<string> WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>Rotas de escrita sem permissão de catálogo, e por quê.</summary>
    private static readonly string[] Exceptions =
    [
        "/api/v1/auth/register-company", // cadastro público de empresa
        "/api/v1/auth/login",            // anônima
        "/api/v1/auth/logout",           // qualquer usuário logado
        "/api/v1/auth/renew",            // qualquer usuário logado
        "/api/v1/tenants/switch",        // só o dono (validado no serviço)
        "/api/v1/funcionarios/{id:guid}/transferir", // só o dono (validado no serviço)
        "/api/v1/arquivos",              // upload/remoção de arquivos: qualquer usuário logado (chave isolada por empresa)
        "/api/v1/arquivos/url-upload",
    ];

    /// <summary>Permissões validadas dentro de um serviço (dependem do conteúdo da requisição), não por rota.</summary>
    private static readonly string[] ServiceEnforced = [Permissions.SalesEditPrice];

    [Fact]
    public void Every_Write_Route_Requires_A_Catalog_Permission_Or_Is_A_Known_Exception()
    {
        var endpoints = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();

        var unprotected = new List<string>();
        var covered = 0;

        foreach (var endpoint in endpoints)
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var writeMethod = methods.FirstOrDefault(m => WriteMethods.Contains(m));
            if (writeMethod is null) continue;

            var pattern = (endpoint.RoutePattern.RawText ?? string.Empty).TrimEnd('/');
            if (Exceptions.Contains(pattern)) continue;

            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy)
                .Where(p => p is not null);

            if (policies.Any(p => Permissions.IsValid(p))) covered++;
            else unprotected.Add($"{writeMethod} {pattern}");
        }

        Assert.True(unprotected.Count == 0, "Rotas de escrita sem permissão: " + string.Join("; ", unprotected));
        Assert.True(covered >= 27, $"Esperava ao menos 27 rotas de escrita protegidas, achei {covered}.");
    }

    [Fact]
    public void Every_Catalog_Permission_Is_Used_By_At_Least_One_Route()
    {
        var used = factory.Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .SelectMany(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>())
            .Select(a => a.Policy)
            .ToHashSet();

        var orphans = Permissions.AllKeys.Where(key => !used.Contains(key) && !ServiceEnforced.Contains(key)).ToList();
        Assert.True(orphans.Count == 0, "Permissões do catálogo sem nenhuma rota: " + string.Join(", ", orphans));
    }

    [Fact]
    public void Catalog_Entries_Are_Complete()
    {
        foreach (var p in Permissions.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Label), p.Key);
            Assert.False(string.IsNullOrWhiteSpace(p.Description), p.Key);
            Assert.False(string.IsNullOrWhiteSpace(p.Group), p.Key);
        }
    }
}
