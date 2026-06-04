namespace Andy.CodeIndex.Application.Options;

public class IndexingOptions
{
    public string DataDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".andy-code-index");

    public int WorkerCount { get; set; } = 1;
    public int SearchLimit { get; set; } = 10;

    /// <summary>
    /// Number of minutes after which a Running task with no heartbeat update
    /// is considered hung and is transitioned to <c>TimedOut</c> by the backend
    /// watchdog (§7.4 timeout backstop). The client MUST NOT use its own
    /// wall-clock timer to infer timeouts; it reads the <c>status</c> field from
    /// GET /api/v1/queue/{id} and observes the backend-emitted <c>TimedOut</c>.
    /// Default: 30 minutes.
    /// </summary>
    public int HeartbeatTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// How often the watchdog scans for timed-out tasks, in minutes.
    /// Default: 5 minutes.
    /// </summary>
    public int WatchdogIntervalMinutes { get; set; } = 5;
}
