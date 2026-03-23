using System.CommandLine;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
var baseUrl = new Option<string>("--base-url", () => "https://localhost:5101", "API base URL");
var apiKey = new Option<string?>("--token", "Bearer token for authentication");
var formatOpt = new Option<string>("--format", () => "table", "Output format: table or json");

var rootCommand = new RootCommand("Andy CodeIndex CLI - semantic code indexing for the Andy ecosystem");
rootCommand.AddGlobalOption(baseUrl);
rootCommand.AddGlobalOption(apiKey);
rootCommand.AddGlobalOption(formatOpt);

// --- repo commands ---
var repoCommand = new Command("repo", "Manage repositories");

var repoList = new Command("list", "List all tracked repositories");
repoList.SetHandler(async (string url, string? token, string format) =>
{
    var client = CreateClient(url, token);
    var repos = await client.GetFromJsonAsync<JsonElement[]>($"{url}/api/v1/repositories", jsonOptions);
    if (repos is null) { AnsiConsole.MarkupLine("[red]Failed to fetch repositories[/]"); return; }

    if (format == "json") { Console.WriteLine(JsonSerializer.Serialize(repos, new JsonSerializerOptions { WriteIndented = true })); return; }

    var table = new Table().Border(TableBorder.Rounded);
    table.AddColumns("Name", "Provider", "Status", "Default Branch", "Last Synced");
    foreach (var r in repos)
    {
        var status = r.GetProperty("status").GetString() ?? "";
        var statusColor = status switch { "indexed" => "green", "cloning" or "indexing" => "blue", "error" => "red", _ => "grey" };
        table.AddRow(
            r.GetProperty("name").GetString() ?? "",
            r.GetProperty("provider").ToString(),
            $"[{statusColor}]{status}[/]",
            r.GetProperty("defaultBranch").ValueKind == JsonValueKind.Null ? "-" : r.GetProperty("defaultBranch").GetString()!,
            r.GetProperty("lastSyncedAt").ValueKind == JsonValueKind.Null ? "Never" : r.GetProperty("lastSyncedAt").GetString()![..16]
        );
    }
    AnsiConsole.Write(table);
    AnsiConsole.MarkupLine($"[grey]{repos.Length} repositories[/]");
}, baseUrl, apiKey, formatOpt);

var repoAddUrl = new Argument<string>("url", "Repository URL to add");
var repoAddPat = new Option<string?>("--pat", "Personal access token for private repos");
var repoAdd = new Command("add", "Add a repository for indexing") { repoAddUrl, repoAddPat };
repoAdd.SetHandler(async (string url, string? token, string repoUrl, string? pat) =>
{
    var client = CreateClient(url, token);
    var body = pat is not null ? new { url = repoUrl, personalAccessToken = pat } : (object)new { url = repoUrl };
    var response = await client.PostAsJsonAsync($"{url}/api/v1/repositories", body);

    if (response.IsSuccessStatusCode)
    {
        var repo = await response.Content.ReadFromJsonAsync<JsonElement>(jsonOptions);
        AnsiConsole.MarkupLine($"[green]Added[/] {repo.GetProperty("name").GetString()} (status: {repo.GetProperty("status").GetString()})");
    }
    else
    {
        var error = await response.Content.ReadAsStringAsync();
        AnsiConsole.MarkupLine($"[red]Error {(int)response.StatusCode}[/]: {error}");
    }
}, baseUrl, apiKey, repoAddUrl, repoAddPat);

var repoRemoveId = new Argument<string>("id", "Repository ID to remove");
var repoRemoveForce = new Option<bool>("--force", "Skip confirmation");
var repoRemove = new Command("remove", "Remove a repository") { repoRemoveId, repoRemoveForce };
repoRemove.SetHandler(async (string url, string? token, string id, bool force) =>
{
    if (!force && !AnsiConsole.Confirm($"Delete repository {id}?", false)) return;

    var client = CreateClient(url, token);
    var response = await client.DeleteAsync($"{url}/api/v1/repositories/{id}");
    AnsiConsole.MarkupLine(response.IsSuccessStatusCode ? "[green]Deleted[/]" : $"[red]Error {(int)response.StatusCode}[/]");
}, baseUrl, apiKey, repoRemoveId, repoRemoveForce);

