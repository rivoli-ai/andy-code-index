using Andy.Auth.Extensions;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Handlers;
using Andy.CodeIndex.Infrastructure.Repositories;
using Andy.CodeIndex.Infrastructure.Services;
using Andy.Rbac.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// --- Server URLs for MCP metadata ---
var serverUrl = builder.Configuration["Urls"]?.Split(';').FirstOrDefault()
    ?? (builder.Environment.IsDevelopment() ? "https://localhost:5101" : "https://localhost:5101");
var protectedResourceUrl = $"{serverUrl}/mcp";
var andyAuthAuthority = builder.Configuration["AndyAuth:Authority"] ?? "";

// --- Database ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<CodeIndexDbContext>(options =>
        options.UseNpgsql(connectionString, o => o.UseVector()));
}

// --- Authentication (Andy.Auth) ---
if (!string.IsNullOrEmpty(andyAuthAuthority))
{
    builder.Services.AddAndyAuth(builder.Configuration);

    // Post-configure JWT bearer to accept MCP resource URLs as valid audiences (RFC 8707)
    builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var existingAudiences = options.TokenValidationParameters.ValidAudiences?.ToList() ?? [];
        if (!string.IsNullOrEmpty(options.TokenValidationParameters.ValidAudience) &&
            !existingAudiences.Contains(options.TokenValidationParameters.ValidAudience))
        {
            existingAudiences.Add(options.TokenValidationParameters.ValidAudience);
        }
        existingAudiences.Add(protectedResourceUrl);
        options.TokenValidationParameters.ValidAudiences = existingAudiences;
        options.TokenValidationParameters.ValidAudience = null;
    });

    // MCP OAuth Protected Resource Metadata (RFC 8707)
    builder.Services.AddAuthentication()
        .AddMcp(mcpOptions =>
        {
            mcpOptions.ResourceMetadataUri = new Uri($"{serverUrl}/mcp/.well-known/oauth-protected-resource");
            mcpOptions.ResourceMetadata = new()
            {
                Resource = new Uri(protectedResourceUrl),
                ResourceDocumentation = new Uri("https://github.com/rivoli-ai/andy-code-index"),
                AuthorizationServers = { new Uri(andyAuthAuthority) },
                ScopesSupported = ["openid", "profile", "email"],
            };
        });
}
else
{
    // Dev fallback: no auth enforcement for local development
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();
    });
}

// --- RBAC (Andy.Rbac.Client) ---
var rbacBaseUrl = builder.Configuration["Rbac:ApiBaseUrl"];
if (!string.IsNullOrEmpty(rbacBaseUrl))
{
    builder.Services.AddRbacClient(options =>
    {
        options.ApiBaseUrl = rbacBaseUrl;
        options.ApplicationCode = "code-index";
    });
}

// --- Repositories ---
builder.Services.AddScoped<ICodeRepositoryRepository, CodeRepositoryRepository>();
builder.Services.AddScoped<ICommitRepository, CommitRepository>();
builder.Services.AddScoped<IEnrichmentRepository, EnrichmentRepository>();
builder.Services.AddScoped<IIndexingTaskRepository, IndexingTaskRepository>();

// --- Services ---
builder.Services.AddDataProtection();
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IApiKeyResolver, ApiKeyResolver>();
builder.Services.AddScoped<IRepositoryService, RepositoryService>();
builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddScoped<IChunkingService, ChunkingService>();
builder.Services.AddScoped<ITaskQueue, TaskQueueService>();
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
builder.Services.AddScoped<IEnrichmentGeneratorService, EnrichmentGeneratorService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddSingleton<RankFusionService>();
builder.Services.AddHttpClient("EmbeddingService");
builder.Services.AddScoped<IEmbeddingProvider>(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("EmbeddingService");
    return new Andy.CodeIndex.Infrastructure.Services.OpenAiEmbeddingProvider(
        httpClient,
        sp.GetRequiredService<IOptions<EmbeddingOptions>>(),
        sp.GetRequiredService<IApiKeyResolver>(),
        sp.GetRequiredService<ILogger<Andy.CodeIndex.Infrastructure.Services.OpenAiEmbeddingProvider>>());
});
builder.Services.AddHttpClient("Discovery");
builder.Services.AddHttpClient("Chat");
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IRepoDiscoveryService, Andy.CodeIndex.Infrastructure.Discovery.RepoDiscoveryService>();
builder.Services.AddHostedService<Andy.CodeIndex.Infrastructure.Discovery.SeedService>();

