using NpgsqlTypes;
using Serilog.Sinks.PostgreSQL;

namespace ShopHub.Infrastructure.Logging;

/// <summary>
/// Column layout of sys.logs (Serilog warnings/errors). The table itself is created by a migration so the
/// schema is versioned like every other table; keep both in sync.
/// </summary>
public static class LogTableColumns
{
    public static readonly IDictionary<string, ColumnWriterBase> Writers = new Dictionary<string, ColumnWriterBase>
    {
        ["raise_date"] = new TimestampColumnWriter(NpgsqlDbType.TimestampTz),
        ["level"] = new LevelColumnWriter(true, NpgsqlDbType.Varchar),
        ["message"] = new RenderedMessageColumnWriter(NpgsqlDbType.Text),
        ["message_template"] = new MessageTemplateColumnWriter(NpgsqlDbType.Text),
        ["exception"] = new ExceptionColumnWriter(NpgsqlDbType.Text),
        ["properties"] = new PropertiesColumnWriter(NpgsqlDbType.Jsonb),
    };

    public const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS sys.logs (
            id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            raise_date timestamptz NOT NULL,
            level varchar(20) NOT NULL,
            message text,
            message_template text,
            exception text,
            properties jsonb
        );
        CREATE INDEX IF NOT EXISTS ix_logs_raise_date ON sys.logs (raise_date DESC, id DESC);
        """;
}
