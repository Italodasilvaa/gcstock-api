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

- Dois bancos no PostgreSQL da VPS (`db.iwsolutions.cloud:5432`): `gcstock_nprd` (desenvolvimento/homologação, com seed de demonstração) e `gcstock_prd` (produção, sem seed). O desenvolvimento local usa o `gcstock_nprd`; o `gcstock_prd` só é acessado pela stack de produção (ver `deploy/VPS.md`).
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
Host=db.iwsolutions.cloud;Port=5432;Database=gcstock_nprd;Username=admin;SSL Mode=Prefer;Application Name=GCStock.Api
```

A string completa (com `Password=...`) vem de fora do repositório:

- **Desenvolvimento local:** user-secrets (uma vez por máquina, em cada projeto):

  ```bash
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=db.iwsolutions.cloud;Port=5432;Database=gcstock_nprd;Username=admin;Password=<SENHA>;SSL Mode=Prefer;Application Name=GCStock.Api" --project src/GCStock.Api
  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=db.iwsolutions.cloud;Port=5432;Database=gcstock_nprd;Username=admin;Password=<SENHA>;SSL Mode=Prefer;Application Name=GCStock.Worker" --project src/GCStock.Worker
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
| `__SERVICE_NAME__`      | Nome da stack (`gcstock-prd` / `gcstock-nprd`)   |
| `__HOST_DOMAIN__`       | Domínio público exposto via Traefik         |

A stack roda na VPS (Swarm + Traefik + Postgres próprio), usa as redes `public` e `backend`, lê a connection string e a
chave JWT de **Swarm secrets** (`<stack>-db-connection`, `<stack>-jwt-secret`) e aplica migrations ao iniciar
(`Database__MigrateOnStartup=true`). **Passo a passo de preparação do servidor (secrets, DNS, registry): `deploy/VPS.md`.**

### Pipeline (Azure DevOps)

`azure-pipelines.yml` é disparado apenas pelas branches `production` e `staging`:

| Branch       | Stack       | Host                          | Appsettings               |
| ------------ | ----------- | ------------------------------ | -------------------------- |
| `production` | `gcstock-prd` | `api.iwsolutions.cloud`       | `appsettings.Production.json` |
| `staging`    | `gcstock-nprd` | `api-nprd.iwsolutions.cloud`   | `appsettings.Staging.json`    |

Etapas do pipeline:

1. Restore, build e testes da solução.
2. Build e push das imagens de API e Worker para `registry.iwsolutions.cloud`.
3. Geração do `stack.yml` com os placeholders substituídos.
4. Deploy via `docker stack deploy` no ambiente correspondente.


## Permissões de funcionários

Cada funcionário recebe permissões granulares de **criar, editar e excluir** em cada tela, guardadas em
`Employees.Permissions` (`text[]`). O papel (`admin`/`gerente`/`operador`) é só um rótulo e não concede nada.

- **Dono da empresa** (Administrator): possui **todas** as permissões, sem nenhum cadastro.
- **Consultar** (GET) é liberado a qualquer usuário autenticado.
- **Atribuir permissões** é exclusivo do dono (a API responde 403 para outros usuários, mesmo com `funcionarios.editar`).
- Funcionário novo nasce **sem** permissões. A permissão é lida do banco a cada chamada, então revogar vale na hora,
  mesmo com o token ainda válido.

| Tela | Permissões |
|------|-----------|
| Produtos | `produtos.criar`, `produtos.editar`, `produtos.excluir` |
| Categorias de produto | `categorias.criar`, `categorias.editar` (inclui reordenar), `categorias.excluir` |
| Kits de churrasco | `kits.criar`, `kits.editar`, `kits.excluir` |
| Compras | `compras.criar` (lançar entrada), `compras.editar` (editar pedidos, inclusive pagos) |
| Vendas (PDV) | `vendas.criar` (finalizar venda), `vendas.alterar-preco` (vender com preço diferente do cadastro; validado no `PosService`), `vendas.cancelar` (cancelar/estornar venda) |
| Clientes | `clientes.criar`, `clientes.editar` |
| Funcionários | `funcionarios.criar`, `funcionarios.editar`, `funcionarios.excluir` |
| Contas a pagar | `contas-pagar.criar`, `contas-pagar.baixar` |
| Contas a receber | `contas-receber.criar`, `contas-receber.liquidar` |
| Cartões | `cartoes.antecipar` |
| Configurações | `taxas.criar`, `taxas.editar` (inclui ativar e reordenar), `alertas.editar` |

São 27 permissões: 26 protegem rotas de escrita (28 rotas) e `vendas.alterar-preco` é conferida dentro do PDV. Rotas de escrita sem permissão de catálogo (exceções): login/cadastro/logout/renovação,
troca e transferência entre empresas (só o dono, validado no serviço) e upload de arquivos (qualquer usuário logado).

- `GET /api/v1/permissoes` devolve o catálogo (rótulo, descrição e grupo), usado pelo cadastro de funcionários.
- `AuthUserDto.Permissions` (login, `/auth/me`, renovação) traz as permissões do usuário para o front esconder botões.
- Nova permissão: adicione a constante e a entrada em `Permissions.All` (`GCStock.Application/Access`) e proteja a rota com
  `.RequireAuthorization(Permissions.Xxx)`. A policy é registrada sozinha e o front exibe a opção. O teste
  `WriteRoutesPermissionTests` falha se uma rota de escrita nova ficar sem permissão.