// --- Task handlers ---
builder.Services.AddScoped<ITaskHandler, CloneRepositoryHandler>();
builder.Services.AddScoped<ITaskHandler, SyncRepositoryHandler>();
builder.Services.AddScoped<ITaskHandler, ScanCommitHandler>();
builder.Services.AddScoped<ITaskHandler, ExtractSnippetsHandler>();
builder.Services.AddScoped<ITaskHandler, CreateBM25IndexHandler>();
builder.Services.AddScoped<ITaskHandler, CreateCodeEmbeddingsHandler>();
builder.Services.AddScoped<ITaskHandler, CreateApiDocsHandler>();
builder.Services.AddScoped<ITaskHandler, CreateArchitectureDocsHandler>();
builder.Services.AddScoped<ITaskHandler, CreateDatabaseSchemaHandler>();
builder.Services.AddScoped<ITaskHandler, CreateCommitDescriptionHandler>();
builder.Services.AddScoped<ITaskHandler, CreateCookbookHandler>();
builder.Services.AddScoped<ITaskHandler, CreateWikiHandler>();
builder.Services.AddScoped<ITaskHandler, CreateSummaryEnrichmentsHandler>();
builder.Services.AddScoped<ITaskHandler, CreateSummaryEmbeddingsHandler>();
builder.Services.AddScoped<ITaskHandler, ExtractDependenciesHandler>();
builder.Services.AddScoped<ITaskHandler, ExtractCommitHistoryHandler>();
builder.Services.AddScoped<IDependencyParserService, Andy.CodeIndex.Infrastructure.Parsers.DependencyParserService>();

// --- Background services ---
builder.Services.AddHostedService<BackgroundWorkerService>();
builder.Services.AddHostedService<PeriodicSyncService>();

// --- OpenTelemetry ---
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(Andy.CodeIndex.Infrastructure.Telemetry.CodeIndexTelemetry.ServiceName))
    .WithMetrics(metrics => metrics
        .AddMeter(Andy.CodeIndex.Infrastructure.Telemetry.CodeIndexTelemetry.ServiceName));

// --- Options ---
builder.Services.Configure<IndexingOptions>(builder.Configuration.GetSection("Indexing"));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("Sync"));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection(EmbeddingOptions.SectionName));
builder.Services.Configure<EnrichmentLlmOptions>(builder.Configuration.GetSection(EnrichmentLlmOptions.SectionName));
builder.Services.Configure<DiscoveryOptions>(builder.Configuration.GetSection(DiscoveryOptions.SectionName));

// --- MCP Server ---
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// --- Swagger ---
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Andy.CodeIndex API",
        Version = "v1",
        Description = "Semantic code indexing service for the Andy ecosystem"
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme."
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// --- CORS ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200",
                "http://localhost:4201",
                "https://localhost:4201")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });

    options.AddPolicy("AllowMcpClients", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// --- Middleware ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngularApp");
app.UseAuthentication();
app.UseAuthorization();

// --- MCP auth debugging middleware ---
if (app.Environment.IsDevelopment() && !string.IsNullOrEmpty(andyAuthAuthority))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/mcp"))
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            var hasAuth = context.Request.Headers.ContainsKey("Authorization");
            var authScheme = hasAuth ? context.Request.Headers.Authorization.ToString().Split(' ').FirstOrDefault() : null;
            var isAuthenticated = context.User?.Identity?.IsAuthenticated ?? false;
            var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? context.User?.FindFirst("sub")?.Value;

            logger.LogInformation(
                "MCP Request: Method={Method} Path={Path} HasAuth={HasAuth} Scheme={Scheme} Authenticated={IsAuth} UserId={UserId}",
                context.Request.Method, context.Request.Path, hasAuth, authScheme, isAuthenticated, userId);
        }
        await next();
    });
}

