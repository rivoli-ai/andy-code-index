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

    private const string CreateInsertTriggerSql =
        $"""
        CREATE TRIGGER IF NOT EXISTS "{FtsTable}_after_insert"
        AFTER INSERT ON "Enrichments"
        BEGIN
            INSERT INTO "{FtsTable}" (EnrichmentId, Content)
            VALUES (NEW."Id", NEW."Content");
        END;
        """;

    private const string CreateUpdateTriggerSql =
        $"""
        CREATE TRIGGER IF NOT EXISTS "{FtsTable}_after_update"
        AFTER UPDATE OF "Content" ON "Enrichments"
        BEGIN
            DELETE FROM "{FtsTable}" WHERE EnrichmentId = OLD."Id";
            INSERT INTO "{FtsTable}" (EnrichmentId, Content)
            VALUES (NEW."Id", NEW."Content");
        END;
        """;

    private const string CreateDeleteTriggerSql =
        $"""
        CREATE TRIGGER IF NOT EXISTS "{FtsTable}_after_delete"
        AFTER DELETE ON "Enrichments"
        BEGIN
            DELETE FROM "{FtsTable}" WHERE EnrichmentId = OLD."Id";
        END;
        """;

    private const string ReconcileFtsSql =
        $"""
        DELETE FROM "{FtsTable}"
        WHERE NOT EXISTS (
            SELECT 1 FROM "Enrichments" e
            WHERE lower(CAST(e."Id" AS TEXT)) = lower("{FtsTable}".EnrichmentId)
              AND e."Content" = "{FtsTable}".Content
        );

        INSERT INTO "{FtsTable}" (EnrichmentId, Content)
        SELECT e."Id", e."Content"
        FROM "Enrichments" e
        WHERE NOT EXISTS (
            SELECT 1 FROM "{FtsTable}" f
            WHERE lower(f.EnrichmentId) = lower(CAST(e."Id" AS TEXT))
        );
        """;

    public static async Task InitializeAsync(CodeIndexDbContext context, CancellationToken ct = default)
    {
        await context.Database.EnsureCreatedAsync(ct);
        await context.Database.ExecuteSqlRawAsync(CreateFtsSql, ct);
        await context.Database.ExecuteSqlRawAsync(CreateInsertTriggerSql, ct);
        await context.Database.ExecuteSqlRawAsync(CreateUpdateTriggerSql, ct);
        await context.Database.ExecuteSqlRawAsync(CreateDeleteTriggerSql, ct);
        await context.Database.ExecuteSqlRawAsync(ReconcileFtsSql, ct);
    }

    public static void Initialize(CodeIndexDbContext context)
    {
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw(CreateFtsSql);
        context.Database.ExecuteSqlRaw(CreateInsertTriggerSql);
        context.Database.ExecuteSqlRaw(CreateUpdateTriggerSql);
        context.Database.ExecuteSqlRaw(CreateDeleteTriggerSql);
        context.Database.ExecuteSqlRaw(ReconcileFtsSql);
    }
}