var repoSyncId = new Option<string?>("--id", "Repository ID (syncs all if omitted)");
var repoSync = new Command("sync", "Trigger repository sync") { repoSyncId };
repoSync.SetHandler(async (string url, string? token, string? id) =>
{
    var client = CreateClient(url, token);
    if (id is not null)
    {
        var response = await client.PostAsync($"{url}/api/v1/repositories/{id}/sync", null);
        AnsiConsole.MarkupLine(response.IsSuccessStatusCode ? "[green]Sync queued[/]" : $"[red]Error {(int)response.StatusCode}[/]");
    }
    else
    {
        var repos = await client.GetFromJsonAsync<JsonElement[]>($"{url}/api/v1/repositories", jsonOptions);
        if (repos is null) return;
        foreach (var r in repos)
        {
            var repoId = r.GetProperty("id").GetString()!;
            var name = r.GetProperty("name").GetString();
            var response = await client.PostAsync($"{url}/api/v1/repositories/{repoId}/sync", null);
            AnsiConsole.MarkupLine(response.IsSuccessStatusCode ? $"[green]Synced[/] {name}" : $"[red]Failed[/] {name}");
        }
    }
}, baseUrl, apiKey, repoSyncId);

var repoStatusId = new Option<string?>("--id", "Repository ID");
var repoStatus = new Command("status", "Show repository indexing status") { repoStatusId };
repoStatus.SetHandler(async (string url, string? token, string? id) =>
{
    var client = CreateClient(url, token);
    if (id is not null)
    {
        var repo = await client.GetFromJsonAsync<JsonElement>($"{url}/api/v1/repositories/{id}", jsonOptions);
        if (repo.ValueKind == JsonValueKind.Undefined) { AnsiConsole.MarkupLine("[red]Not found[/]"); return; }
        PrintRepoDetail(repo);
    }
    else
    {
        var syncStatus = await client.GetFromJsonAsync<JsonElement>($"{url}/api/v1/sync/status", jsonOptions);
        if (syncStatus.ValueKind != JsonValueKind.Undefined)
        {
            AnsiConsole.MarkupLine($"Sync: {(syncStatus.GetProperty("enabled").GetBoolean() ? "[green]enabled[/]" : "[grey]disabled[/]")}");
            AnsiConsole.MarkupLine($"Interval: {syncStatus.GetProperty("intervalSeconds").GetInt32() / 60} min");
            AnsiConsole.MarkupLine($"Repositories: {syncStatus.GetProperty("repositoriesTracked").GetInt32()}");
        }
    }
}, baseUrl, apiKey, repoStatusId);

repoCommand.AddCommand(repoList);
repoCommand.AddCommand(repoAdd);
repoCommand.AddCommand(repoRemove);
repoCommand.AddCommand(repoSync);
repoCommand.AddCommand(repoStatus);

// --- search commands ---
var searchCommand = new Command("search", "Search indexed code");