- Migration `AddEmployeePermissions`: funcionários existentes passam a ter lista vazia (`'{}'`); o dono precisa liberar
  o que cada um deve fazer.

## Estoque, lotes e unidades

- **Fonte da verdade:** `Product.Stock`. Vendas, compras (inclusive edição) e ajuste manual do estoque mexem nele.
- **Lotes** (`StockBatch`, tela de Estoque) nunca somam mais que o estoque do produto: o excesso é consumido primeiro dos lotes
  que vencem antes (FEFO), em `StockLots.ReconcileAsync`. Lotes zerados somem do Estoque. O estoque do produto que não está em
  nenhum lote (digitado manualmente no cadastro, sem compra) aparece no Estoque como uma linha "Sem lote", então todo produto com
  estoque aparece e a soma bate com a tela de Produtos. Excluir o produto remove os lotes dele.
- **Unidade:** lotes e movimentações guardam a unidade do cadastro do produto (`kg`, `pct`, `un`, `cx`) no campo `Unit`.
  Os campos `AvailableKg`/`AmountKg` têm nome histórico: a quantidade está na unidade de `Unit`, não necessariamente em kg.
- **PDV:** cada item vendido gera uma movimentação própria, com a quantidade e a unidade reais.

## Vendas: histórico, cancelamento e bloqueio de estoque

- **Histórico** (`GET /api/v1/vendas`, tela *Histórico de Vendas*): cada venda do PDV com cupom, forma de pagamento, desconto, taxa,
  líquido e os itens (quantidade, unidade e preço efetivamente cobrado, em `SaleItems`). Vendas anteriores ao histórico foram
  reconstruídas das baixas de estoque (`PriceKnown = false` quando o preço por item não pôde ser deduzido).
- **Cancelar venda** (`POST /api/v1/vendas/{saleId}/cancelar`, permissão `vendas.cancelar`): devolve o estoque dos itens **ao mesmo lote de onde saíram** (a venda registra em `SaleItemLots` quanto tirou de cada lote, FEFO; kit devolve os
  componentes), remove o título a receber (cartão ou a prazo), desconta do total gasto do cliente e marca a venda como `cancelada`
  (ela sai de `/financeiro/vendas` e dos relatórios, mas continua no histórico). Não cancela se o título a receber já foi liquidado nem
  uma venda já cancelada. Se a venda for antiga (sem registro de lote) ou o lote tiver sido refeito por edição de compra, a quantidade volta para o estoque do produto e aparece como "Sem lote".
- **Bloqueio de estoque:** `PosService` soma o que cada produto precisa (avulso + componentes dos kits) e recusa a venda (409 "Estoque
  insuficiente: ...") se passar do saldo. A venda roda em transação com os produtos travados (`FOR UPDATE`), então vendas simultâneas
  do mesmo estoque não o deixam negativo.

## Custo médio e resultado financeiro

- **Custo médio móvel do estoque** (`Product.CostPrice`): cada compra se mistura ao estoque que já existe na hora,
  novo custo = (estoque × custo atual + quantidade × custo da compra) ÷ (estoque + quantidade). **A venda não altera o custo.**
  Editar uma compra tira a contribuição antiga da média e coloca a nova (`PurchaseService.AddToAverageCost/RemoveFromAverageCost`).
  Estoque vazio assume o custo da compra. Um custo digitado à mão no cadastro vale até a próxima entrada do produto.
- **Custo na venda:** cada item vendido guarda o custo médio do momento (`SaleItem.UnitCost/TotalCost`; kit = soma dos componentes).
  Vendas anteriores ao controle foram preenchidas pelo custo médio atual e marcadas `CostEstimated` (aparecem com * na tela).
  Lucro da venda = valor líquido recebido − custo das mercadorias (`SaleHistoryDto.Profit`).
- **Resultado (DRE)** (`GET /api/v1/financeiro/resultado?de=&ate=`, aba *Financeiro > Resultado (DRE)*): gasto em entradas (compras, pagas e a pagar,
  por fornecedor), vendas (bruta, taxas, líquida; canceladas não contam), custo das mercadorias vendidas, lucro bruto e margem,
  despesas operacionais (contas a pagar da categoria "outros"), folha (salários ativos × meses do período), resultado do período,
  investimentos/equipamentos (mostrados à parte, fora do resultado), valor do estoque ao custo médio e lucro por produto.
  O dia da venda é o de Brasília (UTC-3). Entradas só viram custo quando a mercadoria é vendida.

## Armazenamento de arquivos (Cloudflare R2)

Imagens, vídeos e documentos vão para o Cloudflare R2 (um bucket por ambiente: `gcstock-nprd` e `gcstock-prd`); o banco
guarda apenas a URL (ex.: `ImageUrl` de produtos e kits). Endpoints em `/api/v1/arquivos` (upload multipart, URL
pré-assinada para upload direto e remoção). Configuração, credenciais e CORS do bucket: `deploy/VPS.md`, seção 6.

## Monitoramento

- Liveness/Readiness via `/health/live` e `/health/ready` (consumíveis por probes de orquestradores e Traefik).
- Logs estruturados via `ILogger` na API e no Worker (probe SQL periódico loga sucesso/falha).
