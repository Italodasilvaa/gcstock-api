using GCStock.Application.Abstractions;
using GCStock.Application.Audit;
using GCStock.Application.Auth;
using GCStock.Application.Catalog;
using GCStock.Application.Clients;
using GCStock.Application.Employees;
using GCStock.Application.Finance;
using GCStock.Application.Inventory;
using GCStock.Application.Pdv;
using GCStock.Application.Purchasing;
using GCStock.Application.Sales;
using GCStock.Application.Storage;
using GCStock.Application.Tenants;
using GCStock.Infra.Database;
using GCStock.Infra.HealthChecks;
using GCStock.Infra.Security;
using GCStock.Infra.Services;
using GCStock.Infra.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GCStock.Infra.DependencyInjection;

public static class InfraServiceCollectionExtensions
{
    public static IServiceCollection AddInfra(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDatabaseProbe, SqlProbeService>();

        services
            .AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

        services.AddDbContext<GCStockDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddScoped<IAuditRecorder, AuditRecorder>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<IEmployeeService, EmployeeService>();

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
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
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IFinancialSummaryService, FinancialSummaryService>();
        services.AddScoped<IStockLossService, StockLossService>();
        services.AddScoped<IClientService, ClientService>();

        services.Configure<R2Settings>(configuration.GetSection(R2Settings.SectionName));
        services.AddSingleton<IFileStorage, R2FileStorage>();

        return services;
    }
}
