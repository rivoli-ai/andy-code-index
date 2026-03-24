export interface Enrichment {
  id: string;
  repositoryId: string;
  repositoryName?: string;
  commitId?: string;
  type: string;
  subtype: string;
  title?: string;
  content: string;
  filePath?: string;
  startLine?: number;
  endLine?: number;
  language?: string;
  quality: number;
  createdAt: string;
}

export interface EnrichmentListResponse {
  results: Enrichment[];
  totalCount: number;
  offset: number;
  limit: number;
}
