#!/bin/sh
# Restore ShopHub from backups (spec 6.4). Run from the repository root on the server:
#   deploy/scripts/restore.sh backups/db/shophub-20261006-193000.dump [--files]
# 1. stops the API (no writes while restoring), 2. restores PostgreSQL from the dump (objects recreated),
# 3. with --files, copies ./backups/minio back into MinIO, 4. starts the API and rebuilds the search index.
set -eu
dump=${1:?Cách dùng: restore.sh <tệp .dump> [--files]}
[ -f "$dump" ] || { echo "Không thấy tệp $dump"; exit 1; }
compose="docker compose -f docker-compose.yml -f docker-compose.prod.yml"

printf 'Khôi phục CSDL từ %s — dữ liệu hiện tại sẽ bị THAY THẾ. Gõ "dong y" để tiếp tục: ' "$dump"
read -r answer
[ "$answer" = "dong y" ] || { echo "Đã huỷ."; exit 1; }

$compose stop api
$compose exec -T postgres sh -c 'pg_restore --clean --if-exists --no-owner -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$dump"

if [ "${2:-}" = "--files" ]; then
  $compose run --rm --entrypoint sh backup-files -c 'mc mirror --overwrite /backups/ sh/'
fi

$compose start api
echo "Đã khôi phục. Lập lại chỉ mục tìm kiếm: Quản trị → Tham số / Việc nền, hoặc POST /api/admin/search/reindex."
