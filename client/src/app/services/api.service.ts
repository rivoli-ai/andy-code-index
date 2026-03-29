import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Repository, CreateRepositoryRequest } from '../models/repository.model';
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

  getRepository(id: string): Observable<Repository> {
    return this.http.get<Repository>(`${this.baseUrl}/repositories/${id}`);
  }

  createRepository(request: CreateRepositoryRequest): Observable<Repository> {
    return this.http.post<Repository>(`${this.baseUrl}/repositories`, request);
  }

  updateRepository(id: string, update: { syncIntervalMinutes?: number | null }): Observable<Repository> {
    return this.http.put<Repository>(`${this.baseUrl}/repositories/${id}`, update);
  }

  deleteRepository(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/repositories/${id}`);
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

  // Tasks
  getTasks(): Observable<IndexingTask[]> {
    return this.http.get<IndexingTask[]>(`${this.baseUrl}/queue`);
  }

  getPipelines(): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/queue/pipelines`);
  }
}
