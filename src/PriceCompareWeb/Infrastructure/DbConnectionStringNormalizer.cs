using System;
using System.Collections.Generic;
using System.IO;
using Npgsql;

namespace PriceCompareWeb.Infrastructure
{
    /// <summary>
    /// BTS-155: guards against exhausting Supabase's connection limit.
    ///
    /// Every Lambda instance keeps its own Npgsql pool, and Npgsql allows 100 connections per pool
    /// by default. A handful of concurrent instances therefore blows past Supavisor's session-mode
    /// limit (15 clients), which surfaces as XX000 EMAXCONNSESSION and takes the whole API down.
    ///
    /// The deployed connection string should use transaction mode (port 6543, Pooling=false), but
    /// that lives in deployment parameters; this keeps a sane cap even when it is misconfigured.
    /// Explicit values in the connection string always win.
    /// </summary>
    public static class DbConnectionStringNormalizer
    {
        public const int DefaultMaxPoolSize = 5;
        public const int DefaultConnectionIdleLifetimeSeconds = 60;

        public static string? Normalize(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            NpgsqlConnectionStringBuilder builder;
            try
            {
                builder = new NpgsqlConnectionStringBuilder(connectionString);
            }
            catch (ArgumentException)
            {
                // Unparseable — leave it alone and let the provider report the real error.
                return connectionString;
            }

            var rootCertificateResolved = ResolveRootCertificate(builder);

            // Client-side pooling disabled (transaction mode): nothing to cap.
            if (!builder.Pooling)
            {
                return rootCertificateResolved ? builder.ConnectionString : connectionString;
            }

            // NpgsqlConnectionStringBuilder.ContainsKey answers "is this a known keyword", not
            // "did the caller set it", so read the keys straight off the original string.
            var specified = SpecifiedKeys(connectionString!);

            if (!specified.Contains("maximumpoolsize") && !specified.Contains("maxpoolsize"))
            {
                builder.MaxPoolSize = DefaultMaxPoolSize;
            }

            if (!specified.Contains("connectionidlelifetime"))
            {
                builder.ConnectionIdleLifetime = DefaultConnectionIdleLifetimeSeconds;
            }

            return builder.ConnectionString;
        }

        /// <summary>Connection string without credentials, for startup logging.</summary>
        public static string Describe(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return "(empty)";
            }

            try
            {
                var b = new NpgsqlConnectionStringBuilder(connectionString);
                return $"Host={b.Host};Port={b.Port};Database={b.Database};Username={b.Username};" +
                       $"SslMode={b.SslMode};TrustServerCertificate={b.TrustServerCertificate};" +
                       $"Pooling={b.Pooling};MaxPoolSize={b.MaxPoolSize};ConnectionIdleLifetime={b.ConnectionIdleLifetime};";
            }
            catch (ArgumentException)
            {
                return "(unparseable connection string)";
            }
        }

        /// <summary>
        /// Connection string with a relative <c>Root Certificate</c> path made absolute against the
        /// app directory, for callers that build their own Npgsql options (the job Lambdas).
        /// </summary>
        public static string ResolveRootCertificate(string connectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return ResolveRootCertificate(builder) ? builder.ConnectionString : connectionString;
        }

        /// <summary>
        /// The Supabase CA ships in the publish output (certs/), but the process working directory is
        /// not always the app directory (tests, IDE runs), so a relative Root Certificate path is
        /// resolved against AppContext.BaseDirectory. Missing files are left for Npgsql to report.
        /// </summary>
        private static bool ResolveRootCertificate(NpgsqlConnectionStringBuilder builder)
        {
            var rootCertificate = builder.RootCertificate;
            if (string.IsNullOrWhiteSpace(rootCertificate) || Path.IsPathRooted(rootCertificate))
            {
                return false;
            }

            var candidate = Path.Combine(AppContext.BaseDirectory, rootCertificate);
            if (!File.Exists(candidate))
            {
                return false;
            }

            builder.RootCertificate = candidate;
            return true;
        }

        private static HashSet<string> SpecifiedKeys(string connectionString)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                keys.Add(part.Substring(0, separator).Replace(" ", string.Empty).Trim());
            }

            return keys;
        }
    }
}
