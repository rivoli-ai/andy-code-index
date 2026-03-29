using Andy.CodeIndex.Infrastructure.Services;
using FluentAssertions;

namespace Andy.CodeIndex.Tests.Unit.Services;

public class BinaryDetectionServiceTests
{
    private readonly BinaryDetectionService _service = new();

    [Theory]
    [InlineData("image.png")]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("icon.gif")]
    [InlineData("icon.ico")]
    [InlineData("app.exe")]
    [InlineData("library.dll")]
    [InlineData("library.so")]
    [InlineData("library.dylib")]
    [InlineData("archive.zip")]
    [InlineData("archive.tar")]
    [InlineData("archive.gz")]
    [InlineData("font.woff")]
    [InlineData("font.woff2")]
    [InlineData("font.ttf")]
    [InlineData("compiled.class")]
    [InlineData("compiled.pyc")]
    [InlineData("object.o")]
    [InlineData("object.obj")]
    [InlineData("debug.pdb")]
    [InlineData("document.pdf")]
    [InlineData("video.mp4")]
    [InlineData("audio.mp3")]
    public void IsBinary_BinaryExtension_ReturnsTrue(string filePath)
    {
        var (isBinary, reason) = _service.IsBinary(filePath);

        isBinary.Should().BeTrue();
        reason.Should().Contain("known binary format");
    }

    [Theory]
    [InlineData("main.cs")]
    [InlineData("app.ts")]
    [InlineData("index.js")]
    [InlineData("main.py")]
    [InlineData("readme.md")]
    [InlineData("config.json")]
    [InlineData("style.css")]
    [InlineData("page.html")]
    [InlineData("data.yaml")]
    [InlineData("script.sh")]
    public void IsBinary_TextExtension_ReturnsFalse(string filePath)
    {
        var (isBinary, _) = _service.IsBinary(filePath);

        isBinary.Should().BeFalse();
    }

    [Fact]
    public void IsBinary_NoExtension_ReturnsFalse()
    {
        var (isBinary, _) = _service.IsBinary("Makefile");

        isBinary.Should().BeFalse();
    }

    [Fact]
    public void IsBinary_ExtensionCheck_IsCaseInsensitive()
    {
        var (isBinary, _) = _service.IsBinary("IMAGE.PNG");

        isBinary.Should().BeTrue();
    }
}
