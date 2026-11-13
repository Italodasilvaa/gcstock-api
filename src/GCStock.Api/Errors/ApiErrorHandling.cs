using System.Text.Json;
using GCStock.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GCStock.Api.Errors;

/// <summary>
/// Tratamento global de erros e contexto de log. As rotas continuam tratando os erros de negócio que conhecem; o que escapa
/// delas cai aqui e vira uma resposta JSON { message, traceId } em vez de um 500 cru, com o erro completo no log.
/// </summary>
public static class ApiErrorHandling
{
    /// <summary>Limite do corpo de requisições JSON (as rotas de arquivo têm limite próprio em FileRules).</summary>
    private const long MaxJsonBodyBytes = 2 * 1024 * 1024;

    public static void UseApiErrorHandling(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
        {
            var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            if (exception is null) return;

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GCStock.Api.Errors");
            var (status, message, expected) = Map(exception);

            // Erro de negócio esperado (validação, conflito) é aviso; o resto é erro inesperado, com stack no log.
            if (expected)
            {
                logger.LogWarning(exception, "Requisição recusada ({Status}) {Method} {Path}: {Message}",
                    status, context.Request.Method, context.Request.Path, exception.Message);
            }
            else
            {
                logger.LogError(exception, "Erro inesperado em {Method} {Path} (trace {TraceId})",
                    context.Request.Method, context.Request.Path, context.TraceIdentifier);
            }

            if (context.Response.HasStarted) return;
            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                message,
                traceId = context.TraceIdentifier,
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }));
    }

    /// <summary>Limita o corpo das requisições (exceto upload de arquivos) e coloca empresa/usuário/trace em todo log da requisição.</summary>
    public static void UseRequestContext(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api/v1/arquivos"))
            {
                var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = MaxJsonBodyBytes;
            }

            var scope = new Dictionary<string, object?> { ["TraceId"] = context.TraceIdentifier };
            var user = context.RequestServices.GetService<ICurrentUserContext>();
            if (user is { IsAuthenticated: true })
            {
                scope["CompanyId"] = user.CompanyId;
                scope["UserId"] = user.UserId;
            }

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GCStock.Api.Request");
            using (logger.BeginScope(scope))
            {
                await next();
            }
        });
    }

    private static (int Status, string Message, bool Expected) Map(Exception exception) => exception switch
    {
        ArgumentException ex => (StatusCodes.Status400BadRequest, ex.Message, true),
        BadHttpRequestException => (StatusCodes.Status400BadRequest, "Requisição inválida: confira os dados enviados.", true),
        JsonException => (StatusCodes.Status400BadRequest, "Requisição inválida: os dados enviados não puderam ser lidos.", true),
        UnauthorizedAccessException ex => (StatusCodes.Status403Forbidden, ex.Message, true),
        KeyNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message, true),
        InvalidOperationException ex => (StatusCodes.Status409Conflict, ex.Message, true),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
            "Os dados mudaram enquanto você editava. Atualize a tela e tente de novo.", true),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
            (StatusCodes.Status409Conflict, "Já existe um registro com esses dados (duplicado).", true),
        DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } } =>
            (StatusCodes.Status409Conflict, "Este registro está em uso por outros dados e não pode ser alterado ou excluído.", true),
        _ => (StatusCodes.Status500InternalServerError,
            "Erro inesperado no servidor. Se continuar, informe o código abaixo ao suporte.", false),
    };
}
