using Microsoft.EntityFrameworkCore;

namespace Andy.CodeIndex.Infrastructure.Data;

/// <summary>
/// Creates the schema for an embedded SQLite code index plus the FTS5 virtual
/// table used for BM25 keyword search. Idempotent and safe to call on every
/// startup of the API or an embedded host (e.g. andy-cli's <c>.andy/</c> store).
/// </summary>
public static class SqliteDatabaseInitializer
{
    /// <summary>Name of the FTS5 virtual table mirroring enrichment content.</summary>
    public const string FtsTable = "EnrichmentFts";

    private const string CreateFtsSql =
        $"CREATE VIRTUAL TABLE IF NOT EXISTS \"{FtsTable}\" USING fts5(EnrichmentId UNINDEXED, Content);";

    public static async Task InitializeAsync(CodeIndexDbContext context, CancellationToken ct = default)
    {
        await context.Database.EnsureCreatedAsync(ct);
        await context.Database.ExecuteSqlRawAsync(CreateFtsSql, ct);
    }

    public static void Initialize(CodeIndexDbContext context)
    {
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw(CreateFtsSql);
    }
}
