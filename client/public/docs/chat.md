# Chat

Chat lets you ask questions about your codebase in natural language. It combines LLM reasoning with code search to provide accurate, context-aware answers.

## Starting a Conversation

1. Navigate to **Chat**.
2. Type your question in the input field.
3. The system searches your indexed code for relevant context, then generates an answer.

### Example Questions

- "How does authentication work in this project?"
- "What database migrations exist and what do they change?"
- "Explain the error handling strategy in the API layer."

## How It Works

Chat uses a retrieval-augmented generation (RAG) pipeline:

1. Your question is converted to a search query.
2. Relevant code snippets are retrieved from the index.
3. The snippets and your question are sent to the LLM.
4. The LLM generates an answer grounded in your actual code.

This ensures answers reference real code rather than hallucinated examples.

## Function Calling

The chat system supports function calling, allowing the LLM to:

- **Search code** -- Run additional searches to find relevant context.
- **Read files** -- Fetch full file contents when snippets are not enough.
- **List files** -- Browse the repository file tree.

Function calls happen automatically when the LLM determines it needs more information.

## Conversation Management

### History

Previous conversations are saved and accessible from the chat sidebar. Click a past conversation to resume it.

### New Conversation

Click **New Chat** to start a fresh conversation without prior context.

### Context Window

Each conversation maintains context across messages. The system manages context size automatically, keeping the most relevant information within the LLM's token limit.

## Multi-Repository Support

Chat can search across all indexed repositories. Specify a repository in your question to focus results:

- "In the backend repo, how are API routes defined?"
- "Show me the database models from the data-layer project."

## Configuration

Chat uses the LLM key configured in [Settings](settings). The same key is used for both enrichments and chat.

### Model Selection

The model used for chat responses depends on your configured LLM provider and the model specified in Settings.

## Tips for Better Answers

- Be specific about what you want to know.
- Reference file names or module names when you know them.
- Ask follow-up questions to drill deeper into a topic.
- Use "explain" for conceptual questions and "show" for code examples.
