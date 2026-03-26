namespace Andy.CodeIndex.Domain.Entities;

public class ChatConversation
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string Title { get; set; }
    public Guid? RepositoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Repository? Repository { get; set; }
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
}

public class ChatMessage
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public string? SourcesJson { get; set; }
    public DateTime CreatedAt { get; set; }

    public ChatConversation Conversation { get; set; } = null!;
}