var searchQuery = new Argument<string>("query", "Search query");
var searchMode = new Option<string>("--mode", () => "hybrid", "Search mode: semantic, keyword, hybrid");
var searchLang = new Option<string?>("--lang", "Filter by language");
var searchRepo = new Option<string?>("--repo", "Filter by repository ID");
var searchLimit = new Option<int>("--limit", () => 10, "Max results");
searchCommand.AddArgument(searchQuery);
searchCommand.AddOption(searchMode);
searchCommand.AddOption(searchLang);
searchCommand.AddOption(searchRepo);
searchCommand.AddOption(searchLimit);
searchCommand.SetHandler(async (string url, string? token, string format, string query, string mode, string? lang, string? repo, int limit) =>
{
    var client = CreateClient(url, token);
    JsonElement results;

    if (mode == "hybrid")
    {
        var body = new { query, limit, languages = lang is not null ? new[] { lang } : null, repositoryIds = repo is not null ? new[] { repo } : null };
        var response = await client.PostAsJsonAsync($"{url}/api/v1/search", body);
        results = await response.Content.ReadFromJsonAsync<JsonElement>(jsonOptions);
    }
    else
    {
        var paramName = mode == "semantic" ? "query" : "keywords";
        var endpoint = $"{url}/api/v1/search/{mode}?{paramName}={Uri.EscapeDataString(query)}&limit={limit}";
        if (lang is not null) endpoint += $"&language={lang}";
        if (repo is not null) endpoint += $"&repositoryId={repo}";
        results = await client.GetFromJsonAsync<JsonElement>(endpoint, jsonOptions);
    }

    if (format == "json") { Console.WriteLine(results.GetRawText()); return; }

    var items = results.GetProperty("results");
    AnsiConsole.MarkupLine($"[grey]{results.GetProperty("totalCount").GetInt32()} results ({results.GetProperty("searchMode").GetString()}, {results.GetProperty("durationMs").GetInt64()}ms)[/]");
    AnsiConsole.WriteLine();

    foreach (var item in items.EnumerateArray())
    {
        var filePath = item.GetProperty("filePath").ValueKind == JsonValueKind.Null ? "?" : item.GetProperty("filePath").GetString()!;
        var score = item.GetProperty("score").GetDouble();
        var repoName = item.GetProperty("repositoryName").ValueKind == JsonValueKind.Null ? "" : item.GetProperty("repositoryName").GetString()!;

        AnsiConsole.MarkupLine($"[blue]{filePath}[/] [grey]({repoName})[/] [yellow]{score:P1}[/]");
        var content = item.GetProperty("content").GetString() ?? "";
        if (content.Length > 200) content = content[..200] + "...";
        AnsiConsole.WriteLine(content);
        AnsiConsole.WriteLine();
    }
}, baseUrl, apiKey, formatOpt, searchQuery, searchMode, searchLang, searchRepo, searchLimit);

// --- search grep subcommand ---
var grepCommand = new Command("grep", "Search file contents with regex");
var grepPattern = new Argument<string>("pattern", "Regex pattern");
var grepRepoId = new Option<string>("--repo", "Repository ID") { IsRequired = true };
var grepGlob = new Option<string?>("--glob", "File glob filter");
var grepLimit2 = new Option<int>("--limit", () => 50, "Max results");
grepCommand.AddArgument(grepPattern);
grepCommand.AddOption(grepRepoId);
grepCommand.AddOption(grepGlob);
grepCommand.AddOption(grepLimit2);
grepCommand.SetHandler(async (string url, string? token, string pattern, string repoId, string? glob, int limit) =>
{
    var client = CreateClient(url, token);
    var endpoint = $"{url}/api/v1/repositories/{repoId}/grep?pattern={Uri.EscapeDataString(pattern)}&limit={limit}";
    if (glob is not null) endpoint += $"&glob={Uri.EscapeDataString(glob)}";
    var results = await client.GetFromJsonAsync<JsonElement[]>(endpoint, jsonOptions);
    if (results is null) return;

    foreach (var r in results)
    {
        var file = r.GetProperty("filePath").GetString();
        var line = r.GetProperty("lineNumber").GetInt32();
        var content = r.GetProperty("lineContent").GetString();
        AnsiConsole.MarkupLine($"[blue]{file}[/]:[yellow]{line}[/]: {content}");
    }
    AnsiConsole.MarkupLine($"[grey]{results.Length} matches[/]");
}, baseUrl, apiKey, grepPattern, grepRepoId, grepGlob, grepLimit2);

searchCommand.AddCommand(grepCommand);

// --- enrichments commands ---
var enrichCommand = new Command("enrichments", "Browse enrichments");

