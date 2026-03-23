import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { Repository } from '../../models/repository.model';
import { RepositoryHistoryComponent } from './repository-history.component';
import { RepositoryAnalyticsComponent } from './repository-analytics.component';

@Component({
  selector: 'app-repository-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, RepositoryHistoryComponent, RepositoryAnalyticsComponent],
  template: `
    <div *ngIf="loading" style="display:flex;justify-content:center;padding:3rem"><div class="spinner"></div></div>

    <div *ngIf="!loading && repo">
      <div class="page-header">
        <h1>{{ repo.name }}</h1>
        <div style="display:flex;gap:0.75rem">
          <button class="btn btn-secondary" (click)="sync()" [disabled]="syncing">
            <i class="bi bi-arrow-repeat"></i> Sync
          </button>
          <button class="btn btn-danger" (click)="confirmDelete()">
            <i class="bi bi-trash"></i> Delete
          </button>
        </div>
      </div>

      <div style="display:grid;grid-template-columns:1fr 1fr;gap:1.5rem;margin-bottom:1.5rem">
        <div class="card">
          <h3 style="margin-bottom:1rem;font-size:1rem">Details</h3>
          <div class="detail-row"><span class="label">URL</span><a [href]="repo.url" target="_blank">{{ repo.url }}</a></div>
          <div class="detail-row"><span class="label">Provider</span><span class="badge badge-muted">{{ repo.provider }}</span></div>
          <div class="detail-row"><span class="label">Status</span><span class="badge" [ngClass]="statusClass(repo.status)">{{ repo.status }}</span></div>
          <div class="detail-row"><span class="label">Default Branch</span><span>{{ repo.defaultBranch || '—' }}</span></div>
          <div class="detail-row"><span class="label">Last Synced</span><span>{{ repo.lastSyncedAt ? (repo.lastSyncedAt | date:'medium') : 'Never' }}</span></div>
        </div>
        <div class="card" *ngIf="repo.stats">
          <h3 style="margin-bottom:1rem;font-size:1rem">Statistics</h3>
          <div class="stat-grid">
            <div class="stat"><div class="stat-value">{{ repo.stats.commitCount }}</div><div class="stat-label">Commits</div></div>
            <div class="stat"><div class="stat-value">{{ repo.stats.enrichmentCount }}</div><div class="stat-label">Enrichments</div></div>
            <div class="stat"><div class="stat-value">{{ repo.stats.pendingTaskCount }}</div><div class="stat-label">Pending Tasks</div></div>
          </div>
        </div>
      </div>

      <div class="card" *ngIf="repo.branches && repo.branches.length > 0">
        <h3 style="margin-bottom:1rem;font-size:1rem">Branches</h3>
        <div class="tag-list">
          <span *ngFor="let branch of repo.branches" class="badge" [ngClass]="branch.isDefault ? 'badge-primary' : 'badge-muted'">
            {{ branch.name }}
          </span>
        </div>
      </div>

      <app-repository-history [repositoryId]="repo.id" style="margin-top:1.5rem;display:block" />
      <app-repository-analytics [repositoryId]="repo.id" />
    </div>

    <div *ngIf="!loading && !repo" class="empty-state card">
      <i class="bi bi-exclamation-circle"></i>
      <h3>Repository not found</h3>
      <a routerLink="/repositories" class="btn btn-primary mt-2">Back to Repositories</a>
    </div>
  `,
  styles: [`
    .detail-row { display: flex; align-items: center; padding: 0.5rem 0; border-bottom: 1px solid var(--border); }
    .detail-row:last-child { border-bottom: none; }
    .detail-row .label { width: 140px; font-size: 0.875rem; color: var(--text-muted); font-weight: 500; }
    .stat-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 1rem; }
    .stat { text-align: center; }
    .stat-value { font-size: 1.5rem; font-weight: 700; color: var(--primary); }
    .stat-label { font-size: 0.8125rem; color: var(--text-muted); }
    .tag-list { display: flex; flex-wrap: wrap; gap: 0.5rem; }
  `]
})
export class RepositoryDetailComponent implements OnInit {
  repo: Repository | null = null;
  loading = true;
  syncing = false;

  constructor(private api: ApiService, private route: ActivatedRoute, private router: Router) {}

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.api.getRepository(id).subscribe({
      next: repo => { this.repo = repo; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }

  sync() {
    if (!this.repo) return;
    this.syncing = true;
    this.api.syncRepository(this.repo.id).subscribe({
      next: () => this.syncing = false,
      error: () => this.syncing = false
    });
  }

  confirmDelete() {
    if (!this.repo || !confirm(`Delete ${this.repo.name}? This will remove all indexed data.`)) return;
    this.api.deleteRepository(this.repo.id).subscribe({
      next: () => this.router.navigate(['/repositories'])
    });
  }

  statusClass(status: string): string {
    switch (status) {
      case 'indexed': return 'badge-success';
      case 'indexing': case 'cloning': case 'cloned': return 'badge-info';
      case 'error': return 'badge-danger';
      default: return 'badge-muted';
    }
  }
}
