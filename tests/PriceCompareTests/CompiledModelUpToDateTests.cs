using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PriceCompareData.CompiledModels;
using PriceCompareData.Data;

namespace PriceCompareTests
{
    /// <summary>
    /// BTS-152: Program.cs loads the compiled model (UseModel) instead of running OnModelCreating on
    /// every cold start. If an entity or OnModelCreating changes and the compiled model is not
    /// regenerated, the app silently runs against a stale model — these tests fail instead.
    ///
    /// Regenerate with:
    ///   dotnet ef dbcontext optimize -p src/PriceCompareData -s src/PriceCompareData \
    ///     -o CompiledModels -n PriceCompareData.CompiledModels
    /// </summary>
    public class CompiledModelUpToDateTests
    {
        private static IModel RuntimeModel()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=compiled_model_test")
                .Options;
            using var db = new AppDbContext(options);
            return db.Model;
        }

        [Fact]
        public void CompiledModel_HasSameEntityTypes_AsOnModelCreating()
        {
            var runtime = RuntimeModel().GetEntityTypes().Select(e => e.Name).OrderBy(n => n).ToList();
            var compiled = AppDbContextModel.Instance.GetEntityTypes().Select(e => e.Name).OrderBy(n => n).ToList();

            Assert.Equal(runtime, compiled);
        }

        [Fact]
        public void CompiledModel_HasSameTablesAndColumns_AsOnModelCreating()
        {
            static string[] Describe(IModel model) => model.GetEntityTypes()
                .SelectMany(e => e.GetProperties()
                    .Select(p => $"{e.GetTableName()}.{p.GetColumnName()}:{p.ClrType.Name}:{p.IsNullable}"))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(Describe(RuntimeModel()), Describe(AppDbContextModel.Instance));
        }

        [Fact]
        public void CompiledModel_HasSameIndexesAndKeys_AsOnModelCreating()
        {
            static string[] Describe(IModel model) => model.GetEntityTypes()
                .SelectMany(e => e.GetIndexes()
                    .Select(i => $"IX {e.GetTableName()}({string.Join(',', i.Properties.Select(p => p.GetColumnName()))}):{i.IsUnique}")
                    .Concat(e.GetKeys()
                        .Select(k => $"PK {e.GetTableName()}({string.Join(',', k.Properties.Select(p => p.GetColumnName()))})")))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(Describe(RuntimeModel()), Describe(AppDbContextModel.Instance));
        }
    }
}
