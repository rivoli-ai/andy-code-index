export interface Enrichment {
  id: string;
  repositoryId: string;
  commitId?: string;
  type: string;
  subtype: string;
  title?: string;
  content: string;
  filePath?: string;
  startLine?: number;
  endLine?: number;
  language?: string;
  createdAt: string;
}

export interface EnrichmentListResponse {
  results: Enrichment[];
  totalCount: number;
  offset: number;
  limit: number;
}
