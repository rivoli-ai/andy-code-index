using System.Text.Json;
using System.Text.RegularExpressions;
using Andy.CodeIndex.Application.Interfaces;

namespace Andy.CodeIndex.Infrastructure.Parsers;

public class DependencyParserService : IDependencyParserService
{
    private static readonly string[] DependencyFileNames =
    [
        ".csproj", "Directory.Build.props", "Directory.Packages.props", "packages.config",
        "package.json", "requirements.txt", "Pipfile", "pyproject.toml", "setup.py",
        "go.mod", "pom.xml", "build.gradle", "build.gradle.kts",
        "Cargo.toml", "Gemfile", "composer.json"
    ];

    public bool CanParse(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return DependencyFileNames.Any(f =>
            name.Equals(f, StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(f, StringComparison.OrdinalIgnoreCase));
    }

    public List<PackageDependency> Parse(string fileName, string content)
    {
        var name = Path.GetFileName(fileName);

        if (name.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase))
            return ParseCsproj(content, fileName);

        if (name.Equals("packages.config", StringComparison.OrdinalIgnoreCase))
            return ParsePackagesConfig(content, fileName);

        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
            return ParsePackageJson(content, fileName);

        if (name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase))
            return ParseRequirementsTxt(content, fileName);

        if (name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
            return ParsePyprojectToml(content, fileName);

        if (name.Equals("go.mod", StringComparison.OrdinalIgnoreCase))
            return ParseGoMod(content, fileName);

        if (name.Equals("pom.xml", StringComparison.OrdinalIgnoreCase))
            return ParsePomXml(content, fileName);

        if (name.StartsWith("build.gradle", StringComparison.OrdinalIgnoreCase))
            return ParseBuildGradle(content, fileName);

        if (name.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase))
            return ParseCargoToml(content, fileName);

        if (name.Equals("Gemfile", StringComparison.OrdinalIgnoreCase))
            return ParseGemfile(content, fileName);

        if (name.Equals("composer.json", StringComparison.OrdinalIgnoreCase))
            return ParseComposerJson(content, fileName);