var enrichListType = new Option<string?>("--type", "Filter by type: Architecture, Development, History, Usage");
var enrichListSubtype = new Option<string?>("--subtype", "Filter by subtype: Chunk, APIDocs, Cookbook, Wiki, etc.");
var enrichListRepo = new Option<string?>("--repo", "Filter by repository ID");
var enrichListLimit = new Option<int>("--limit", () => 20, "Max results");
enrichCommand.AddOption(enrichListType);
enrichCommand.AddOption(enrichListSubtype);
enrichCommand.AddOption(enrichListRepo);
enrichCommand.AddOption(enrichListLimit);
enrichCommand.SetHandler(async (string url, string? token, string format, string? type, string? subtype, string? repo, int limit) =>
{
    var client = CreateClient(url, token);
    var endpoint = $"{url}/api/v1/enrichments?limit={limit}";
    if (type is not null) endpoint += $"&type={type}";
    if (subtype is not null) endpoint += $"&subtype={subtype}";
    if (repo is not null) endpoint += $"&repositoryId={repo}";

    var result = await client.GetFromJsonAsync<JsonElement>(endpoint, jsonOptions);
    if (format == "json") { Console.WriteLine(result.GetRawText()); return; }

    var items = result.GetProperty("results");
    AnsiConsole.MarkupLine($"[grey]{result.GetProperty("totalCount").GetInt32()} enrichments[/]");

    foreach (var e in items.EnumerateArray())
    {
        var eType = e.GetProperty("type").GetString();
        var eSubtype = e.GetProperty("subtype").GetString();
        var title = e.GetProperty("title").ValueKind == JsonValueKind.Null ? "" : e.GetProperty("title").GetString()!;
        var filePath = e.GetProperty("filePath").ValueKind == JsonValueKind.Null ? "" : e.GetProperty("filePath").GetString()!;
        AnsiConsole.MarkupLine($"[blue]{eType}/{eSubtype}[/] {title} [grey]{filePath}[/]");
    }
}, baseUrl, apiKey, formatOpt, enrichListType, enrichListSubtype, enrichListRepo, enrichListLimit);

// --- status command ---
var statusCommand = new Command("status", "Show system status");
statusCommand.SetHandler(async (string url, string? token) =>
{
    var client = CreateClient(url, token);
    try
    {
        var health = await client.GetStringAsync($"{url}/health");
        AnsiConsole.MarkupLine($"[green]API healthy[/] ({url})");

        var repos = await client.GetFromJsonAsync<JsonElement[]>($"{url}/api/v1/repositories", jsonOptions);
        var syncStatus = await client.GetFromJsonAsync<JsonElement>($"{url}/api/v1/sync/status", jsonOptions);

        var indexed = repos?.Count(r => r.GetProperty("status").GetString() == "indexed") ?? 0;
        var total = repos?.Length ?? 0;

        AnsiConsole.MarkupLine($"Repositories: {indexed}/{total} indexed");
        if (syncStatus.ValueKind != JsonValueKind.Undefined)
        {
            var enabled = syncStatus.GetProperty("enabled").GetBoolean();
            AnsiConsole.MarkupLine($"Periodic sync: {(enabled ? "[green]enabled[/]" : "[grey]disabled[/]")} ({syncStatus.GetProperty("intervalSeconds").GetInt32() / 60} min)");
        }
    }
    catch (HttpRequestException ex)
    {
        AnsiConsole.MarkupLine($"[red]Cannot reach API[/] at {url}: {ex.Message}");
    }
}, baseUrl, apiKey);

rootCommand.AddCommand(repoCommand);
rootCommand.AddCommand(searchCommand);
rootCommand.AddCommand(enrichCommand);
rootCommand.AddCommand(statusCommand);

return await rootCommand.InvokeAsync(args);

// --- Helpers ---

static HttpClient CreateClient(string baseUrl, string? token)
{
    var handler = new HttpClientHandler();
    // Allow self-signed certs for local dev
    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
    var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    if (token is not null)
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return client;
}

static void PrintRepoDetail(JsonElement repo)
{
    AnsiConsole.MarkupLine($"[bold]{repo.GetProperty("name").GetString()}[/]");
    AnsiConsole.MarkupLine($"  URL: {repo.GetProperty("url").GetString()}");
    AnsiConsole.MarkupLine($"  Status: {repo.GetProperty("status").GetString()}");
    if (repo.TryGetProperty("stats", out var stats) && stats.ValueKind != JsonValueKind.Null)
    {
        AnsiConsole.MarkupLine($"  Commits: {stats.GetProperty("commitCount").GetInt32()}");
        AnsiConsole.MarkupLine($"  Enrichments: {stats.GetProperty("enrichmentCount").GetInt32()}");
        AnsiConsole.MarkupLine($"  Pending tasks: {stats.GetProperty("pendingTaskCount").GetInt32()}");
    }
}
