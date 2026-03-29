using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Writer;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class PdfDocumentParserTests
{
    private readonly PdfDocumentParser _parser;

    public PdfDocumentParserTests()
    {
        var options = Options.Create(new DocumentParsingOptions
        {
            Enabled = true,
            Pdf = new PdfParsingOptions { Enabled = true, MaxPages = 100 }
        });

        _parser = new PdfDocumentParser(options, NullLogger<PdfDocumentParser>.Instance);
    }

    [Fact]
    public void CanParse_PdfExtension_ReturnsTrue()
    {
        _parser.CanParse(".pdf").Should().BeTrue();
    }

    [Fact]
    public void CanParse_PdfExtensionUpperCase_ReturnsTrue()
    {
        _parser.CanParse(".PDF").Should().BeTrue();
    }

    [Fact]
    public void CanParse_NonPdfExtension_ReturnsFalse()
    {
        _parser.CanParse(".docx").Should().BeFalse();
        _parser.CanParse(".txt").Should().BeFalse();
        _parser.CanParse(".xlsx").Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_ValidPdf_ExtractsText()
    {
        // Create a minimal PDF programmatically
        var pdfBytes = CreateTestPdf("Hello from page one", "Second page content");

        using var stream = new MemoryStream(pdfBytes);
        var result = await _parser.ParseAsync(stream, "test.pdf");

        result.TextContent.Should().NotBeNullOrEmpty();
        result.TextContent.Should().Contain("Hello from page one");
        result.TextContent.Should().Contain("Second page content");
        result.PageCount.Should().Be(2);
        result.Sections.Should().HaveCount(2);
        result.Sections[0].PageNumber.Should().Be(1);
        result.Sections[1].PageNumber.Should().Be(2);
    }

    [Fact]
    public async Task ParseAsync_ValidPdf_ExtractsMetadata()
    {
        var pdfBytes = CreateTestPdf("Test content");

        using var stream = new MemoryStream(pdfBytes);
        var result = await _parser.ParseAsync(stream, "test.pdf");

        result.Metadata.Should().ContainKey("pageCount");
        result.Metadata["pageCount"].Should().Be("1");
    }

    [Fact]
    public async Task ParseAsync_CorruptPdf_ReturnsEmptyDocument()
    {
        var corruptBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE };

        using var stream = new MemoryStream(corruptBytes);
        var result = await _parser.ParseAsync(stream, "corrupt.pdf");

        result.TextContent.Should().BeEmpty();
        result.Metadata.Should().ContainKey("error");
    }

    [Fact]
    public async Task ParseAsync_MaxPagesRespected()
    {
        // Create parser with maxPages=1
        var options = Options.Create(new DocumentParsingOptions
        {
            Enabled = true,
            Pdf = new PdfParsingOptions { Enabled = true, MaxPages = 1 }
        });
        var limitedParser = new PdfDocumentParser(options, NullLogger<PdfDocumentParser>.Instance);

        var pdfBytes = CreateTestPdf("Page 1 text", "Page 2 text", "Page 3 text");

        using var stream = new MemoryStream(pdfBytes);
        var result = await limitedParser.ParseAsync(stream, "multi.pdf");

        result.PageCount.Should().Be(3); // Total page count should still reflect actual PDF
        result.Sections.Should().HaveCount(1); // But only 1 section extracted
        result.TextContent.Should().Contain("Page 1 text");
        result.TextContent.Should().NotContain("Page 2 text");
    }

    [Fact]
    public void IsPasswordProtectedException_EncryptMessage_ReturnsTrue()
    {
        var ex = new InvalidOperationException("The document is encrypted and cannot be opened");
        PdfDocumentParser.IsPasswordProtectedException(ex).Should().BeTrue();
    }

    [Fact]
    public void IsPasswordProtectedException_PasswordMessage_ReturnsTrue()
    {
        var ex = new InvalidOperationException("Password required to open this document");
        PdfDocumentParser.IsPasswordProtectedException(ex).Should().BeTrue();
    }

    [Fact]
    public void IsPasswordProtectedException_UnrelatedMessage_ReturnsFalse()
    {
        var ex = new InvalidOperationException("Something else went wrong");
        PdfDocumentParser.IsPasswordProtectedException(ex).Should().BeFalse();
    }

    private static byte[] CreateTestPdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();

        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);

        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
            page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(72, 720), font);
        }

        return builder.Build();
    }
}
