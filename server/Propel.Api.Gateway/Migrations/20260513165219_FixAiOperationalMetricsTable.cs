using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Propel.Api.Gateway.Migrations
{
    /// <inheritdoc />
    public partial class FixAiOperationalMetricsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create AiOperationalMetrics table with quoted PascalCase name (as EF model expects).
            // Uses IF NOT EXISTS so it is safe to run even if the table already exists.
            // In a C# verbatim string, "" is a literal double-quote character.
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""AiOperationalMetrics"" (
    id             uuid          NOT NULL,
    metric_type    integer       NOT NULL,
    session_id     uuid,
    model_version  varchar(100)  NOT NULL,
    value_a        numeric(18,4),
    value_b        numeric(18,4),
    metadata       varchar(1000),
    recorded_at    timestamptz   NOT NULL,
    CONSTRAINT ""PK_AiOperationalMetrics"" PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ""IX_AiOperationalMetrics_MetricType_RecordedAt""
    ON ""AiOperationalMetrics"" (metric_type ASC, recorded_at DESC);

CREATE INDEX IF NOT EXISTS ""IX_AiOperationalMetrics_RecordedAt""
    ON ""AiOperationalMetrics"" (recorded_at DESC);

CREATE INDEX IF NOT EXISTS ""IX_AiOperationalMetrics_SessionId""
    ON ""AiOperationalMetrics"" (session_id)
    WHERE session_id IS NOT NULL;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""AiOperationalMetrics"";");
        }
    }
}
