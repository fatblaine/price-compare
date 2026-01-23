using System.Security.Claims;
using System.Text;
using System.Linq;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Polly;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Jobs;
using PriceCompareCore.Services;
using PriceCompareCore.Utils;
using PriceCompareData.Data;
using PriceCompareWeb.JobsLambda;
using PriceCompareWeb.Services;
using Quartz;
using PriceCompareCore.Config;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FavoriteAlertSettings>(
    builder.Configuration.GetSection("FavoriteAlerts"));

builder.Services.AddScoped<IFavoritePriceTrackingService, FavoritePriceTrackingService>();

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));

builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();

builder.Services.AddScoped<IReceiptProcessingService, ReceiptProcessingService>();

builder.Services.AddScoped<IReceiptOcrService, AwsRekognitionReceiptOcrService>();

builder.Services.AddScoped<IReceiptStorageService, S3ReceiptStorageService>();

builder.Services.Configure<AwsOptions>(
    builder.Configuration.GetSection("Aws"));

builder.Services.Configure<RekognitionOptions>(
    builder.Configuration.GetSection("Rekognition"));

// Lambda Hosting
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// register AWS SDK for .NET services
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonDynamoDB>();
builder.Services.AddSingleton<IDynamoDBContext, DynamoDBContext>();
// AWS Scheduler client for reading schedules.
builder.Services.AddAWSService<Amazon.Scheduler.IAmazonScheduler>();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
// Swagger with JWT support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "PriceCompare API", Version = "v1" });

    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste JWT access token (do NOT include the 'Bearer ' prefix)"
    };

    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            scheme,
            Array.Empty<string>()
        }
    });
});
builder.Services.AddHttpContextAccessor();

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Default", policy =>
    {
        if (allowedOrigins != null && allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }
        else
        {
            policy.AllowAnyOrigin();
        }

        policy.AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Redis
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
    options.InstanceName = "PriceCompare_";
});

var enableQuartzJobs = builder.Configuration.GetValue<bool?>("Scraping:EnableQuartz") ?? false;
if (enableQuartzJobs)
{
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

        // favorite price tracking
        var favoriteTrackJobKey = new JobKey("FavoritePriceTrackingJob");
        q.AddJob<FavoritePriceTrackingJob>(opts => opts.WithIdentity(favoriteTrackJobKey));
        q.AddTrigger(opts => opts
            .ForJob(favoriteTrackJobKey)
            .WithIdentity("FavoritePriceTrackingJob-trigger")
            .WithCronSchedule("0 0 5 ? * WED"));
    });

    builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
}
else
{
    builder.Services.AddLogging();
}

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

// Receipt service
builder.Services.AddScoped<IReceiptService, ReceiptService>();

// Favorite service
builder.Services.AddScoped<IFavoriteService, FavoriteService>();

// Admin schedule aggregation (AWS + Quartz).
builder.Services.AddScoped<AdminScheduleService>();

// Auth service
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>();
if (jwtSettings == null || string.IsNullOrWhiteSpace(jwtSettings.Secret))
{
    throw new InvalidOperationException("JwtSettings:Secret is missing in configuration.");
}
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            // Use UTF8 to avoid losing bytes if secret contains non-ASCII chars
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            NameClaimType = ClaimTypes.NameIdentifier,
            ClockSkew = TimeSpan.Zero
        };
    });
var adminEmails = builder.Configuration.GetSection("Admin:Emails").Get<string[]>() ?? Array.Empty<string>();
var adminEmailsEnv = builder.Configuration["AdminEmails"];
if (!string.IsNullOrWhiteSpace(adminEmailsEnv))
{
    var envEmails = adminEmailsEnv
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    adminEmails = adminEmails.Concat(envEmails).ToArray();
}
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAssertion(ctx =>
        {
            var email = ctx.User.FindFirstValue(ClaimTypes.Email);
            return !string.IsNullOrWhiteSpace(email) &&
                   adminEmails.Any(a => string.Equals(a, email, StringComparison.OrdinalIgnoreCase));
        }));
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// builder.Services.AddDbContext<AppDbContext>(options =>
//     options.UseSqlServer(connectionString));

// use PostgreSQL instead of SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
        npgsqlOptions.CommandTimeout(120)));

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(connectionString))
{
    try
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        var safe = $"Host={csb.Host};Port={csb.Port};Database={csb.Database};Username={csb.Username};SslMode={csb.SslMode};TrustServerCertificate={csb.TrustServerCertificate};Pooling={csb.Pooling};";
        app.Logger.LogInformation("DB connection (sanitized): {ConnectionString}", safe);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to parse DB connection string for logging.");
    }
}
else
{
    app.Logger.LogWarning("DefaultConnection is empty or missing.");
}

var envOverride = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
app.Logger.LogInformation("Env override set for ConnectionStrings__DefaultConnection: {IsSet}", !string.IsNullOrWhiteSpace(envOverride));
app.Logger.LogInformation("Environment: {EnvironmentName}", app.Environment.EnvironmentName);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Default");

app.UseAuthentication();
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
            else if (string.Equals(target, "FAVORITE_TRACK", StringComparison.OrdinalIgnoreCase))
            {
                var job = new FavoritePriceTrackingLambda();
                await job.Handler();
            }
            else
            {
                Console.WriteLine("No valid TARGET_JOB environment variable found.");
                Console.WriteLine("Available: COLES_SPECIAL | WWS_SPECIAL | FAVORITE_TRACK");
            }
        }
    }
}
