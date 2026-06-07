using System.Text;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
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
            case "javascript":
                AnalyzeJavaScript(content, result);
                break;
            case "typescript":
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

    // Acornima-backed JavaScript analysis (real AST). Replaces routing JS
    // through the TypeScript regex, which missed arrow-function exports,
    // `export default`, and class methods entirely. Acornima parses ECMAScript +
    // JSX but not TypeScript type syntax, so .ts stays on the heuristic path.
    // (epic #243 / story #248)
    internal static void AnalyzeJavaScript(string content, CodeAnalysisResult result)
    {
        Module program;
        try
        {
            program = new Parser().ParseModule(content);
        }
        catch (Exception)
        {
            // Acornima throws on input it cannot parse (e.g. TS-flavored JS);
            // fall back to the heuristic analyzer so we still extract something.
            AnalyzeTypeScript(content, result);
            return;
        }

        foreach (var stmt in program.Body)
        {
            switch (stmt)
            {
                case ExportNamedDeclaration { Declaration: { } named }:
                    AddJsDeclaration(named, result, exported: true);
                    break;
                case ExportDefaultDeclaration def:
                    AddJsDeclaration(def.Declaration, result, exported: true);
                    break;
                default:
                    AddJsDeclaration(stmt, result, exported: false);
                    break;
            }
        }
    }

    private static void AddJsDeclaration(Node node, CodeAnalysisResult result, bool exported)
    {
        switch (node)
        {
            case ClassDeclaration cls:
                result.Classes.Add(BuildJsClass(cls));
                break;
            case FunctionDeclaration fn:
                result.Functions.Add(new ApiFunction
                {
                    Name = fn.Id?.Name ?? "default",
                    ReturnType = "any",
                    IsExported = exported,
                    IsAsync = fn.Async,
                    Parameters = JsParameters(fn.Params)
                });
                break;
            case VariableDeclaration vd:
                foreach (var d in vd.Declarations)
                {
                    // export const foo = () => {} / = function () {}
                    if (d.Init is IFunction fnExpr && d.Id is Identifier id)
                    {
                        result.Functions.Add(new ApiFunction
                        {
                            Name = id.Name,
                            ReturnType = "any",
                            IsExported = exported,
                            IsAsync = fnExpr.Async,
                            Parameters = JsParameters(fnExpr.Params)
                        });
                    }
                }
                break;
        }
    }

    private static ApiClass BuildJsClass(ClassDeclaration cls)
    {
        var apiClass = new ApiClass { Name = cls.Id?.Name ?? "default" };
        if (cls.SuperClass is Identifier super)
            apiClass.BaseClass = super.Name;

        foreach (var member in cls.Body.Body)
        {
            if (member is MethodDefinition { Key: Identifier key } md && md.Value is { } fn)
            {
                apiClass.Methods.Add(new ApiMethod
                {
                    Name = key.Name,
                    ReturnType = "any",
                    AccessModifier = "public",
                    IsStatic = md.Static,
                    IsAsync = fn.Async,
                    Parameters = JsParameters(fn.Params)
                });
            }
        }
        return apiClass;
    }

    private static List<ApiParameter> JsParameters(in NodeList<Node> parameters)
    {
        var list = new List<ApiParameter>();
        foreach (var p in parameters)
        {
            switch (p)
            {
                case Identifier id:
                    list.Add(new ApiParameter { Name = id.Name, Type = "any" });
                    break;
                case AssignmentPattern { Left: Identifier lid } ap:
                    list.Add(new ApiParameter { Name = lid.Name, Type = "any", DefaultValue = ap.Right.ToString() });
                    break;
                case RestElement { Argument: Identifier rid }:
                    list.Add(new ApiParameter { Name = "..." + rid.Name, Type = "any" });
                    break;
            }
        }
        return list;
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

        // Exported arrow-function consts: `export const f = (...) => ...` and
        // `export const f = async (...) => ...`. Previously missed entirely
        // despite being the dominant modern export style (story #248).
        foreach (Match m in Regex.Matches(content,
            @"export\s+const\s+(\w+)\s*(?::\s*[^=\n]+?)?=\s*(?:async\s+)?(?:\([^)]*\)|[\w$]+)[^=\n;{]*=>"))
        {
            if (result.Functions.Any(f => f.Name == m.Groups[1].Value)) continue;
            result.Functions.Add(new ApiFunction { Name = m.Groups[1].Value, ReturnType = "any", IsExported = true });
        }

        // export default class / function
        foreach (Match m in Regex.Matches(content,
            @"export\s+default\s+(?:abstract\s+)?class\s+(\w+)(?:\s+extends\s+(\w+))?"))
        {
            if (result.Classes.Any(c => c.Name == m.Groups[1].Value)) continue;
            result.Classes.Add(new ApiClass { Name = m.Groups[1].Value, BaseClass = m.Groups[2].Success ? m.Groups[2].Value : null });
        }
        foreach (Match m in Regex.Matches(content,
            @"export\s+default\s+(?:async\s+)?function\s+(\w+)"))
        {
            if (result.Functions.Any(f => f.Name == m.Groups[1].Value)) continue;
            result.Functions.Add(new ApiFunction { Name = m.Groups[1].Value, ReturnType = "any", IsExported = true });
        }
    }

    // Indentation-aware Python analysis. The previous regex only matched
    // column-0 `class`/`def`, so it dropped every method inside a class and
    // every `async def`. This line scanner tracks the enclosing class by indent
    // and understands async defs, decorators (@staticmethod/@classmethod →
    // static, @property → property), and return annotations. (story #248)
    internal static void AnalyzePython(string content, CodeAnalysisResult result)
    {
        var lines = content.Replace("\r\n", "\n").Replace("\t", "    ").Split('\n');
        ApiClass? currentClass = null;
        var classIndent = -1;
        var decorators = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var trimmed = line.Trim();
            var indent = line.Length - line.TrimStart(' ').Length;

            // Dedent past the class body closes the class (decorators belong to
            // the member that follows, so they don't trigger a close).
            if (currentClass is not null && indent <= classIndent && !trimmed.StartsWith('@'))
            {
                currentClass = null;
                classIndent = -1;
            }

            if (trimmed.StartsWith('@'))
            {
                decorators.Add(trimmed.TrimStart('@'));
                continue;
            }

            var classMatch = Regex.Match(trimmed, @"^class\s+(\w+)\s*(?:\(([^)]*)\))?\s*:");
            if (classMatch.Success)
            {
                var cls = new ApiClass { Name = classMatch.Groups[1].Value };
                if (classMatch.Groups[2].Success)
                {
                    var bases = classMatch.Groups[2].Value.Split(',')
                        .Select(b => b.Trim()).Where(b => b.Length > 0).ToList();
                    if (bases.Count > 0 && bases[0] != "object")
                        cls.BaseClass = bases[0];
                }
                result.Classes.Add(cls);
                currentClass = cls;
                classIndent = indent;
                decorators.Clear();
                continue;
            }

            var defMatch = Regex.Match(trimmed, @"^(async\s+)?def\s+(\w+)\s*\((.*)\)\s*(?:->\s*(.+?))?\s*:");
            if (defMatch.Success)
            {
                var isAsync = defMatch.Groups[1].Success;
                var name = defMatch.Groups[2].Value;
                var returnType = defMatch.Groups[4].Success ? defMatch.Groups[4].Value.Trim() : "None";
                var isStatic = decorators.Any(d => d.StartsWith("staticmethod") || d.StartsWith("classmethod"));
                var isProperty = decorators.Any(d => d.StartsWith("property"));

                if (currentClass is not null && indent > classIndent)
                {
                    if (isProperty && IsPublicPy(name))
                    {
                        currentClass.Properties.Add(new ApiProperty
                        {
                            Name = name, Type = returnType, AccessModifier = "public",
                            HasGetter = true, HasSetter = false
                        });
                    }
                    else if (IncludePyMember(name) && !isProperty)
                    {
                        currentClass.Methods.Add(new ApiMethod
                        {
                            Name = name, ReturnType = returnType, AccessModifier = "public",
                            IsAsync = isAsync, IsStatic = isStatic,
                            Parameters = PyParameters(defMatch.Groups[3].Value, skipFirst: !isStatic)
                        });
                    }
                }
                else if (indent == 0 && !name.StartsWith('_'))
                {
                    result.Functions.Add(new ApiFunction
                    {
                        Name = name, ReturnType = returnType, IsExported = true, IsAsync = isAsync,
                        Parameters = PyParameters(defMatch.Groups[3].Value, skipFirst: false)
                    });
                }

                decorators.Clear();
                continue;
            }

            decorators.Clear();
        }
    }

    // Include public methods and dunders (e.g. __init__); skip single-underscore
    // "private" members.
    private static bool IncludePyMember(string name) =>
        !name.StartsWith('_') || (name.StartsWith("__") && name.EndsWith("__"));

    private static bool IsPublicPy(string name) => !name.StartsWith('_');

    private static List<ApiParameter> PyParameters(string paramString, bool skipFirst)
    {
        var list = new List<ApiParameter>();
        var first = true;
        foreach (var part in SplitTopLevel(paramString, ','))
        {
            var token = part.Trim();
            if (token.Length == 0 || token is "*" or "/") continue;
            if (skipFirst && first && (token == "self" || token == "cls"))
            {
                first = false;
                continue;
            }
            first = false;

            var name = token.TrimStart('*');
            string? type = null;
            string? def = null;

            var eq = name.IndexOf('=');
            if (eq >= 0)
            {
                def = name[(eq + 1)..].Trim();
                name = name[..eq];
            }
            var colon = name.IndexOf(':');
            if (colon >= 0)
            {
                type = name[(colon + 1)..].Trim();
                name = name[..colon];
            }
            name = name.Trim();
            if (name.Length == 0) continue;

            list.Add(new ApiParameter { Name = name, Type = type ?? "Any", DefaultValue = def });
        }
        return list;
    }

    // Splits on a separator that is not nested inside (), [], {} or <>.
    private static List<string> SplitTopLevel(string text, char sep)
    {
        var result = new List<string>();
        var depth = 0;
        var current = new StringBuilder();
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '(' or '[' or '{' or '<': depth++; break;
                case ')' or ']' or '}' or '>': depth--; break;
            }
            if (ch == sep && depth <= 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
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
