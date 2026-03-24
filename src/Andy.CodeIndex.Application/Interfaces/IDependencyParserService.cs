namespace Andy.CodeIndex.Application.Interfaces;

public interface IDependencyParserService
{
    List<PackageDependency> Parse(string fileName, string content);
    bool CanParse(string fileName);
}

public class PackageDependency
{
    public required string Name { get; set; }
    public string? Version { get; set; }
    public string Scope { get; set; } = "runtime"; // runtime, dev, test, build
    public required string Source { get; set; } // nuget, npm, pypi, go, maven, cargo, gem, composer
    public required string SourceFile { get; set; }
}
