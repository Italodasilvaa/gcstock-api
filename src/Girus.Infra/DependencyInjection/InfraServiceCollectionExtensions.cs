using Girus.Application.Abstractions;
using Girus.Application.Audit;
using Girus.Application.Auth;
using Girus.Application.Catalog;
using Girus.Application.Clients;
using Girus.Application.Employees;
using Girus.Application.Finance;
using Girus.Application.Inventory;
using Girus.Application.Pdv;
using Girus.Application.Purchasing;
using Girus.Application.Tenants;
using Girus.Infra.Database;
using Girus.Infra.HealthChecks;
using Girus.Infra.Security;
using Girus.Infra.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Girus.Infra.DependencyInjection;

public static class InfraServiceCollectionExtensions
{
    public static IServiceCollection AddInfra(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDatabaseProbe, SqlProbeService>();

        services
            .AddHealthChecks()
            .AddCheck<SqlServerHealthCheck>("database", tags: ["ready"]);

        services.AddDbContext<GirusDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddScoped<IAuditRecorder, AuditRecorder>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IEmployeeService, EmployeeService>();

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IKitService, KitService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPurchaseService, PurchaseService>();

        services.AddScoped<IPayableService, PayableService>();
        services.AddScoped<IReceivableService, ReceivableService>();
        services.AddScoped<IFeeService, FeeService>();
        services.AddScoped<IAlertSettingService, AlertSettingService>();
        services.AddScoped<ICardSettlementService, CardSettlementService>();
        services.AddScoped<ISalesHistoryService, SalesHistoryService>();

        services.AddScoped<IPosService, PosService>();
        services.AddScoped<IClientService, ClientService>();

        return services;
    }
}
