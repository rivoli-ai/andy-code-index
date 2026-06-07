using System.Text.Json;
using System.Text.RegularExpressions;
using Andy.CodeIndex.Application.Interfaces;

namespace Andy.CodeIndex.Infrastructure.Parsers;

public class DependencyParserService : IDependencyParserService
{
    private static readonly string[] DependencyFileNames =
    [
        ".csproj", "Directory.Build.props", "Directory.Packages.props", "packages.config",
        "package.json", "package-lock.json", "requirements.txt", "Pipfile", "pyproject.toml", "setup.py",
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

        if (name.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase))
            return ParsePackageLockJson(content, fileName);

        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
            return ParsePackageJson(content, fileName);

        if (name.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase))
            return ParseRequirementsTxt(content, fileName);

        if (name.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
            return ParsePyprojectToml(content, fileName);

        if (name.Equals("setup.py", StringComparison.OrdinalIgnoreCase))
            return ParseSetupPy(content, fileName);

        if (name.Equals("Pipfile", StringComparison.OrdinalIgnoreCase))
            return ParsePipfile(content, fileName);

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

        // <PackageReference Include="X" Version="Y" />, self-closing or with a body
        // that may carry a child <Version>Y</Version> element (story #255).
        foreach (Match m in Regex.Matches(content,
            @"<PackageReference\s+Include=""([^""]+)""([^>]*?)(?:/>|>(.*?)</PackageReference>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var version = VersionFromAttributes(m.Groups[2].Value)
                ?? (m.Groups[3].Success ? VersionFromChildElement(m.Groups[3].Value) : null);
            deps.Add(new PackageDependency
            {
                Name = m.Groups[1].Value,
                Version = version,
                Source = "nuget",
                SourceFile = file
            });
        }

        // Central Package Management: versions live in Directory.Packages.props as
        // <PackageVersion Include="X" Version="Y" />. Without this, CPM repos report
        // null versions for every package (story #255).
        foreach (Match m in Regex.Matches(content,
            @"<PackageVersion\s+Include=""([^""]+)""([^>]*?)/?>", RegexOptions.IgnoreCase))
        {
            deps.Add(new PackageDependency
            {
                Name = m.Groups[1].Value,
                Version = VersionFromAttributes(m.Groups[2].Value),
                Source = "nuget",
                SourceFile = file
            });
        }

        return deps;
    }

    private static string? VersionFromAttributes(string attributes)
    {
        var m = Regex.Match(attributes, @"Version=""([^""]+)""", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? VersionFromChildElement(string inner)
    {
        var m = Regex.Match(inner, @"<Version>\s*([^<]+?)\s*</Version>", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
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

            // Scope per dependency map. peer/optional were previously ignored (story #257).
            AddNpmScope(root, "dependencies", "runtime", deps, file);
            AddNpmScope(root, "devDependencies", "dev", deps, file);
            AddNpmScope(root, "peerDependencies", "peer", deps, file);
            AddNpmScope(root, "optionalDependencies", "optional", deps, file);
        }
        catch { }
        return deps;
    }

    private static void AddNpmScope(JsonElement root, string property, string scope, List<PackageDependency> deps, string file)
    {
        if (root.TryGetProperty(property, out var map) && map.ValueKind == JsonValueKind.Object)
            foreach (var prop in map.EnumerateObject())
                deps.Add(new PackageDependency { Name = prop.Name, Version = prop.Value.GetString(), Scope = scope, Source = "npm", SourceFile = file });
    }

    // Resolved versions from package-lock.json (lockfileVersion 2/3 "packages" map,
    // or v1 "dependencies" map). Captures direct top-level packages (story #257).
    internal static List<PackageDependency> ParsePackageLockJson(string content, string file)
    {
        var deps = new List<PackageDependency>();
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("packages", out var packages) && packages.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in packages.EnumerateObject())
                {
                    // Keys look like "node_modules/<name>" (direct) or deeper (transitive).
                    // Capture direct entries: a single node_modules segment, optional @scope.
                    var key = prop.Name;
                    if (!key.StartsWith("node_modules/", StringComparison.Ordinal)) continue;
                    var name = key["node_modules/".Length..];
                    if (name.Contains("/node_modules/", StringComparison.Ordinal)) continue; // transitive
                    var version = prop.Value.TryGetProperty("version", out var v) ? v.GetString() : null;
                    var scope = prop.Value.TryGetProperty("dev", out var dev) && dev.ValueKind == JsonValueKind.True ? "dev" : "runtime";
                    deps.Add(new PackageDependency { Name = name, Version = version, Scope = scope, Source = "npm", SourceFile = file });
                }
            }
            else if (root.TryGetProperty("dependencies", out var v1) && v1.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in v1.EnumerateObject())
                {
                    var version = prop.Value.TryGetProperty("version", out var v) ? v.GetString() : null;
                    var scope = prop.Value.TryGetProperty("dev", out var dev) && dev.ValueKind == JsonValueKind.True ? "dev" : "runtime";
                    deps.Add(new PackageDependency { Name = prop.Name, Version = version, Scope = scope, Source = "npm", SourceFile = file });
                }
            }
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
            // Skip blanks, comments, and option lines (-r, -c, -e, --hash, ...).
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.StartsWith('-')) continue;

            // Strip inline comment and environment marker (`pkg==1 ; python_version<'3.8'`).
            var hash = trimmed.IndexOf(" #", StringComparison.Ordinal);
            if (hash >= 0) trimmed = trimmed[..hash].Trim();
            var semi = trimmed.IndexOf(';');
            if (semi >= 0) trimmed = trimmed[..semi].Trim();
            if (trimmed.Length == 0) continue;

            // URL form: `name @ https://...` keeps the name, drops the URL as version.
            var at = trimmed.IndexOf(" @ ", StringComparison.Ordinal);
            if (at >= 0)
            {
                var urlName = StripExtras(trimmed[..at].Trim());
                if (urlName.Length > 0)
                    deps.Add(new PackageDependency { Name = urlName, Version = null, Source = "pypi", SourceFile = file });
                continue;
            }

            // name[extra1,extra2]<version specifier>
            var match = Regex.Match(trimmed, @"^([a-zA-Z0-9_.-]+)(\[[^\]]*\])?\s*([><=!~].*)?$");
            if (match.Success)
            {
                deps.Add(new PackageDependency
                {
                    Name = match.Groups[1].Value,
                    Version = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null,
                    Source = "pypi",
                    SourceFile = file
                });
            }
        }
        return deps;
    }

    private static string StripExtras(string name)
    {
        var bracket = name.IndexOf('[');
        return bracket >= 0 ? name[..bracket] : name;
    }

    // Handles PEP 621 ([project] dependencies / optional-dependencies arrays) and
    // Poetry ([tool.poetry.*dependencies] key = value tables) (story #256).
    internal static List<PackageDependency> ParsePyprojectToml(string content, string file)
    {
        var deps = new List<PackageDependency>();
        var section = "";
        var inPep621Array = false;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line;
                inPep621Array = false;
                continue;
            }

            if (section == "[project]" && Regex.IsMatch(line, @"^dependencies\s*=\s*\["))
            {
                AddQuotedPyEntries(line, "runtime", deps, file);
                inPep621Array = !line.Contains(']');
                continue;
            }
            if (inPep621Array)
            {
                AddQuotedPyEntries(line, "runtime", deps, file);
                if (line.Contains(']')) inPep621Array = false;
                continue;
            }

            // [project.optional-dependencies] is a table of arrays: extra = ["pkg>=1"].
            if (section == "[project.optional-dependencies]")
            {
                AddQuotedPyEntries(line, "optional", deps, file);
                continue;
            }

            // Poetry tables: [tool.poetry.dependencies], .dev-dependencies, .group.X.dependencies
            if (section.StartsWith("[tool.poetry") && section.EndsWith("dependencies]"))
            {
                var m = Regex.Match(line, @"^([A-Za-z0-9_.\-]+)\s*=\s*(.+)$");
                if (m.Success && !m.Groups[1].Value.Equals("python", StringComparison.OrdinalIgnoreCase))
                {
                    var scope = section.Contains("dev") || section.Contains("group") ? "dev" : "runtime";
                    deps.Add(new PackageDependency
                    {
                        Name = m.Groups[1].Value,
                        Version = ExtractTomlVersion(m.Groups[2].Value),
                        Scope = scope,
                        Source = "pypi",
                        SourceFile = file
                    });
                }
            }
        }
        return deps;
    }

    internal static List<PackageDependency> ParseSetupPy(string content, string file)
    {
        var deps = new List<PackageDependency>();
        var m = Regex.Match(content, @"install_requires\s*=\s*\[(.*?)\]", RegexOptions.Singleline);
        if (m.Success)
            AddQuotedPyEntries(m.Groups[1].Value, "runtime", deps, file);
        return deps;
    }

    internal static List<PackageDependency> ParsePipfile(string content, string file)
    {
        var deps = new List<PackageDependency>();
        var section = "";
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line; continue; }

            if (section is "[packages]" or "[dev-packages]")
            {
                var m = Regex.Match(line, @"^([A-Za-z0-9_.\-]+)\s*=\s*(.+)$");
                if (m.Success)
                {
                    var version = ExtractTomlVersion(m.Groups[2].Value);
                    deps.Add(new PackageDependency
                    {
                        Name = m.Groups[1].Value,
                        Version = version == "*" ? null : version,
                        Scope = section == "[dev-packages]" ? "dev" : "runtime",
                        Source = "pypi",
                        SourceFile = file
                    });
                }
            }
        }
        return deps;
    }

    // Extracts dependency specs from quoted strings in a line/block: "pkg[extra]>=1.0".
    private static void AddQuotedPyEntries(string text, string scope, List<PackageDependency> deps, string file)
    {
        foreach (Match q in Regex.Matches(text, @"[""']([^""']+)[""']"))
        {
            var entry = q.Groups[1].Value.Trim();
            var m = Regex.Match(entry, @"^([a-zA-Z0-9_.-]+)(\[[^\]]*\])?\s*([><=!~].*)?$");
            if (m.Success && m.Groups[1].Value.Length > 1)
            {
                deps.Add(new PackageDependency
                {
                    Name = m.Groups[1].Value,
                    Version = m.Groups[3].Success ? m.Groups[3].Value.Trim() : null,
                    Scope = scope,
                    Source = "pypi",
                    SourceFile = file
                });
            }
        }
    }

    // Poetry/Pipfile value can be a string ("^1.0") or an inline table ({ version = "^1.0", ... }).
    private static string? ExtractTomlVersion(string value)
    {
        var table = Regex.Match(value, @"version\s*=\s*[""']([^""']+)[""']");
        if (table.Success) return table.Groups[1].Value;
        var str = Regex.Match(value, @"^[""']([^""']+)[""']");
        return str.Success ? str.Groups[1].Value : null;
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
