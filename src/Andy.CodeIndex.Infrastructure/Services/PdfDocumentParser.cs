using System.Text;
using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace Andy.CodeIndex.Infrastructure.Services;

public class PdfDocumentParser : IDocumentParser
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    private readonly DocumentParsingOptions _options;
    private readonly ILogger<PdfDocumentParser> _logger;

    public PdfDocumentParser(
        IOptions<DocumentParsingOptions> options,
        ILogger<PdfDocumentParser> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool CanParse(string extension)
        => SupportedExtensions.Contains(extension);

    public Task<ParsedDocument> ParseAsync(Stream content, string filePath, CancellationToken ct = default)
    {
        try
        {
            using var document = PdfDocument.Open(content);
            var pageCount = document.NumberOfPages;
            var maxPages = Math.Min(pageCount, _options.Pdf.MaxPages);

            // Extract metadata
            var title = GetMetadataValue(document.Information?.Title);
            var author = GetMetadataValue(document.Information?.Author);

            var metadata = new Dictionary<string, string>();
            if (title != null) metadata["title"] = title;
            if (author != null) metadata["author"] = author;
            if (document.Information?.Creator != null)
                metadata["creator"] = document.Information.Creator;
            if (document.Information?.Producer != null)
                metadata["producer"] = document.Information.Producer;
            metadata["pageCount"] = pageCount.ToString();

            // Extract text page by page
            var sections = new List<DocumentSection>();
            var allText = new StringBuilder();

            for (int i = 1; i <= maxPages; i++)
            {
                ct.ThrowIfCancellationRequested();

                var page = document.GetPage(i);
                var pageText = page.Text ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    sections.Add(new DocumentSection
                    {
                        Content = pageText.Trim(),
                        PageNumber = i,
                        Title = $"Page {i}"
                    });

                    if (allText.Length > 0)
                        allText.AppendLine();
                    allText.Append(pageText.Trim());
                }
            }

            if (pageCount > maxPages)
            {
                _logger.LogInformation(
                    "PDF {File} has {Total} pages, only parsed first {Max} pages",
                    filePath, pageCount, maxPages);
            }

            var result = new ParsedDocument
            {
                TextContent = allText.ToString(),
                Title = title,
                Author = author,
                PageCount = pageCount,
                Metadata = metadata,
                Sections = sections
            };

            return Task.FromResult(result);
        }
        catch (Exception ex) when (IsPasswordProtectedException(ex))
        {
            _logger.LogWarning("PDF {File} is password-protected, skipping", filePath);
            return Task.FromResult(new ParsedDocument
            {
                TextContent = string.Empty,
                PageCount = 0,
                Metadata = new Dictionary<string, string> { ["error"] = "password-protected" }
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to parse PDF {File}, skipping", filePath);
            return Task.FromResult(new ParsedDocument
            {
                TextContent = string.Empty,
                PageCount = 0,
                Metadata = new Dictionary<string, string> { ["error"] = ex.Message }
            });
        }
    }

    private static string? GetMetadataValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static bool IsPasswordProtectedException(Exception ex)
    {
        var message = ex.Message;
        return message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
            || message.Contains("password", StringComparison.OrdinalIgnoreCase)
            || message.Contains("protected", StringComparison.OrdinalIgnoreCase);
    }
}
