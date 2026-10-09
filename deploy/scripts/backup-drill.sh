#!/bin/sh
# Backup / restore drill (G4-D), run on the server from the repository root:
#   SH_COMPOSE="docker compose -f docker-compose.yml -f docker-compose.vm.yml" deploy/scripts/backup-drill.sh
# 1. takes a fresh dump exactly like the sys.backup job (pg_dump -Fc) and checks it with pg_restore --list;
# 2. restores it into a scratch database (shophub_drill) next to the live one — the live data is never touched;
# 3. compares row counts and money totals live vs restored (they must be equal: the dump is a consistent snapshot);
# 4. mirrors every MinIO bucket into backups/minio-drill and compares object counts;
# 5. drops the scratch database. Exit code 0 only when everything matches.
set -eu
compose=${SH_COMPOSE:-docker compose -f docker-compose.yml -f docker-compose.prod.yml}
stamp=$(date +%Y%m%d-%H%M%S)
dump="backups/db/drill-$stamp.dump"
mkdir -p backups/db backups/minio-drill

psql_live() { $compose exec -T postgres sh -c "psql -tA -U \"\$POSTGRES_USER\" -d \"\$POSTGRES_DB\" -c \"$1\""; }
psql_drill() { $compose exec -T postgres sh -c "psql -tA -U \"\$POSTGRES_USER\" -d shophub_drill -c \"$1\""; }

echo "1. Sao lưu → $dump"
start=$(date +%s)
$compose exec -T postgres sh -c 'pg_dump -Fc -U "$POSTGRES_USER" -d "$POSTGRES_DB"' > "$dump"
$compose exec -T postgres sh -c 'pg_restore --list' < "$dump" > /dev/null
echo "   $(du -h "$dump" | cut -f1), $(( $(date +%s) - start )) s, pg_restore --list đọc được"

# Figures taken in the same breath as the dump (the demo has little traffic; a busy site compares to the dump itself)
query="select concat_ws(' | ',
  (select count(*) from iam.users), (select count(*) from catalog.products), (select count(*) from catalog.skus),
  (select count(*) from sales.orders), (select count(*) from sales.order_items), (select coalesce(sum(grand_total), 0) from sales.orders),
  (select count(*) from engage.reviews), (select count(*) from finance.ledger_entries),
  (select coalesce(sum(case when direction = 'Debit' then amount else -amount end), 0) from finance.ledger_entries),
  (select count(*) from promo.flash_sale_slots), (select count(*) from sys.system_parameters))"
live=$(psql_live "$query")

echo "2. Phục hồi vào CSDL tạm shophub_drill"
start=$(date +%s)
$compose exec -T postgres sh -c 'dropdb --if-exists -U "$POSTGRES_USER" shophub_drill && createdb -U "$POSTGRES_USER" shophub_drill'
$compose exec -T postgres sh -c 'pg_restore --no-owner -U "$POSTGRES_USER" -d shophub_drill' < "$dump"
echo "   $(( $(date +%s) - start )) s"
restored=$(psql_drill "$query")

echo "3. So sánh (người dùng | sản phẩm | SKU | đơn | dòng đơn | tổng tiền đơn | đánh giá | bút toán | nợ − có | khung Flash Sale | tham số)"
echo "   thật:      $live"
echo "   phục hồi:  $restored"
ok=1
[ "$live" = "$restored" ] || { echo "   LỆCH"; ok=0; }

echo "4. Tệp MinIO → backups/minio-drill, rồi phục hồi thử vào bucket tạm sh-drill"
# mc runs in the minio-init image (it carries the MC_HOST_sh alias); per bucket: objects live, files mirrored, objects
# after copying the mirror back into a scratch bucket — the three must be equal
# The mc image has no awk / find / sed: counts come from mc itself (it lists local folders too) and wc
files=$($compose run --rm -T --no-deps -v "$PWD/backups/minio-drill:/drill" --entrypoint sh minio-init -c '
  mc mirror --overwrite --quiet sh/ /drill/ > /dev/null
  mc mb --ignore-existing sh/sh-drill > /dev/null
  for b in sh-products sh-reviews sh-kyc sh-chat sh-banners sh-returns; do
    live=$(mc ls --recursive sh/$b 2>/dev/null | wc -l)
    mirrored=$(mc ls --recursive /drill/$b 2>/dev/null | wc -l)
    [ "$mirrored" -gt 0 ] && mc mirror --overwrite --quiet /drill/$b sh/sh-drill/$b > /dev/null 2>&1
    back=$(mc ls --recursive sh/sh-drill/$b 2>/dev/null | wc -l)
    echo "$b $live $mirrored $back"
  done
  mc rb --force sh/sh-drill > /dev/null' 2>/dev/null) || true
[ -n "$files" ] || { echo "   không đọc được MinIO"; ok=0; }
echo "$files" | while read -r b live mirrored back; do
  if [ -n "$b" ]; then printf "   %-12s trong MinIO %5s · đã sao %5s · phục hồi %5s
" "$b" "$live" "$mirrored" "$back"; fi
done
if echo "$files" | while read -r b live mirrored back; do
     [ -z "$b" ] || { [ "$live" = "$mirrored" ] && [ "$mirrored" = "$back" ]; } || exit 1
   done; then :; else echo "   LỆCH tệp"; ok=0; fi

echo "5. Dọn CSDL tạm"
$compose exec -T postgres sh -c 'dropdb -U "$POSTGRES_USER" shophub_drill'
[ $ok = 1 ] && echo "DIỄN TẬP ĐẠT: bản sao lưu phục hồi đủ, số liệu khớp." || { echo "DIỄN TẬP CHƯA ĐẠT"; exit 1; }
