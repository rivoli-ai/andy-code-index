using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Andy.CodeIndex.Infrastructure.Services;

public class ChatService : IChatService
{
    private readonly CodeIndexDbContext _context;
    private readonly ISearchService _searchService;
    private readonly IApiKeyResolver _apiKeyResolver;
    private readonly EnrichmentLlmOptions _llmOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatService> _logger;

    // Simple in-memory conversation store (per conversationId)
    private static readonly ConcurrentDictionary<string, List<ConversationMessage>> Conversations = new();

    // Available if any key source exists (user or system)
    public bool IsAvailable => true; // Actual check done at runtime via resolver

    public ChatService(
        CodeIndexDbContext context,
        ISearchService searchService,
        IApiKeyResolver apiKeyResolver,
        IOptions<EnrichmentLlmOptions> llmOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<ChatService> logger)
    {
        _context = context;
        _searchService = searchService;
        _apiKeyResolver = apiKeyResolver;
        _llmOptions = llmOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, string? userId = null, CancellationToken ct = default)
    {
        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();

        // 1. Resolve LLM API key: user LLM key -> user embedding key -> system LLM key -> system embedding key
        var (apiKey, model, source) = await _apiKeyResolver.ResolveLlmKeyAsync(userId, ct);

        if (string.IsNullOrEmpty(apiKey))
        {
            return new ChatResponse
            {
                Reply = "No LLM API key configured. Set an API key in Settings or configure Enrichment:ApiKey in appsettings.",
                ConversationId = conversationId,
                Model = _llmOptions.Model
            };
        }

        // 2. Search for relevant context (RBAC filtering happens in SearchService)
        var filter = new SearchFilter();
        if (request.RepositoryId.HasValue)
            filter.RepositoryIds = [request.RepositoryId.Value];

        var searchResults = await _searchService.KeywordSearchAsync(request.Message, filter, limit: 8, ct);

        var sources = searchResults.Results.Select(r => new ChatSource
        {
            FilePath = r.FilePath ?? "unknown",
            StartLine = r.StartLine,
            EndLine = r.EndLine,
            Content = r.Content.Length > 300 ? r.Content[..300] + "..." : r.Content,
            Language = r.Language,
            RepositoryName = r.RepositoryName,
            Score = r.Score
        }).ToList();

        // 3. Build repo context
        var repoContext = "";
        if (request.RepositoryId.HasValue)
        {
            var repo = await _context.Repositories.FindAsync([request.RepositoryId.Value], ct);
            if (repo is not null)
            {
                var enrichmentCount = await _context.Enrichments.CountAsync(e => e.RepositoryId == repo.Id, ct);
                var languages = await _context.Enrichments
                    .Where(e => e.RepositoryId == repo.Id && e.Language != null)
                    .Select(e => e.Language!).Distinct().ToListAsync(ct);
                repoContext = $"\nRepository: {repo.Name} ({repo.Url})\nLanguages: {string.Join(", ", languages)}\nEnrichments: {enrichmentCount}\nDefault branch: {repo.DefaultBranch ?? "main"}\n";
            }
        }
        else
        {
            var repos = await _context.Repositories.ToListAsync(ct);
            var repoSummaries = new List<string>();
            foreach (var r in repos)
            {
                var chunkCount = await _context.Enrichments.CountAsync(e => e.RepositoryId == r.Id && e.Subtype == EnrichmentSubtype.Chunk, ct);
                var languages = await _context.Enrichments
                    .Where(e => e.RepositoryId == r.Id && e.Language != null)
                    .Select(e => e.Language!).Distinct().ToListAsync(ct);
                repoSummaries.Add($"- {r.Name} ({r.Url}): {chunkCount} code chunks, languages: {string.Join(", ", languages)}, status: {r.Status}");
            }
            repoContext = $"\nIndexed repositories ({repos.Count}):\n{string.Join("\n", repoSummaries)}\n";
        }

        // 4. Build conversation with history
        var history = Conversations.GetOrAdd(conversationId, _ => []);

        var codeContext = sources.Count > 0
            ? "\n\nRelevant code from the indexed repositories:\n" +
              string.Join("\n---\n", sources.Select(s =>
                  $"File: {s.RepositoryName}/{s.FilePath}" +
                  (s.StartLine.HasValue ? $" (lines {s.StartLine}-{s.EndLine})" : "") +
                  $"\n```{s.Language}\n{s.Content}\n```"))
            : "";

        var systemPrompt = $@"You are a code assistant with access to indexed source code repositories. Answer questions about the codebase using the provided context. Be specific, reference file paths and line numbers when relevant. If you don't have enough context, say so.
{repoContext}{codeContext}";

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        // Add conversation history (last 10 exchanges)
        foreach (var msg in history.TakeLast(20))
        {
            messages.Add(new { role = msg.Role, content = msg.Content });
        }

        messages.Add(new { role = "user", content = request.Message });

        // 5. Call LLM
        var client = _httpClientFactory.CreateClient("Chat");
        client.BaseAddress = new Uri(_llmOptions.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.Timeout = TimeSpan.FromSeconds(_llmOptions.TimeoutSeconds);

        var llmRequest = new
        {
            model,  // Uses resolved model (from user settings or system config)
            messages,
            max_tokens = 2000,
            temperature = 0.3
        };

        string reply;
        try
        {
            var response = await client.PostAsJsonAsync("chat/completions", llmRequest, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<LlmResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
            reply = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "No response from model.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed");
            reply = $"LLM call failed: {ex.Message}. Check your API key and model configuration in Settings.";
        }

        // 6. Store conversation
        history.Add(new ConversationMessage("user", request.Message));
        history.Add(new ConversationMessage("assistant", reply));

        // Keep conversation size bounded
        if (history.Count > 40)
            history.RemoveRange(0, history.Count - 40);

        return new ChatResponse
        {
            Reply = reply,
            ConversationId = conversationId,
            Sources = sources,
            Model = model
        };
    }

    private class LlmResponse
    {
        public List<LlmChoice>? Choices { get; set; }
    }

    private class LlmChoice
    {
        public LlmMessage? Message { get; set; }
    }

    private class LlmMessage
    {
        public string? Content { get; set; }
    }

    private record ConversationMessage(string Role, string Content);
}
