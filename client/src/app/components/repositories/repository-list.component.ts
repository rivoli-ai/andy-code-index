import { Component, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';
import { Repository } from '../../models/repository.model';

@Component({
  selector: 'app-repository-list',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule],
  template: `
    <div class="page-header">
      <h1>Repositories</h1>
      <a routerLink="/repositories/add" class="btn btn-primary">
        <i class="bi bi-plus-lg"></i> Add Repository
      </a>
    </div>

    <div class="card" *ngIf="loading">
      <div style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>
    </div>

    <div class="empty-state card" *ngIf="!loading && repositories.length === 0">
      <i class="bi bi-folder2-open"></i>
      <h3>No repositories yet</h3>
      <p>Add a repository to start indexing code.</p>
      <a routerLink="/repositories/add" class="btn btn-primary">Add Repository</a>
    </div>

    <!-- Filters -->
    <div class="card mb-2" *ngIf="!loading && repositories.length > 0" style="padding:0.75rem 1rem">
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
        <button class="btn btn-sm btn-secondary" (click)="clearFilters()" *ngIf="nameFilter || statusFilter || providerFilter">
          Clear
        </button>
        <span class="text-muted" style="margin-left:auto;font-size:0.8125rem">
          Showing {{ filteredRepositories.length }} of {{ repositories.length }}
        </span>
      </div>
    </div>

    <div class="card" *ngIf="!loading && filteredRepositories.length > 0">
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Provider</th>
            <th>Status</th>
            <th>Enrichments</th>
            <th>Embeddings</th>
            <th>Last Synced</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr *ngFor="let repo of filteredRepositories">
            <td><a [routerLink]="['/repositories', repo.id]">{{ repo.name }}</a></td>
            <td><span class="badge badge-muted">{{ repo.provider }}</span></td>
            <td>
              <span class="badge" [ngClass]="statusClass(repo.status)">{{ repo.status }}</span>
            </td>
            <td class="text-muted">{{ repo.stats?.enrichmentCount || 0 }}</td>
            <td>
              <span *ngIf="repo.stats?.hasEmbeddings">{{ repo.stats?.embeddingCount }}</span>
              <span class="text-muted" *ngIf="!repo.stats?.hasEmbeddings">--</span>
            </td>
            <td class="text-muted">{{ repo.lastSyncedAt ? (repo.lastSyncedAt | date:'short') : 'Never' }}</td>
            <td>
              <button class="btn btn-sm btn-secondary" (click)="sync(repo)" [disabled]="isBusy(repo.id)">
                <span *ngIf="!isBusy(repo.id)"><i class="bi bi-arrow-repeat"></i> Sync</span>
                <span *ngIf="isBusy(repo.id)"><div class="spinner" style="width:14px;height:14px;display:inline-block;vertical-align:middle"></div> Syncing...</span>
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <div *ngIf="!loading && repositories.length > 0 && filteredRepositories.length === 0" class="empty-state card">
      <i class="bi bi-funnel"></i>
      <h3>No matching repositories</h3>
      <p>Try adjusting your filters.</p>
      <button class="btn btn-secondary" (click)="clearFilters()">Clear Filters</button>
    </div>

    <div class="error-message" *ngIf="error">{{ error }}</div>
  `,
  styles: [`
    .error-message { color: var(--danger); margin-top: 1rem; padding: 0.75rem; background: rgba(220,53,69,0.1); border-radius: var(--radius); }
  `]
})
export class RepositoryListComponent implements OnInit {
  repositories: Repository[] = [];
  loading = true;
  error = '';
  syncing: Record<string, boolean> = {};
  busyRepos: Set<string> = new Set();
  nameFilter = '';
  statusFilter = '';
  providerFilter = '';
  sortBy = 'name';

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.loadRepositories();
    this.loadPipelines();
  }

  loadRepositories() {
    this.loading = true;
    this.api.getRepositories().subscribe({
      next: repos => { this.repositories = repos; this.loading = false; },
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

  loadPipelines() {
    this.api.getPipelines().subscribe({
      next: pipelines => {
        this.busyRepos = new Set(pipelines.map((p: any) => p.repositoryId));
      }
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

  statusClass(status: string): string {
    switch (status) {
      case 'indexed': return 'badge-success';
      case 'indexing': case 'cloning': case 'cloned': return 'badge-info';
      case 'error': return 'badge-danger';
      default: return 'badge-muted';
    }
  }
}
