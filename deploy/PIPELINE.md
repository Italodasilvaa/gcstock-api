# Esteira de publicação (API e UI)

Duas esteiras no GitHub Actions, uma em cada repositório, com o mesmo fluxo:

| Repositório | Arquivo | Publica | Homologação (nprd) | Produção (prd) |
|---|---|---|---|---|
| `gcstock-api` | `.github/workflows/esteira.yml` | API + Worker (imagens `gcstock-api` e `gcstock-worker`) | `api-nprd.iwsolutions.cloud` | `api.iwsolutions.cloud` |
| `gcstock-ui` | `.github/workflows/esteira.yml` | Front-end (imagem `gcstock-ui`, nginx) | `app-nprd.iwsolutions.cloud` | `app.iwsolutions.cloud` |

## Fluxo

```
feature ──PR──▶ main        valida (não publica)
main ──merge──▶ stg     valida, gera imagem e publica em HOMOLOGAÇÃO
stg ──merge──▶ prod        valida, gera imagem e publica em PRODUÇÃO (com aprovação manual)
```

- **Pull Request** (para `main`, `stg` ou `prod`): só valida (API: build e testes; UI: checagem de tipos e build). Não gera imagem nem publica.
- **Push em `stg` ou `prod`**: valida, gera a imagem com a tag do build (`<BuildId>` e `latest-<ambiente>`), publica no Swarm e confere a saúde.
- A UI de cada ambiente é construída apontando para a API do mesmo ambiente (`angular.json`: configuração `staging` ou `production`), então homologação e produção nunca se misturam.
- Publique primeiro a **API** e depois a **UI** quando a mudança mexer nos dois (a UI nova pode depender de rotas novas da API).

## O que cada esteira faz

**API** (`gcstock-api`)
1. `dotnet build`.
2. Testes sem banco: **bloqueiam** a publicação se falharem.
3. Testes com PostgreSQL (Testcontainers): rodam e aparecem no resultado, mas **não bloqueiam** por enquanto (foram escritos e nunca rodaram; quando estiverem validados, remova o `continueOnError` do passo).
4. Build e push das imagens da API e do Worker.
5. Gera o `stack.yml` do ambiente e copia para o servidor.
6. **Backup do banco** antes de publicar (veja abaixo). Se falhar, a publicação para e nada muda.
7. `docker stack deploy`. A API aplica as migrations pendentes ao subir (`Database__MigrateOnStartup=true`).
8. Verifica `https://<host>/health/ready` por até 3 minutos. Se não responder, a esteira falha e o Swarm volta sozinho para a versão anterior (`failure_action: rollback`).

**UI** (`gcstock-ui`)
1. `npm ci`, checagem de tipos (`npm run lint`) e `ng build` do ambiente.
2. Build e push da imagem (nginx servindo o Angular, com cache longo nos arquivos com hash e `index.html` sem cache).
3. Gera o `stack.yml`, copia para o servidor e faz `docker stack deploy`.
4. Verifica `https://<host>/health` por até 2 minutos.

## Backup e dados do banco

O passo de backup roda no servidor, via SSH, antes de cada publicação:

- `pg_dump -Fc` do banco do ambiente (`gcstock_prd` ou `gcstock_nprd`), dentro do contêiner do Postgres.
- Arquivo em `/var/backups/gcstock/<banco>_<data>_build<id>.dump`. Mantém os 14 mais recentes de cada banco (`BackupKeep` / `BackupPruneFrom` no `.github/workflows/esteira.yml`).
- Se o contêiner do Postgres não for achado (filtro `^postgres_postgres[.]`, o servidor do banco; o serviço `postgres_postgres-backup` é outro e não serve) ou o backup sair vazio, a publicação **é cancelada**. Ajuste `POSTGRES_CONTAINER_FILTER` e `POSTGRES_USER` no topo do `.github/workflows/esteira.yml` se o nome do serviço for diferente.
- As migrations do sistema só **adicionam** colunas, tabelas e índices e nunca apagam dados. Mesmo assim o backup existe como ponto de volta.

