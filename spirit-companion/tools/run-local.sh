#!/usr/bin/env bash
# Runs Little Souls locally, from nothing: Singular's services in Docker (Postgres with pgvector, Keycloak, S3, imgproxy,
# Mailpit), the Keycloak realm, the control-plane schema, the "souls" tenant with the Little Souls app provisioned from
# provision/little-souls.app.json, then the Little Souls host (Singular's API + the module) on http://localhost:5070.
# The web app is http://localhost:5070/souls/
#
#   tools/get-creatura.sh && tools/get-singular.sh      # once
#   DEEPSEEK_API_KEY=sk-... tools/run-local.sh           # or no key when a network proxy adds it
#
# Works on macOS (Docker Desktop or Colima, the .NET 8 SDK or later, python3) and Linux. Ports used: 5432 (Postgres), 8082
# (Keycloak), 9000 (S3), 8088 (imgproxy), 1025/8025 (Mailpit), 5099 (console), 5070 (the app): stop anything else there,
# or move a port with KEYCLOAK_PORT, S3_PORT, IMGPROXY_PORT, AZURITE_PORT (10000; imgproxy brings Azurite along),
# MAILPIT_SMTP_PORT, MAILPIT_UI_PORT or CONSOLE_PORT (Postgres stays on 5432: the apps' connection string). When another
# checkout of Singular's stack already runs as the compose project "deploy" (its default), set COMPOSE_PROJECT_NAME
# (e.g. littlesouls) so this one gets its own containers and volumes.
#
# Idempotent: re-running converges. Singular is not modified. Two of its own steps are done by hand here because at the
# pinned commit scripts/bring-up.sh still asks for a "minio" service (now "s3") and the demo seeder fails on a demo
# template; neither is needed for Little Souls.
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT=$PWD
SF=$ROOT/external/singular-framework
RUN=${LS_RUN_DIR:-/tmp/ls}
mkdir -p "$RUN"
# Ports (header). The compose variables are exported for docker compose; the apps below are told the same addresses.
export KEYCLOAK_PORT=${KEYCLOAK_PORT:-8082} S3_PORT=${S3_PORT:-9000} IMGPROXY_PORT=${IMGPROXY_PORT:-8088} \
  AZURITE_PORT=${AZURITE_PORT:-10000} MAILPIT_SMTP_PORT=${MAILPIT_SMTP_PORT:-1025} MAILPIT_UI_PORT=${MAILPIT_UI_PORT:-8025}
CONSOLE_PORT=${CONSOLE_PORT:-5099}
KC=http://localhost:$KEYCLOAK_PORT
CONSOLE=http://localhost:$CONSOLE_PORT
# .NET: whatever is installed, or the dotnet-install location
if [ -x "$HOME/.dotnet/dotnet" ]; then export PATH=$HOME/.dotnet:$PATH DOTNET_ROOT=${DOTNET_ROOT:-$HOME/.dotnet}; fi
command -v dotnet >/dev/null || { echo "Install the .NET 8 SDK (or later): https://dotnet.microsoft.com/download"; exit 1; }
# Only newer runtimes installed (a Mac with the .NET 9/10 SDK has no 8.0 runtime)? Run the net8.0 apps on the nearest one.
dotnet --list-runtimes | grep -q 'Microsoft.AspNetCore.App 8\.' || export DOTNET_ROLL_FORWARD=${DOTNET_ROLL_FORWARD:-Major}
say() { printf '\n\033[1m%s\033[0m\n' "$*"; }
# Start a server in the background, detached from this script (setsid on Linux, nohup elsewhere).
detach() { local log=$1; shift; if command -v setsid >/dev/null; then setsid -f "$@" >"$log" 2>&1 </dev/null; else nohup "$@" >"$log" 2>&1 </dev/null & disown; fi; }

[ -f "$SF/src/SingularFramework.Api/Program.cs" ] || { echo "Run tools/get-singular.sh first."; exit 1; }
[ -f "$ROOT/external/creatura/src/Creatura/Core/ReferenceLoop.cs" ] || { echo "Run tools/get-creatura.sh first."; exit 1; }
if ! docker info >/dev/null 2>&1; then
  if [ "$(uname)" = "Darwin" ] && command -v colima >/dev/null; then
    echo "Starting Colima…"; colima start >/dev/null
  elif [ "$(uname)" = "Darwin" ]; then
    echo "Starting Docker Desktop…"; open -a Docker || { echo "Install Docker Desktop: https://www.docker.com/products/docker-desktop"; exit 1; }
  else
    # a pid file left by a previous container points at whatever process has that number now
    if [ -f /var/run/docker.pid ] && ! grep -q dockerd "/proc/$(cat /var/run/docker.pid)/cmdline" 2>/dev/null; then rm -f /var/run/docker.pid; fi
    detach "$RUN/dockerd.log" dockerd
  fi
  for _ in $(seq 1 60); do docker info >/dev/null 2>&1 && break; sleep 2; done
  docker info >/dev/null 2>&1 || { echo "Docker is not running."; exit 1; }
