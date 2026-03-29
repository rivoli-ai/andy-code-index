using Andy.CodeIndex.Application.Interfaces;
using Andy.CodeIndex.Application.Options;
using Andy.CodeIndex.Domain.Entities;
using Andy.CodeIndex.Domain.Enums;
using Andy.CodeIndex.Infrastructure.Handlers;
using Andy.CodeIndex.Tests.Unit.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Andy.CodeIndex.Tests.Unit.Handlers;

public class ExtractDocumentTextHandlerTests : IDisposable
{
    private readonly Infrastructure.Data.CodeIndexDbContext _context;
    private readonly Mock<IGitService> _gitServiceMock = new();
    private readonly Mock<IDocumentParser> _parserMock = new();
    private readonly ExtractDocumentTextHandler _handler;
    private readonly Repository _testRepo;

    public ExtractDocumentTextHandlerTests()
    {
        _context = TestDbContextFactory.Create();

        _parserMock.Setup(p => p.CanParse(".pdf")).Returns(true);
        _parserMock.Setup(p => p.CanParse(It.Is<string>(e => e != ".pdf"))).Returns(false);

        _handler = new ExtractDocumentTextHandler(
            _context,
            _gitServiceMock.Object,
            new[] { _parserMock.Object },
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            Options.Create(new DocumentParsingOptions { Enabled = true, Pdf = new PdfParsingOptions { Enabled = true, MaxPages = 100 } }),
            NullLogger<ExtractDocumentTextHandler>.Instance);

        _testRepo = new Repository
        {
            Id = Guid.NewGuid(),
            Name = "test-repo",
            Url = "https://github.com/test/repo",
            Provider = GitProvider.GitHub,
            LastIndexedCommitSha = "abc123",
            Status = "indexed",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _context.Repositories.Add(_testRepo);

        var commit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = "abc123",
            Message = "test commit",
            CommittedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _context.Commits.Add(commit);
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public void Operation_IsExtractDocumentText()
    {
        _handler.Operation.Should().Be(TaskOperation.ExtractDocumentText);
    }

    [Fact]
    public async Task HandleAsync_CreatesEnrichmentsForPdfFiles()
    {
        var cloneDir = "/tmp/test/repos/" + _testRepo.Id;
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns(cloneDir);
        _gitServiceMock.Setup(g => g.ListFilesAsync(cloneDir, "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GitFileInfo>
            {
                new() { Path = "docs/guide.pdf", Size = 1024, Hash = "blobhash1" },
                new() { Path = "src/main.cs", Size = 512, Hash = "blobhash2" } // non-PDF, should be ignored
            });
        _gitServiceMock.Setup(g => g.ReadFileBytesAsync(cloneDir, "abc123", "docs/guide.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF header

        _parserMock.Setup(p => p.ParseAsync(It.IsAny<Stream>(), "docs/guide.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedDocument
            {
                TextContent = "This is the guide content",
                Title = "User Guide",
                Author = "Test Author",
                PageCount = 1,
                Sections = new List<DocumentSection>
                {
                    new() { Content = "This is the guide content", PageNumber = 1, Title = "Page 1" }
                }
            });

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractDocumentText,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var enrichments = _context.Enrichments.Where(e => e.RepositoryId == _testRepo.Id).ToList();
        enrichments.Should().HaveCount(1);
        enrichments[0].Subtype.Should().Be(EnrichmentSubtype.DocumentText);
        enrichments[0].Type.Should().Be(EnrichmentType.Development);
        enrichments[0].Content.Should().Be("This is the guide content");
        enrichments[0].FilePath.Should().Be("docs/guide.pdf");
        enrichments[0].Title.Should().Contain("User Guide");
        enrichments[0].Language.Should().Be("pdf");
    }

    [Fact]
    public async Task HandleAsync_SetsCommitId()
    {
        var cloneDir = "/tmp/test/repos/" + _testRepo.Id;
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns(cloneDir);
        _gitServiceMock.Setup(g => g.ListFilesAsync(cloneDir, "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GitFileInfo>
            {
                new() { Path = "readme.pdf", Size = 1024, Hash = "blobhash1" }
            });
        _gitServiceMock.Setup(g => g.ReadFileBytesAsync(cloneDir, "abc123", "readme.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        _parserMock.Setup(p => p.ParseAsync(It.IsAny<Stream>(), "readme.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedDocument
            {
                TextContent = "PDF content",
                PageCount = 1,
                Sections = new List<DocumentSection>
                {
                    new() { Content = "PDF content", PageNumber = 1, Title = "Page 1" }
                }
            });

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractDocumentText,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var enrichment = _context.Enrichments.First(e => e.RepositoryId == _testRepo.Id);
        var commit = _context.Commits.First(c => c.RepositoryId == _testRepo.Id && c.Sha == "abc123");
        enrichment.CommitId.Should().Be(commit.Id);
    }

    [Fact]
    public async Task HandleAsync_SkipsUnchangedFiles()
    {
        // Set up two commits to test skip-if-unchanged
        var prevCommit = new Commit
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Sha = "prev123",
            Message = "previous commit",
            CommittedAt = DateTime.UtcNow.AddHours(-1),
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        _context.Commits.Add(prevCommit);

        // Add a RepositoryFile for the previous commit with the same hash
        _context.RepositoryFiles.Add(new RepositoryFile
        {
            Id = Guid.NewGuid(),
            CommitId = prevCommit.Id,
            Path = "docs/guide.pdf",
            Size = 1024,
            Hash = "samehash",
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        });
        await _context.SaveChangesAsync();

        var cloneDir = "/tmp/test/repos/" + _testRepo.Id;
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns(cloneDir);
        _gitServiceMock.Setup(g => g.ListFilesAsync(cloneDir, "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GitFileInfo>
            {
                new() { Path = "docs/guide.pdf", Size = 1024, Hash = "samehash" } // Same hash as previous
            });

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractDocumentText,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        // ReadFileBytesAsync should NOT have been called since the file was skipped
        _gitServiceMock.Verify(
            g => g.ReadFileBytesAsync(It.IsAny<string>(), It.IsAny<string>(), "docs/guide.pdf", It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_CreatesMultipleEnrichmentsForMultiPagePdf()
    {
        var cloneDir = "/tmp/test/repos/" + _testRepo.Id;
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns(cloneDir);
        _gitServiceMock.Setup(g => g.ListFilesAsync(cloneDir, "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GitFileInfo>
            {
                new() { Path = "docs/manual.pdf", Size = 2048, Hash = "blobhash1" }
            });
        _gitServiceMock.Setup(g => g.ReadFileBytesAsync(cloneDir, "abc123", "docs/manual.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        _parserMock.Setup(p => p.ParseAsync(It.IsAny<Stream>(), "docs/manual.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedDocument
            {
                TextContent = "Page 1 content\nPage 2 content",
                Title = "Manual",
                PageCount = 2,
                Sections = new List<DocumentSection>
                {
                    new() { Content = "Page 1 content", PageNumber = 1, Title = "Page 1" },
                    new() { Content = "Page 2 content", PageNumber = 2, Title = "Page 2" }
                }
            });

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractDocumentText,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        var enrichments = _context.Enrichments
            .Where(e => e.RepositoryId == _testRepo.Id && e.Subtype == EnrichmentSubtype.DocumentText)
            .OrderBy(e => e.StartLine)
            .ToList();

        enrichments.Should().HaveCount(2);
        enrichments[0].Content.Should().Be("Page 1 content");
        enrichments[0].StartLine.Should().Be(1);
        enrichments[1].Content.Should().Be("Page 2 content");
        enrichments[1].StartLine.Should().Be(2);
    }

    [Fact]
    public async Task HandleAsync_DisabledGlobally_DoesNothing()
    {
        var handler = new ExtractDocumentTextHandler(
            _context,
            _gitServiceMock.Object,
            new[] { _parserMock.Object },
            Options.Create(new IndexingOptions { DataDir = "/tmp/test" }),
            Options.Create(new DocumentParsingOptions { Enabled = false }),
            NullLogger<ExtractDocumentTextHandler>.Instance);

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractDocumentText,
            CreatedAt = DateTime.UtcNow
        };

        await handler.HandleAsync(task);

        _gitServiceMock.Verify(
            g => g.ListFilesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NoDocumentFiles_DoesNothing()
    {
        var cloneDir = "/tmp/test/repos/" + _testRepo.Id;
        _gitServiceMock.Setup(g => g.GetCloneDir("/tmp/test", _testRepo.Id)).Returns(cloneDir);
        _gitServiceMock.Setup(g => g.ListFilesAsync(cloneDir, "abc123", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GitFileInfo>
            {
                new() { Path = "src/main.cs", Size = 512, Hash = "hash1" },
                new() { Path = "README.md", Size = 256, Hash = "hash2" }
            });

        var task = new IndexingTask
        {
            Id = Guid.NewGuid(),
            RepositoryId = _testRepo.Id,
            Operation = TaskOperation.ExtractDocumentText,
            CreatedAt = DateTime.UtcNow
        };

        await _handler.HandleAsync(task);

        _context.Enrichments.Where(e => e.RepositoryId == _testRepo.Id).Should().BeEmpty();
    }
}