Restaurar um backup (no servidor):

```bash
PG=$(docker ps --filter "name=^postgres_postgres[.]" --format '{{.Names}}' | head -n 1)
docker exec -i "$PG" pg_restore --clean --if-exists -U admin -d gcstock_prd < /var/backups/gcstock/<arquivo>.dump
```

## Voltar uma versão (rollback)

- **Automático:** se a nova versão não ficar saudável, o Swarm volta sozinho (`update_config.failure_action: rollback`).
- **Manual, só a aplicação:** `docker service rollback gcstock-prd_api` (ou `gcstock-ui-prd_ui`). O banco continua como está: as migrations são aditivas, então a versão anterior da API segue funcionando com o banco novo.
- **Reverter para um build antigo:** rode de novo a esteira do commit desejado (ou re-implante a tag `<BuildId>` antiga).
- **Banco:** só restaure o backup se for realmente necessário, porque isso desfaz também o que foi gravado depois dele.

## Configuração única (fazer uma vez)

1. **Branches:** `stg` e `prod` já existem nos dois repositórios (a partir de `main`).
2. **Chave SSH de deploy:** na VPS, crie uma chave só para a esteira e autorize-a:
   `ssh-keygen -t ed25519 -f ~/gh_deploy -N ""` ; `cat ~/gh_deploy.pub >> ~/.ssh/authorized_keys` ; copie o conteúdo de `~/gh_deploy` (a privada) para o segredo `VPS_SSH_KEY` e depois apague o arquivo da VPS.
3. **Segredos** (GitHub > repositório > Settings > Secrets and variables > Actions), nos DOIS repositórios:
   `VPS_HOST` (`2.25.255.102`), `VPS_USER` (o usuário SSH, ex.: root), `VPS_SSH_KEY` (chave privada acima), `REGISTRY_USER` e `REGISTRY_PASSWORD` (login do `registry.iwsolutions.cloud`).
4. **Environments** `staging` e `production` (Settings > Environments) nos dois repositórios. Em `production`, marque **Required reviewers** e adicione você: produção só publica depois do seu OK. (Em repositório privado no plano gratuito essa opção pode não existir; nesse caso a proteção é só o merge manual `stg` -> `prod`.)
5. **DNS** (Cloudflare, CNAME para `proxy.iwsolutions.cloud`, com proxy): `app`, `app-nprd`, `api`, `api-nprd`. O Traefik emite o certificado (`myresolver`); no Cloudflare use SSL/TLS "Total".
6. **Pasta de backup:** a esteira cria `/var/backups/gcstock`. Mude `BACKUP_DIR` no workflow se quiser outro disco.
7. **CORS:** a API já aceita `*.iwsolutions.cloud`.

## Publicar à mão (sem esteira)

API: veja `deploy/VPS.md`, seção 5. UI:

```bash
docker build -f deploy/ui.Dockerfile --build-arg BUILD_CONFIGURATION=production \
  -t registry.iwsolutions.cloud/gcstock-ui:manual .
docker push registry.iwsolutions.cloud/gcstock-ui:manual

sed -e 's|__REGISTRY_URL__|registry.iwsolutions.cloud|g' -e 's|__UI_IMAGE_NAME__|gcstock-ui|g' \
    -e 's|__DOCKER_TAG__|manual|g' -e 's|__SERVICE_NAME__|gcstock-ui-prd|g' \
    -e 's|__HOST_DOMAIN__|app.iwsolutions.cloud|g' deploy/stacks/stack.yml > stack.ui.prd.yml
docker stack deploy --with-registry-auth -c stack.ui.prd.yml gcstock-ui-prd
```

(O `stack.yml` e o `ui.Dockerfile` da UI ficam no repositório `gcstock-ui`, em `deploy/`.)
