# GC Stock API

Base .NET 10 em DDD/Clean Architecture para a API e o Worker do GC Stock ERP (antigo Girus).

## Estrutura da solução

```
GCStock.slnx
src/
  GCStock.Api           # API HTTP (minimal APIs, Swagger, CORS, health checks)
  GCStock.Worker         # Worker Service com probe SQL periódico
  GCStock.Application    # Casos de uso / regras de aplicação
  GCStock.Domain         # Entidades e regras de domínio
  GCStock.Infra          # Acesso a dados, health checks, integrações
tests/
  GCStock.UnitTests        # Testes unitários (Application/Domain)
  GCStock.IntegrationTests  # Testes de integração (Api/Infra)
deploy/
  api.Dockerfile
  worker.Dockerfile
  stacks/stack.yml
azure-pipelines.yml
```

### Referências entre projetos

- `GCStock.Application` → `GCStock.Domain`
- `GCStock.Infra` → `GCStock.Application`, `GCStock.Domain`
- `GCStock.Api` → `GCStock.Application`, `GCStock.Infra`
- `GCStock.Worker` → `GCStock.Application`, `GCStock.Infra`
- `GCStock.UnitTests` → `GCStock.Application`, `GCStock.Domain`
- `GCStock.IntegrationTests` → `GCStock.Api`, `GCStock.Infra`

## API

- **Swagger**: disponível em `/swagger` (Swashbuckle.AspNetCore 7.3.1).
- **CORS**: libera `localhost`, `iwsolutions.cloud` e subdomínios (`*.iwsolutions.cloud`).
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

## Domínio de negócio (GC Stock ERP)

A API implementa, com banco real (PostgreSQL via EF Core) e autenticação JWT real (bcrypt + JWT), o mesmo contrato
que o front-end `gcstock-ui` hoje simula localmente no `MockDataService` (multi-empresa, produtos, kits, estoque,
compras, financeiro, PDV, clientes, funcionários e auditoria). Ver `src/GCStock.Api/Endpoints/*.cs` para o mapeamento
completo das rotas — resumo:

| Área | Rotas | Autenticação |
| --- | --- | --- |
| Auth | `POST /api/v1/auth/register-company`, `POST /api/v1/auth/login`, `POST /api/v1/auth/logout`, `GET /api/v1/auth/me`, `GET /api/v1/auth/companies`, `GET /api/v1/auth/companies/{id}/quick-login-email` | login/register/companies são públicas; `me`/`logout` exigem token |
| Tenants | `GET /api/v1/tenants/my-companies`, `POST /api/v1/tenants/switch`, `GET /api/v1/tenants/filiais` | token (troca de empresa só para o dono) |
| Funcionários | `GET/POST /api/v1/funcionarios`, `PUT/DELETE /api/v1/funcionarios/{id}`, `POST /api/v1/funcionarios/{id}/transferir` | token |
| Produtos | `GET/POST /api/v1/produtos`, `PUT/DELETE /api/v1/produtos/{id}` | token |
| Kits | `GET/POST /api/v1/kits-churrasco`, `PUT /api/v1/kits-churrasco/{id}` | token |
| Estoque | `GET /api/v1/estoque/lotes`, `GET /api/v1/estoque/movimentacoes` | token |
| Compras | `GET /api/v1/compras`, `POST /api/v1/compras/entrada` | token |
| Financeiro | `GET/POST /api/v1/financeiro/contas-a-pagar` (+ `/{id}/baixar`), `GET/POST /api/v1/financeiro/clientes-a-receber` (+ `/{id}/liquidar`), `GET /api/v1/financeiro/liquidacoes-cartao` (+ `/{id}/antecipar`), `GET /api/v1/financeiro/vendas` | token |
| Configurações | `GET/PATCH /api/v1/configuracoes/taxas/{id}`, `GET/PUT /api/v1/configuracoes/alertas-sanitarios` | token |
| PDV | `POST /api/v1/pdv/finalizar-venda` | token |
| Clientes | `GET/POST /api/v1/clientes`, `PUT /api/v1/clientes/{id}` | token |
| Auditoria | `GET /api/v1/auditoria` | token |

Todos os dados são isolados por empresa (`companyId` extraído das claims do JWT) — o mesmo modelo multi-tenant do mock
(um Administrator pode ter N empresas; um Employee pertence a exatamente uma).

