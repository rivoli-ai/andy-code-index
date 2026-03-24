import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { ApiService } from '../../services/api.service';
import { SearchResults } from '../../models/search.model';
import { environment } from '../../../environments/environment';

interface FilterOptions {
  repositories: { id: string; name: string }[];
  languages: string[];
}

@Component({
  selector: 'app-search',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <div class="page-header">
      <h1>Search</h1>
    </div>

    <div class="card mb-2">
      <div style="display:flex;gap:0.75rem;align-items:end;flex-wrap:wrap">
        <div class="form-group" style="flex:1;margin-bottom:0;min-width:250px">
          <input class="form-control" [(ngModel)]="query" placeholder="Search code..."
                 (keyup.enter)="search(true)" style="font-size:1rem;padding:0.75rem 1rem">
        </div>
        <div class="form-group" style="margin-bottom:0">
          <select class="form-control" [(ngModel)]="mode" style="width:130px">
            <option value="hybrid">Hybrid</option>
            <option value="semantic">Semantic</option>
            <option value="keyword">Keyword</option>
          </select>
        </div>
        <div class="form-group" style="margin-bottom:0" *ngIf="filters">
          <select class="form-control" [(ngModel)]="selectedRepo" style="width:160px">
            <option value="">All Repos</option>
            <option *ngFor="let r of filters.repositories" [value]="r.id">{{ r.name }}</option>
          </select>
        </div>
        <div class="form-group" style="margin-bottom:0" *ngIf="filters">
          <select class="form-control" [(ngModel)]="selectedLang" style="width:140px">
            <option value="">All Languages</option>
            <option *ngFor="let l of filters.languages" [value]="l">{{ l }}</option>
          </select>
        </div>
        <button class="btn btn-primary" (click)="search(true)" [disabled]="searching || !query">
          <i class="bi bi-search"></i> Search
        </button>
      </div>
    </div>

    <div *ngIf="searching" style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>

    <div *ngIf="results && !searching">
      <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:1rem">
        <span class="text-muted" style="font-size:0.875rem">
          {{ results.totalCount }} results in {{ results.durationMs }}ms ({{ results.searchMode }})
        </span>
        <div style="display:flex;gap:0.5rem;align-items:center" *ngIf="results.totalCount > pageSize">
          <button class="btn btn-sm btn-secondary" (click)="prevPage()" [disabled]="offset === 0">Previous</button>
          <span class="text-muted" style="font-size:0.8125rem">
            {{ offset + 1 }}-{{ Math.min(offset + pageSize, results.totalCount) }} of {{ results.totalCount }}
          </span>
          <button class="btn btn-sm btn-secondary" (click)="nextPage()" [disabled]="offset + pageSize >= results.totalCount">Next</button>
        </div>
      </div>

      <div class="card search-result" *ngFor="let item of results.results" style="margin-bottom:0.75rem">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:0.5rem">
          <div style="display:flex;align-items:center;gap:0.5rem;flex-wrap:wrap">
            <a [routerLink]="['/repositories', item.repositoryId]" class="badge badge-primary" style="text-decoration:none" *ngIf="item.repositoryName">
              {{ item.repositoryName }}
            </a>
            <code style="font-size:0.8125rem">{{ item.filePath }}</code>
            <span class="badge badge-muted" *ngIf="item.language">{{ item.language }}</span>
          </div>
          <span class="badge badge-info">{{ formatScore(item.score) }}</span>
        </div>
        <pre style="margin:0;max-height:200px;overflow:hidden"><code>{{ truncateContent(item.content) }}</code></pre>
        <div style="display:flex;justify-content:space-between;align-items:center;margin-top:0.5rem">
          <span class="text-muted" style="font-size:0.75rem" *ngIf="item.startLine">
            Lines {{ item.startLine }}-{{ item.endLine }}
          </span>
          <a [routerLink]="['/enrichments']" [queryParams]="{repositoryId: item.repositoryId}"
             class="text-muted" style="font-size:0.75rem">
            View enrichments
          </a>
        </div>
      </div>

      <!-- Pagination at bottom -->
      <div style="display:flex;justify-content:center;gap:0.75rem;margin-top:1.5rem" *ngIf="results.totalCount > pageSize">
        <button class="btn btn-secondary" (click)="prevPage()" [disabled]="offset === 0">Previous</button>
        <span class="text-muted" style="align-self:center;font-size:0.875rem">
          Page {{ currentPage }} of {{ totalPages }}
        </span>
        <button class="btn btn-secondary" (click)="nextPage()" [disabled]="offset + pageSize >= results.totalCount">Next</button>
      </div>
    </div>

    <div *ngIf="results && results.results.length === 0 && !searching" class="card" style="text-align:center;padding:2rem">
      <i class="bi bi-search" style="font-size:2rem;color:var(--text-muted);display:block;margin-bottom:1rem"></i>
      <h3 style="margin-bottom:0.5rem">No results for "{{ query }}"</h3>
      <p class="text-muted" style="margin-bottom:1rem">
        Searched using <strong>{{ mode }}</strong> mode
        <span *ngIf="getActiveFilterLabel()"> with filters: {{ getActiveFilterLabel() }}</span>
      </p>
      <div style="text-align:left;max-width:400px;margin:0 auto;font-size:0.875rem">
        <p style="font-weight:600;margin-bottom:0.5rem">Suggestions:</p>
        <ul style="padding-left:1.25rem;margin:0">
          <li *ngIf="mode !== 'keyword'">Try <a href="javascript:void(0)" (click)="switchMode('keyword')">keyword search</a> for exact matches</li>
          <li *ngIf="mode !== 'semantic'">Try <a href="javascript:void(0)" (click)="switchMode('semantic')">semantic search</a> for conceptual matches</li>
          <li *ngIf="selectedRepo || selectedLang">
            <a href="javascript:void(0)" (click)="clearFilters()">Remove filters</a> to search all repos and languages
          </li>
          <li>Use simpler or more general keywords</li>
          <li>Check that repositories are indexed (status: "indexed")</li>
        </ul>
      </div>
    </div>
  `,
  styles: [`
    .search-result:hover { border-color: var(--primary-light); }
    pre { font-size: 0.8rem; }
  `]
})
export class SearchComponent implements OnInit {
  query = '';
  mode = 'hybrid';
  selectedRepo = '';
  selectedLang = '';
  results: SearchResults | null = null;
  filters: FilterOptions | null = null;
  searching = false;
  offset = 0;
  pageSize = 10;
  Math = Math;

  constructor(private api: ApiService, private http: HttpClient) {}

  ngOnInit() {
    this.http.get<FilterOptions>(`${environment.apiUrl}/search/filters`).subscribe({
      next: f => this.filters = f
    });
  }

  get currentPage(): number { return Math.floor(this.offset / this.pageSize) + 1; }
  get totalPages(): number { return this.results ? Math.ceil(this.results.totalCount / this.pageSize) : 0; }

  search(resetPage = false) {
    if (!this.query.trim()) return;
    if (resetPage) this.offset = 0;
    this.searching = true;

    const repoId = this.selectedRepo || undefined;
    const lang = this.selectedLang || undefined;

    if (this.mode === 'hybrid') {
      const body: any = { query: this.query, limit: this.pageSize, offset: this.offset };
      if (repoId) body.repositoryIds = [repoId];
      if (lang) body.languages = [lang];
      this.api.hybridSearch(body).subscribe({
        next: r => { this.results = r; this.searching = false; },
        error: () => this.searching = false
      });
    } else {
      const searchFn = this.mode === 'semantic'
        ? this.api.semanticSearch(this.query, lang, repoId, this.pageSize)
        : this.api.keywordSearch(this.query, lang, repoId, this.pageSize);
      searchFn.subscribe({
        next: r => { this.results = r; this.searching = false; },
        error: () => this.searching = false
      });
    }
  }

  nextPage() {
    this.offset += this.pageSize;
    this.search();
  }

  prevPage() {
    this.offset = Math.max(0, this.offset - this.pageSize);
    this.search();
  }

  formatScore(score: number): string {
    if (score < 0.01) return '<1%';
    return (score * 100).toFixed(1) + '%';
  }

  truncateContent(content: string): string {
    return content.length > 500 ? content.substring(0, 500) + '...' : content;
  }

  getActiveFilterLabel(): string {
    const parts: string[] = [];
    if (this.selectedRepo && this.filters) {
      const repo = this.filters.repositories.find(r => r.id === this.selectedRepo);
      if (repo) parts.push(repo.name);
    }
    if (this.selectedLang) parts.push(this.selectedLang);
    return parts.join(', ');
  }

  switchMode(mode: string) {
    this.mode = mode;
    this.search(true);
  }

  clearFilters() {
    this.selectedRepo = '';
    this.selectedLang = '';
    this.search(true);
  }
}
