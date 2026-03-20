using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Data;
using Andy.CodeIndex.Infrastructure.Handlers;
using Andy.CodeIndex.Infrastructure.Repositories;
using Andy.CodeIndex.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<CodeIndexDbContext>(options =>
        options.UseNpgsql(connectionString, o => o.UseVector()));
}

// Repositories
builder.Services.AddScoped<ICodeRepositoryRepository, CodeRepositoryRepository>();
builder.Services.AddScoped<ICommitRepository, CommitRepository>();
builder.Services.AddScoped<IEnrichmentRepository, EnrichmentRepository>();
builder.Services.AddScoped<IIndexingTaskRepository, IndexingTaskRepository>();

// Services
builder.Services.AddScoped<IRepositoryService, RepositoryService>();
builder.Services.AddSingleton<IGitService, GitService>();
builder.Services.AddScoped<IChunkingService, ChunkingService>();
builder.Services.AddScoped<ITaskQueue, TaskQueueService>();
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<ICodeAnalysisService, CodeAnalysisService>();
builder.Services.AddScoped<IEnrichmentGeneratorService, EnrichmentGeneratorService>();
builder.Services.AddScoped<ISearchService, SearchService>();
builder.Services.AddSingleton<RankFusionService>();
builder.Services.AddHttpClient<IEmbeddingProvider, OpenAiEmbeddingProvider>();

// Task handlers
builder.Services.AddScoped<ITaskHandler, CloneRepositoryHandler>();
builder.Services.AddScoped<ITaskHandler, ScanCommitHandler>();
builder.Services.AddScoped<ITaskHandler, ExtractSnippetsHandler>();
builder.Services.AddScoped<ITaskHandler, CreateApiDocsHandler>();

// Background services
builder.Services.AddHostedService<BackgroundWorkerService>();
builder.Services.AddHostedService<PeriodicSyncService>();

// Options
builder.Services.Configure<IndexingOptions>(builder.Configuration.GetSection("Indexing"));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("Sync"));
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));

// MCP Server
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// Swagger
builder.Services.AddControllers();
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

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "https://localhost:4200")
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

// Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngularApp");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// MCP endpoint
app.MapMcp("/mcp")
    .RequireCors("AllowMcpClients");

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

// Auto-migrate in development
if (app.Environment.IsDevelopment() && !string.IsNullOrEmpty(connectionString))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CodeIndexDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

// Make Program accessible for WebApplicationFactory in integration tests
public partial class Program { }
