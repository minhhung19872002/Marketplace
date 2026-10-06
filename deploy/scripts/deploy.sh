#!/bin/sh
# Production deploy (spec 7): build images tagged with the git revision, start them, keep the running tag and the
# previous one, prune the rest. Run from the repository root:  deploy/scripts/deploy.sh
set -eu
compose="docker compose -f docker-compose.yml -f docker-compose.prod.yml"
tag=$(git rev-parse --short HEAD)
export SH_IMAGE_TAG="$tag"

[ -f deploy/certs/fullchain.pem ] && [ -f deploy/certs/privkey.pem ] || {
  echo "Thiếu chứng chỉ HTTPS trong deploy/certs (fullchain.pem, privkey.pem) — xem docs/04 mục HTTPS."; exit 1; }
mkdir -p backups/db backups/minio

echo "Build $tag…"
$compose build
for app in api web seller admin; do docker tag "shophub/$app:$tag" "shophub/$app:latest"; done

echo "Khởi động…"
$compose up -d --remove-orphans

# Wait for the API to be ready before declaring success
i=0
until $compose exec -T api curl -fsS http://localhost:8080/health/ready > /dev/null 2>&1; do
  i=$((i + 1)); [ $i -gt 60 ] && { echo "API không sẵn sàng sau 5 phút — xem: $compose logs api"; exit 1; }
  sleep 5
done

# Keep the image in use and the one before it (rollback: SH_IMAGE_TAG=<cũ> $compose up -d); drop older tags
for app in api web seller admin; do
  docker images "shophub/$app" --format '{{.CreatedAt}}\t{{.Tag}}' | sort -r | cut -f2 | grep -v '^latest$' | tail -n +3 |
    while read -r old; do docker rmi "shophub/$app:$old" > /dev/null 2>&1 || true; done
done
docker image prune -f > /dev/null
echo "Đã triển khai $tag."