app.MapControllers();

// --- MCP endpoint ---
app.MapMcp("/mcp")
    .RequireCors("AllowMcpClients")
    .RequireAuthorization();

// --- OAuth Protected Resource Metadata & Proxy Endpoints (RFC 8707) ---
if (!string.IsNullOrEmpty(andyAuthAuthority))
{
    var oauthMetadataJsonOptions = new System.Text.Json.JsonSerializerOptions
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Protected resource metadata
    app.MapGet("/.well-known/oauth-protected-resource", (IServiceProvider sp) =>
    {
        var optionsMonitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
            ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationOptions>>();
        var options = optionsMonitor.Get(
            ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme);
        return Results.Json(options.ResourceMetadata, oauthMetadataJsonOptions);
    }).AllowAnonymous().RequireCors("AllowMcpClients");

    app.MapGet("/mcp/.well-known/oauth-protected-resource", (IServiceProvider sp) =>
    {
        var optionsMonitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
            ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationOptions>>();
        var options = optionsMonitor.Get(
            ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme);
        return Results.Json(options.ResourceMetadata, oauthMetadataJsonOptions);
    }).AllowAnonymous().RequireCors("AllowMcpClients");

    // Some clients mistakenly append /mcp to well-known paths
    app.MapGet("/.well-known/oauth-protected-resource/mcp", (IServiceProvider sp) =>
    {
        var optionsMonitor = sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<
            ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationOptions>>();
        var options = optionsMonitor.Get(
            ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme);
        return Results.Json(options.ResourceMetadata, oauthMetadataJsonOptions);
    }).AllowAnonymous().RequireCors("AllowMcpClients");

    // OpenID Configuration -- redirect to Andy.Auth
    app.MapGet("/.well-known/openid-configuration", () =>
        Results.Redirect($"{andyAuthAuthority}/.well-known/openid-configuration", permanent: false))
        .AllowAnonymous().RequireCors("AllowMcpClients");

    app.MapGet("/.well-known/oauth-authorization-server", () =>
        Results.Redirect($"{andyAuthAuthority}/.well-known/openid-configuration", permanent: false))
        .AllowAnonymous().RequireCors("AllowMcpClients");

    app.MapGet("/.well-known/openid-configuration/mcp", () =>
        Results.Redirect($"{andyAuthAuthority}/.well-known/openid-configuration", permanent: false))
        .AllowAnonymous().RequireCors("AllowMcpClients");

    app.MapGet("/.well-known/oauth-authorization-server/mcp", () =>
        Results.Redirect($"{andyAuthAuthority}/.well-known/openid-configuration", permanent: false))
        .AllowAnonymous().RequireCors("AllowMcpClients");

    // Proxy authorization endpoint -- redirect to Andy.Auth
    app.MapGet("/authorize", (HttpContext ctx) =>
    {
        var qs = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : string.Empty;
        return Results.Redirect($"{andyAuthAuthority}/connect/authorize{qs}", permanent: false);
    }).AllowAnonymous().RequireCors("AllowMcpClients");

    // Proxy token endpoint -- 307 redirect preserves POST body
    app.MapPost("/token", (HttpContext ctx) =>
    {
        var qs = ctx.Request.QueryString.HasValue ? ctx.Request.QueryString.Value : string.Empty;
        ctx.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
        ctx.Response.Headers.Location = $"{andyAuthAuthority}/connect/token{qs}";
        return Task.CompletedTask;
    }).AllowAnonymous().RequireCors("AllowMcpClients");
}

// --- Health check ---
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

// --- Auto-migrate in development ---
if (app.Environment.IsDevelopment() && !string.IsNullOrEmpty(connectionString))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CodeIndexDbContext>();
    if (db.Database.IsNpgsql())
        await db.Database.MigrateAsync();
}

app.Run();

// Make Program accessible for WebApplicationFactory in integration tests
public partial class Program { }
