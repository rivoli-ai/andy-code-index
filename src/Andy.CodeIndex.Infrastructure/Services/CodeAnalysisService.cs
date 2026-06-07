using System.Text;
using System.Text.RegularExpressions;
using Andy.CodeIndex.Application.Interfaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
            if (cls.Summary is not null)
                sb.AppendLine($"_{cls.Summary}_  ");
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
                    sb.AppendLine($"- `{prefix}{staticMod}{method.ReturnType} {method.Name}({paramStr})`"
                        + (method.Summary is not null ? $" — {method.Summary}" : ""));
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

    // Roslyn-backed C# analysis. Replaces the previous regex engine, which
    // missed records/structs, mis-attributed methods in nested/multi-class
    // files (it appended every method to the most-recent class), missed
    // expression-bodied members, and could not read XML doc comments. The
    // syntax tree owns each member under its declaring type, so attribution is
    // exact. (epic #243 / story #247)
    internal static void AnalyzeCSharp(string content, CodeAnalysisResult result)
    {
        var root = CSharpSyntaxTree.ParseText(content).GetRoot();

        // DescendantNodes yields nested types too; each type's Members contains
        // only its *direct* members, so nested-type members are attributed to
        // the nested type rather than leaking to the outer one.
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case InterfaceDeclarationSyntax iface:
                    result.Interfaces.Add(BuildInterface(iface));
                    break;
                // RecordDeclarationSyntax covers both `record` and `record struct`.
                case RecordDeclarationSyntax rec:
                    result.Classes.Add(BuildClass(rec, rec.ParameterList));
                    break;
                case ClassDeclarationSyntax cls:
                    result.Classes.Add(BuildClass(cls, parameterList: null));
                    break;
                case StructDeclarationSyntax st:
                    result.Classes.Add(BuildClass(st, parameterList: null));
                    break;
                case EnumDeclarationSyntax en:
                    result.Enums.Add(BuildEnum(en));
                    break;
            }
        }
    }

    private static ApiClass BuildClass(TypeDeclarationSyntax type, ParameterListSyntax? parameterList)
    {
        var cls = new ApiClass { Name = type.Identifier.Text, Summary = GetSummary(type) };
        SplitBaseList(type.BaseList, b => cls.BaseClass = b, cls.ImplementedInterfaces);

        // Positional record parameters become public init-only properties.
        if (parameterList is not null)
        {
            foreach (var p in parameterList.Parameters)
            {
                cls.Properties.Add(new ApiProperty
                {
                    Name = p.Identifier.Text,
                    Type = p.Type?.ToString() ?? "var",
                    AccessModifier = "public",
                    HasGetter = true,
                    HasSetter = false
                });
            }
        }

        AddMembers(type.Members, cls.Methods, cls.Properties, interfaceDefaults: false);
        return cls;
    }

    private static ApiInterface BuildInterface(InterfaceDeclarationSyntax type)
    {
        var iface = new ApiInterface { Name = type.Identifier.Text, Summary = GetSummary(type) };
        AddMembers(type.Members, iface.Methods, iface.Properties, interfaceDefaults: true);
        return iface;
    }

    private static ApiEnum BuildEnum(EnumDeclarationSyntax type) => new()
    {
        Name = type.Identifier.Text,
        Summary = GetSummary(type),
        Values = type.Members.Select(m => m.Identifier.Text).ToList()
    };

    private static void AddMembers(
        SyntaxList<MemberDeclarationSyntax> members,
        List<ApiMethod> methods, List<ApiProperty> properties, bool interfaceDefaults)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax m:
                {
                    var access = Accessibility(m.Modifiers, interfaceDefaults);
                    if (IsPrivate(access)) break;
                    var method = new ApiMethod
                    {
                        Name = m.Identifier.Text,
                        ReturnType = m.ReturnType.ToString(),
                        AccessModifier = access,
                        IsStatic = m.Modifiers.Any(SyntaxKind.StaticKeyword),
                        IsAsync = m.Modifiers.Any(SyntaxKind.AsyncKeyword),
                        Summary = GetSummary(m)
                    };
                    foreach (var p in m.ParameterList.Parameters)
                    {
                        method.Parameters.Add(new ApiParameter
                        {
                            Name = p.Identifier.Text,
                            Type = p.Type?.ToString() ?? "var",
                            DefaultValue = p.Default?.Value.ToString()
                        });
                    }
                    methods.Add(method);
                    break;
                }
                case PropertyDeclarationSyntax p:
                {
                    var access = Accessibility(p.Modifiers, interfaceDefaults);
                    if (IsPrivate(access)) break;
                    // Expression-bodied properties (`=> ...`) are getter-only.
                    var hasSetter = p.ExpressionBody is null &&
                        (p.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)
                            || a.IsKind(SyntaxKind.InitAccessorDeclaration)) ?? false);
                    properties.Add(new ApiProperty
                    {
                        Name = p.Identifier.Text,
                        Type = p.Type.ToString(),
                        AccessModifier = access,
                        HasGetter = true,
                        HasSetter = hasSetter,
                        Summary = GetSummary(p)
                    });
                    break;
                }
            }
        }
    }

    // Splits a base list into the base class (first non-interface entry) and
    // interfaces. Without a full semantic model a single file cannot resolve
    // whether a base type is a class or interface, so we use the .NET naming
    // convention (interfaces are `I` + UpperCamelCase). C# also requires the
    // base class, if present, to appear first.
    private static void SplitBaseList(BaseListSyntax? baseList, Action<string> setBaseClass, List<string> interfaces)
    {
        if (baseList is null) return;
        foreach (var entry in baseList.Types)
        {
            var name = entry.Type.ToString();
            if (LooksLikeInterface(name))
                interfaces.Add(name);
            else
                setBaseClass(name);
        }
    }

    private static bool LooksLikeInterface(string name) =>
        name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]);

    private static string Accessibility(SyntaxTokenList modifiers, bool interfaceDefaults)
    {
        var isPublic = modifiers.Any(SyntaxKind.PublicKeyword);
        var isProtected = modifiers.Any(SyntaxKind.ProtectedKeyword);
        var isInternal = modifiers.Any(SyntaxKind.InternalKeyword);
        var isPrivate = modifiers.Any(SyntaxKind.PrivateKeyword);

        if (isProtected && isInternal) return "protected internal";
        if (isPrivate && isProtected) return "private protected";
        if (isPublic) return "public";
        if (isProtected) return "protected";
        if (isInternal) return "internal";
        if (isPrivate) return "private";
        // No explicit modifier: interface members are public, type members private.
        return interfaceDefaults ? "public" : "private";
    }

    private static bool IsPrivate(string access) => access is "private" or "private protected";

    private static string? GetSummary(SyntaxNode node)
    {
        var doc = node.GetLeadingTrivia()
            .Select(t => t.GetStructure())
            .OfType<DocumentationCommentTriviaSyntax>()
            .FirstOrDefault();

        var summary = doc?.Content.OfType<XmlElementSyntax>()
            .FirstOrDefault(e => e.StartTag.Name.LocalName.Text == "summary");
        if (summary is null) return null;

        // Strip `///` exteriors and collapse whitespace into a single line.
        var text = summary.Content.ToFullString();
        text = Regex.Replace(text, @"///", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return string.IsNullOrEmpty(text) ? null : text;
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
}
