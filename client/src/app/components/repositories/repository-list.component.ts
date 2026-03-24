import { Component, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { Repository } from '../../models/repository.model';

@Component({
  selector: 'app-repository-list',
  standalone: true,
  imports: [CommonModule, RouterLink],
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

    <div class="card" *ngIf="!loading && repositories.length > 0">
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
          <tr *ngFor="let repo of repositories">
            <td><a [routerLink]="['/repositories', repo.id]">{{ repo.name }}</a></td>
            <td><span class="badge badge-muted">{{ repo.provider }}</span></td>
            <td>
              <span class="badge" [ngClass]="statusClass(repo.status)">{{ repo.status }}</span>
            </td>
            <td class="text-muted">{{ repo.stats?.enrichmentCount || 0 }}</td>
            <td>
              <span *ngIf="repo.stats?.hasEmbeddings">{{ repo.stats.embeddingCount }}</span>
              <span class="text-muted" *ngIf="!repo.stats?.hasEmbeddings">--</span>
            </td>
            <td class="text-muted">{{ repo.lastSyncedAt ? (repo.lastSyncedAt | date:'short') : 'Never' }}</td>
            <td>
              <button class="btn btn-sm btn-secondary" (click)="sync(repo)" [disabled]="syncing[repo.id]">
                <i class="bi bi-arrow-repeat"></i> Sync
              </button>
            </td>
          </tr>
        </tbody>
      </table>
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

  constructor(private api: ApiService) {}

  ngOnInit() { this.loadRepositories(); }

  loadRepositories() {
    this.loading = true;
    this.api.getRepositories().subscribe({
      next: repos => { this.repositories = repos; this.loading = false; },
      error: err => { this.error = 'Failed to load repositories'; this.loading = false; }
    });
  }

  sync(repo: Repository) {
    this.syncing[repo.id] = true;
    this.api.syncRepository(repo.id).subscribe({
      next: () => { this.syncing[repo.id] = false; },
      error: () => { this.syncing[repo.id] = false; }
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
