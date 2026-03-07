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

builder.Services.Configure<ScrapeExportOptions>(
    builder.Configuration.GetSection("ScrapeExport"));

builder.Services.Configure<OpenRouterOptions>(
    builder.Configuration.GetSection("OpenRouter"));

builder.Services.Configure<LlmLogOptions>(
     builder.Configuration.GetSection("LlmLog"));

// Match job services
builder.Services.AddScoped<IMatchJobService, MatchJobService>();
builder.Services.AddScoped<IProductMatchingService, ProductMatchingService>();
builder.Services.AddScoped<IMatchLlmReviewService, MatchLlmReviewService>();

// OpenRouter services
builder.Services.AddHttpClient("OpenRouter", client =>
{
    client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddSingleton<IEmbeddingService, OpenRouterEmbeddingService>();
builder.Services.AddScoped<IVectorSearchService, OpenRouterVectorSearchService>();
builder.Services.AddScoped<IMatchVerificationService, OpenRouterMatchVerificationService>();

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
        var localTz = TimeZoneInfo.Local;

        // scrape data - coles down down
        var jobKey = new JobKey("ColesRefreshJob");
        q.AddJob<ColesRefreshJob>(opts => opts.WithIdentity(jobKey));
        q.AddTrigger(opts => opts
            .ForJob(jobKey)
            .WithIdentity("ColesRefreshJob-trigger")
            .WithCronSchedule("0 10 0 ? * WED", x => x.InTimeZone(localTz)));

        // scrape data - coles on special (no scheduled trigger)

        // scrape data - woolworths lower shelf price (DOM)
        var jobKeyWwsLowerShelf = new JobKey("WwsLowerShelfDomJob");
        q.AddJob<WwsLowerShelfDomJob>(opts => opts.WithIdentity(jobKeyWwsLowerShelf));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyWwsLowerShelf)
            .WithIdentity("WwsLowerShelfDomJob-trigger")
            .WithCronSchedule("0 10 10 ? * WED", x => x.InTimeZone(localTz)));

        // scrape data - woolworths everyday low price (DOM)
        var jobKeyWwsEverydayLow = new JobKey("WwsEverydayLowPriceDomJob");
        q.AddJob<WwsEverydayLowPriceDomJob>(opts => opts.WithIdentity(jobKeyWwsEverydayLow));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyWwsEverydayLow)
            .WithIdentity("WwsEverydayLowPriceDomJob-trigger")
            .WithCronSchedule("0 40 10 ? * WED", x => x.InTimeZone(localTz)));

        // scrape data - woolworths half price (DOM)
        var jobKeyWwsHalfPrice = new JobKey("WwsHalfPriceDomJob");
        q.AddJob<WwsHalfPriceDomJob>(opts => opts.WithIdentity(jobKeyWwsHalfPrice));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyWwsHalfPrice)
            .WithIdentity("WwsHalfPriceDomJob-trigger")
            .WithCronSchedule("0 10 11 ? * WED", x => x.InTimeZone(localTz)));

        // scrape data - woolworths buy more save more (DOM)
        var jobKeyWwsBuyMoreSaveMore = new JobKey("WwsBuyMoreSaveMoreDomJob");
        q.AddJob<WwsBuyMoreSaveMoreDomJob>(opts => opts.WithIdentity(jobKeyWwsBuyMoreSaveMore));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyWwsBuyMoreSaveMore)
            .WithIdentity("WwsBuyMoreSaveMoreDomJob-trigger")
            .WithCronSchedule("0 10 12 ? * WED", x => x.InTimeZone(localTz)));

        // scrape data - woolworths summer price (DOM) - schedule disabled; replaced by autumn-price
        var jobKeyWwsSummerPrice = new JobKey("WwsSummerPriceDomJob");
        q.AddJob<WwsSummerPriceDomJob>(opts => opts.WithIdentity(jobKeyWwsSummerPrice).StoreDurably());

        // scrape data - woolworths autumn price (DOM)
        var jobKeyWwsAutumnPrice = new JobKey("WwsAutumnPriceDomJob");
        q.AddJob<WwsAutumnPriceDomJob>(opts => opts.WithIdentity(jobKeyWwsAutumnPrice));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyWwsAutumnPrice)
            .WithIdentity("WwsAutumnPriceDomJob-trigger")
            .WithCronSchedule("0 40 12 ? * WED", x => x.InTimeZone(localTz)));

        // weekly sequential scrape - coles DOM categories (Wed 00:10 local, every 30 mins)
        var jobKeyColesMeatSeafood = new JobKey("ColesMeatSeafoodDomJob");
        q.AddJob<ColesMeatSeafoodDomJob>(opts => opts.WithIdentity(jobKeyColesMeatSeafood));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesMeatSeafood)
            .WithIdentity("ColesMeatSeafoodDomJob-trigger")
            .WithCronSchedule("0 40 0 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesFruitVegetables = new JobKey("ColesFruitVegetablesDomJob");
        q.AddJob<ColesFruitVegetablesDomJob>(opts => opts.WithIdentity(jobKeyColesFruitVegetables));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesFruitVegetables)
            .WithIdentity("ColesFruitVegetablesDomJob-trigger")
            .WithCronSchedule("0 10 1 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesDairyEggsFridge = new JobKey("ColesDairyEggsFridgeDomJob");
        q.AddJob<ColesDairyEggsFridgeDomJob>(opts => opts.WithIdentity(jobKeyColesDairyEggsFridge));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesDairyEggsFridge)
            .WithIdentity("ColesDairyEggsFridgeDomJob-trigger")
            .WithCronSchedule("0 40 1 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesBakery = new JobKey("ColesBakeryDomJob");
        q.AddJob<ColesBakeryDomJob>(opts => opts.WithIdentity(jobKeyColesBakery));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesBakery)
            .WithIdentity("ColesBakeryDomJob-trigger")
            .WithCronSchedule("0 10 2 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesDeli = new JobKey("ColesDeliDomJob");
        q.AddJob<ColesDeliDomJob>(opts => opts.WithIdentity(jobKeyColesDeli));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesDeli)
            .WithIdentity("ColesDeliDomJob-trigger")
            .WithCronSchedule("0 40 2 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesPantry = new JobKey("ColesPantryDomJob");
        q.AddJob<ColesPantryDomJob>(opts => opts.WithIdentity(jobKeyColesPantry));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesPantry)
            .WithIdentity("ColesPantryDomJob-trigger")
            .WithCronSchedule("0 10 3 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesDietaryWorldFoods = new JobKey("ColesDietaryWorldFoodsDomJob");
        q.AddJob<ColesDietaryWorldFoodsDomJob>(opts => opts.WithIdentity(jobKeyColesDietaryWorldFoods));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesDietaryWorldFoods)
            .WithIdentity("ColesDietaryWorldFoodsDomJob-trigger")
            .WithCronSchedule("0 40 3 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesChipsChocolatesSnacks = new JobKey("ColesChipsChocolatesSnacksDomJob");
        q.AddJob<ColesChipsChocolatesSnacksDomJob>(opts => opts.WithIdentity(jobKeyColesChipsChocolatesSnacks));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesChipsChocolatesSnacks)
            .WithIdentity("ColesChipsChocolatesSnacksDomJob-trigger")
            .WithCronSchedule("0 10 4 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesDrinks = new JobKey("ColesDrinksDomJob");
        q.AddJob<ColesDrinksDomJob>(opts => opts.WithIdentity(jobKeyColesDrinks));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesDrinks)
            .WithIdentity("ColesDrinksDomJob-trigger")
            .WithCronSchedule("0 40 4 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesLiquorland = new JobKey("ColesLiquorlandDomJob");
        q.AddJob<ColesLiquorlandDomJob>(opts => opts.WithIdentity(jobKeyColesLiquorland));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesLiquorland)
            .WithIdentity("ColesLiquorlandDomJob-trigger")
            .WithCronSchedule("0 10 5 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesFrozen = new JobKey("ColesFrozenDomJob");
        q.AddJob<ColesFrozenDomJob>(opts => opts.WithIdentity(jobKeyColesFrozen));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesFrozen)
            .WithIdentity("ColesFrozenDomJob-trigger")
            .WithCronSchedule("0 40 5 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesCleaningLaundry = new JobKey("ColesCleaningLaundryDomJob");
        q.AddJob<ColesCleaningLaundryDomJob>(opts => opts.WithIdentity(jobKeyColesCleaningLaundry));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesCleaningLaundry)
            .WithIdentity("ColesCleaningLaundryDomJob-trigger")
            .WithCronSchedule("0 10 6 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesHealthBeauty = new JobKey("ColesHealthBeautyDomJob");
        q.AddJob<ColesHealthBeautyDomJob>(opts => opts.WithIdentity(jobKeyColesHealthBeauty));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesHealthBeauty)
            .WithIdentity("ColesHealthBeautyDomJob-trigger")
            .WithCronSchedule("0 40 6 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesBaby = new JobKey("ColesBabyDomJob");
        q.AddJob<ColesBabyDomJob>(opts => opts.WithIdentity(jobKeyColesBaby));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesBaby)
            .WithIdentity("ColesBabyDomJob-trigger")
            .WithCronSchedule("0 10 7 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesPet = new JobKey("ColesPetDomJob");
        q.AddJob<ColesPetDomJob>(opts => opts.WithIdentity(jobKeyColesPet));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesPet)
            .WithIdentity("ColesPetDomJob-trigger")
            .WithCronSchedule("0 40 7 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesHomeGarden = new JobKey("ColesHomeGardenDomJob");
        q.AddJob<ColesHomeGardenDomJob>(opts => opts.WithIdentity(jobKeyColesHomeGarden));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesHomeGarden)
            .WithIdentity("ColesHomeGardenDomJob-trigger")
            .WithCronSchedule("0 10 8 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesBigPackValue = new JobKey("ColesBigPackValueDomJob");
        q.AddJob<ColesBigPackValueDomJob>(opts => opts.WithIdentity(jobKeyColesBigPackValue));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesBigPackValue)
            .WithIdentity("ColesBigPackValueDomJob-trigger")
            .WithCronSchedule("0 40 8 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesBonusCreditProducts = new JobKey("ColesBonusCreditProductsDomJob");
        q.AddJob<ColesBonusCreditProductsDomJob>(opts => opts.WithIdentity(jobKeyColesBonusCreditProducts));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesBonusCreditProducts)
            .WithIdentity("ColesBonusCreditProductsDomJob-trigger")
            .WithCronSchedule("0 10 9 ? * WED", x => x.InTimeZone(localTz)));

        var jobKeyColesDeliverMoreRange = new JobKey("ColesDeliverMoreRangeDomJob");
        q.AddJob<ColesDeliverMoreRangeDomJob>(opts => opts.WithIdentity(jobKeyColesDeliverMoreRange));
        q.AddTrigger(opts => opts
            .ForJob(jobKeyColesDeliverMoreRange)
            .WithIdentity("ColesDeliverMoreRangeDomJob-trigger")
            .WithCronSchedule("0 40 9 ? * WED", x => x.InTimeZone(localTz)));

        // delete data (quarterly, first Tuesday 06:00)
        var cleanJobKey = new JobKey("CleanPriceHistoryJob");
        q.AddJob<CleanPriceHistoryJob>(opts => opts.WithIdentity(cleanJobKey));
        q.AddTrigger(opts => opts
            .ForJob(cleanJobKey)
            .WithIdentity("CleanPriceHistoryJob-trigger")
            .WithCronSchedule("0 0 6 ? 1/3 TUE#1", x => x.InTimeZone(localTz)));

        // favorite price tracking
        var favoriteTrackJobKey = new JobKey("FavoritePriceTrackingJob");
        q.AddJob<FavoritePriceTrackingJob>(opts => opts.WithIdentity(favoriteTrackJobKey));
        q.AddTrigger(opts => opts
            .ForJob(favoriteTrackJobKey)
            .WithIdentity("FavoritePriceTrackingJob-trigger")
        // testing
        // .WithCronSchedule("0 */1 * ? * *"));
        .WithCronSchedule("0 50 12 ? * WED", x => x.InTimeZone(localTz)));
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
builder.Services.AddHttpClient<IColesDownDomScraperService, ColesDownDomScraperService>();
builder.Services.AddHttpClient<IColesMeatSeafoodDomScraperService, ColesMeatSeafoodDomScraperService>();
builder.Services.AddHttpClient<IColesFruitVegetablesDomScraperService, ColesFruitVegetablesDomScraperService>();
builder.Services.AddHttpClient<IColesDairyEggsFridgeDomScraperService, ColesDairyEggsFridgeDomScraperService>();
builder.Services.AddHttpClient<IColesBakeryDomScraperService, ColesBakeryDomScraperService>();
builder.Services.AddHttpClient<IColesDeliDomScraperService, ColesDeliDomScraperService>();
builder.Services.AddHttpClient<IColesPantryDomScraperService, ColesPantryDomScraperService>();
builder.Services.AddHttpClient<IColesDietaryWorldFoodsDomScraperService, ColesDietaryWorldFoodsDomScraperService>();
builder.Services.AddHttpClient<IColesChipsChocolatesSnacksDomScraperService, ColesChipsChocolatesSnacksDomScraperService>();
builder.Services.AddHttpClient<IColesDrinksDomScraperService, ColesDrinksDomScraperService>();
builder.Services.AddHttpClient<IColesLiquorlandDomScraperService, ColesLiquorlandDomScraperService>();
builder.Services.AddHttpClient<IColesFrozenDomScraperService, ColesFrozenDomScraperService>();
builder.Services.AddHttpClient<IColesCleaningLaundryDomScraperService, ColesCleaningLaundryDomScraperService>();
builder.Services.AddHttpClient<IColesHealthBeautyDomScraperService, ColesHealthBeautyDomScraperService>();
builder.Services.AddHttpClient<IColesBabyDomScraperService, ColesBabyDomScraperService>();
builder.Services.AddHttpClient<IColesPetDomScraperService, ColesPetDomScraperService>();
builder.Services.AddHttpClient<IColesHomeGardenDomScraperService, ColesHomeGardenDomScraperService>();
builder.Services.AddHttpClient<IColesBigPackValueDomScraperService, ColesBigPackValueDomScraperService>();
builder.Services.AddHttpClient<IColesBonusCreditProductsDomScraperService, ColesBonusCreditProductsDomScraperService>();
builder.Services.AddHttpClient<IColesDeliverMoreRangeDomScraperService, ColesDeliverMoreRangeDomScraperService>();

