#!/bin/sh
# Nightly PostgreSQL backup (run by the backup-db service of docker-compose.prod.yml, or by hand:
#   docker compose -f docker-compose.yml -f docker-compose.prod.yml exec backup-db sh /usr/local/bin/backup-db.sh)
# Custom-format dump (compressed, restorable table by table), checked with pg_restore --list, old ones pruned.
set -eu
stamp=$(date -u +%Y%m%d-%H%M%S)
file="/backups/shophub-$stamp.dump"
pg_dump --format=custom --compress=6 --no-owner --file="$file.partial"
# A dump that cannot be listed is not a backup
pg_restore --list "$file.partial" > /dev/null
mv "$file.partial" "$file"
find /backups -name 'shophub-*.dump' -mtime +"${SH_BACKUP_KEEP_DAYS:-14}" -delete
echo "$(date -u +%FT%TZ) backup ok: $file ($(du -h "$file" | cut -f1))"
