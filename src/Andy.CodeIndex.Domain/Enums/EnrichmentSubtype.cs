namespace Andy.CodeIndex.Domain.Enums;

public enum EnrichmentSubtype
{
    // Architecture
    Physical,
    DatabaseSchema,

    // Development
    Chunk,
    Snippet,
    SnippetSummary,
    Example,
    ExampleSummary,

    // History
    CommitDescription,

    // Usage
    Cookbook,
    APIDocs,
    Wiki,

    // Architecture
    Dependencies,

    // History
    CommitHistory,

    // Cross-cutting
    Ownership,
    Security,
    Operations,
    Quality,

    // Documents
    DocumentText,

    // Insights
    FeatureMap,
    ArchitectureAnalysis,
    DesignAnalysis,
    ImplementationAnalysis,
    DependencyAnalysis,
    TestAnalysis,
    SecurityAnalysis,
    DeploymentAnalysis,
    OperationsAnalysis,
    LocalSetupGuide,
    InsightReport,
    TechStack
}
