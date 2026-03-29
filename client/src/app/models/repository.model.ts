export interface Repository {
  id: string;
  name: string;
  url: string;
  provider: string;
  defaultBranch?: string;
  lastIndexedCommitSha?: string;
  lastSyncedAt?: string;
  syncIntervalMinutes?: number | null;
  status: string;
  createdAt: string;
  updatedAt: string;
  stats?: RepositoryStats;
  branches?: Branch[];
  tags?: Tag[];
}

export interface RepositoryStats {
  commitCount: number;
  fileCount: number;
  enrichmentCount: number;
  embeddingCount: number;
  hasEmbeddings: boolean;
  pendingTaskCount: number;
}

export interface Branch {
  name: string;
  headCommitSha?: string;
  isDefault: boolean;
}

export interface Tag {
  name: string;
  commitSha: string;
}

export interface CreateRepositoryRequest {
  url: string;
  personalAccessToken?: string;
}
