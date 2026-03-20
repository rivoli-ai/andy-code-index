import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { Enrichment, EnrichmentListResponse } from '../../models/enrichment.model';

@Component({
  selector: 'app-enrichment-browser',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-header">
      <h1>Enrichments</h1>
    </div>

    <div class="card mb-2">
      <div style="display:flex;gap:1rem;flex-wrap:wrap">
        <div class="form-group" style="margin-bottom:0">
          <select class="form-control" [(ngModel)]="typeFilter" (change)="loadEnrichments()" style="width:180px">
            <option value="">All Types</option>
            <option value="Architecture">Architecture</option>
            <option value="Development">Development</option>
            <option value="History">History</option>
            <option value="Usage">Usage</option>
          </select>
        </div>
        <div class="form-group" style="margin-bottom:0">
          <select class="form-control" [(ngModel)]="subtypeFilter" (change)="loadEnrichments()" style="width:180px">
            <option value="">All Subtypes</option>
            <option value="Chunk">Chunk</option>
            <option value="APIDocs">API Docs</option>
            <option value="Cookbook">Cookbook</option>
            <option value="Wiki">Wiki</option>
            <option value="Physical">Architecture</option>
            <option value="DatabaseSchema">DB Schema</option>
            <option value="CommitDescription">Commit Desc</option>
          </select>
        </div>
      </div>
    </div>

    <div *ngIf="loading" style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>

    <div *ngIf="!loading && enrichments.length > 0">
      <div class="text-muted mb-2" style="font-size:0.875rem">{{ totalCount }} enrichments</div>
      <div class="card" *ngFor="let e of enrichments" style="margin-bottom:1rem;cursor:pointer" (click)="toggleExpand(e.id)">
        <div style="display:flex;justify-content:space-between;align-items:center">
          <div>
            <span class="badge badge-primary">{{ e.type }}</span>
            <span class="badge badge-muted" style="margin-left:0.25rem">{{ e.subtype }}</span>
            <strong style="margin-left:0.75rem">{{ e.title || e.filePath || 'Untitled' }}</strong>
          </div>
          <span class="badge badge-muted" *ngIf="e.language">{{ e.language }}</span>
        </div>
        <div *ngIf="expandedId === e.id" style="margin-top:1rem">
          <pre><code>{{ e.content }}</code></pre>
        </div>
      </div>
      <div style="display:flex;justify-content:center;gap:0.75rem;margin-top:1rem" *ngIf="totalCount > enrichments.length">
        <button class="btn btn-secondary" (click)="loadMore()">Load More</button>
      </div>
    </div>

    <div *ngIf="!loading && enrichments.length === 0" class="empty-state card">
      <i class="bi bi-file-earmark-text"></i>
      <h3>No enrichments found</h3>
      <p>Index a repository to generate enrichments.</p>
    </div>
  `
})
export class EnrichmentBrowserComponent implements OnInit {
  enrichments: Enrichment[] = [];
  totalCount = 0;
  loading = true;
  typeFilter = '';
  subtypeFilter = '';
  expandedId: string | null = null;
  offset = 0;

  constructor(private api: ApiService) {}

  ngOnInit() { this.loadEnrichments(); }

  loadEnrichments() {
    this.offset = 0;
    this.loading = true;
    const params: Record<string, string | number> = { offset: 0, limit: 20 };
    if (this.typeFilter) params['type'] = this.typeFilter;
    if (this.subtypeFilter) params['subtype'] = this.subtypeFilter;

    this.api.getEnrichments(params).subscribe({
      next: res => { this.enrichments = res.results; this.totalCount = res.totalCount; this.loading = false; },
      error: () => this.loading = false
    });
  }

  loadMore() {
    this.offset += 20;
    const params: Record<string, string | number> = { offset: this.offset, limit: 20 };
    if (this.typeFilter) params['type'] = this.typeFilter;
    if (this.subtypeFilter) params['subtype'] = this.subtypeFilter;

    this.api.getEnrichments(params).subscribe({
      next: res => this.enrichments.push(...res.results)
    });
  }

  toggleExpand(id: string) { this.expandedId = this.expandedId === id ? null : id; }
}
