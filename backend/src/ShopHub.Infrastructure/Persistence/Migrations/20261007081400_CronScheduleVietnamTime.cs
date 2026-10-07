using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CronScheduleVietnamTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Recurring jobs now run in Vietnam time (L081): an hour (or list of hours) written for UTC moves by +7 h so every job
            // keeps running at the same moment. Day-of-week / day-of-month schedules are left as they are (none is seeded).
            migrationBuilder.Sql("""
                UPDATE sys.system_parameters p
                SET value = x.m[1] || ' ' || (SELECT string_agg(((h::int + 31) % 24)::text, ',' ORDER BY ord)
                                              FROM unnest(string_to_array(x.m[2], ',')) WITH ORDINALITY AS t(h, ord)) || ' * * *'
                FROM (SELECT key, regexp_match(value, '^([0-9]+|\*/[0-9]+) ([0-9]+(?:,[0-9]+)*) \* \* \*$') AS m
                      FROM sys.system_parameters WHERE data_type = 'Cron') x
                WHERE p.key = x.key AND x.m IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recurring jobs now run in Vietnam time (L081): an hour (or list of hours) written for UTC moves by -7 h so every job
            // keeps running at the same moment. Day-of-week / day-of-month schedules are left as they are (none is seeded).
            migrationBuilder.Sql("""
                UPDATE sys.system_parameters p
                SET value = x.m[1] || ' ' || (SELECT string_agg(((h::int + 17) % 24)::text, ',' ORDER BY ord)
                                              FROM unnest(string_to_array(x.m[2], ',')) WITH ORDINALITY AS t(h, ord)) || ' * * *'
                FROM (SELECT key, regexp_match(value, '^([0-9]+|\*/[0-9]+) ([0-9]+(?:,[0-9]+)*) \* \* \*$') AS m
                      FROM sys.system_parameters WHERE data_type = 'Cron') x
                WHERE p.key = x.key AND x.m IS NOT NULL;
                """);
        }
    }
}
