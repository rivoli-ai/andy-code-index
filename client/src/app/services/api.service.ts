import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Repository, CreateRepositoryRequest, SparklineData, ActivityHeatmap, StorageStats } from '../models/repository.model';
import { SearchResults, SearchRequest } from '../models/search.model';
import { EnrichmentListResponse, Enrichment } from '../models/enrichment.model';
import { IndexingTask } from '../models/task.model';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) {}

  // Repositories
  getRepositories(): Observable<Repository[]> {
    return this.http.get<Repository[]>(`${this.baseUrl}/repositories`);
  }

  getOrganizations(): Observable<{ name: string; count: number }[]> {
    return this.http.get<{ name: string; count: number }[]>(`${this.baseUrl}/repositories/organizations`);
  }

  getRepository(id: string): Observable<Repository> {
    return this.http.get<Repository>(`${this.baseUrl}/repositories/${id}`);
  }

  createRepository(request: CreateRepositoryRequest): Observable<Repository> {
    return this.http.post<Repository>(`${this.baseUrl}/repositories`, request);
  }

  checkRepositoryUrl(url: string): Observable<{ tracked: boolean; existingRepositoryId?: string; name?: string }> {
    return this.http.get<{ tracked: boolean; existingRepositoryId?: string; name?: string }>(
      `${this.baseUrl}/repositories/check-url?url=${encodeURIComponent(url)}`);
  }

  updateRepository(id: string, update: { syncIntervalMinutes?: number | null }): Observable<Repository> {
    return this.http.put<Repository>(`${this.baseUrl}/repositories/${id}`, update);
  }

  deleteRepository(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/repositories/${id}`);
  }

  wipeEnrichments(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/repositories/${id}/enrichments`);
  }

  syncRepository(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/repositories/${id}/sync`, {});
  }

  // Search
  hybridSearch(request: SearchRequest): Observable<SearchResults> {
    return this.http.post<SearchResults>(`${this.baseUrl}/search`, request);
  }

  semanticSearch(query: string, language?: string, repositoryId?: string, limit = 10): Observable<SearchResults> {
    let params = new HttpParams().set('query', query).set('limit', limit);
    if (language) params = params.set('language', language);
    if (repositoryId) params = params.set('repositoryId', repositoryId);
    return this.http.get<SearchResults>(`${this.baseUrl}/search/semantic`, { params });
  }

  keywordSearch(keywords: string, language?: string, repositoryId?: string, limit = 10): Observable<SearchResults> {
    let params = new HttpParams().set('keywords', keywords).set('limit', limit);
    if (language) params = params.set('language', language);
    if (repositoryId) params = params.set('repositoryId', repositoryId);
    return this.http.get<SearchResults>(`${this.baseUrl}/search/keyword`, { params });
  }

  // Storage
  getRepositoryStorage(id: string): Observable<StorageStats> {
    return this.http.get<StorageStats>(`${this.baseUrl}/repositories/${id}/storage`);
  }

  getGlobalStorage(): Observable<StorageStats> {
    return this.http.get<StorageStats>(`${this.baseUrl}/enrichments/storage`);
  }

  // Enrichments
  getEnrichments(params: Record<string, string | number>): Observable<EnrichmentListResponse> {
    let httpParams = new HttpParams();
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') {
        httpParams = httpParams.set(key, String(value));
      }
    });
    return this.http.get<EnrichmentListResponse>(`${this.baseUrl}/enrichments`, { params: httpParams });
  }

  getEnrichmentCounts(params?: Record<string, string>): Observable<Record<string, number>> {
    let httpParams = new HttpParams();
    if (params) {
      Object.entries(params).forEach(([key, value]) => {
        if (value) httpParams = httpParams.set(key, value);
      });
    }
    return this.http.get<Record<string, number>>(`${this.baseUrl}/enrichments/counts`, { params: httpParams });
  }

  getEnrichment(id: string): Observable<Enrichment> {
    return this.http.get<Enrichment>(`${this.baseUrl}/enrichments/${id}`);
  }

  // Activity Analytics
  getActivitySparkline(repoId: string): Observable<SparklineData> {
    return this.http.get<SparklineData>(`${this.baseUrl}/repositories/${repoId}/analytics/activity-sparkline`);
  }

  getActivityHeatmap(repoId: string, weeksBack = 52): Observable<ActivityHeatmap> {
    return this.http.get<ActivityHeatmap>(`${this.baseUrl}/repositories/${repoId}/analytics/activity-heatmap?weeksBack=${weeksBack}`);
  }

  getBulkSparklines(repoIds: string[]): Observable<Record<string, SparklineData>> {
    const ids = repoIds.join(',');
    return this.http.get<Record<string, SparklineData>>(`${this.baseUrl}/repositories/analytics/bulk/activity-sparklines?repositoryIds=${ids}`);
  }

  // Tasks
  getTasks(): Observable<IndexingTask[]> {
    return this.http.get<IndexingTask[]>(`${this.baseUrl}/queue`);
  }

  getPipelines(): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/queue/pipelines`);
  }

  getKeyHealth(): Observable<{
    llmKeyValid: boolean;
    embeddingKeyValid: boolean;
    llmError: string | null;
    embeddingError: string | null;
    lastChecked: string;
  }> {
    return this.http.get<any>(`${this.baseUrl}/settings/health`);
  }
}
