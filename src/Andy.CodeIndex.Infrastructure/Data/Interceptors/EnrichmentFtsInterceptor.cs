using System.Runtime.CompilerServices;
using Andy.CodeIndex.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Andy.CodeIndex.Infrastructure.Data.Interceptors;

/// <summary>
/// Keeps the SQLite FTS5 table (<see cref="SqliteDatabaseInitializer.FtsTable"/>)
/// in sync with <see cref="Enrichment"/> rows so keyword search has BM25-ranked
/// content. Registered only for the SQLite provider.
///
/// Pending changes are captured from the change tracker before save and applied
/// to the (independent, non-modelled) FTS table after save. State is kept per
/// <see cref="DbContext"/> via a weak table so a single shared interceptor
/// instance is safe across contexts.
/// </summary>
public sealed class EnrichmentFtsInterceptor : SaveChangesInterceptor
{
    private sealed class Pending
    {
        public List<(Guid Id, string Content)> Upserts { get; } = new();
        public List<Guid> Deletes { get; } = new();
    }

    private readonly ConditionalWeakTable<DbContext, Pending> _pending = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Apply(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        await ApplyAsync(eventData.Context, ct);
        return await base.SavedChangesAsync(eventData, result, ct);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
            return;

        var pending = new Pending();
        foreach (var entry in context.ChangeTracker.Entries<Enrichment>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                case EntityState.Modified:
                    pending.Upserts.Add((entry.Entity.Id, entry.Entity.Content));
                    break;
                case EntityState.Deleted:
                    pending.Deletes.Add(entry.Entity.Id);
                    break;
            }
        }

        if (pending.Upserts.Count > 0 || pending.Deletes.Count > 0)
            _pending.AddOrUpdate(context, pending);
    }

    private static string Delete => $"DELETE FROM \"{SqliteDatabaseInitializer.FtsTable}\" WHERE EnrichmentId = {{0}};";
    private static string Insert => $"INSERT INTO \"{SqliteDatabaseInitializer.FtsTable}\" (EnrichmentId, Content) VALUES ({{0}}, {{1}});";

    private void Apply(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending))
            return;
        _pending.Remove(context);

        foreach (var id in pending.Deletes)
            context.Database.ExecuteSqlRaw(Delete, id.ToString());

        foreach (var (id, content) in pending.Upserts)
        {
            // FTS5 has no UPSERT; delete-then-insert keeps a single row per id.
            context.Database.ExecuteSqlRaw(Delete, id.ToString());
            context.Database.ExecuteSqlRaw(Insert, id.ToString(), content);
        }
    }

    private async Task ApplyAsync(DbContext? context, CancellationToken ct)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending))
            return;
        _pending.Remove(context);

        foreach (var id in pending.Deletes)
            await context.Database.ExecuteSqlRawAsync(Delete, new object[] { id.ToString() }, ct);

        foreach (var (id, content) in pending.Upserts)
        {
            await context.Database.ExecuteSqlRawAsync(Delete, new object[] { id.ToString() }, ct);
            await context.Database.ExecuteSqlRawAsync(Insert, new object[] { id.ToString(), content }, ct);
        }
    }
}
