using System;
using System.Globalization;

namespace PriceCompareCore.Utils
{
    /// <summary>
    /// PostgreSQL literals for generated SQL scripts that must run unchanged in another database
    /// (e.g. through tools/run-sql-imports.ps1), so bind parameters are not an option.
    /// Assumes standard_conforming_strings = on (the PostgreSQL default): only quotes need doubling.
    /// </summary>
    public static class SqlLiteral
    {
        public static string Text(string? value)
        {
            if (value is null)
            {
                return "NULL";
            }

            // NUL cannot be stored in a PostgreSQL text value.
            return "'" + value.Replace("\0", string.Empty).Replace("'", "''") + "'";
        }

        public static string Decimal(decimal? value) =>
            value is null ? "NULL" : value.Value.ToString(CultureInfo.InvariantCulture);

        public static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string Date(DateOnly value) =>
            "DATE '" + value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "'";

        public static string TimestampUtc(DateTime utc) =>
            "TIMESTAMPTZ '" + DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "+00'";
    }
}
