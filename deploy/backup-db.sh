#!/usr/bin/env bash
# Backup completo do banco ANTES de publicar (a API aplica as migrations ao subir).
# Roda no servidor, via SSH, chamado pela esteira: bash -s -- <banco> <usuario> <build> <filtro> <pasta> <manter> < backup-db.sh
# Se falhar, a publicação para e nada é alterado. Restaurar:
#   pg_restore --clean --if-exists -U <usuario> -d <banco> <arquivo>.dump
set -e
DB="$1"; PGUSER="$2"; BUILD="$3"; FILTER="${4:-^postgres}"; DIR="${5:-/var/backups/gcstock}"; KEEP="${6:-14}"

PG=$(docker ps --filter "name=$FILTER" --format '{{.Names}}' | head -n 1)
if [ -z "$PG" ]; then echo "Conteiner do Postgres nao encontrado (filtro $FILTER)."; exit 1; fi

mkdir -p "$DIR"
FILE="$DIR/${DB}_$(date +%Y%m%d_%H%M%S)_build${BUILD}.dump"
docker exec "$PG" pg_dump -U "$PGUSER" -Fc "$DB" > "$FILE"
test -s "$FILE" || { echo "Backup vazio, publicacao cancelada."; rm -f "$FILE"; exit 1; }
echo "Backup gerado: $FILE"

# Mantém só os mais recentes deste banco.
ls -1t "$DIR/${DB}"_*.dump | tail -n +$((KEEP + 1)) | xargs -r rm -f
