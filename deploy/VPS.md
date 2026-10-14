# Deploy do GC Stock na VPS (Srv2016965 / 2.25.255.102)

Stack: Swarm + Traefik (`myresolver`) + Postgres da própria VPS. API em `https://api.iwsolutions.cloud`.
Execute **uma vez** no servidor (`ssh root@2.25.255.102`) antes do primeiro deploy.

## 1. Banco e secrets

```bash
# Banco do sistema (já criado e populado em produção). Para o ambiente de homologação:
# docker exec -it $(docker ps -q -f name=postgres_postgres.1) createdb -U admin gcstock_hml

# Connection string interna (host "postgres", rede overlay backend). Troque <SENHA_POSTGRES>.
printf 'Host=postgres;Port=5432;Database=gcstock;Username=admin;Password=<SENHA_POSTGRES>;Application Name=GCStock' \
  | docker secret create gcstock-prd-db-connection -

# Chave JWT (>= 32 caracteres, NÃO reaproveitar a de desenvolvimento)
openssl rand -base64 48 | tr -d '\n' | docker secret create gcstock-prd-jwt-secret -
```

Homologação (opcional): mesmos comandos com `gcstock-stg-db-connection` / `gcstock-stg-jwt-secret` e `Database=gcstock_hml`.

## 2. DNS (Cloudflare)

| Host | Aponta para | Proxy |
|------|-------------|-------|
| `api.iwsolutions.cloud` | 2.25.255.102 | Proxied (laranja) |
| `api-hml.iwsolutions.cloud` (opcional) | 2.25.255.102 | Proxied (laranja) |

## 3. Registry

O pipeline publica as imagens em `registry.iwsolutions.cloud`. No servidor, faça login para o Swarm puxar as imagens:

```bash
docker login registry.iwsolutions.cloud -u admin
```

## 4. Azure DevOps (service connections)

- **Docker Registry** chamada `registry.iwsolutions.cloud` (usuário `admin` do registry).
- **SSH** chamadas `swarm-production` (e `swarm-staging`) apontando para `root@2.25.255.102`.
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
