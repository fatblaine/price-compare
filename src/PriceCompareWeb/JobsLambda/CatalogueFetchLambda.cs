using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PriceCompareCore.Jobs;
using PriceCompareCore.Services;
using PriceCompareCore.Services.Catalogue;
using PriceCompareCore.Utils;
using PriceCompareData.Data;
using PriceCompareData.Entities.Jobs;

namespace PriceCompareWeb.JobsLambda
{
    /// <summary>
    /// TARGET_JOB=CATALOGUE_FETCH (BTS-163): fetches both supermarkets' next-week catalogues from
    /// SaleFinder and writes them straight into the configured database. Scheduled Monday 17:30 and
    /// 21:00 and Tuesday 08:00 (Sydney); on Tuesday a missing catalogue is a failure.
    /// </summary>
    public class CatalogueFetchLambda
    {
        private static readonly int[] ShopTypes = { 1, 0 };   // Woolworths, Coles

        private readonly AppDbContext _db;
        private readonly ICatalogueFetchService _service;
        private readonly ILogger<CatalogueFetchService> _logger;

        public CatalogueFetchLambda()
        {
            var conn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? throw new InvalidOperationException("Missing database connection string.");

            var config = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();
            var options = new CatalogueOptions();
            config.GetSection(CatalogueOptions.SectionName).Bind(options);

            var csb = new Npgsql.NpgsqlConnectionStringBuilder(
                PriceCompareWeb.Infrastructure.DbConnectionStringNormalizer.ResolveRootCertificate(conn)) { Pooling = false };
            _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(csb.ConnectionString, o => o.CommandTimeout(300))
                .UseModel(PriceCompareData.CompiledModels.AppDbContextModel.Instance)
                .Options);

            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            _logger = loggerFactory.CreateLogger<CatalogueFetchService>();

            var services = new ServiceCollection();
            services.AddHttpClient(SaleFinderClient.HttpClientName, ConfigureSaleFinderHttpClient);
            var httpClientFactory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

            _service = new CatalogueFetchService(
                new SaleFinderClient(httpClientFactory),
                new DbContextCatalogueSqlExecutor(_db),
                _db,
                Options.Create(options),
                _logger);
        }

        /// <summary>Shared with Program.cs so the API process and the Lambda send the same headers.</summary>
        public static void ConfigureSaleFinderHttpClient(HttpClient client)
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        }

        public async Task Handler()
        {
            var failIfNotPublished = CatalogueFetchJob.FailIfNotPublished(SydneyClock.Today());
            var errors = new List<Exception>();

            // Each shop is recorded separately so one failing does not hide the other.
            foreach (var shopType in ShopTypes)
            {
                var start = DateTime.UtcNow;
                Exception? error = null;
                try
                {
                    await _service.RunAsync(shopType, failIfNotPublished);
                }
                catch (Exception ex)
                {
                    error = ex;
                    errors.Add(ex);
                    _logger.LogError(ex, "Catalogue fetch failed for shop type {ShopType}", shopType);
                }
                finally
                {
                    var end = DateTime.UtcNow;
                    var run = new JobRun
                    {
                        JobName = shopType == 1 ? "CatalogueFetchJob-Wws" : "CatalogueFetchJob-Coles",
                        Source = "aws",
                        ScheduledTime = null,
                        StartTime = start,
                        EndTime = end,
                        Status = error == null ? "success" : "fail",
                        DurationMs = (int)(end - start).TotalMilliseconds,
                        ErrorMessage = error?.GetBaseException().Message,
                        RequestId = Environment.GetEnvironmentVariable("AWS_REQUEST_ID"),
                        Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                    };
                    _db.ChangeTracker.Clear();
                    await JobRunRecorder.TryRecordAsync(_db, run, _logger);
                }
            }

            if (errors.Count > 0)
            {
                throw new AggregateException("Catalogue fetch failed.", errors);
            }
        }
    }
}
