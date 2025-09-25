using Microsoft.EntityFrameworkCore;
using Polly;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Jobs;
using PriceCompareCore.Services;
using PriceCompareData.Data;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Lambda Hosting
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger
builder.Services.AddSwaggerGen();

// Redis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "PriceCompare_";
});

// Quartz
// builder.Services.AddQuartz(q =>
// {
//     q.UseMicrosoftDependencyInjectionJobFactory();
//     q.AddJobListener<LoggingJobListener>();

//     // scrape data - coles down down
//     var jobKey = new JobKey("ColesRefreshJob");
//     q.AddJob<ColesRefreshJob>(opts => opts.WithIdentity(jobKey));
//     q.AddTrigger(opts => opts
//         .ForJob(jobKey)
//         .WithIdentity("ColesRefreshJob-trigger")
//         .WithCronSchedule("0 0 2 ? * WED"));

//     // scrape data - coles on special
//     var jobKeySpecial = new JobKey("ColesRefreshJobSpecial");
//     q.AddJob<ColesRefreshJobSpecial>(opts => opts.WithIdentity(jobKeySpecial));
//     q.AddTrigger(opts => opts
//         .ForJob(jobKeySpecial)
//         .WithIdentity("ColesRefreshJobSpecial-trigger")
//         .WithCronSchedule("0 0 3 ? * WED"));

//     // scrape data - wws
//     var jobKeyWwsSpecial = new JobKey("WwsRefreshJobSpecial");
//     q.AddJob<ColesRefreshJobSpecial>(opts => opts.WithIdentity(jobKeyWwsSpecial));
//     q.AddTrigger(opts => opts
//         .ForJob(jobKeyWwsSpecial)
//         .WithIdentity("WwsRefreshJobSpecial-trigger")
//         .WithCronSchedule("0 0 4 ? * WED"));

//     // delete data
//     var cleanJobKey = new JobKey("CleanPriceHistoryJob");
//     q.AddJob<CleanPriceHistoryJob>(opts => opts.WithIdentity(cleanJobKey));
//     q.AddTrigger(opts => opts
//         .ForJob(cleanJobKey)
//         .WithIdentity("CleanPriceHistoryJob-trigger")
//         .WithCronSchedule("0 0 1 ? 1/3 4#1"));
// });

// builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

builder.Services.AddHttpClient<IColesDownScraperService, ColesDownScraperService>()
    .AddTransientHttpErrorPolicy(policy =>
        policy.WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

builder.Services.AddHttpClient<IColesSpecialScraperService, ColesSpecialScraperService>()
    .AddTransientHttpErrorPolicy(policy =>
        policy.WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

builder.Services.AddScoped<IWoolworthsSpecialScraperService, WoolworthsSpecialScraperService>();

builder.Services.AddScoped<ICategoryMappingService, CategoryMappingService>();

builder.Services.AddScoped<IIngestionService, IngestionService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
