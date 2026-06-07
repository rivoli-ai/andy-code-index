import { Component, OnInit, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { HealthService } from '../../services/health.service';
import { PinService } from '../../services/pin.service';
import { Repository, SparklineData } from '../../models/repository.model';
import { RepositorySparklineComponent } from './repository-sparkline.component';

@Component({
  selector: 'app-repository-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, RepositorySparklineComponent],
  template: `
    @if (!(healthService.isConnected$ | async)) {
      <div class="warning-banner">
        <i class="bi bi-exclamation-triangle"></i> Backend unavailable - some features are disabled
      </div>
    }
    
    <div class="page-header">
      <h1>Repositories</h1>
      <a routerLink="/repositories/add" class="btn btn-primary" [class.disabled]="!(healthService.isConnected$ | async)">
        <i class="bi bi-plus-lg"></i> Add Repository
      </a>
    </div>
    
    @if (loading) {
      <div class="card">
        <div style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>
      </div>
    }
    
    @if (!loading && repositories.length === 0) {
      <div class="empty-state card">
        <i class="bi bi-folder2-open"></i>
        <h3>No repositories yet</h3>
        <p>Add a repository to start indexing code.</p>
        <a routerLink="/repositories/add" class="btn btn-primary">Add Repository</a>
      </div>
    }
    
    <!-- Filters -->
    @if (!loading && repositories.length > 0) {
      <div class="card mb-2" style="padding:0.75rem 1rem">
        <div style="display:flex;gap:0.75rem;flex-wrap:wrap;align-items:center">
          <input class="form-control" [(ngModel)]="nameFilter" placeholder="Search by name..." style="width:200px;padding:0.375rem 0.75rem">
          <select class="form-control" [(ngModel)]="statusFilter" style="width:130px">
            <option value="">All Status</option>
            <option value="pending">Pending</option>
            <option value="cloning">Cloning</option>
            <option value="indexed">Indexed</option>
            <option value="indexing">Indexing</option>
            <option value="error">Error</option>
          </select>
          <select class="form-control" [(ngModel)]="orgFilter" style="width:160px">
            <option value="">All Organizations</option>
            @for (org of organizations; track org) {
              <option [value]="org.name">{{ org.name }} ({{ org.count }})</option>
            }
          </select>
          <select class="form-control" [(ngModel)]="providerFilter" style="width:140px">
            <option value="">All Providers</option>
            <option value="GitHub">GitHub</option>
            <option value="GitLab">GitLab</option>
            <option value="Gitea">Gitea</option>
            <option value="AzureDevOps">Azure DevOps</option>
          </select>
          <select class="form-control" [(ngModel)]="sortBy" style="width:150px">
            <option value="name">Sort: Name</option>
            <option value="lastSynced">Sort: Last Synced</option>
            <option value="enrichments">Sort: Enrichments</option>
            <option value="embeddings">Sort: Embeddings</option>
          </select>
          @if (nameFilter || statusFilter || providerFilter || orgFilter) {
            <button class="btn btn-sm btn-secondary" (click)="clearFilters()">
              Clear
            </button>
          }
          <span class="text-muted" style="margin-left:auto;font-size:0.8125rem">
            Showing {{ filteredRepositories.length }} of {{ repositories.length }}
          </span>
        </div>
      </div>
    }
    
    @if (!loading && filteredRepositories.length > 0) {
      <div class="card">
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Provider</th>
              <th>Status</th>
              <th>Activity</th>
              <th>Enrichments</th>
              <th>Embeddings</th>
              <th>Last Synced</th>
              <th>Actions</th>
            </tr>
          </thead>
          <tbody>
            @for (repo of filteredRepositories; track repo) {
              <tr>
                <td>
                  <a [routerLink]="['/repositories', repo.id]">{{ repo.name }}</a>
                  @if (repo.stats?.needsAttention) {
                    <i class="bi bi-exclamation-triangle-fill"
                      style="color:#e6a700;margin-left:0.5rem;font-size:0.75rem;cursor:help"
                    [title]="repo.stats?.attentionReason || 'Needs attention'"></i>
                  }
                  @if (repo.organization) {
                    <div class="text-muted" style="font-size:0.75rem">{{ repo.organization }}</div>
                  }
                </td>
                <td><span class="badge badge-muted">{{ repo.provider }}</span></td>
                <td>
                  <span class="badge" [ngClass]="statusClass(repo.status)">{{ repo.status }}</span>
                </td>
                <td>
                  @if (sparklines.get(repo.id)) {
                    <app-repository-sparkline
                      [weeklyData]="sparklines.get(repo.id)!.weeklyData">
                    </app-repository-sparkline>
                  }
                  @if (!sparklines.get(repo.id)) {
                    <span class="text-muted" style="font-size:0.75rem">--</span>
                  }
                </td>
                <td class="text-muted">{{ repo.stats?.enrichmentCount || 0 }}</td>
                <td>
                  @if (repo.stats?.hasEmbeddings) {
                    <span>{{ repo.stats?.embeddingCount }}</span>
                  }
                  @if (!repo.stats?.hasEmbeddings) {
                    <span class="text-muted">--</span>
                  }
                </td>
                <td class="text-muted">{{ repo.lastSyncedAt ? (repo.lastSyncedAt | date:'short') : 'Never' }}</td>
                <td>
                  <div style="display:flex;gap:0.375rem;align-items:center">
                    <button class="btn btn-sm btn-secondary" (click)="togglePin(repo.id)"
                      [title]="pinService.isPinned(repo.id) ? 'Unpin from dashboard' : 'Pin to dashboard'">
                      <i class="bi" [ngClass]="pinService.isPinned(repo.id) ? 'bi-pin-fill' : 'bi-pin-angle'"></i>
                    </button>
                    <button class="btn btn-sm btn-secondary" (click)="sync(repo)" [disabled]="isBusy(repo.id)">
                      @if (!isBusy(repo.id)) {
                        <span><i class="bi bi-arrow-repeat"></i> Sync</span>
                      }
                      @if (isBusy(repo.id)) {
                        <span><div class="spinner" style="width:14px;height:14px;display:inline-block;vertical-align:middle"></div> Syncing...</span>
                      }
                    </button>
                  </div>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
    
    @if (!loading && repositories.length > 0 && filteredRepositories.length === 0) {
      <div class="empty-state card">
        <i class="bi bi-funnel"></i>
        <h3>No matching repositories</h3>
        <p>Try adjusting your filters.</p>
        <button class="btn btn-secondary" (click)="clearFilters()">Clear Filters</button>
      </div>
    }
    
    @if (error) {
      <div class="error-message">{{ error }}</div>
    }
    `,
  styles: [`
    .error-message { color: var(--danger); margin-top: 1rem; padding: 0.75rem; background: rgba(220,53,69,0.1); border-radius: var(--radius); }
    .warning-banner { background: rgba(255,193,7,0.15); color: #856404; border: 1px solid rgba(255,193,7,0.3); border-radius: var(--radius); padding: 0.5rem 1rem; margin-bottom: 1rem; font-size: var(--font-sm); }
    .btn.disabled { opacity: 0.5; pointer-events: none; }
  `]
})
export class RepositoryListComponent implements OnInit {
  private api = inject(ApiService);
  healthService = inject(HealthService);
  pinService = inject(PinService);

  repositories: Repository[] = [];
  loading = true;
  error = '';
  syncing: Record<string, boolean> = {};
  busyRepos: Set<string> = new Set();
  sparklines: Map<string, SparklineData> = new Map();
  nameFilter = '';
  statusFilter = '';
  providerFilter = '';
  orgFilter = '';
  sortBy = 'name';
  organizations: { name: string; count: number }[] = [];

  ngOnInit() {
    this.loadRepositories();
    this.loadPipelines();
    this.loadOrganizations();
  }

  loadRepositories() {
    this.loading = true;
    this.api.getRepositories().subscribe({
      next: repos => {
        this.repositories = repos;
        this.loading = false;
        this.loadSparklines(repos);
      },
      error: (err: any) => {
        if (err.status === 403) {
          this.error = err.error?.error || 'Access denied. You do not have permission to view repositories.';
        } else if (err.status === 401) {
          this.error = 'Session expired. Please sign in again.';
        } else {
          this.error = 'Failed to load repositories. Check that the server is running.';
        }
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

  loadPipelines() {
    this.api.getPipelines().subscribe({
      next: pipelines => {
        this.busyRepos = new Set(pipelines.map((p: any) => p.repositoryId));
      }
    });
  }

  loadOrganizations() {
    this.api.getOrganizations().subscribe({
      next: orgs => { this.organizations = orgs; }
    });
  }

  get filteredRepositories(): Repository[] {
    let repos = this.repositories;
    if (this.nameFilter) {
      const q = this.nameFilter.toLowerCase();
      repos = repos.filter(r => r.name.toLowerCase().includes(q));
    }
    if (this.statusFilter) repos = repos.filter(r => r.status === this.statusFilter);
    if (this.providerFilter) repos = repos.filter(r => r.provider === this.providerFilter);
    if (this.orgFilter) repos = repos.filter(r => r.organization === this.orgFilter);

    return repos.sort((a, b) => {
      switch (this.sortBy) {
        case 'lastSynced': return (b.lastSyncedAt || '').localeCompare(a.lastSyncedAt || '');
        case 'enrichments': return (b.stats?.enrichmentCount || 0) - (a.stats?.enrichmentCount || 0);
        case 'embeddings': return (b.stats?.embeddingCount || 0) - (a.stats?.embeddingCount || 0);
        default: return a.name.localeCompare(b.name);
      }
    });
  }

  clearFilters() {
    this.nameFilter = '';
    this.statusFilter = '';
    this.providerFilter = '';
    this.orgFilter = '';
    this.sortBy = 'name';
  }

  isBusy(repoId: string): boolean {
    return this.busyRepos.has(repoId) || this.syncing[repoId];
  }

  sync(repo: Repository) {
    this.syncing[repo.id] = true;
    this.error = '';
    this.api.syncRepository(repo.id).subscribe({
      next: () => { this.syncing[repo.id] = false; this.loadPipelines(); },
      error: (err) => {
        this.syncing[repo.id] = false;
        if (err.status === 409) {
          this.error = err.error?.error || 'Sync already in progress for this repository.';
        }
      }
    });
  }

  togglePin(repoId: string) {
    this.pinService.toggle(repoId);
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
