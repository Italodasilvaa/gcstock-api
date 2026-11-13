# Esteira de publicação (API e UI)

Duas esteiras no Azure Pipelines, uma em cada repositório, com o mesmo fluxo:

| Repositório | Arquivo | Publica | Homologação (nprd) | Produção (prd) |
|---|---|---|---|---|
| `gcstock-api` | `azure-pipelines.yml` | API + Worker (imagens `gcstock-api` e `gcstock-worker`) | `api-nprd.iwsolutions.cloud` | `api.iwsolutions.cloud` |
| `gcstock-ui` | `azure-pipelines.yml` | Front-end (imagem `gcstock-ui`, nginx) | `app-nprd.iwsolutions.cloud` | `app.iwsolutions.cloud` |

## Fluxo

```
feature ──PR──▶ main        valida (não publica)
main ──merge──▶ staging     valida, gera imagem e publica em HOMOLOGAÇÃO
staging ──merge──▶ production   valida, gera imagem e publica em PRODUÇÃO (com aprovação manual)
```

- **Pull Request** (para `main`, `staging` ou `production`): só valida (API: build e testes; UI: checagem de tipos e build). Não gera imagem nem publica.
- **Push em `staging` ou `production`**: valida, gera a imagem com a tag do build (`<BuildId>` e `latest-<ambiente>`), publica no Swarm e confere a saúde.
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
- Arquivo em `/var/backups/gcstock/<banco>_<data>_build<id>.dump`. Mantém os 14 mais recentes de cada banco (`BackupKeep` / `BackupPruneFrom` no `azure-pipelines.yml`).
- Se o contêiner do Postgres não for achado (filtro `^postgres`) ou o backup sair vazio, a publicação **é cancelada**. Ajuste `PostgresContainerFilter` e `PostgresUser` no topo do `azure-pipelines.yml` se o nome do serviço for diferente.
- As migrations do sistema só **adicionam** colunas, tabelas e índices e nunca apagam dados. Mesmo assim o backup existe como ponto de volta.

Restaurar um backup (no servidor):

```bash
PG=$(docker ps --filter "name=^postgres" --format '{{.Names}}' | head -n 1)
docker exec -i "$PG" pg_restore --clean --if-exists -U admin -d gcstock_prd < /var/backups/gcstock/<arquivo>.dump
```

## Voltar uma versão (rollback)

- **Automático:** se a nova versão não ficar saudável, o Swarm volta sozinho (`update_config.failure_action: rollback`).
- **Manual, só a aplicação:** `docker service rollback gcstock-prd_api` (ou `gcstock-ui-prd_ui`). O banco continua como está: as migrations são aditivas, então a versão anterior da API segue funcionando com o banco novo.
- **Reverter para um build antigo:** rode de novo a esteira do commit desejado (ou re-implante a tag `<BuildId>` antiga).
- **Banco:** só restaure o backup se for realmente necessário, porque isso desfaz também o que foi gravado depois dele.

## Configuração única (fazer uma vez)

1. **Branches:** crie `staging` e `production` a partir de `main` nos dois repositórios (`git branch staging main && git push -u origin staging`, idem `production`).
2. **Pipeline da UI:** no Azure DevOps, Pipelines > New pipeline > GitHub > `gcstock-ui` > "Existing Azure Pipelines YAML file" > `/azure-pipelines.yml`. (O da API já existe.)
3. **Service connections** (já usadas pela esteira da API, a UI reaproveita): `registry.iwsolutions.cloud` (Docker Registry) e `swarm-production` / `swarm-staging` (SSH para a VPS).
4. **Environments** `production` e `staging` (Pipelines > Environments). Em **`production`**, adicione **Approvals and checks > Approvals** com você como aprovador: assim, produção só publica depois do seu OK.
5. **DNS** (Cloudflare, proxied): `app.iwsolutions.cloud` e `app-nprd.iwsolutions.cloud` apontando para `2.25.255.102` (os de API já existem). O Traefik emite o certificado sozinho (`myresolver`).
6. **Pasta de backup:** nada a criar (a esteira cria `/var/backups/gcstock`). Se quiser, mova-a para um disco com mais espaço alterando `BackupDir`.
7. **CORS:** a API já aceita `*.iwsolutions.cloud`, então `app` e `app-nprd` funcionam sem mudança.

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
