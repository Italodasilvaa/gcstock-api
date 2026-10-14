# Deploy do GC Stock na VPS (Srv2016965 / 2.25.255.102)

Stack: Swarm + Traefik (`myresolver`) + Postgres da própria VPS. API em `https://api.iwsolutions.cloud`.
Execute **uma vez** no servidor (`ssh root@2.25.255.102`) antes do primeiro deploy.

## 1. Bancos e secrets

Dois bancos na mesma instância Postgres (já criados, com as migrations aplicadas):

| Ambiente | Banco | Stack | Host da API | Dados |
|----------|-------|-------|-------------|-------|
| nprd (desenvolvimento/homologação) | `gcstock_nprd` | `gcstock-nprd` | `api-nprd.iwsolutions.cloud` | seed de demonstração (logins com senha `123`) |
| prd (produção) | `gcstock_prd` | `gcstock-prd` | `api.iwsolutions.cloud` | vazio (sem seed) |

O desenvolvimento local usa `gcstock_nprd` (user-secrets). O `gcstock_prd` só é acessado pela stack de produção.

No servidor, crie os secrets de cada stack (host interno `postgres`, rede overlay `backend`). Troque `<SENHA_POSTGRES>`:

```bash
# --- prd ---
printf 'Host=postgres;Port=5432;Database=gcstock_prd;Username=admin;Password=<SENHA_POSTGRES>;Application Name=GCStock' \
  | docker secret create gcstock-prd-db-connection -
openssl rand -base64 48 | tr -d '\n' | docker secret create gcstock-prd-jwt-secret -   # >= 32 caracteres, único por ambiente

# --- nprd ---
printf 'Host=postgres;Port=5432;Database=gcstock_nprd;Username=admin;Password=<SENHA_POSTGRES>;Application Name=GCStock' \
  | docker secret create gcstock-nprd-db-connection -
openssl rand -base64 48 | tr -d '\n' | docker secret create gcstock-nprd-jwt-secret -
```

Migrations novas: aplicadas sozinhas quando a API sobe (`Database__MigrateOnStartup=true`). Para aplicar manualmente em um banco:

```bash
dotnet ef database update --project src/GCStock.Infra --startup-project src/GCStock.Api \
  --connection "Host=db.iwsolutions.cloud;Port=5432;Database=gcstock_prd;Username=admin;Password=<SENHA>"
```

## 2. DNS (Cloudflare)

| Host | Aponta para | Proxy |
|------|-------------|-------|
| `api.iwsolutions.cloud` | 2.25.255.102 | Proxied (laranja) |
| `api-nprd.iwsolutions.cloud` | 2.25.255.102 | Proxied (laranja) |

## 3. Registry

O pipeline publica as imagens em `registry.iwsolutions.cloud`. No servidor, faça login para o Swarm puxar as imagens:

```bash
docker login registry.iwsolutions.cloud -u admin
```

## 4. Azure DevOps (service connections)

- **Docker Registry** chamada `registry.iwsolutions.cloud` (usuário `admin` do registry).
- **SSH** chamadas `swarm-production` (e `swarm-staging` (nprd)) apontando para `root@2.25.255.102`.
- Environments `production` / `staging`.

## 5. Deploy manual (sem pipeline)

```bash
docker build -f deploy/api.Dockerfile    -t registry.iwsolutions.cloud/gcstock-api:manual .
docker build -f deploy/worker.Dockerfile -t registry.iwsolutions.cloud/gcstock-worker:manual .
docker push registry.iwsolutions.cloud/gcstock-api:manual
docker push registry.iwsolutions.cloud/gcstock-worker:manual

sed -e 's|__REGISTRY_URL__|registry.iwsolutions.cloud|g' -e 's|__API_IMAGE_NAME__|gcstock-api|g' \
    -e 's|__WORKER_IMAGE_NAME__|gcstock-worker|g' -e 's|__DOCKER_TAG__|manual|g' \
    -e 's|__ENVIRONMENT__|Production|g' -e 's|__SERVICE_NAME__|gcstock-prd|g' \
    -e 's|__HOST_DOMAIN__|api.iwsolutions.cloud|g' deploy/stacks/stack.yml > stack.prd.yml
docker stack deploy --with-registry-auth -c stack.prd.yml gcstock-prd
```

Conferir: `docker service ls`, `curl https://api.iwsolutions.cloud/health`.
