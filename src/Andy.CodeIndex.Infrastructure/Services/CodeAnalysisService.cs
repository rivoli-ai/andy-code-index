using System.Text;
using System.Text.RegularExpressions;
using Andy.CodeIndex.Application.Interfaces;

namespace Andy.CodeIndex.Infrastructure.Services;

public class CodeAnalysisService : ICodeAnalysisService
{
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "csharp", "typescript", "python", "go", "java", "javascript"
    };

    public bool SupportsLanguage(string language) => SupportedLanguages.Contains(language);

    public CodeAnalysisResult Analyze(string content, string filePath, string language)
    {
        var result = new CodeAnalysisResult { FilePath = filePath, Language = language };

        switch (language.ToLowerInvariant())
        {
            case "csharp":
                AnalyzeCSharp(content, result);
                break;
            case "typescript":
            case "javascript":
                AnalyzeTypeScript(content, result);
                break;
            case "python":
                AnalyzePython(content, result);
                break;
            case "go":
                AnalyzeGo(content, result);
                break;
            case "java":
                AnalyzeJava(content, result);
                break;
        }

        return result;
    }

    public string GenerateApiDocs(CodeAnalysisResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# API Documentation: {Path.GetFileName(result.FilePath)}");
        sb.AppendLine($"**Language:** {result.Language}  ");
        sb.AppendLine($"**File:** `{result.FilePath}`");
        sb.AppendLine();

        foreach (var cls in result.Classes)
        {
            sb.AppendLine($"## Class: `{cls.Name}`");
            if (cls.BaseClass is not null)
                sb.AppendLine($"**Extends:** `{cls.BaseClass}`  ");
            if (cls.ImplementedInterfaces.Count > 0)
                sb.AppendLine($"**Implements:** {string.Join(", ", cls.ImplementedInterfaces.Select(i => $"`{i}`"))}  ");
            sb.AppendLine();

            if (cls.Properties.Count > 0)
            {
                sb.AppendLine("### Properties");
                foreach (var prop in cls.Properties)
                    sb.AppendLine($"- `{prop.Type} {prop.Name}` {(prop.AccessModifier is not null ? $"({prop.AccessModifier})" : "")}");
                sb.AppendLine();
            }

            if (cls.Methods.Count > 0)
            {
                sb.AppendLine("### Methods");
                foreach (var method in cls.Methods)
                {
                    var paramStr = string.Join(", ", method.Parameters.Select(p =>
                        p.DefaultValue is not null ? $"{p.Type} {p.Name} = {p.DefaultValue}" : $"{p.Type} {p.Name}"));
                    var prefix = method.IsAsync ? "async " : "";
                    var staticMod = method.IsStatic ? "static " : "";
                    sb.AppendLine($"- `{prefix}{staticMod}{method.ReturnType} {method.Name}({paramStr})`");
                }
                sb.AppendLine();
            }
        }

        foreach (var iface in result.Interfaces)
        {
            sb.AppendLine($"## Interface: `{iface.Name}`");
            sb.AppendLine();

            foreach (var method in iface.Methods)
            {
                var paramStr = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
                sb.AppendLine($"- `{method.ReturnType} {method.Name}({paramStr})`");
            }

            foreach (var prop in iface.Properties)
                sb.AppendLine($"- `{prop.Type} {prop.Name}`");

            sb.AppendLine();
        }

        foreach (var func in result.Functions)
        {
            var paramStr = string.Join(", ", func.Parameters.Select(p => $"{p.Type} {p.Name}"));
            var export = func.IsExported ? "exported " : "";
            sb.AppendLine($"### {export}Function: `{func.ReturnType} {func.Name}({paramStr})`");
        }

        foreach (var e in result.Enums)
        {
            sb.AppendLine($"## Enum: `{e.Name}`");
            sb.AppendLine($"Values: {string.Join(", ", e.Values.Select(v => $"`{v}`"))}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    internal static void AnalyzeCSharp(string content, CodeAnalysisResult result)
    {
        // Classes
        foreach (Match m in Regex.Matches(content,
            @"(public|internal)\s+(abstract\s+|static\s+|sealed\s+)*class\s+(\w+)(?:\s*:\s*([^{]+))?"))
        {
            var cls = new ApiClass { Name = m.Groups[3].Value };
            if (m.Groups[4].Success)
            {
                var bases = m.Groups[4].Value.Split(',').Select(b => b.Trim()).ToList();
                if (bases.Count > 0 && !bases[0].StartsWith("I"))
                    cls.BaseClass = bases[0];
                cls.ImplementedInterfaces = bases.Where(b => b.StartsWith("I") && b.Length > 1 && char.IsUpper(b[1])).ToList();
            }
            result.Classes.Add(cls);
        }

        // Interfaces
        foreach (Match m in Regex.Matches(content,
            @"(public|internal)\s+interface\s+(I\w+)"))
        {
            result.Interfaces.Add(new ApiInterface { Name = m.Groups[2].Value });
        }

        // Public methods
        foreach (Match m in Regex.Matches(content,
            @"(public|protected|internal)\s+(static\s+)?(async\s+)?([\w<>\[\],\s\?]+?)\s+(\w+)\s*\(([^)]*)\)"))
        {
            var method = new ApiMethod
            {
                Name = m.Groups[5].Value,
                ReturnType = m.Groups[4].Value.Trim(),
                AccessModifier = m.Groups[1].Value,
                IsStatic = m.Groups[2].Success,
                IsAsync = m.Groups[3].Success
            };

            if (m.Groups[6].Success && !string.IsNullOrWhiteSpace(m.Groups[6].Value))
            {
                foreach (var param in SplitParameters(m.Groups[6].Value))
                {
                    var parts = param.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        method.Parameters.Add(new ApiParameter
                        {
                            Type = string.Join(" ", parts[..^1]),
                            Name = parts[^1].TrimEnd(',')
                        });
                    }
                }
            }

            // Attach to most recent class if any
            if (result.Classes.Count > 0)
                result.Classes[^1].Methods.Add(method);
        }

        // Public properties
        foreach (Match m in Regex.Matches(content,
            @"(public|protected|internal)\s+(required\s+)?([\w<>\[\],\?\s]+?)\s+(\w+)\s*\{"))
        {
            var name = m.Groups[4].Value;
            if (name is "class" or "interface" or "enum" or "struct" or "void") continue;

            var prop = new ApiProperty
            {
                Name = name,
                Type = m.Groups[3].Value.Trim(),
                AccessModifier = m.Groups[1].Value,
                HasGetter = true,
                HasSetter = true
            };

            if (result.Classes.Count > 0)
                result.Classes[^1].Properties.Add(prop);
        }

        // Enums
        foreach (Match m in Regex.Matches(content,
            @"(public|internal)\s+enum\s+(\w+)\s*\{([^}]+)\}"))
        {
            var values = m.Groups[3].Value.Split(',')
                .Select(v => v.Trim().Split('=')[0].Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
            result.Enums.Add(new ApiEnum { Name = m.Groups[2].Value, Values = values });
        }
    }

    internal static void AnalyzeTypeScript(string content, CodeAnalysisResult result)
    {
        // Exported classes
        foreach (Match m in Regex.Matches(content,
            @"export\s+class\s+(\w+)(?:\s+extends\s+(\w+))?(?:\s+implements\s+([\w,\s]+))?"))
        {
            var cls = new ApiClass { Name = m.Groups[1].Value, BaseClass = m.Groups[2].Success ? m.Groups[2].Value : null };
            if (m.Groups[3].Success)
                cls.ImplementedInterfaces = m.Groups[3].Value.Split(',').Select(i => i.Trim()).ToList();
            result.Classes.Add(cls);
        }

        // Exported interfaces
        foreach (Match m in Regex.Matches(content,
            @"export\s+interface\s+(\w+)"))
        {
            result.Interfaces.Add(new ApiInterface { Name = m.Groups[1].Value });
        }

        // Exported functions
        foreach (Match m in Regex.Matches(content,
            @"export\s+(async\s+)?function\s+(\w+)\s*\(([^)]*)\)(?:\s*:\s*([\w<>\[\]|&\s]+))?"))
        {
            var func = new ApiFunction
            {
                Name = m.Groups[2].Value,
                ReturnType = m.Groups[4].Success ? m.Groups[4].Value.Trim() : "void",
                IsExported = true
            };
            result.Functions.Add(func);
        }

        // Exported type aliases/enums
        foreach (Match m in Regex.Matches(content,
            @"export\s+enum\s+(\w+)\s*\{([^}]+)\}"))
        {
            var values = m.Groups[2].Value.Split(',')
                .Select(v => v.Trim().Split('=')[0].Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();
            result.Enums.Add(new ApiEnum { Name = m.Groups[1].Value, Values = values });
        }
    }

    internal static void AnalyzePython(string content, CodeAnalysisResult result)
    {
        // Classes
        foreach (Match m in Regex.Matches(content,
            @"^class\s+(\w+)(?:\(([^)]+)\))?:", RegexOptions.Multiline))
        {
            var cls = new ApiClass { Name = m.Groups[1].Value };
            if (m.Groups[2].Success)
            {
                var bases = m.Groups[2].Value.Split(',').Select(b => b.Trim()).ToList();
                if (bases.Count > 0 && bases[0] != "object")
                    cls.BaseClass = bases[0];
            }
            result.Classes.Add(cls);
        }

        // Functions (module-level)
        foreach (Match m in Regex.Matches(content,
            @"^def\s+(\w+)\s*\(([^)]*)\)(?:\s*->\s*([\w\[\],\s]+))?:", RegexOptions.Multiline))
        {
            var name = m.Groups[1].Value;
            if (name.StartsWith('_')) continue; // Skip private

            result.Functions.Add(new ApiFunction
            {
                Name = name,
                ReturnType = m.Groups[3].Success ? m.Groups[3].Value.Trim() : "None",
                IsExported = true
            });
        }
    }

    internal static void AnalyzeGo(string content, CodeAnalysisResult result)
    {
        // Exported types (structs)
        foreach (Match m in Regex.Matches(content,
            @"type\s+([A-Z]\w+)\s+struct\s*\{"))
        {
            result.Classes.Add(new ApiClass { Name = m.Groups[1].Value });
        }

        // Exported interfaces
        foreach (Match m in Regex.Matches(content,
            @"type\s+([A-Z]\w+)\s+interface\s*\{"))
        {
            result.Interfaces.Add(new ApiInterface { Name = m.Groups[1].Value });
        }

        // Exported functions
        foreach (Match m in Regex.Matches(content,
            @"^func\s+(\([^)]+\)\s+)?([A-Z]\w+)\s*\(([^)]*)\)(?:\s*(?:\(([^)]+)\)|([\w*\[\]]+)))?",
            RegexOptions.Multiline))
        {
            var name = m.Groups[2].Value;
            var returnType = m.Groups[4].Success ? m.Groups[4].Value.Trim()
                           : m.Groups[5].Success ? m.Groups[5].Value.Trim()
                           : "void";

            result.Functions.Add(new ApiFunction
            {
                Name = name,
                ReturnType = returnType,
                IsExported = true
            });
        }
    }

    internal static void AnalyzeJava(string content, CodeAnalysisResult result)
    {
        // Classes
        foreach (Match m in Regex.Matches(content,
            @"public\s+(abstract\s+)?class\s+(\w+)(?:\s+extends\s+(\w+))?(?:\s+implements\s+([\w,\s]+))?"))
        {
            var cls = new ApiClass { Name = m.Groups[2].Value, BaseClass = m.Groups[3].Success ? m.Groups[3].Value : null };
            if (m.Groups[4].Success)
                cls.ImplementedInterfaces = m.Groups[4].Value.Split(',').Select(i => i.Trim()).ToList();
            result.Classes.Add(cls);
        }

        // Interfaces
        foreach (Match m in Regex.Matches(content,
            @"public\s+interface\s+(\w+)"))
        {
            result.Interfaces.Add(new ApiInterface { Name = m.Groups[1].Value });
        }

        // Enums
        foreach (Match m in Regex.Matches(content,
            @"public\s+enum\s+(\w+)\s*\{([^}]+)\}"))
        {
            var values = m.Groups[2].Value.Split(',')
                .Select(v => v.Trim().Split('(')[0].Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v) && v != ";")
                .ToList();
            result.Enums.Add(new ApiEnum { Name = m.Groups[1].Value, Values = values });
        }
    }

    private static List<string> SplitParameters(string paramString)
    {
        // Simple split handling generic types like List<string>
        var result = new List<string>();
        var depth = 0;
        var current = new StringBuilder();

        foreach (var ch in paramString)
        {
            if (ch == '<') depth++;
            else if (ch == '>') depth--;

            if (ch == ',' && depth == 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}
