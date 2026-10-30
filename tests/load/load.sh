#!/usr/bin/env bash
# Fills a running Compose stack with TOTAL events through the API, then times the queries the dashboard makes.
# Usage: docker compose up --build -d && tests/load/load.sh      (TOTAL=1000000 for a bigger run)
set -euo pipefail

API=${API:-http://localhost:8080}
TOTAL=${TOTAL:-500000}
EMAIL=${ADMIN_EMAIL:-admin@example.com}
PASSWORD=${ADMIN_PASSWORD:-admin-password-local}
JAR=$(mktemp)
psql() { docker compose exec -T db psql -U logs -d log_analytics -tAc "$1"; }

for _ in $(seq 1 60); do curl -sf "$API/health/ready" > /dev/null && break; sleep 2; done
curl -sf -c "$JAR" -X POST "$API/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" > /dev/null
NAME="Load test $(date +%s)"
KEY=$(curl -sf -b "$JAR" -X POST "$API/api/sources" -H 'Content-Type: application/json' -d "{\"name\":\"$NAME\"}" | jq -r .apiKey)

echo "== Sending $TOTAL events"
START=$(date +%s.%N)
docker compose run --rm --no-deps -T -e MODE=load -e LOGS_API_KEY="$KEY" -e TOTAL="$TOTAL" -e CONCURRENCY=4 -e BATCH=1000 generator | tee /tmp/load-send.json
for _ in $(seq 1 300); do
  STORED=$(psql "SELECT count(*) FROM log_events e JOIN sources s ON s.id = e.source_id WHERE s.name = '$NAME'")
  [ "$STORED" -ge "$TOTAL" ] && break
  sleep 1
done
END=$(date +%s.%N)
[ "$STORED" -eq "$TOTAL" ] || { echo "Only $STORED of $TOTAL events were stored"; exit 1; }
SECONDS_TOTAL=$(echo "$END - $START" | bc)
echo "Stored all $TOTAL events in ${SECONDS_TOTAL} s ($(echo "$TOTAL / $SECONDS_TOTAL" | bc) events/s end to end)"

ROWS=$(psql "SELECT count(*) FROM log_events")
SIZE=$(psql "SELECT pg_size_pretty(sum(pg_total_relation_size(c.oid))) FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid JOIN pg_class p ON p.oid = i.inhparent WHERE p.relname = 'log_events'")
ORDER=$(psql "SELECT properties->>'OrderId' FROM log_events WHERE properties ? 'OrderId' AND properties->>'OrderId' LIKE 'L-%' LIMIT 1")
TRACE=$(psql "SELECT trace_id FROM log_events WHERE trace_id IS NOT NULL LIMIT 1")

echo
echo "== Query times with $ROWS events in the table ($SIZE with indexes)"
echo "| Query | Time |"
echo "|---|---|"
time_query() {
  # The second run is reported; the first warms the cache the way a dashboard refresh would.
  curl -sf -b "$JAR" -o /dev/null "$API$2"
  local t
  t=$(curl -sf -b "$JAR" -o /dev/null -w '%{time_total}' "$API$2")
  printf '| %s | %.0f ms |\n' "$1" "$(echo "$t * 1000" | bc)"
}
time_query "Overview, 24 hours" "/api/stats/overview?range=24h"
time_query "Volume chart, 24 hours" "/api/stats/volume?range=24h"
time_query "Top errors, 24 hours" "/api/stats/top-errors?range=24h"
time_query "Search, newest 50, 24 hours" "/api/events?range=24h"
time_query "Full-text \"failed\", errors only, 24 hours" "/api/events?range=24h&level=Error&q=failed"
time_query "Property OrderId=$ORDER, 24 hours" "/api/events?range=24h&prop=OrderId:$ORDER"
time_query "One trace" "/api/traces/$TRACE"
time_query "Volume chart, 7 days" "/api/stats/volume?range=7d"
