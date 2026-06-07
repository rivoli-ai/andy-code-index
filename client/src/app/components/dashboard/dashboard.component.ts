import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { PinService } from '../../services/pin.service';
import { HealthService } from '../../services/health.service';
import { Repository, SparklineData } from '../../models/repository.model';
import { RepositorySparklineComponent } from '../repositories/repository-sparkline.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, RepositorySparklineComponent],
  template: `
    <div class="page-header">
      <h1>Dashboard</h1>
      <a routerLink="/repositories" class="btn btn-secondary">
        <i class="bi bi-folder2-open"></i> All Repositories
      </a>
    </div>
    
    @if (loading) {
      <div class="card">
        <div style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>
      </div>
    }
    
    @if (!loading && pinnedRepos.length === 0) {
      <div class="empty-state card">
        <i class="bi bi-pin-angle"></i>
        <h3>No pinned repositories</h3>
        <p>Pin repositories from the <a routerLink="/repositories">repository list</a> for quick access.</p>
      </div>
    }
    
    @if (!loading && pinnedRepos.length > 0) {
      <div class="dashboard-grid">
        @for (repo of pinnedRepos; track repo) {
          <div class="dashboard-card card">
            <div class="card-header">
              <div class="card-title-row">
                <a [routerLink]="['/repositories', repo.id]" class="repo-name">{{ repo.name }}</a>
                <span class="badge" [ngClass]="statusClass(repo.status)">{{ repo.status }}</span>
              </div>
              <div class="card-meta">
                <span class="badge badge-muted">{{ repo.provider }}</span>
                @if (repo.organization) {
                  <span class="text-muted">{{ repo.organization }}</span>
                }
              </div>
            </div>
            <div class="card-stats">
              <div class="stat">
                <span class="stat-value">{{ repo.stats?.commitCount || 0 }}</span>
                <span class="stat-label">Commits</span>
              </div>
              <div class="stat">
                <span class="stat-value">{{ repo.stats?.enrichmentCount || 0 }}</span>
                <span class="stat-label">Enrichments</span>
                @if (repo.stats?.storageSizeBytes) {
                  <span class="stat-sub" style="font-size:var(--font-xs);color:var(--text-muted)">{{ formatBytes(repo.stats!.storageSizeBytes) }}</span>
                }
              </div>
              <div class="stat">
                <span class="stat-value">{{ repo.stats?.hasEmbeddings ? (repo.stats?.embeddingCount || 0) : '--' }}</span>
                <span class="stat-label">Embeddings</span>
              </div>
            </div>
            @if (sparklines.get(repo.id)) {
              <div class="card-activity">
                <app-repository-sparkline [weeklyData]="sparklines.get(repo.id)!.weeklyData"></app-repository-sparkline>
              </div>
            }
            <div class="card-footer">
              <span class="text-muted last-synced">
                <i class="bi bi-clock"></i>
                {{ repo.lastSyncedAt ? (repo.lastSyncedAt | date:'short') : 'Never synced' }}
              </span>
              <div class="card-actions">
                <button class="btn btn-sm btn-secondary" (click)="sync(repo)" [disabled]="syncing[repo.id]"
                  title="Sync repository">
                  @if (!syncing[repo.id]) {
                    <span><i class="bi bi-arrow-repeat"></i></span>
                  }
                  @if (syncing[repo.id]) {
                    <span><div class="spinner" style="width:14px;height:14px;display:inline-block;vertical-align:middle"></div></span>
                  }
                </button>
                <a [routerLink]="['/repositories', repo.id]" class="btn btn-sm btn-secondary" title="View details">
                  <i class="bi bi-eye"></i>
                </a>
                <button class="btn btn-sm btn-secondary" (click)="unpin(repo.id)" title="Unpin from dashboard">
                  <i class="bi bi-pin-fill"></i>
                </button>
              </div>
            </div>
          </div>
        }
      </div>
    }
    
    @if (error) {
      <div class="error-message">{{ error }}</div>
    }
    `,
  styles: [`
    .dashboard-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 1rem;
    }
    .dashboard-card {
      display: flex;
      flex-direction: column;
      padding: 1.25rem;
    }
    .card-header { margin-bottom: 1rem; }
    .card-title-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      margin-bottom: 0.375rem;
    }
    .repo-name {
      font-weight: 600;
      font-size: var(--font-base);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .card-meta {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: var(--font-xs);
    }
    .card-stats {
      display: flex;
      gap: 1.5rem;
      margin-bottom: 1rem;
      padding: 0.75rem 0;
      border-top: 1px solid var(--border);
      border-bottom: 1px solid var(--border);
    }
    .stat { display: flex; flex-direction: column; }
    .stat-value { font-weight: 700; font-size: var(--font-lg); }
    .stat-label { font-size: var(--font-xs); color: var(--text-muted); }
    .card-activity {
      margin-bottom: 1rem;
      overflow: hidden;
    }
    .card-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-top: auto;
    }
    .last-synced {
      font-size: var(--font-xs);
      display: flex;
      align-items: center;
      gap: 0.375rem;
    }
    .card-actions {
      display: flex;
      gap: 0.375rem;
    }
    .error-message {
      color: var(--danger);
      margin-top: 1rem;
      padding: 0.75rem;
      background: rgba(220,53,69,0.1);
      border-radius: var(--radius);
    }
    .empty-state a { color: var(--primary); }
  `]
})
export class DashboardComponent implements OnInit {
  private api = inject(ApiService);
  private pinService = inject(PinService);
  healthService = inject(HealthService);

