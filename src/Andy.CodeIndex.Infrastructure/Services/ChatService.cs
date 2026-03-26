using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Andy.CodeIndex.Application.DTOs;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
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
    private readonly IQuestionClassifier _classifier;
    private readonly EnrichmentLlmOptions _llmOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatService> _logger;

    // Available if any key source exists (user or system)
    public bool IsAvailable => true; // Actual check done at runtime via resolver

    public ChatService(
        CodeIndexDbContext context,
        ISearchService searchService,
        IApiKeyResolver apiKeyResolver,
        IQuestionClassifier classifier,
        IOptions<EnrichmentLlmOptions> llmOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<ChatService> logger)
    {
        _context = context;
        _searchService = searchService;
        _apiKeyResolver = apiKeyResolver;
        _classifier = classifier;
        _llmOptions = llmOptions.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, string? userId = null, CancellationToken ct = default)
    {
        var conversationId = request.ConversationId ?? Guid.NewGuid().ToString();

        // 0. Create or load conversation
        var conversation = await _context.ChatConversations
            .FirstOrDefaultAsync(c => c.Id.ToString() == conversationId, ct);

        if (conversation == null)
        {
            conversation = new ChatConversation
            {
                Id = Guid.Parse(conversationId),
                UserId = userId ?? "anonymous",
                Title = request.Message.Length > 60 ? request.Message[..57] + "..." : request.Message,
                RepositoryId = request.RepositoryId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.ChatConversations.Add(conversation);
            await _context.SaveChangesAsync(ct);
        }

        // 1. Resolve LLM API key
        var (apiKey, model, source) = await _apiKeyResolver.ResolveLlmKeyAsync(userId, ct);

        if (string.IsNullOrEmpty(apiKey))
        {
            // Still persist the user's message even without LLM
            var noKeyReply = "No LLM API key configured. Set an API key in Settings or configure Enrichment:ApiKey in appsettings.";
            _context.ChatMessages.Add(new Domain.Entities.ChatMessage { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "user", Content = request.Message, CreatedAt = DateTime.UtcNow });
            _context.ChatMessages.Add(new Domain.Entities.ChatMessage { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = "assistant", Content = noKeyReply, CreatedAt = DateTime.UtcNow });
            conversation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            return new ChatResponse
            {
                Reply = noKeyReply,
                ConversationId = conversationId,
                Model = _llmOptions.Model
            };
        }

        // 2. Classify the question and fetch relevant enrichment documents
        var classification = _classifier.Classify(request.Message);

        var docQuery = _context.Enrichments
            .Include(e => e.Repository)
            .Where(e => e.Subtype != EnrichmentSubtype.Chunk && e.Quality >= 0.3);

        if (request.RepositoryId.HasValue)
            docQuery = docQuery.Where(e => e.RepositoryId == request.RepositoryId.Value);

        if (classification.DimensionId != "general")
        {
            var subtypes = classification.RequiredEnrichments;
            docQuery = docQuery.Where(e => subtypes.Contains(e.Subtype));
        }

        var enrichmentDocs = await docQuery.OrderByDescending(e => e.Quality).Take(10).ToListAsync(ct);

        // If primary enrichments are insufficient, try fallbacks
        if (enrichmentDocs.Count < 2 && classification.FallbackEnrichments.Length > 0)
        {
            var fallbacks = classification.FallbackEnrichments;
            var fallbackQuery = _context.Enrichments
                .Include(e => e.Repository)
                .Where(e => fallbacks.Contains(e.Subtype) && e.Quality >= 0.3);
            if (request.RepositoryId.HasValue)
                fallbackQuery = fallbackQuery.Where(e => e.RepositoryId == request.RepositoryId.Value);
            var fallbackDocs = await fallbackQuery.OrderByDescending(e => e.Quality).Take(5).ToListAsync(ct);
            enrichmentDocs.AddRange(fallbackDocs);
        }

        // 3. Also do keyword search on code chunks for specific code context
        var filter = new SearchFilter();
        if (request.RepositoryId.HasValue)
            filter.RepositoryIds = [request.RepositoryId.Value];

        var searchResults = await _searchService.KeywordSearchAsync(request.Message, filter, limit: 5, ct);

        var sources = new List<ChatSource>();

        // Add enrichment docs as sources
        foreach (var doc in enrichmentDocs)
        {
            var content = doc.Content.Length > 1500 ? doc.Content[..1500] + "..." : doc.Content;
            sources.Add(new ChatSource
            {
                FilePath = $"[{doc.Subtype}] {doc.Title ?? doc.Subtype.ToString()}",
                Content = content,
                Language = doc.Language,
                RepositoryName = doc.Repository?.Name,
                Score = 1.0
            });
        }

        // Add code search results
        sources.AddRange(searchResults.Results.Select(r => new ChatSource
        {
            FilePath = r.FilePath ?? "unknown",
            StartLine = r.StartLine,
            EndLine = r.EndLine,
            Content = r.Content.Length > 300 ? r.Content[..300] + "..." : r.Content,
            Language = r.Language,
            RepositoryName = r.RepositoryName,
            Score = r.Score
        }));

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

        // 4. Load conversation history
        var history = await _context.ChatMessages
            .Where(m => m.ConversationId == conversation.Id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content })
            .ToListAsync(ct);

        var codeContext = sources.Count > 0
            ? "\n\nRelevant code from the indexed repositories:\n" +
              string.Join("\n---\n", sources.Select(s =>
                  $"File: {s.RepositoryName}/{s.FilePath}" +
                  (s.StartLine.HasValue ? $" (lines {s.StartLine}-{s.EndLine})" : "") +
                  $"\n```{s.Language}\n{s.Content}\n```"))
            : "";

        var classificationHint = classification.MatchedQuestionText is not null
            ? $"\nThe user is likely asking about: {classification.MatchedQuestionText} (dimension: {classification.DimensionLabel})\n"
            : "";

        var systemPrompt = $@"You are a code assistant with access to indexed source code repositories. Answer questions about the codebase using the provided context. Be specific, reference file paths and line numbers when relevant. If you don't have enough context, say so.
{classificationHint}{repoContext}{codeContext}";

        var messages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        // Add conversation history (last 20 messages)
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

        // 6. Persist messages to database
        var sourcesJson = sources.Count > 0 ? JsonSerializer.Serialize(sources) : null;
        _context.ChatMessages.Add(new Domain.Entities.ChatMessage
        {
            Id = Guid.NewGuid(), ConversationId = conversation.Id,
            Role = "user", Content = request.Message, CreatedAt = DateTime.UtcNow
        });
        _context.ChatMessages.Add(new Domain.Entities.ChatMessage
        {
            Id = Guid.NewGuid(), ConversationId = conversation.Id,
            Role = "assistant", Content = reply, SourcesJson = sourcesJson, CreatedAt = DateTime.UtcNow
        });
        conversation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

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

}
