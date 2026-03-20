import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { SearchResults, SearchResultItem } from '../../models/search.model';
import { Repository } from '../../models/repository.model';

@Component({
  selector: 'app-search',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-header">
      <h1>Search</h1>
    </div>

    <div class="card mb-2">
      <div style="display:flex;gap:1rem;align-items:end">
        <div class="form-group" style="flex:1;margin-bottom:0">
          <input class="form-control" [(ngModel)]="query" placeholder="Search code..."
                 (keyup.enter)="search()" style="font-size:1rem;padding:0.75rem 1rem">
        </div>
        <div class="form-group" style="margin-bottom:0">
          <select class="form-control" [(ngModel)]="mode" style="width:140px">
            <option value="hybrid">Hybrid</option>
            <option value="semantic">Semantic</option>
            <option value="keyword">Keyword</option>
          </select>
        </div>
        <button class="btn btn-primary" (click)="search()" [disabled]="searching || !query">
          <i class="bi bi-search"></i> Search
        </button>
      </div>
    </div>

    <div *ngIf="searching" style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>

    <div *ngIf="results && !searching">
      <div class="text-muted mb-2" style="font-size:0.875rem">
        {{ results.totalCount }} results in {{ results.durationMs }}ms ({{ results.searchMode }})
      </div>
      <div class="card" *ngFor="let item of results.results" style="margin-bottom:1rem">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:0.75rem">
          <div>
            <code style="font-size:0.8125rem">{{ item.filePath }}</code>
            <span class="badge badge-muted" style="margin-left:0.5rem" *ngIf="item.language">{{ item.language }}</span>
          </div>
          <div>
            <span class="badge badge-primary">{{ (item.score * 100).toFixed(1) }}%</span>
            <span class="text-muted" style="margin-left:0.5rem;font-size:0.8125rem">{{ item.repositoryName }}</span>
          </div>
        </div>
        <pre><code>{{ item.content }}</code></pre>
        <div class="text-muted" style="font-size:0.75rem;margin-top:0.5rem" *ngIf="item.startLine">
          Lines {{ item.startLine }}–{{ item.endLine }}
        </div>
      </div>
    </div>

    <div *ngIf="results && results.results.length === 0 && !searching" class="empty-state card">
      <i class="bi bi-search"></i>
      <h3>No results found</h3>
      <p>Try different keywords or search mode.</p>
    </div>
  `
})
export class SearchComponent {
  query = '';
  mode = 'hybrid';
  results: SearchResults | null = null;
  searching = false;

  constructor(private api: ApiService) {}

  search() {
    if (!this.query.trim()) return;
    this.searching = true;

    const obs = this.mode === 'semantic'
      ? this.api.semanticSearch(this.query)
      : this.mode === 'keyword'
        ? this.api.keywordSearch(this.query)
        : this.api.hybridSearch({ query: this.query });

    obs.subscribe({
      next: results => { this.results = results; this.searching = false; },
      error: () => { this.searching = false; }
    });
  }
}