builder.Services.AddScoped<IWoolworthsSpecialScraperService, WoolworthsSpecialScraperService>();
builder.Services.AddScoped<IWoolworthsLowerShelfDomScraperService, WoolworthsLowerShelfDomScraperService>();
builder.Services.AddScoped<IWoolworthsEverydayLowPriceDomScraperService, WoolworthsEverydayLowPriceDomScraperService>();
builder.Services.AddScoped<IWoolworthsHalfPriceDomScraperService, WoolworthsHalfPriceDomScraperService>();
builder.Services.AddScoped<IWoolworthsBuyMoreSaveMoreDomScraperService, WoolworthsBuyMoreSaveMoreDomScraperService>();
builder.Services.AddScoped<IWoolworthsSummerPriceDomScraperService, WoolworthsSummerPriceDomScraperService>();
builder.Services.AddScoped<IWoolworthsAutumnPriceDomScraperService, WoolworthsAutumnPriceDomScraperService>();

builder.Services.AddScoped<ICategoryMappingService, CategoryMappingService>();

builder.Services.AddScoped<IIngestionService, IngestionService>();
builder.Services.AddScoped<IScrapeExportService, ScrapeExportService>();

// Products service
builder.Services.AddScoped<PriceCompareCore.Interfaces.IProductService, PriceCompareCore.Services.ProductService>();

// Receipt service
builder.Services.AddScoped<IReceiptService, ReceiptService>();

// Favorite service
builder.Services.AddScoped<IFavoriteService, FavoriteService>();

// Admin schedule aggregation (AWS + Quartz).
builder.Services.AddScoped<AdminScheduleService>();
builder.Services.AddScoped<IScrapeImportSqlService, ScrapeImportSqlService>();

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