        return [];
    }

    // --- .NET ---
    internal static List<PackageDependency> ParseCsproj(string content, string file)
    {
        var deps = new List<PackageDependency>();
        foreach (Match m in Regex.Matches(content,
            @"<PackageReference\s+Include=""([^""]+)""(?:\s+Version=""([^""]+)"")?", RegexOptions.IgnoreCase))
        {
            deps.Add(new PackageDependency
            {
                Name = m.Groups[1].Value,
                Version = m.Groups[2].Success ? m.Groups[2].Value : null,
                Source = "nuget",
                SourceFile = file
            });
        }
        return deps;
    }

    internal static List<PackageDependency> ParsePackagesConfig(string content, string file)
    {
        var deps = new List<PackageDependency>();
        foreach (Match m in Regex.Matches(content,
            @"<package\s+id=""([^""]+)""\s+version=""([^""]+)""", RegexOptions.IgnoreCase))
        {
            deps.Add(new PackageDependency
            {
                Name = m.Groups[1].Value,
                Version = m.Groups[2].Value,
                Source = "nuget",
                SourceFile = file
            });
        }
        return deps;
    }

    // --- Node.js ---
    internal static List<PackageDependency> ParsePackageJson(string content, string file)
    {
        var deps = new List<PackageDependency>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("dependencies", out var runtime))
                foreach (var prop in runtime.EnumerateObject())
                    deps.Add(new PackageDependency { Name = prop.Name, Version = prop.Value.GetString(), Scope = "runtime", Source = "npm", SourceFile = file });

            if (root.TryGetProperty("devDependencies", out var dev))
                foreach (var prop in dev.EnumerateObject())
                    deps.Add(new PackageDependency { Name = prop.Name, Version = prop.Value.GetString(), Scope = "dev", Source = "npm", SourceFile = file });
        }
        catch { }
        return deps;
    }

    // --- Python ---
    internal static List<PackageDependency> ParseRequirementsTxt(string content, string file)
    {
        var deps = new List<PackageDependency>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('-')) continue;

            var match = Regex.Match(trimmed, @"^([a-zA-Z0-9_.-]+)\s*([><=!~]+\s*.+)?$");
            if (match.Success)
            {
                deps.Add(new PackageDependency
                {
                    Name = match.Groups[1].Value,
                    Version = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null,
                    Source = "pypi",
                    SourceFile = file
                });
            }
        }
        return deps;
    }

    internal static List<PackageDependency> ParsePyprojectToml(string content, string file)
    {
        var deps = new List<PackageDependency>();
        var inDeps = false;
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed == "[project.dependencies]" || trimmed == "dependencies = [")
                inDeps = true;
            else if (trimmed.StartsWith('[') && inDeps)
                inDeps = false;
            else if (inDeps)
            {
                var match = Regex.Match(trimmed, @"""?([a-zA-Z0-9_.-]+)(?:\s*([><=!~].+?))?""?,?$");
                if (match.Success && match.Groups[1].Value.Length > 1)
                    deps.Add(new PackageDependency { Name = match.Groups[1].Value, Version = match.Groups[2].Success ? match.Groups[2].Value : null, Source = "pypi", SourceFile = file });
            }
        }
        return deps;
    }

    // --- Go ---
    internal static List<PackageDependency> ParseGoMod(string content, string file)
    {
        var deps = new List<PackageDependency>();
        var inRequire = false;
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed == "require (") inRequire = true;
            else if (trimmed == ")" && inRequire) inRequire = false;
            else if (inRequire || trimmed.StartsWith("require "))
            {
                var match = Regex.Match(trimmed, @"^\s*(?:require\s+)?([^\s]+)\s+([^\s]+)");
                if (match.Success && match.Groups[1].Value.Contains('/'))
                    deps.Add(new PackageDependency { Name = match.Groups[1].Value, Version = match.Groups[2].Value, Source = "go", SourceFile = file });
            }
        }
        return deps;
    }

    // --- Java/Maven ---
    internal static List<PackageDependency> ParsePomXml(string content, string file)
    {
        var deps = new List<PackageDependency>();
        foreach (Match m in Regex.Matches(content,
            @"<dependency>\s*<groupId>([^<]+)</groupId>\s*<artifactId>([^<]+)</artifactId>(?:\s*<version>([^<]+)</version>)?(?:\s*<scope>([^<]+)</scope>)?",
            RegexOptions.Singleline))
        {
            deps.Add(new PackageDependency
            {
                Name = $"{m.Groups[1].Value}:{m.Groups[2].Value}",
                Version = m.Groups[3].Success ? m.Groups[3].Value : null,
                Scope = m.Groups[4].Success ? m.Groups[4].Value : "runtime",
                Source = "maven",
                SourceFile = file
            });
        }
        return deps;
    }

    // --- Gradle ---
    internal static List<PackageDependency> ParseBuildGradle(string content, string file)
    {
        var deps = new List<PackageDependency>();
        foreach (Match m in Regex.Matches(content,
            @"(implementation|api|testImplementation|compileOnly|runtimeOnly)\s*[\('""]([^'"")\s]+)(?::([^'"")\s]+))?(?::([^'"")\s]+))?['""\)]"))
        {
            var scope = m.Groups[1].Value switch
            {
                "testImplementation" => "test",
                "compileOnly" => "build",
                _ => "runtime"
            };
            deps.Add(new PackageDependency
            {
                Name = m.Groups[2].Value + (m.Groups[3].Success ? $":{m.Groups[3].Value}" : ""),
                Version = m.Groups[4].Success ? m.Groups[4].Value : null,
                Scope = scope,
                Source = "maven",
                SourceFile = file
            });
        }
        return deps;
    }

    // --- Rust ---
    internal static List<PackageDependency> ParseCargoToml(string content, string file)
    {
        var deps = new List<PackageDependency>();
        var inDeps = false;
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed == "[dependencies]" || trimmed == "[dev-dependencies]")
            {
                inDeps = true;
                continue;
            }
            if (trimmed.StartsWith('[') && inDeps) { inDeps = false; continue; }

            if (inDeps)
            {
                var match = Regex.Match(trimmed, @"^([a-zA-Z0-9_-]+)\s*=\s*""?([^""]+)""?");
                if (match.Success)
                    deps.Add(new PackageDependency { Name = match.Groups[1].Value, Version = match.Groups[2].Value, Source = "cargo", SourceFile = file });
            }
        }
        return deps;
    }

    // --- Ruby ---
    internal static List<PackageDependency> ParseGemfile(string content, string file)
    {
        var deps = new List<PackageDependency>();
        foreach (var line in content.Split('\n'))
        {
            var match = Regex.Match(line.Trim(), @"gem\s+['""]([^'""]+)['""](?:\s*,\s*['""]([^'""]+)['""])?");
            if (match.Success)
                deps.Add(new PackageDependency { Name = match.Groups[1].Value, Version = match.Groups[2].Success ? match.Groups[2].Value : null, Source = "gem", SourceFile = file });
        }
        return deps;
    }

    // --- PHP ---
    internal static List<PackageDependency> ParseComposerJson(string content, string file)
    {
        var deps = new List<PackageDependency>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("require", out var req))
                foreach (var prop in req.EnumerateObject())
                    if (prop.Name != "php") deps.Add(new PackageDependency { Name = prop.Name, Version = prop.Value.GetString(), Scope = "runtime", Source = "composer", SourceFile = file });

            if (root.TryGetProperty("require-dev", out var dev))
                foreach (var prop in dev.EnumerateObject())
                    deps.Add(new PackageDependency { Name = prop.Name, Version = prop.Value.GetString(), Scope = "dev", Source = "composer", SourceFile = file });
        }
        catch { }
        return deps;
    }
}
