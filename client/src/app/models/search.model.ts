export interface SearchResults {
  results: SearchResultItem[];
  totalCount: number;
  searchMode: string;
  durationMs: number;
}

export interface SearchResultItem {
  enrichmentId: string;
  content: string;
  score: number;
  filePath?: string;
  startLine?: number;
  endLine?: number;
  language?: string;
  repositoryId: string;
  repositoryName?: string;
  commitSha?: string;
}

export interface SearchRequest {
  query: string;
  limit?: number;
  languages?: string[];
  repositoryIds?: string[];
  commitSha?: string;
  filePath?: string;
}
