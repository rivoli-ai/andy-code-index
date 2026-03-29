namespace Andy.CodeIndex.Application.Options;

public class DocumentParsingOptions
{
    public const string SectionName = "Indexing:DocumentParsing";

    public bool Enabled { get; set; } = true;
    public PdfParsingOptions Pdf { get; set; } = new();
}

public class PdfParsingOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxPages { get; set; } = 100;
}
