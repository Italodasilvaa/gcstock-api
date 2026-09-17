# Prompt Base Para Gerar o Projeto Girus API

Receita pronta (sem placeholders) para gerar a base .NET da API do Girus.
Cole a secao 5 (Versao Ultra Compacta) para qualquer IA (Claude, Gemini, ChatGPT, Copilot) gerar a solucao.

## 1) Entradas do Projeto

- NOME_PASCAL: Girus
- NOME_KEBAB: girus
- NOME_SLUG: girus
- DOMINIO_PRD: api-girus.com.br
- DOMINIO_STG: api-girus-hml.com.br

## 2) Prompt Curto (copiar e colar)

Crie uma solucao .NET 10 (slnx) em DDD/Clean com nome Girus.
Estrutura:
- src/Girus.Api
- src/Girus.Worker
- src/Girus.Application
- src/Girus.Domain
- src/Girus.Infra
- tests/Girus.UnitTests
- tests/Girus.IntegrationTests

Regras:
- TargetFramework net10.0 em todos os projetos.
- Referencias:
  - Application -> Domain
  - Infra -> Application, Domain
  - Api -> Application, Infra
  - Worker -> Application, Infra
  - UnitTests -> Application, Domain
  - IntegrationTests -> Api, Infra
- API com:
  - CORS: localhost, valee.com.br e subdominios
  - Swagger (Swashbuckle.AspNetCore 7.3.1)
  - Health checks:
    - /health/live (self)
    - /health/ready (SQL Server SELECT 1)
    - /health (alias de ready)
  - resposta JSON estruturada de health
- Worker com hosted service que faz probe SQL periodico (SELECT 1) e loga sucesso/falha.
- AppSettings em API e Worker:
  - appsettings.json
  - appsettings.development.json
  - appsettings.staging.json
  - appsettings.production.json
- ConnectionStrings:DefaultConnection padrao:
  Server=db-nprd.valee.com.br;Database=dbValee-Refactor;User Id=sa;Password=SqlAdm2010;TrustServerCertificate=True;
- Pasta deploy:
  - deploy/api.Dockerfile
  - deploy/worker.Dockerfile
  - deploy/stacks/stack.yml
- Pipeline Azure em azure-pipelines.yml:
  - trigger: production e staging
  - RegistryUrl: cr.valee.com.br
  - ApiImageName: girus-api
  - WorkerImageName: girus-worker
  - production:
    - StackName: girus-prd
    - HostDomain: api-girus.valee.com.br
    - appsettings production
  - staging:
    - StackName: girus-stg
    - HostDomain: api-girus-hml.valee.com.br
    - appsettings staging
  - build/push de API e Worker
  - stack deploy com placeholders em deploy/stacks/stack.yml
- Atualize README com estrutura, monitoramento e deploy.
- Inclua .dockerignore e .gitignore adequados.
- Rode build final da solucao e garanta sucesso.

## 3) Placeholders Obrigatorios

No deploy/stacks/stack.yml, usar:
- __REGISTRY_URL__
- __API_IMAGE_NAME__
- __WORKER_IMAGE_NAME__
- __DOCKER_TAG__
- __ENVIRONMENT__
- __SERVICE_NAME__
- __HOST_DOMAIN__

## 4) Checklist Final

- Existe Girus.slnx
- Build da solucao passa
- Swagger abre
- /health/live e /health/ready respondem JSON
- Worker inicia e executa probe SQL
- Pipeline usa somente production e staging

## 5) Versao Ultra Compacta (copiar e colar direto)

Crie base .NET 10 (slnx) DDD/Clean chamada Girus com:
src/{Girus.Api,Worker,Application,Domain,Infra} e tests/{Girus.UnitTests,IntegrationTests}.
Refs: Application->Domain; Infra->Application+Domain; Api->Application+Infra; Worker->Application+Infra; Unit->Application+Domain; Integration->Api+Infra.
API: CORS (localhost,valee.com.br,*.valee.com.br), Swagger Swashbuckle 7.3.1, health live/ready + /health alias.
Ready deve validar SQL Server com SELECT 1 e retornar JSON estruturado (status,duration,timestamp,checks).
Worker: hosted service com probe SQL periodico (SELECT 1), intervalo por Worker:DatabaseProbeIntervalSeconds, logs de sucesso/falha.
Appsettings em API e Worker: appsettings.json, development, staging, production.
ConnectionStrings:DefaultConnection padrao = Server=db-nprd.valee.com.br;Database=dbValee-Refactor;User Id=sa;Password=SqlAdm2010;TrustServerCertificate=True;
Criar deploy/api.Dockerfile, deploy/worker.Dockerfile, deploy/stacks/stack.yml.
Stack placeholders: __REGISTRY_URL__, __API_IMAGE_NAME__, __WORKER_IMAGE_NAME__, __DOCKER_TAG__, __ENVIRONMENT__, __SERVICE_NAME__, __HOST_DOMAIN__.
Pipeline azure-pipelines.yml:
trigger somente production e staging; RegistryUrl=cr.valee.com.br; ApiImageName=girus-api; WorkerImageName=girus-worker.
Branch production: StackName=girus-prd, HostDomain=api-girus.valee.com.br, usa appsettings.production.json.
Branch staging: StackName=girus-stg, HostDomain=api-girus-hml.valee.com.br, usa appsettings.staging.json.
Build/push API+Worker e deploy swarm via stack deploy com substituicao de placeholders.
Atualizar README + .gitignore + .dockerignore e garantir dotnet build da solucao com sucesso.
