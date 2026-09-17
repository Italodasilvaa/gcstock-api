# Girus API

Base .NET 10 em DDD/Clean Architecture para a API e o Worker do Girus.

## Estrutura da solução

```
Girus.slnx
src/
  Girus.Api           # API HTTP (minimal APIs, Swagger, CORS, health checks)
  Girus.Worker         # Worker Service com probe SQL periódico
  Girus.Application    # Casos de uso / regras de aplicação
  Girus.Domain         # Entidades e regras de domínio
  Girus.Infra          # Acesso a dados, health checks, integrações
tests/
  Girus.UnitTests        # Testes unitários (Application/Domain)
  Girus.IntegrationTests  # Testes de integração (Api/Infra)
deploy/
  api.Dockerfile
  worker.Dockerfile
  stacks/stack.yml
azure-pipelines.yml
```

### Referências entre projetos

- `Girus.Application` → `Girus.Domain`
- `Girus.Infra` → `Girus.Application`, `Girus.Domain`
- `Girus.Api` → `Girus.Application`, `Girus.Infra`
- `Girus.Worker` → `Girus.Application`, `Girus.Infra`
- `Girus.UnitTests` → `Girus.Application`, `Girus.Domain`
- `Girus.IntegrationTests` → `Girus.Api`, `Girus.Infra`

## API

- **Swagger**: disponível em `/swagger` (Swashbuckle.AspNetCore 7.3.1).
- **CORS**: libera `localhost`, `valee.com.br` e subdomínios (`*.valee.com.br`).
- **Health checks**:
  - `GET /health/live` — verificação de liveness (self, não depende de infraestrutura externa).
  - `GET /health/ready` — verificação de readiness, valida o SQL Server com `SELECT 1`.
  - `GET /health` — alias de `/health/ready`.

  Resposta JSON estruturada:

  ```json
  {
    "status": "Healthy",
    "duration": 12.34,
    "timestamp": "2026-09-17T12:00:00Z",
    "checks": [
      {
        "name": "database",
        "status": "Healthy",
        "duration": 12.34,
        "description": "SQL Server disponível (SELECT 1).",
        "error": null
      }
    ]
  }
  ```

## Worker

Hosted service (`Girus.Worker.Worker`) que executa um probe periódico no SQL Server (`SELECT 1`), com intervalo configurável via `Worker:DatabaseProbeIntervalSeconds` (padrão: 30 segundos). Sucesso e falha são registrados via `ILogger`.

## Configuração

`Girus.Api` e `Girus.Worker` seguem o padrão de configuração por ambiente:

- `appsettings.json`
- `appsettings.Development.json`
- `appsettings.Staging.json`
- `appsettings.Production.json`

`ConnectionStrings:DefaultConnection` já vem com o valor padrão do ambiente de desenvolvimento/homologação:

```
Server=db-nprd.valee.com.br;Database=dbValee-Refactor;User Id=sa;Password=SqlAdm2010;TrustServerCertificate=True;
```

## Build, testes e execução local

```bash
dotnet build Girus.slnx
dotnet test Girus.slnx
dotnet run --project src/Girus.Api
dotnet run --project src/Girus.Worker
```

## Deploy

### Docker

- `deploy/api.Dockerfile` — build/publish multi-stage da API (`mcr.microsoft.com/dotnet/aspnet:10.0`), expõe a porta `8080`.
- `deploy/worker.Dockerfile` — build/publish multi-stage do Worker (`mcr.microsoft.com/dotnet/runtime:10.0`).

### Stack (Docker Swarm)

`deploy/stacks/stack.yml` define os serviços `api` e `worker` com placeholders substituídos em tempo de deploy:

| Placeholder            | Descrição                                  |
| ----------------------- | ------------------------------------------- |
| `__REGISTRY_URL__`      | URL do registry de imagens                  |
| `__API_IMAGE_NAME__`    | Nome da imagem da API                       |
| `__WORKER_IMAGE_NAME__` | Nome da imagem do Worker                    |
| `__DOCKER_TAG__`        | Tag da imagem (build id)                    |
| `__ENVIRONMENT__`       | Ambiente (`production` / `staging`)         |
| `__SERVICE_NAME__`      | Nome da stack (`girus-prd` / `girus-stg`)   |
| `__HOST_DOMAIN__`       | Domínio público exposto via Traefik         |

### Pipeline (Azure DevOps)

`azure-pipelines.yml` é disparado apenas pelas branches `production` e `staging`:

| Branch       | Stack       | Host                          | Appsettings               |
| ------------ | ----------- | ------------------------------ | -------------------------- |
| `production` | `girus-prd` | `api-girus.valee.com.br`       | `appsettings.Production.json` |
| `staging`    | `girus-stg` | `api-girus-hml.valee.com.br`   | `appsettings.Staging.json`    |

Etapas do pipeline:

1. Restore, build e testes da solução.
2. Build e push das imagens de API e Worker para `cr.valee.com.br`.
3. Geração do `stack.yml` com os placeholders substituídos.
4. Deploy via `docker stack deploy` no ambiente correspondente.

## Monitoramento

- Liveness/Readiness via `/health/live` e `/health/ready` (consumíveis por probes de orquestradores e Traefik).
- Logs estruturados via `ILogger` na API e no Worker (probe SQL periódico loga sucesso/falha).
