using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Sitim.Api.Options;
using Sitim.Api.Security;
using Sitim.Api.Services;
using Sitim.Core.Options;
using Sitim.Core.Services;
using Sitim.Infrastructure.Data;
using Sitim.Infrastructure.Identity;
using Sitim.Infrastructure.Orthanc;
using Sitim.Infrastructure.Services;
using System.Text;

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

// CORS — allow Blazor UI (Sitim.Web) to call this API
builder.Services.AddCors(options =>
{
    options.AddPolicy("SitimWeb", policy =>
    {
        policy.WithOrigins(
                "https://localhost:5001",
                "http://localhost:5000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Controllers
builder.Services.AddControllers();

// Swagger (better for rapid manual testing than the minimal OpenAPI endpoint)

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SITIM API", Version = "v1" });

    const string schemeId = "Bearer";

    c.AddSecurityDefinition(schemeId, new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer", // lowercase per RFC 7235
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });

    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(schemeId, document)] = new List<string>()
    });
});

// Options
builder.Services.Configure<OrthancOptions>(builder.Configuration.GetSection("Orthanc"));
builder.Services.Configure<OhifOptions>(builder.Configuration.GetSection("Ohif"));

// Auth (JWT)
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

// Local storage
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

// Database (PostgreSQL)
var dbConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(dbConnectionString));
builder.Services.AddIdentityCore<ApplicationUser>(opt =>
{
    // Keep dev password policy reasonable; tighten in production.
    opt.Password.RequiredLength = 8;
    opt.Password.RequireDigit = false;
    opt.Password.RequireNonAlphanumeric = false;
    opt.Password.RequireUppercase = false;
    opt.Password.RequireLowercase = false;
})
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<ITokenService, TokenService>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

// Hangfire (durable background jobs)
builder.Services.AddHangfire(cfg =>
{
    cfg.SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
       .UseSimpleAssemblyNameTypeSerializer()
       .UseRecommendedSerializerSettings()
       .UsePostgreSqlStorage(
           options =>
           {
               options.UseNpgsqlConnection(dbConnectionString);
           },
           new PostgreSqlStorageOptions
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

// Seed roles + dev admin user (idempotent)
await IdentitySeeder.SeedAsync(app.Services, app.Configuration, CancellationToken.None);
// DB schema is managed via EF Core migrations.
//   dotnet ef database update -p Sitim.Infrastructure -s Sitim.Api

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("SitimWeb");

app.UseAuthentication();

app.UseAuthorization();
if (app.Environment.IsDevelopment())
{
    // Hangfire dashboard (DEV)
    app.UseHangfireDashboard("/hangfire", new Hangfire.DashboardOptions
    {
        Authorization = new[] { new HangfireDashboardAuthFilter() }
    });
}


app.MapControllers();

await app.RunAsync();
