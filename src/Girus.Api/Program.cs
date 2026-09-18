using System.Text;
using Girus.Api.Cors;
using Girus.Api.Endpoints;
using Girus.Api.HealthChecks;
using Girus.Application.Abstractions;
using Girus.Infra.Database;
using Girus.Infra.DependencyInjection;
using Girus.Infra.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddAuthorization();

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

// Em desenvolvimento, aplica as migrations pendentes e semeia os dados de demonstração
// automaticamente. Em Staging/Production, rode `dotnet ef database update` manualmente/no pipeline.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<GirusDbContext>();
    await db.Database.MigrateAsync();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DbSeeder.SeedAsync(db, hasher);
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors(CorsPolicyExtensions.PolicyName);

app.UseHttpsRedirection();

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
app.MapCatalogEndpoints();
app.MapInventoryEndpoints();
app.MapPurchaseEndpoints();
app.MapFinanceEndpoints();
app.MapConfiguracoesEndpoints();
app.MapPdvEndpoints();
app.MapClientEndpoints();
app.MapAuditEndpoints();

app.Run();

public partial class Program;
