namespace Andy.CodeIndex.Application.Interfaces;

public interface ICodeAnalysisService
{
    CodeAnalysisResult Analyze(string content, string filePath, string language);
    string GenerateApiDocs(CodeAnalysisResult result);
    bool SupportsLanguage(string language);

    /// <summary>
    /// Returns the 1-based source line at which each top-level type/method/function
    /// declaration begins, with a short context label (e.g. "Type.Method"). Used by
    /// the chunker to align chunk boundaries to declarations instead of arbitrary
    /// size cuts. Returns an empty list for languages without a structural parser.
    /// </summary>
    IReadOnlyList<StructuralBoundary> GetStructuralBoundaries(string content, string language);
}

/// <summary>A declaration boundary: the line it starts on and an enclosing-context label.</summary>
public readonly record struct StructuralBoundary(int Line, string Context);

public class CodeAnalysisResult
{
    public required string FilePath { get; set; }
    public required string Language { get; set; }
    public List<ApiClass> Classes { get; set; } = [];
    public List<ApiInterface> Interfaces { get; set; } = [];
    public List<ApiFunction> Functions { get; set; } = [];
    public List<ApiEnum> Enums { get; set; } = [];
}

public class ApiClass
{
    public required string Name { get; set; }
    public string? BaseClass { get; set; }
    public List<string> ImplementedInterfaces { get; set; } = [];
    public List<ApiMethod> Methods { get; set; } = [];
    public List<ApiProperty> Properties { get; set; } = [];
    public string? Summary { get; set; }
}

public class ApiInterface
{
    public required string Name { get; set; }
    public List<ApiMethod> Methods { get; set; } = [];
    public List<ApiProperty> Properties { get; set; } = [];
    public string? Summary { get; set; }
}

public class ApiMethod
{
    public required string Name { get; set; }
    public required string ReturnType { get; set; }
    public List<ApiParameter> Parameters { get; set; } = [];
    public string? AccessModifier { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAsync { get; set; }
    public string? Summary { get; set; }
}

public class ApiProperty
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? AccessModifier { get; set; }
    public bool HasGetter { get; set; }
    public bool HasSetter { get; set; }
    public string? Summary { get; set; }
}

public class ApiParameter
{
    public required string Name { get; set; }
    public required string Type { get; set; }
    public string? DefaultValue { get; set; }
}

public class ApiFunction
{
    public required string Name { get; set; }
    public required string ReturnType { get; set; }
    public List<ApiParameter> Parameters { get; set; } = [];
    public bool IsExported { get; set; }
    public bool IsAsync { get; set; }
    public string? Summary { get; set; }
}

public class ApiEnum
{
    public required string Name { get; set; }
    public List<string> Values { get; set; } = [];
    public string? Summary { get; set; }
}
