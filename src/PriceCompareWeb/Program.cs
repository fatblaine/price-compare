using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Polly;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Jobs;
using PriceCompareCore.Services;
using PriceCompareData.Data;
using PriceCompareWeb.JobsLambda;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Lambda Hosting
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// register AWS SDK for .NET services
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddSingleton<IDynamoDBContext, DynamoDBContext>();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
// Swagger
builder.Services.AddSwaggerGen();

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                    ?? new[] { "http://localhost:3000" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// Redis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "PriceCompare_";
});

// Quartz
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    q.AddJobListener<LoggingJobListener>();

    // scrape data - coles down down
    var jobKey = new JobKey("ColesRefreshJob");
    q.AddJob<ColesRefreshJob>(opts => opts.WithIdentity(jobKey));
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("ColesRefreshJob-trigger")
        .WithCronSchedule("0 0 2 ? * WED"));

    // scrape data - coles on special
    var jobKeySpecial = new JobKey("ColesRefreshJobSpecial");
    q.AddJob<ColesRefreshJobSpecial>(opts => opts.WithIdentity(jobKeySpecial));
    q.AddTrigger(opts => opts
        .ForJob(jobKeySpecial)
        .WithIdentity("ColesRefreshJobSpecial-trigger")
        .WithCronSchedule("0 0 3 ? * WED"));

    // scrape data - wws
    var jobKeyWwsSpecial = new JobKey("WwsRefreshJobSpecial");
    q.AddJob<WwsRefreshJobSpecial>(opts => opts.WithIdentity(jobKeyWwsSpecial));
    q.AddTrigger(opts => opts
        .ForJob(jobKeyWwsSpecial)
        .WithIdentity("WwsRefreshJobSpecial-trigger")
        .WithCronSchedule("0 0 4 ? * WED"));

    // delete data
    var cleanJobKey = new JobKey("CleanPriceHistoryJob");
    q.AddJob<CleanPriceHistoryJob>(opts => opts.WithIdentity(cleanJobKey));
    q.AddTrigger(opts => opts
        .ForJob(cleanJobKey)
        .WithIdentity("CleanPriceHistoryJob-trigger")
        .WithCronSchedule("0 0 1 ? 1/3 4#1"));
});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

builder.Services.AddHttpClient<IColesDownScraperService, ColesDownScraperService>()
    .AddTransientHttpErrorPolicy(policy =>
        policy.WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

builder.Services.AddScoped<IColesSpecialScraperService, ColesSpecialScraperService>();

builder.Services.AddScoped<IWoolworthsSpecialScraperService, WoolworthsSpecialScraperService>();

builder.Services.AddScoped<ICategoryMappingService, CategoryMappingService>();

builder.Services.AddScoped<IIngestionService, IngestionService>();

// Products service
builder.Services.AddScoped<PriceCompareCore.Interfaces.IProductService, PriceCompareCore.Services.ProductService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// builder.Services.AddDbContext<AppDbContext>(options =>
//     options.UseSqlServer(connectionString));

// use PostgreSQL instead of SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Default");

app.UseAuthorization();

app.MapControllers();

app.Run();

// =========================================================
// ✅ Glue code for Container Lambda execution
// =========================================================

namespace PriceCompareWeb
{
    public class LambdaEntryPoint
    {
        // AWS entry point
        public async Task FunctionHandlerAsync()
        {
            var target = Environment.GetEnvironmentVariable("TARGET_JOB");

            if (string.Equals(target, "COLES_SPECIAL", StringComparison.OrdinalIgnoreCase))
            {
                var job = new ColesRefreshSpecialLambda();
                await job.Handler();
            }
            else if (string.Equals(target, "WWS_SPECIAL", StringComparison.OrdinalIgnoreCase))
            {
                var job = new WwsRefreshSpecialLambda();
                await job.Handler();
            }
            else
            {
                Console.WriteLine("No valid TARGET_JOB environment variable found.");
                Console.WriteLine("Available: COLES_SPECIAL | WWS_SPECIAL");
            }
        }
    }
}