### Autenticação (JWT + bcrypt)

- Senhas de `Administrator` e `Employee` são armazenadas com hash bcrypt (`BCrypt.Net-Next`).
- Login devolve um JWT (HMAC-SHA256) com claims `sub`, `name`, `email`, `role`, `company_id` e, quando aplicável,
  `administrator_id` (dono) ou `employee_id` (funcionário). Validade padrão: 480 minutos (`Jwt:ExpiresMinutes`).
- Envie o token em `Authorization: Bearer <token>` nas rotas protegidas.
- **`Jwt:SecretKey`**: em `appsettings.json` há um valor de desenvolvimento gerado apenas para rodar localmente.
  Em Staging/Production o valor está vazio de propósito e a API **recusa iniciar** se a variável de ambiente
  `Jwt__SecretKey` (≥ 32 caracteres) não estiver definida.
- `GET /api/v1/auth/companies/{id}/quick-login-email` é uma conveniência de demonstração (mesmo comportamento do
  "acesso rápido" da tela de login do mock) que expõe o e-mail do dono de uma empresa sem autenticação — **remova
  ou proteja este endpoint antes de qualquer uso real em produção**.

### Banco de dados e seed

- O banco é o PostgreSQL da VPS (`db.iwsolutions.cloud:5432`, banco dedicado `gcstock`, separado do `main_db`).
  Todos os ambientes (inclusive o desenvolvimento local) consomem esse mesmo banco — ver "Configuração" abaixo.
- Em ambiente **Development**, a API aplica as migrations pendentes e popula o banco automaticamente ao iniciar
  (`DbSeeder`), recriando os mesmos donos/empresas de demonstração que existiam no mock (para não perder o fluxo de
  login durante a migração):

  | E-mail | Senha | Empresa |
  | --- | --- | --- |
  | `italodasilvaa@gmail.com` | `123` | M&C Frig Carnes Nobres (+ M&C Frig Litoral) |
  | `contato@angusprime.com.br` | `123` | Angus Prime Steakhouse & Boutique |
  | `diretoria@braseirodosul.com.br` | `123` | Frigorífico Braseiro do Sul |

- Em **Staging/Production**, rode as migrations manualmente (ou via pipeline) antes do primeiro deploy:

  ```bash
  dotnet ef database update --project src/GCStock.Infra --startup-project src/GCStock.Api
  ```

### Como conectar o front-end (gcstock-ui)

O `gcstock-ui` hoje fala inteiramente com `MockDataService` (dados no `localStorage`). Para plugar nesta API:

1. Trocar cada chamada `mockData.xxx(...)` por uma chamada HTTP equivalente (mesmos nomes de campos — os DTOs em
   `GCStock.Application/*/*.cs` foram desenhados espelhando `src/app/core/models/models.ts`).
2. Guardar o `token` retornado por `/auth/login` (ou `/auth/register-company`) e anexá-lo em todas as chamadas via
   um `HttpInterceptor` (`Authorization: Bearer <token>`).
3. Substituir `getCurrentSession()` por uma chamada a `GET /api/v1/auth/me` na inicialização do app, para validar o
   token salvo e restaurar a sessão.
4. Ajustar `environment.ts`/`environment.development.ts` com a URL base da API (ex.: `http://localhost:5xxx`).

Nenhuma alteração no `gcstock-ui` foi feita nesta tarefa — este passo fica para quando a integração for ligada de fato.

## Worker

Hosted service (`GCStock.Worker.Worker`) que executa um probe periódico no PostgreSQL (`SELECT 1`), com intervalo configurável via `Worker:DatabaseProbeIntervalSeconds` (padrão: 30 segundos). Sucesso e falha são registrados via `ILogger`.

## Configuração

`GCStock.Api` e `GCStock.Worker` seguem o padrão de configuração por ambiente:

- `appsettings.json`
- `appsettings.Development.json`
- `appsettings.Staging.json`
- `appsettings.Production.json`

`ConnectionStrings:DefaultConnection` no `appsettings.json` aponta para o PostgreSQL da VPS **sem a senha**
(nunca versione a senha):

```
Host=db.iwsolutions.cloud;Port=5432;Database=gcstock;Username=admin;SSL Mode=Prefer;Application Name=GCStock.Api
```

