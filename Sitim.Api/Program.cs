using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Sitim.Api.Options;
using Sitim.Api.Services;
using Sitim.Core.Options;
using Sitim.Core.Services;
using Sitim.Infrastructure.Data;
using Sitim.Infrastructure.Orthanc;
using Sitim.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Large uploads (dev/MVP). Adjust down in production.
// This controls the maximum size for multipart/form-data (file uploads) and request body size.
const long MaxUploadBytes = 1024L * 1024 * 1024; // 1 GB

builder.Services.Configure<FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = MaxUploadBytes;
    o.ValueLengthLimit = int.MaxValue;
    o.MultipartHeadersLengthLimit = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = MaxUploadBytes;
});

// Controllers
builder.Services.AddControllers();

// Swagger (better for rapid manual testing than the minimal OpenAPI endpoint)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Options
builder.Services.Configure<OrthancOptions>(builder.Configuration.GetSection("Orthanc"));
builder.Services.Configure<OhifOptions>(builder.Configuration.GetSection("Ohif"));

// Local storage
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

// Database (PostgreSQL)
var dbConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(dbConnectionString));
// Hangfire (durable background jobs)
builder.Services.AddHangfire(cfg =>
{
    cfg.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
       .UseSimpleAssemblyNameTypeSerializer()
       .UseRecommendedSerializerSettings()
       .UsePostgreSqlStorage(dbConnectionString, new PostgreSqlStorageOptions
       {
           SchemaName = "hangfire",
           PrepareSchemaIfNecessary = true
       });
});

builder.Services.AddHangfireServer();

// Background scheduling / analysis runner
builder.Services.AddScoped<IAnalysisService, AnalysisService>();
builder.Services.AddScoped<IAnalysisJobScheduler, AnalysisJobScheduler>();

// Orthanc REST client
builder.Services.AddHttpClient<IOrthancClient, OrthancClient>((sp, client) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OrthancOptions>>().Value;
    client.BaseAddress = new Uri(opt.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Local cache (PostgreSQL) for Orthanc studies
builder.Services.AddScoped<IStudyCacheService, StudyCacheService>();

var app = builder.Build();
// DB schema is managed via EF Core migrations.
//   dotnet ef database update -p Sitim.Infrastructure -s Sitim.Api

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Hangfire dashboard (DEV only)
    app.UseHangfireDashboard("/hangfire");
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