fi

say "Services (Docker)"
(cd "$SF" && docker compose -f deploy/docker-compose.yml up -d postgres keycloak s3 imgproxy mailpit >/dev/null)
for _ in $(seq 1 90); do curl -sf -o /dev/null "$KC/realms/master" && break; sleep 2; done
echo "  postgres :5432, keycloak :$KEYCLOAK_PORT"

say "Keycloak realm and control-plane schema"
(cd "$SF" && KEYCLOAK_URL=$KC ./deploy/keycloak/seed-realm.sh >/dev/null)
(cd "$SF/src/SingularFramework.Console" && ASPNETCORE_ENVIRONMENT=Development dotnet run --updateDatabase >/dev/null 2>&1)
echo "  done"

say "Console with provisioning ($CONSOLE)"
[ -f "$RUN/provkey" ] || echo "ls-local-$(openssl rand -hex 16)" > "$RUN/provkey"
if ! curl -s -o /dev/null "$CONSOLE/" 2>/dev/null; then
  (cd "$SF/src/SingularFramework.Console" && export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=$CONSOLE \
    Provisioning__ApiKey="$(cat "$RUN/provkey")" Keycloak__AdminUrl=$KC Keycloak__AdminClientSecret=devonly-backend-secret \
    && detach "$RUN/console.log" dotnet run)
  for _ in $(seq 1 90); do curl -s -o /dev/null "$CONSOLE/" && break; sleep 2; done
fi

say "Tenant 'souls' with the Little Souls app"
python3 - "$ROOT" > "$RUN/tenant.json" <<'PY'
import json, sys
app = json.load(open(sys.argv[1] + "/dotnet/src/SingularFramework.Apps.LittleSouls/provision/little-souls.app.json"))
app["sharedClient"] = False
app["signIn"] = {"directGrant": True, "redirectUris": ["http://localhost:5070/*"], "webOrigins": ["http://localhost:5070"]}
print(json.dumps({"slug": "souls", "displayName": "Little Souls (local)",
                  "database": {"connectionName": "souls-main", "database": "souls_main"},
                  "apps": [app], "administrators": ["owner@littlesouls.test"]}))
PY
curl -s -X POST -H "X-Provisioning-Key: $(cat "$RUN/provkey")" -H "Content-Type: application/json" --data @"$RUN/tenant.json" \
  "$CONSOLE/api/provisioning/tenants" | python3 -c "import json,sys; d=json.load(sys.stdin); c=[s for s in d.get('steps', []) if 'FAILED' in s or 'created' in s]; print('  ' + '\n  '.join(c) if c else '  up to date'); [print('  ! ' + w) for w in d.get('warnings', []) if 'storage' not in w and 'subject' not in w]"

say "Little Souls host (http://localhost:5070)"
pkill -f "net8.0/LittleSouls[.]Host" 2>/dev/null || true
# Singular drains for Shutdown:DrainSeconds (8 s) before it lets the port go: wait until nothing answers.
for _ in $(seq 1 40); do curl -s -o /dev/null http://localhost:5070/health || break; sleep 1; done
(cd "$ROOT/dotnet/src/LittleSouls.Host" && dotnet build -v q >/dev/null)
# A host still shutting down can hold the port for up to Shutdown:TimeoutSeconds: retry the start on a bind failure.
for attempt in 1 2 3 4 5; do
  (cd "$ROOT/dotnet/src/LittleSouls.Host" && export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5070 \
    Assistant__ApiKey="${DEEPSEEK_API_KEY:-proxy-injected}" Keycloak__Issuer=$KC/realms/xafplatform Keycloak__AdminUrl=$KC && detach "$RUN/host.log" ./bin/Debug/net8.0/LittleSouls.Host)
  for _ in $(seq 1 90); do
    curl -s -o /dev/null http://localhost:5070/health && break
    grep -q "address already in use" "$RUN/host.log" 2>/dev/null && break
    sleep 2
  done
  grep -q "address already in use" "$RUN/host.log" 2>/dev/null || break
  echo "  port 5070 still held (attempt $attempt); retrying"; sleep 8
done
curl -s http://localhost:5070/health; echo
echo "  open http://localhost:5070/souls/   (logs: $RUN/host.log)"
