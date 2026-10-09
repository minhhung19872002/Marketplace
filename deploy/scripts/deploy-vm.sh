#!/bin/sh
# Deploy the current commit to the demo VM (behind the shared Caddy), from a developer machine at the repo root:
#   deploy/scripts/deploy-vm.sh            # update: keep the database, MinIO and Meili volumes — sessions stay signed in
#   deploy/scripts/deploy-vm.sh --reseed   # wipe every volume and seed the sample data again (new sample passwords)
# Why two modes (G4-C): every earlier deploy wiped the volumes, so every account, password and refresh token was new —
# that, not the code, is what signed everybody out after a deploy. Reseed only when the sample data itself changed.
# Server-side files that stay out of the repo and are never overwritten: .env, docker-compose.vm.yml,
# deploy/nginx/realip.conf, seed-accounts.txt, backups/.
set -eu
host=${SH_VM_HOST:-hung@14.225.83.93}
dir=${SH_VM_DIR:-apps/shophub}
public_url=${SH_VM_PUBLIC_URL:-https://shophub.bluestar.com.vn}
reseed=0
[ "${1:-}" = "--reseed" ] && reseed=1
rev=$(git rev-parse --short HEAD)
[ -z "$(git status --porcelain)" ] || echo "Lưu ý: có thay đổi chưa commit — chỉ bản commit $rev được triển khai."

git archive --format=tar HEAD | gzip | ssh "$host" "set -e
  new=\$(mktemp -d); tar xzf - -C \"\$new\"
  rsync -a --delete --exclude=.env --exclude=docker-compose.vm.yml --exclude=deploy/nginx/realip.conf --exclude=seed-accounts.txt \
    --exclude=DEPLOY-VM.md --exclude=REVISION --exclude='*.log' --exclude=backups/ \"\$new\"/ ~/$dir/
  rm -rf \"\$new\"; cd ~/$dir; echo $rev > REVISION
  c='docker compose -f docker-compose.yml -f docker-compose.vm.yml'
  COMPOSE_PARALLEL_LIMIT=1 \$c build api web admin seller 2>&1 | grep -E 'Built|ERROR' || true
  if [ $reseed = 1 ]; then \$c down -v 2>&1 | tail -1; fi
  \$c up -d --remove-orphans 2>&1 | tail -1
  # The gateway renders its templates only at start: recreate it so a changed deploy/nginx/*.conf applies
  \$c up -d --no-deps --force-recreate nginx 2>&1 | tail -1
  docker network connect shophub_default proxy-caddy 2>/dev/null || true
  i=0; until curl -fsS -m 5 http://127.0.0.1:18080/health/ready >/dev/null 2>&1; do
    i=\$((i+1)); [ \$i -gt 240 ] && { echo 'API không sẵn sàng sau 20 phút'; exit 1; }; sleep 5; done
  if [ $reseed = 1 ]; then
    \$c exec -T postgres sh -c 'psql -q -U \"\$POSTGRES_USER\" -d \"\$POSTGRES_DB\" -c \"update sys.system_parameters set value = '\''$public_url'\'', updated_at = now() where key = '\''SITE.PUBLIC_URL'\''\"'
    \$c restart api >/dev/null
    umask 077; docker logs shophub-api-1 2>&1 | grep '\\[SEED\\]' | sort -u > seed-accounts.txt
    echo 'Đã nạp lại dữ liệu mẫu — mật khẩu mới trong ~/$dir/seed-accounts.txt'
    i=0; until curl -fsS -m 5 http://127.0.0.1:18080/health/ready >/dev/null 2>&1; do i=\$((i+1)); [ \$i -gt 60 ] && break; sleep 3; done
  fi
  docker inspect -f 'api restarts={{.RestartCount}} oom={{.State.OOMKilled}}' shophub-api-1
  docker image prune -f >/dev/null
  echo \"Đã triển khai $rev\""
