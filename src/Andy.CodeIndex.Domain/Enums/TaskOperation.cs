namespace Andy.CodeIndex.Domain.Enums;

public enum TaskOperation
{
    CloneRepository,
    SyncRepository,
    DeleteRepository,
    ScanCommit,
    RescanCommit,
    ExtractSnippets,
    CreateBM25Index,
    CreateCodeEmbeddings,
    CreateSummaryEnrichments,
    CreateSummaryEmbeddings,
    CreatePublicAPIDocs,
    CreateArchitectureDocs,
    CreateDatabaseSchema,
    CreateCommitDescription,
    CreateCookbook,
    CreateWiki,
    ExtractDependencies,
    ExtractCommitHistory,
    CreateOwnershipDocs,
    CreateSecurityDocs,
    CreateOperationsDocs,
    CreateQualityDocs,
    ExtractDocumentText,
    CreateTechStack,
    CreateInsights
}