  pinnedRepos: Repository[] = [];
  sparklines: Map<string, SparklineData> = new Map();
  loading = true;
  error = '';
  syncing: Record<string, boolean> = {};

  ngOnInit() {
    this.loadPinnedRepos();
  }

  loadPinnedRepos() {
    const pinnedIds = this.pinService.getPinnedIds();
    if (pinnedIds.length === 0) {
      this.loading = false;
      return;
    }

    const requests = pinnedIds.map(id => this.api.getRepository(id));
    forkJoin(requests).subscribe({
      next: repos => {
        this.pinnedRepos = repos;
        this.loading = false;
        this.loadSparklines(repos);
      },
      error: () => {
        this.error = 'Failed to load pinned repositories.';
        this.loading = false;
      }
    });
  }

  loadSparklines(repos: Repository[]) {
    if (repos.length === 0) return;
    const repoIds = repos.map(r => r.id);
    this.api.getBulkSparklines(repoIds).subscribe({
      next: data => {
        Object.entries(data).forEach(([id, sparkline]) => {
          this.sparklines.set(id, sparkline);
        });
      }
    });
  }

  sync(repo: Repository) {
    this.syncing[repo.id] = true;
    this.error = '';
    this.api.syncRepository(repo.id).subscribe({
      next: () => { this.syncing[repo.id] = false; },
      error: (err) => {
        this.syncing[repo.id] = false;
        if (err.status === 409) {
          this.error = err.error?.error || 'Sync already in progress.';
        }
      }
    });
  }

  unpin(repoId: string) {
    this.pinService.unpin(repoId);
    this.pinnedRepos = this.pinnedRepos.filter(r => r.id !== repoId);
  }

  statusClass(status: string): string {
    switch (status) {
      case 'indexed': return 'badge-success';
      case 'indexing': case 'cloning': case 'cloned': return 'badge-info';
      case 'error': return 'badge-danger';
      default: return 'badge-muted';
    }
  }

  formatBytes(bytes: number): string {
    if (bytes === 0) return '0 B';
    const units = ['B', 'KB', 'MB', 'GB'];
    const i = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
    const value = bytes / Math.pow(1024, i);
    return `${value < 10 ? value.toFixed(1) : Math.round(value)} ${units[i]}`;
  }
}
