using System.Text;
using GCStock.Api.Authorization;
using GCStock.Api.Cors;
using GCStock.Api.Endpoints;
using GCStock.Api.HealthChecks;
using GCStock.Application.Abstractions;
using GCStock.Application.Access;
using GCStock.Infra.Database;
using GCStock.Infra.DependencyInjection;
using GCStock.Infra.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Docker/Swarm secrets montados em /run/secrets (um arquivo por chave, ex.: ConnectionStrings__DefaultConnection).
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);

builder.Services.AddInfra(builder.Configuration);

var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey) || jwtSettings.SecretKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey não configurada (ou muito curta). Defina a variável de ambiente 'Jwt__SecretKey' com pelo menos 32 caracteres.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Sem isso, o handler remapeia o claim curto "sub" para o URI legado
        // ClaimTypes.NameIdentifier ao validar o token — e HttpCurrentUserContext.UserId,
        // que procura por JwtRegisteredClaimNames.Sub ("sub"), nunca encontra o claim e lança
        // InvalidOperationException. Isso derruba /auth/me (chamado no boot do front-end) e
        // desloga o usuário mesmo com um token válido.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddAuthorization(options =>
{
    // Uma policy por permissão do catálogo; use .RequireAuthorization(Permissions.X) nas rotas.
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission.Key, policy =>
            policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Key)));
    }
});
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyExtensions.PolicyName, policy =>
    {
        policy
            .SetIsOriginAllowed(CorsPolicyExtensions.IsOriginAllowed)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "GC Stock ERP API",
        Version = "v1",
    });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Informe apenas o token JWT (sem o prefixo 'Bearer ').",
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            []
        },
    });
});

var app = builder.Build();

// Aplica as migrations pendentes em Development ou quando Database:MigrateOnStartup=true (stack da VPS).
// O seed de demonstração roda somente em Development.
var migrateOnStartup = app.Configuration.GetValue<bool>("Database:MigrateOnStartup");
if (app.Environment.IsDevelopment() || migrateOnStartup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GCStockDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
    {
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await DbSeeder.SeedAsync(db, hasher);
    }
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors(CorsPolicyExtensions.PolicyName);

// Em desenvolvimento local, evita o redirecionamento para a porta HTTPS (launchSettings expõe
// http+https juntos): o front-end costuma fixar a URL da API em http://localhost:5105, e o
// redirect para https://localhost:7133 quebra o preflight de CORS quando o certificado dev
// não está confiado no navegador.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthCheckResponseWriter.WriteAsync
});

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapEmployeeEndpoints();
app.MapPermissionEndpoints();
app.MapCatalogEndpoints();
app.MapInventoryEndpoints();
app.MapPurchaseEndpoints();
app.MapFinanceEndpoints();
app.MapConfiguracoesEndpoints();
app.MapPdvEndpoints();
app.MapSalesEndpoints();
app.MapLossEndpoints();
app.MapClientEndpoints();
app.MapAuditEndpoints();
app.MapFileEndpoints();

app.Run();

public partial class Program;
