using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LogAnalytics.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The log table, written by hand: EF Core does not create partitioned tables. It is partitioned by day on
    /// <c>timestamp</c>; the daily partitions themselves are created by <c>PartitionMaintenance</c>. Indexes on the
    /// parent are created on every partition automatically.
    /// </summary>
    [DbContext(typeof(LogsDbContext))]
    [Migration("20261030200000_LogEvents")]
    public partial class LogEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE log_events (
                    id bigint GENERATED ALWAYS AS IDENTITY,
                    "timestamp" timestamptz NOT NULL,
                    received_at timestamptz NOT NULL,
                    source_id uuid NOT NULL,
                    service text NOT NULL,
                    level smallint NOT NULL,
                    message text NOT NULL,
                    message_template text,
                    exception text,
                    trace_id text,
                    span_id text,
                    properties jsonb NOT NULL DEFAULT '{}'::jsonb,
                    search tsvector GENERATED ALWAYS AS (to_tsvector('simple', message || ' ' || coalesce(exception, ''))) STORED,
                    PRIMARY KEY ("timestamp", id)
                ) PARTITION BY RANGE ("timestamp");

                CREATE INDEX ix_log_events_time ON log_events ("timestamp" DESC, id DESC);
                CREATE INDEX ix_log_events_service_time ON log_events (service, "timestamp" DESC);
                CREATE INDEX ix_log_events_level_time ON log_events (level, "timestamp" DESC);
                CREATE INDEX ix_log_events_search ON log_events USING gin (search);
                CREATE INDEX ix_log_events_properties ON log_events USING gin (properties jsonb_path_ops);
                CREATE INDEX ix_log_events_trace ON log_events (trace_id) WHERE trace_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE log_events;");
        }
    }
}
