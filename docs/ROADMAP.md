# Roadmap

A log service for the other projects: applications send structured logs, people search them, see what is
going wrong and get an alert when errors pile up. Everything lives in PostgreSQL, and a dashboard shows it.

## Scope

- **Sources:** every sending application has its own API key (stored as a hash, shown once). Keys only
  send; reading is for signed-in people.
- **Ingestion:** `POST /api/events` with a JSON array, or Serilog's compact format (CLEF, one event per
  line). The same endpoint answers at `/api/events/raw` with `X-Seq-ApiKey`, so an application that already
  logs with `Serilog.Sinks.Seq` only needs a different URL. Up to 1000 events per request; events with
  problems are reported by position while the rest are accepted.
- **Pipeline:** accepted events go into a bounded in-memory queue and are written in batches with
  PostgreSQL `COPY`. When the queue is full the API answers `429` with `Retry-After` instead of falling over.
  A rate limit per source keeps one noisy application from crowding out the others.
- **Search:** time range, minimum level, service, full-text search in messages and exceptions, property
  equality (`orderId = 1001`), trace id; newest first with a cursor.
- **Statistics:** volume per time bucket and level, the most frequent errors grouped by message template,
  error rate per service.
- **Traces:** every event of one trace id together, in order.
- **Alerts:** rules such as "more than 20 errors in 5 minutes from `checkout`"; a background evaluator
  records firings and sends them to a webhook or through the notification-service.
- **Retention:** one partition per day; partitions older than the retention period (14 days by default)
  are dropped, which is instant and leaves no bloat.
- **Dashboard:** sign-in, volume chart, top errors, services, search with expandable events, trace view,
  alerts and sources. Plain HTML, CSS and JavaScript served by the API, in the portfolio design.
- **Sample traffic:** a small generator that logs like a shop's services through `Serilog.Sinks.Seq`, with
  error bursts, so the stack has something to show from the first minute.

Out of scope: log shipping agents, metrics, multi-tenant isolation.

## Data model

| Table | Notes |
|---|---|
| `users` | email (unique), display name, role (`Admin`, `Viewer`), password hash |
| `sources` | name (unique), key prefix (unique), key hash, created, revoked |
| `log_events` | partitioned by day on `timestamp`: source, service, level, message, template, exception, trace and span id, properties (`jsonb`), a generated `tsvector` for full-text search |
| `alert_rules` | name, service (optional), minimum level, threshold, window, cooldown, channel |
| `alert_firings` | rule, window, count, sample events, notified |

## Architecture

```
src/LogAnalytics.Core            Entities and rules, CLEF and JSON event parsing, query parameters
src/LogAnalytics.Infrastructure  EF Core + Npgsql, migrations, partition maintenance, COPY writer, queries
src/LogAnalytics.Api             Ingestion and query API, cookie sign-in, alert evaluator, the dashboard (wwwroot)
src/LogAnalytics.Generator       Sample traffic through Serilog.Sinks.Seq
tests/LogAnalytics.UnitTests
tests/LogAnalytics.IntegrationTests  Real PostgreSQL in Testcontainers
tests/e2e                         Playwright flow through the dashboard
```

## Key decisions

- **PostgreSQL rather than OpenSearch.** One database to run. Daily partitions, GIN indexes on a
  generated `tsvector` and on `jsonb` properties, and `date_bin` cover search and statistics at this scale,
  and the numbers are measured in the README.
- **Partitions for retention.** Deleting old rows would leave dead tuples and long vacuums; dropping a
  day's partition is a metadata change.
- **`COPY` in batches behind a bounded queue.** Ingestion stays fast and the memory it can use is capped;
  callers are told to slow down rather than having their events dropped silently.
- **Seq-compatible endpoint.** Real applications can send logs without new code, which is also how the
  generator works.
- **The dashboard is plain JavaScript.** No build step and nothing to keep in sync with the API.

## Stages

| # | Branch | Content |
|---|---|---|
| 01 | `stage/01-skeleton` | Projects, users and cookie sign-in, sources with hashed keys, partitioned table, partition maintenance and retention, Compose, CI |
| 02 | `stage/02-ingestion` | JSON, CLEF and Seq-compatible ingestion, validation, bounded queue, `COPY` writer, backpressure, rate limits, generator |
| 03 | `stage/03-query` | Search, statistics, traces, alert rules and evaluator, webhook and notification-service channels |
| 04 | `stage/04-dashboard` | Dashboard, Playwright flow, load test |
| 05 | `stage/05-docs` | README (EN + TR), architecture, design decisions, screenshots and walkthrough |

## Later

- Saved searches and shareable links.
- Log-based metrics (counts per property value over time).
- Send background-jobs and notification-service logs here in their Compose stacks.
