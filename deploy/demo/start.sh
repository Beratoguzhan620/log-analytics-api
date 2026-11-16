#!/bin/sh
# Starts the API and, once it answers, the generator. The sample source's key is built from a secret Render
# generates (DEMO_SOURCE_SECRET), so it is stable across restarts and never written into the repository.
set -eu

secret=$(printf '%s' "${DEMO_SOURCE_SECRET:?DEMO_SOURCE_SECRET is not set}" | tr '+/' '-_' | tr -d '=')
export Bootstrap__SampleSourceKey="la_sampleap_${secret}"

cd /app/api
dotnet LogAnalytics.Api.dll &
api=$!

cd /app/generator
LOGS_URL=http://localhost:8080 LOGS_API_KEY="$Bootstrap__SampleSourceKey" dotnet LogAnalytics.Generator.dll &
generator=$!

trap 'kill "$api" "$generator" 2>/dev/null' TERM INT
# The container lives as long as the API does; the generator is only there to feed it.
wait "$api"
status=$?
kill "$generator" 2>/dev/null || true
exit "$status"
