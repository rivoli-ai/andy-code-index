export interface IndexingTask {
  id: string;
  repositoryId: string;
  commitId?: string;
  operation: string;
  status: string;
  progress: number;
  progressMessage?: string;
  errorMessage?: string;
  chainId?: string;
  chainStepIndex?: number;
  chainTotalSteps?: number;
  priority: number;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
}
