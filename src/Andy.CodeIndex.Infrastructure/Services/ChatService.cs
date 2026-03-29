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
    private readonly IChatFileAccessService _fileAccessService;
    private readonly EnrichmentLlmOptions _llmOptions;
    private readonly ChatFileAccessOptions _fileAccessOptions;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ChatService> _logger;

    // Available if any key source exists (user or system)
    public bool IsAvailable => true; // Actual check done at runtime via resolver

    public bool FileAccessEnabled => _fileAccessOptions.Enabled;

    public ChatService(
        CodeIndexDbContext context,
        ISearchService searchService,
        IApiKeyResolver apiKeyResolver,
        IQuestionClassifier classifier,
        IChatFileAccessService fileAccessService,
        IOptions<EnrichmentLlmOptions> llmOptions,
        IOptions<ChatFileAccessOptions> fileAccessOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<ChatService> logger)
    {
        _context = context;
        _searchService = searchService;
        _apiKeyResolver = apiKeyResolver;
        _classifier = classifier;
        _fileAccessService = fileAccessService;
        _llmOptions = llmOptions.Value;
        _fileAccessOptions = fileAccessOptions.Value;
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
        string? repoName = null;
        if (request.RepositoryId.HasValue)
        {
            var repo = await _context.Repositories.FindAsync([request.RepositoryId.Value], ct);
            if (repo is not null)
            {
                repoName = repo.Name;
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

        // Build file access instructions if enabled
        var fileAccessInstructions = "";
        if (_fileAccessOptions.Enabled && request.RepositoryId.HasValue)
        {
            var defaultRef = request.Ref ?? "HEAD";
            fileAccessInstructions = $@"

You have access to a `get_file` tool that can fetch source code files from the repository at any git ref (branch, tag, or commit SHA).
Use it when the user asks about specific files or when you need to read actual source code to answer accurately.

Usage: call get_file with repository_name, ref (branch/tag/SHA), and file_path.
Default ref: {defaultRef}

Examples:
- get_file(repository_name=""{repoName ?? "repo"}"", ref=""main"", file_path=""src/Program.cs"")
- get_file(repository_name=""{repoName ?? "repo"}"", ref=""v1.0"", file_path=""README.md"")

You can fetch up to {_fileAccessOptions.MaxFilesPerTurn} files per turn. Provide line-referenced answers when citing source code.";
        }

        var systemPrompt = $@"You are a code assistant with access to indexed source code repositories. Answer questions about the codebase using the provided context. Be specific, reference file paths and line numbers when relevant. If you don't have enough context, say so.
{classificationHint}{repoContext}{codeContext}{fileAccessInstructions}";

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

        // 5. Call LLM (with function calling loop)
        var client = _httpClientFactory.CreateClient("Chat");
        client.BaseAddress = new Uri(_llmOptions.BaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.Timeout = TimeSpan.FromSeconds(_llmOptions.TimeoutSeconds);

        var useTools = _fileAccessOptions.Enabled && request.RepositoryId.HasValue;
        var toolDefinitions = useTools ? BuildToolDefinitions() : null;

        string reply;
        var fileCounter = new FileCounter();

        try
        {
            reply = await CallLlmWithToolsAsync(
                client, model, messages, toolDefinitions,
                request, repoName, userId, sources,
                fileCounter, ct);
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

    private async Task<string> CallLlmWithToolsAsync(
        HttpClient client,
        string model,
        List<object> messages,
        List<object>? tools,
        ChatRequest request,
        string? repoName,
        string? userId,
        List<ChatSource> sources,
        FileCounter fileCounter,
        CancellationToken ct)
    {
        var maxIterations = _fileAccessOptions.MaxIterations;

        for (var iteration = 0; iteration <= maxIterations; iteration++)
        {
            object llmRequest;
            if (tools is not null && iteration < maxIterations)
            {
                llmRequest = new
                {
                    model,
                    messages,
                    max_tokens = 2000,
                    temperature = 0.3,
                    tools
                };
            }
            else
            {
                // Final iteration or no tools: don't send tools to force text response
                llmRequest = new
                {
                    model,
                    messages,
                    max_tokens = 2000,
                    temperature = 0.3
                };
            }

            var response = await client.PostAsJsonAsync("chat/completions", llmRequest, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<LlmResponse>(responseJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var choice = result?.Choices?.FirstOrDefault();
            if (choice is null)
                return "No response from model.";

            var msg = choice.Message;

            // Check if the model wants to call tools
            if (msg?.ToolCalls is { Count: > 0 } && iteration < maxIterations)
            {
                // Add assistant message with tool calls to conversation
                messages.Add(JsonSerializer.Deserialize<object>(
                    JsonSerializer.Serialize(msg, LlmJsonOptions))!);

                // Process each tool call
                foreach (var toolCall in msg.ToolCalls)
                {
                    var toolResult = await ProcessToolCallAsync(
                        toolCall, request, repoName, userId, sources,
                        fileCounter, ct);

                    messages.Add(new
                    {
                        role = "tool",
                        tool_call_id = toolCall.Id,
                        content = toolResult
                    });
                }

                // Continue loop to call LLM again with tool results
                continue;
            }

            // Model returned a text response
            return msg?.Content ?? "No response from model.";
        }

        return "Max iterations reached. The assistant could not complete the request.";
    }

    private async Task<string> ProcessToolCallAsync(
        LlmToolCall toolCall,
        ChatRequest request,
        string? repoName,
        string? userId,
        List<ChatSource> sources,
        FileCounter fileCounter,
        CancellationToken ct)
    {
        if (toolCall.Function?.Name != "get_file")
        {
            return JsonSerializer.Serialize(new { error = $"Unknown tool: {toolCall.Function?.Name}" });
        }

        // Parse arguments
        GetFileArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<GetFileArgs>(
                toolCall.Function.Arguments ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return JsonSerializer.Serialize(new { error = "Invalid tool arguments" });
        }

        if (args is null || string.IsNullOrEmpty(args.FilePath))
        {
            return JsonSerializer.Serialize(new { error = "Missing file_path argument" });
        }

        // Check max files per turn
        if (fileCounter.Count >= _fileAccessOptions.MaxFilesPerTurn)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"Maximum files per turn reached ({_fileAccessOptions.MaxFilesPerTurn})",
                suggestion = "Prioritize the most relevant files"
            });
        }

        // Resolve repository
        if (!request.RepositoryId.HasValue)
        {
            return JsonSerializer.Serialize(new { error = "No repository context for file access" });
        }

        var gitRef = args.Ref ?? request.Ref ?? "HEAD";

        // Fetch file
        var fileContent = await _fileAccessService.FetchFileForChatAsync(
            request.RepositoryId.Value, gitRef, args.FilePath, userId, ct);

        fileCounter.Count++;

        if (!fileContent.IsSuccess)
        {
            // Build error response with suggestions
            var errorResponse = new Dictionary<string, object> { ["error"] = fileContent.Error! };

            if (fileContent.Error!.Contains("not found", StringComparison.OrdinalIgnoreCase))
                errorResponse["suggestion"] = "Use grep or ls to find the correct path";
            else if (fileContent.Error.Contains("too large", StringComparison.OrdinalIgnoreCase))
                errorResponse["suggestion"] = "Use grep to search for specific patterns";
            else if (fileContent.IsBinary)
                errorResponse["metadata"] = new { size = fileContent.Size };

            return JsonSerializer.Serialize(errorResponse);
        }

        // Add to sources
        sources.Add(new ChatSource
        {
            FilePath = fileContent.FilePath,
            Content = fileContent.Content!,
            Language = fileContent.Language,
            RepositoryName = repoName,
            Score = 1.0,
            Ref = gitRef,
            ResolvedCommitSha = fileContent.ResolvedSha
        });

        return JsonSerializer.Serialize(new
        {
            file_path = fileContent.FilePath,
            content = fileContent.Content,
            language = fileContent.Language,
            size = fileContent.Size,
            resolved_sha = fileContent.ResolvedSha,
            line_count = fileContent.Content?.Split('\n').Length ?? 0
        });
    }

    private static List<object> BuildToolDefinitions()
    {
        return
        [
            new
            {
                type = "function",
                function = new
                {
                    name = "get_file",
                    description = "Fetch the content of a source code file from the repository at a specific git ref (branch, tag, or commit SHA).",
                    parameters = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["repository_name"] = new
                            {
                                type = "string",
                                description = "The repository name"
                            },
                            ["ref"] = new
                            {
                                type = "string",
                                description = "Git ref: branch name, tag, or commit SHA (e.g., 'main', 'v1.0', 'abc1234')"
                            },
                            ["file_path"] = new
                            {
                                type = "string",
                                description = "Path to the file relative to repository root (e.g., 'src/Program.cs')"
                            }
                        },
                        required = new[] { "file_path" }
                    }
                }
            }
        ];
    }

    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private class GetFileArgs
    {
        [JsonPropertyName("repository_name")]
        public string? RepositoryName { get; set; }

        [JsonPropertyName("ref")]
        public string? Ref { get; set; }

        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }
    }

    internal class LlmResponse
    {
        public List<LlmChoice>? Choices { get; set; }
    }

    internal class LlmChoice
    {
        public LlmMessage? Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    internal class LlmMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }

        [JsonPropertyName("tool_calls")]
        public List<LlmToolCall>? ToolCalls { get; set; }
    }

    internal class LlmToolCall
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public LlmFunctionCall? Function { get; set; }
    }

    internal class LlmFunctionCall
    {
        public string? Name { get; set; }
        public string? Arguments { get; set; }
    }

    private class FileCounter
    {
        public int Count { get; set; }
    }
}
