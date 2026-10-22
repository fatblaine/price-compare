using Npgsql;
using PriceCompareWeb.Infrastructure;

namespace PriceCompareTests
{
    /// <summary>
    /// BTS-155: each Lambda instance keeps its own Npgsql pool (100 connections by default), so a
    /// few concurrent instances exhausted Supabase's session-mode limit of 15 and took the API down
    /// with XX000 EMAXCONNSESSION. The cap must hold even if the deployed connection string is wrong.
    /// </summary>
    public class DbConnectionStringNormalizerTests
    {
        private const string SessionMode =
            "Host=db.example.com;Port=5432;Database=postgres;Username=postgres;Password=secret";

        [Fact]
        public void CapsPoolSizeAndIdleLifetime_WhenPoolingIsOnAndNothingIsSpecified()
        {
            var result = new NpgsqlConnectionStringBuilder(DbConnectionStringNormalizer.Normalize(SessionMode));

            Assert.True(result.Pooling);
            Assert.Equal(DbConnectionStringNormalizer.DefaultMaxPoolSize, result.MaxPoolSize);
            Assert.Equal(DbConnectionStringNormalizer.DefaultConnectionIdleLifetimeSeconds, result.ConnectionIdleLifetime);
        }

        [Fact]
        public void KeepsExplicitPoolSettings()
        {
            var explicitSettings = SessionMode + ";Maximum Pool Size=12;Connection Idle Lifetime=300";

            var result = new NpgsqlConnectionStringBuilder(DbConnectionStringNormalizer.Normalize(explicitSettings));

            Assert.Equal(12, result.MaxPoolSize);
            Assert.Equal(300, result.ConnectionIdleLifetime);
        }

        [Fact]
        public void LeavesTransactionModeStringsUntouched()
        {
            var transactionMode = "Host=db.example.com;Port=6543;Database=postgres;Username=postgres;" +
                                  "Password=secret;Pooling=false;No Reset On Close=true";

            var result = DbConnectionStringNormalizer.Normalize(transactionMode);

            Assert.Equal(transactionMode, result);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ReturnsInputUnchanged_WhenMissing(string? connectionString)
        {
            Assert.Equal(connectionString, DbConnectionStringNormalizer.Normalize(connectionString));
        }

        [Fact]
        public void ReturnsInputUnchanged_WhenUnparseable()
        {
            const string broken = "this is not a connection string";

            Assert.Equal(broken, DbConnectionStringNormalizer.Normalize(broken));
        }

        [Fact]
        public void DescribeOmitsPasswordAndShowsPoolSettings()
        {
            var description = DbConnectionStringNormalizer.Describe(DbConnectionStringNormalizer.Normalize(SessionMode));

            Assert.DoesNotContain("secret", description);
            Assert.Contains("Port=5432", description);
            Assert.Contains("Pooling=True", description);
            Assert.Contains($"MaxPoolSize={DbConnectionStringNormalizer.DefaultMaxPoolSize}", description);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ResolvesRelativeRootCertificateAgainstAppDirectory(bool pooling)
        {
            var relative = Path.Combine("certs", $"test-ca-{Guid.NewGuid():N}.crt");
            var absolute = Path.Combine(AppContext.BaseDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, "placeholder");
            try
            {
                var connectionString = SessionMode + $";Pooling={pooling};SSL Mode=VerifyFull;Root Certificate={relative}";

                var result = new NpgsqlConnectionStringBuilder(DbConnectionStringNormalizer.Normalize(connectionString));

                Assert.Equal(absolute, result.RootCertificate);
                Assert.Equal(SslMode.VerifyFull, result.SslMode);
                Assert.Equal(absolute, new NpgsqlConnectionStringBuilder(
                    DbConnectionStringNormalizer.ResolveRootCertificate(connectionString)).RootCertificate);
            }
            finally
            {
                File.Delete(absolute);
            }
        }

        [Fact]
        public void LeavesRootCertificateAlone_WhenFileIsMissing()
        {
            var transactionMode = "Host=db.example.com;Port=6543;Database=postgres;Username=postgres;" +
                                  "Password=secret;Pooling=false;SSL Mode=VerifyFull;Root Certificate=certs/missing.crt";

            Assert.Equal(transactionMode, DbConnectionStringNormalizer.Normalize(transactionMode));
        }

        [Fact]
        public void DescribeHandlesMissingAndUnparseableStrings()
        {
            Assert.Equal("(empty)", DbConnectionStringNormalizer.Describe(null));
            Assert.Equal("(unparseable connection string)", DbConnectionStringNormalizer.Describe("nonsense"));
        }
    }
}
