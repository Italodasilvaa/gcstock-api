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

## 6. Armazenamento de arquivos (Cloudflare R2)

Imagens, vídeos e documentos enviados pelo sistema ficam no R2, em um bucket por ambiente. O banco guarda só a URL.

### 6.1 Na Cloudflare (Dashboard > R2 Object Storage)

1. Ative o R2 na conta (se ainda não estiver) e anote o **Account ID** (Overview, lado direito).
2. Crie dois buckets: `gcstock-nprd` e `gcstock-prd`.
3. **Leitura pública** (para as `<img>` e `<video>` do sistema): em cada bucket, Settings > **Custom Domains** > adicione
   `cdn-nprd.iwsolutions.cloud` (bucket nprd) e `cdn.iwsolutions.cloud` (bucket prd). A Cloudflare cria o DNS sozinha.
   (Evite o domínio `r2.dev`: é limitado e só serve para testes.)
4. **CORS** (necessário só para o upload direto do navegador, `/url-upload`): em cada bucket, Settings > CORS policy:

   ```json
   [
     {
       "AllowedOrigins": ["https://app.iwsolutions.cloud", "http://localhost:4200"],
       "AllowedMethods": ["PUT", "GET", "HEAD"],
       "AllowedHeaders": ["Content-Type"],
       "MaxAgeSeconds": 3600
     }
   ]
   ```

   Ajuste `AllowedOrigins` para o domínio real do front (no bucket prd, não deixe `localhost`).
5. **API token**: R2 > Manage API Tokens > Create. Permissão **Object Read & Write**, restrita ao bucket. Crie um token para
   cada ambiente. Guarde o **Access Key ID** e o **Secret Access Key** (o secret aparece uma única vez).

### 6.2 Configuração da API

`appsettings.json` (nprd) e `appsettings.Production.json` (prd) já trazem o Account ID, o nome do bucket e o domínio público. Para mudar:
`Storage:R2:AccountId` (só no `appsettings.json`) e o `Storage:R2:PublicBaseUrl` (`https://cdn-nprd.iwsolutions.cloud` / `https://cdn.iwsolutions.cloud`).
Sem isso a API sobe normalmente, mas os endpoints de upload respondem **503**.

Credenciais, desenvolvimento local (nprd):

```bash
dotnet user-secrets set "Storage:R2:AccessKeyId" "<ACCESS_KEY_ID_NPRD>" --project src/GCStock.Api
dotnet user-secrets set "Storage:R2:SecretAccessKey" "<SECRET_NPRD>" --project src/GCStock.Api
```

Credenciais, servidor (Swarm secrets lidos pela stack):

```bash
printf '<ACCESS_KEY_ID_PRD>' | docker secret create gcstock-prd-r2-access-key-id -
printf '<SECRET_PRD>'        | docker secret create gcstock-prd-r2-secret-access-key -
printf '<ACCESS_KEY_ID_NPRD>' | docker secret create gcstock-nprd-r2-access-key-id -
printf '<SECRET_NPRD>'        | docker secret create gcstock-nprd-r2-secret-access-key -
```

### 6.3 Endpoints (autenticados, JWT)

| Método | Rota | Uso |
|--------|------|-----|
| POST | `/api/v1/arquivos?pasta=produtos` | Upload multipart (campo `file`), até 100 MB. Retorna `{ key, url, contentType, size }`. Grave `url` em `ImageUrl`. |
| POST | `/api/v1/arquivos/url-upload` | Corpo `{ fileName, contentType, size, folder }`. Retorna uma URL pré-assinada: o navegador faz `PUT` direto no R2 (repetindo o mesmo `Content-Type`). Use para vídeos e arquivos grandes. |
| DELETE | `/api/v1/arquivos?key=<key>` | Remove um arquivo da própria empresa. |

Regras: imagens até 10 MB; documentos (pdf, csv, txt, xlsx, docx, zip) até 25 MB; vídeos (mp4, webm, mov) até 500 MB
(via `url-upload`; o upload pela API é limitado a 100 MB pelo proxy da Cloudflare). As chaves seguem
`{empresaId}/{pasta}/{guid}.{ext}`, o que isola os arquivos entre empresas.