A string completa (com `Password=...`) vem de fora do repositório:

- **Desenvolvimento local:** user-secrets (uma vez por máquina, em cada projeto):

  ```bash
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=db.iwsolutions.cloud;Port=5432;Database=gcstock;Username=admin;Password=<SENHA>;SSL Mode=Prefer;Application Name=GCStock.Api" --project src/GCStock.Api
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=db.iwsolutions.cloud;Port=5432;Database=gcstock;Username=admin;Password=<SENHA>;SSL Mode=Prefer;Application Name=GCStock.Worker" --project src/GCStock.Worker
  ```

- **Staging/Production (container):** variável de ambiente `ConnectionStrings__DefaultConnection`. Rodando no mesmo
  Swarm da VPS, use o host interno `postgres` (rede overlay `backend`) em vez de `db.iwsolutions.cloud`.

A seção `Jwt` (`Issuer`, `Audience`, `SecretKey`, `ExpiresMinutes`) segue o mesmo padrão — ver "Autenticação (JWT +
bcrypt)" acima para os detalhes de `SecretKey` por ambiente.

## Build, testes e execução local

```bash
dotnet build GCStock.slnx
dotnet test GCStock.slnx
dotnet run --project src/GCStock.Api    # aplica migrations + seed automaticamente em Development
dotnet run --project src/GCStock.Worker
```

Depois de rodando, abra `https://localhost:<porta>/swagger` para testar os endpoints (o Swagger já vem configurado
com suporte a Bearer token — clique em "Authorize" e cole o token retornado por `/api/v1/auth/login`).

## Deploy

### Docker

- `deploy/api.Dockerfile` — build/publish multi-stage da API (`mcr.microsoft.com/dotnet/aspnet:10.0`), expõe a porta `8080`.
- `deploy/worker.Dockerfile` — build/publish multi-stage do Worker (`mcr.microsoft.com/dotnet/aspnet:10.0`).

### Stack (Docker Swarm)

`deploy/stacks/stack.yml` define os serviços `api` e `worker` com placeholders substituídos em tempo de deploy:

| Placeholder            | Descrição                                  |
| ----------------------- | ------------------------------------------- |
| `__REGISTRY_URL__`      | URL do registry de imagens                  |
| `__API_IMAGE_NAME__`    | Nome da imagem da API                       |
| `__WORKER_IMAGE_NAME__` | Nome da imagem do Worker                    |
| `__DOCKER_TAG__`        | Tag da imagem (build id)                    |
| `__ENVIRONMENT__`       | Ambiente (`Production` / `Staging`)         |
| `__SERVICE_NAME__`      | Nome da stack (`gcstock-prd` / `gcstock-stg`)   |
| `__HOST_DOMAIN__`       | Domínio público exposto via Traefik         |

A stack roda na VPS (Swarm + Traefik + Postgres próprio), usa as redes `public` e `backend`, lê a connection string e a
chave JWT de **Swarm secrets** (`<stack>-db-connection`, `<stack>-jwt-secret`) e aplica migrations ao iniciar
(`Database__MigrateOnStartup=true`). **Passo a passo de preparação do servidor (secrets, DNS, registry): `deploy/VPS.md`.**

### Pipeline (Azure DevOps)

`azure-pipelines.yml` é disparado apenas pelas branches `production` e `staging`:

| Branch       | Stack       | Host                          | Appsettings               |
| ------------ | ----------- | ------------------------------ | -------------------------- |
| `production` | `gcstock-prd` | `api.iwsolutions.cloud`       | `appsettings.Production.json` |
| `staging`    | `gcstock-stg` | `api-hml.iwsolutions.cloud`   | `appsettings.Staging.json`    |

Etapas do pipeline:

1. Restore, build e testes da solução.
2. Build e push das imagens de API e Worker para `registry.iwsolutions.cloud`.
3. Geração do `stack.yml` com os placeholders substituídos.
4. Deploy via `docker stack deploy` no ambiente correspondente.

## Monitoramento

- Liveness/Readiness via `/health/live` e `/health/ready` (consumíveis por probes de orquestradores e Traefik).
- Logs estruturados via `ILogger` na API e no Worker (probe SQL periódico loga sucesso/falha).
