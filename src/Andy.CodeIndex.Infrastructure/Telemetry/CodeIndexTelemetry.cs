using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Andy.CodeIndex.Infrastructure.Telemetry;

public static class CodeIndexTelemetry
{
    public const string ServiceName = "Andy.CodeIndex";

    public static readonly ActivitySource ActivitySource = new(ServiceName);
    public static readonly Meter Meter = new(ServiceName);

    // Counters
    public static readonly Counter<long> TasksCompleted = Meter.CreateCounter<long>(
        "code_index.tasks.completed", "tasks", "Total completed indexing tasks");
    public static readonly Counter<long> TasksFailed = Meter.CreateCounter<long>(
        "code_index.tasks.failed", "tasks", "Total failed indexing tasks");
    public static readonly Counter<long> SearchRequests = Meter.CreateCounter<long>(
        "code_index.search.requests", "requests", "Total search requests");
    public static readonly Counter<long> SnippetsAdded = Meter.CreateCounter<long>(
        "code_index.snippets.added", "snippets", "Snippets added during indexing");
    public static readonly Counter<long> SnippetsUpdated = Meter.CreateCounter<long>(
        "code_index.snippets.updated", "snippets", "Snippets updated during indexing");
    public static readonly Counter<long> SnippetsDeleted = Meter.CreateCounter<long>(
        "code_index.snippets.deleted", "snippets", "Snippets deleted during indexing");
    public static readonly Counter<long> SnippetsUnchanged = Meter.CreateCounter<long>(
        "code_index.snippets.unchanged", "snippets", "Snippets unchanged during indexing");

    // Histograms
    public static readonly Histogram<double> TaskDuration = Meter.CreateHistogram<double>(
        "code_index.tasks.duration", "seconds", "Task execution duration");
    public static readonly Histogram<double> SearchDuration = Meter.CreateHistogram<double>(
        "code_index.search.duration", "seconds", "Search execution duration");
}
