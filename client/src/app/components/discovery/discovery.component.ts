import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface DiscoveredRepo {
  name: string;
  fullName: string;
  cloneUrl: string;
  provider: string;
  defaultBranch?: string;
  description?: string;
  alreadyTracked: boolean;
  selected?: boolean;
  stars?: number;
  openIssues?: number;
  language?: string;
  lastPushedAt?: string;
  size?: number; // KB
}

@Component({
  selector: 'app-discovery',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="page-header">
      <h1>Discover Repositories</h1>
    </div>
    
    <div class="card mb-2">
      <div class="discover-form">
        <div class="discover-field">
          <label>Provider</label>
          <select class="form-control" [(ngModel)]="provider">
            <option value="github">GitHub</option>
            <option value="azure-devops">Azure DevOps</option>
          </select>
        </div>
        <div class="discover-field" style="flex:1">
          <label>Organization</label>
          <input class="form-control" [(ngModel)]="org" placeholder="e.g., rivoli-ai">
        </div>
        @if (provider === 'azure-devops') {
          <div class="discover-field">
            <label>Project (optional)</label>
            <input class="form-control" [(ngModel)]="project" placeholder="e.g., MyProject">
          </div>
        }
        <div class="discover-field">
          <label>PAT (optional)</label>
          <input class="form-control" type="password" [(ngModel)]="pat" placeholder="For private orgs">
        </div>
        <div class="discover-field discover-action">
          <label>&nbsp;</label>
          <button class="btn btn-primary" (click)="discover()" [disabled]="discovering || !org" style="width:100%">
            {{ discovering ? 'Discovering...' : 'Discover' }}
          </button>
        </div>
      </div>
    </div>
    
    @if (discovering) {
      <div style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>
    }
    
    @if (!discovering && repos.length > 0) {
      <div>
        <div class="card mb-2" style="padding:0.75rem 1rem">
          <div style="display:flex;gap:0.75rem;flex-wrap:wrap;align-items:center">
            <input class="form-control" [(ngModel)]="repoSearch" placeholder="Filter by name..." style="width:200px;padding:0.375rem 0.75rem">
            <select class="form-control" [(ngModel)]="sortBy" style="width:150px;padding:0.375rem 0.75rem">
              <option value="name">Sort: Name</option>
              <option value="stars">Sort: Stars</option>
              <option value="updated">Sort: Last Updated</option>
              <option value="size">Sort: Size</option>
            </select>
            <select class="form-control" [(ngModel)]="languageFilter" style="width:150px;padding:0.375rem 0.75rem">
              <option value="">All Languages</option>
              @for (lang of availableLanguages; track lang) {
                <option [value]="lang">{{ lang }}</option>
              }
            </select>
            <label style="display:flex;align-items:center;gap:0.375rem;font-size:0.875rem;cursor:pointer;margin:0">
              <input type="checkbox" [(ngModel)]="hideTracked"> Hide already tracked
            </label>
            <button class="btn btn-sm btn-secondary" (click)="selectAllVisible()">Select All</button>
            <button class="btn btn-sm btn-secondary" (click)="deselectAll()">Deselect All</button>
            <span class="text-muted" style="margin-left:auto;font-size:0.8125rem">
              Showing {{ filteredRepos.length }} of {{ repos.length }} ({{ trackedCount }} tracked)
            </span>
            <button class="btn btn-primary btn-sm" (click)="addSelected()" [disabled]="adding || selectedCount === 0">
              Add {{ selectedCount }} Selected
            </button>
          </div>
        </div>
        @for (repo of filteredRepos; track repo) {
          <div class="card"
            [class.tracked-repo]="repo.alreadyTracked"
            style="margin-bottom:0.5rem;padding:1rem">
            <div style="display:flex;align-items:center;gap:0.75rem">
              @if (!repo.alreadyTracked) {
                <input type="checkbox" [(ngModel)]="repo.selected" [disabled]="repo.alreadyTracked" style="width:18px;height:18px"
                  >
              }
              @if (repo.alreadyTracked) {
                <i class="bi bi-check-circle-fill" style="color:var(--success);font-size:1.125rem;min-width:18px"></i>
              }
              <div style="flex:1">
                <strong>{{ repo.name }}</strong>
                <span class="text-muted" style="margin-left:0.5rem;font-size:0.8125rem">{{ repo.fullName }}</span>
                @if (repo.alreadyTracked) {
                  <span class="badge badge-success" style="margin-left:0.5rem">Already Tracked</span>
                }
              </div>
              <span class="badge badge-muted">{{ repo.provider }}</span>
              @if (repo.defaultBranch) {
                <span class="text-muted" style="font-size:0.8125rem">{{ repo.defaultBranch }}</span>
              }
            </div>
            <div style="display:flex;gap:0.75rem;align-items:center;margin-top:0.375rem;flex-wrap:wrap">
              @if (repo.stars != null) {
                <span class="text-muted" style="font-size:0.8125rem" title="Stars">&#9733; {{ repo.stars }}</span>
              }
              @if (repo.language) {
                <span class="badge badge-info">{{ repo.language }}</span>
              }
              @if (repo.lastPushedAt) {
                <span class="text-muted" style="font-size:0.8125rem" title="Last pushed">{{ timeAgo(repo.lastPushedAt) }}</span>
              }
              @if (repo.size != null) {
                <span class="text-muted" style="font-size:0.8125rem" title="Repository size">{{ formatSize(repo.size) }}</span>
              }
            </div>
            @if (repo.description) {
              <div class="text-muted" style="font-size:0.8125rem;margin-top:0.25rem">{{ repo.description }}</div>
            }
          </div>
        }
      </div>
    }
    
    @if (!discovering && repos.length === 0 && searched) {
      <div class="empty-state card">
        <i class="bi bi-search"></i>
        <h3>No repositories found</h3>
        <p>Check the organization name and try again.</p>
      </div>
    }
    
    @if (error) {
      <div class="card" style="color:var(--danger);margin-top:1rem">{{ error }}</div>
    }
    @if (addMessage) {
      <div class="card" style="color:var(--success);margin-top:1rem">{{ addMessage }}</div>
    }
    `,
  styles: [`
    .discover-form { display: flex; gap: 1rem; align-items: flex-start; flex-wrap: wrap; }
    .discover-field { display: flex; flex-direction: column; min-width: 160px; }
    .discover-field label { margin-bottom: 0.375rem; font-weight: 500; font-size: var(--font-sm); }
    .discover-field .form-control,
    .discover-field .btn { height: 42px; }
    .discover-action { justify-content: flex-end; }
    :host ::ng-deep .tracked-repo { opacity: 0.55; }
  `]
})
export class DiscoveryComponent {
  provider = 'github';
  org = '';
  project = '';
  pat = '';
  repos: DiscoveredRepo[] = [];
  discovering = false;
  adding = false;
  searched = false;
  error = '';
  addMessage = '';
  repoSearch = '';
  hideTracked = false;
  sortBy = 'name';
  languageFilter = '';

  constructor(private http: HttpClient) {}

  get selectedCount(): number {
    return this.repos.filter(r => r.selected && !r.alreadyTracked).length;
  }

  get trackedCount(): number {
    return this.repos.filter(r => r.alreadyTracked).length;
  }

  get availableLanguages(): string[] {
    const langs = new Set<string>();
    this.repos.forEach(r => { if (r.language) langs.add(r.language); });
    return Array.from(langs).sort();
  }

  get filteredRepos(): DiscoveredRepo[] {
    let result = this.repos;
    if (this.repoSearch) {
      const q = this.repoSearch.toLowerCase();
      result = result.filter(r => r.name.toLowerCase().includes(q) || r.fullName.toLowerCase().includes(q));
    }
    if (this.hideTracked) result = result.filter(r => !r.alreadyTracked);
    if (this.languageFilter) result = result.filter(r => r.language === this.languageFilter);
    return result.sort((a, b) => {
      switch (this.sortBy) {
        case 'stars': return (b.stars ?? 0) - (a.stars ?? 0);
        case 'updated': return new Date(b.lastPushedAt ?? 0).getTime() - new Date(a.lastPushedAt ?? 0).getTime();
        case 'size': return (b.size ?? 0) - (a.size ?? 0);
        default: return a.name.localeCompare(b.name);
      }
    });
  }

  selectAllVisible() {
    this.filteredRepos.filter(r => !r.alreadyTracked).forEach(r => r.selected = true);
  }

  deselectAll() {
    this.repos.forEach(r => r.selected = false);
  }

  timeAgo(dateStr: string): string {
    const now = new Date();
    const date = new Date(dateStr);
    const seconds = Math.floor((now.getTime() - date.getTime()) / 1000);
    if (seconds < 60) return 'just now';
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 30) return `${days}d ago`;
    const months = Math.floor(days / 30);
    if (months < 12) return `${months}mo ago`;
    const years = Math.floor(days / 365);
    return `${years}y ago`;
  }

  formatSize(kb: number): string {
    if (kb < 1024) return `${kb} KB`;
    const mb = kb / 1024;
    if (mb < 1024) return `${mb.toFixed(1)} MB`;
    const gb = mb / 1024;
    return `${gb.toFixed(1)} GB`;
  }

  discover() {
    this.discovering = true;
    this.error = '';
    this.repos = [];
    this.searched = true;

    let url = `${environment.apiUrl}/discover/${this.provider}?org=${encodeURIComponent(this.org)}`;
    if (this.provider === 'azure-devops' && this.project) url += `&project=${encodeURIComponent(this.project)}`;
    if (this.pat) url += `&pat=${encodeURIComponent(this.pat)}`;

    this.http.get<DiscoveredRepo[]>(url).subscribe({
      next: repos => { this.repos = repos; this.discovering = false; },
      error: err => { this.error = 'Discovery failed: ' + (err.error?.message || err.message); this.discovering = false; }
    });
  }

  addSelected() {
    this.adding = true;
    this.addMessage = '';
    const urls = this.repos.filter(r => r.selected && !r.alreadyTracked).map(r => r.cloneUrl);

    this.http.post<any>(`${environment.apiUrl}/discover/sync`, {
      repositoryUrls: urls,
      pat: this.pat || undefined
    }).subscribe({
      next: res => {
        this.addMessage = `Added ${res.added?.length || 0} repositories, skipped ${res.skipped?.length || 0}`;
        this.repos.filter(r => r.selected).forEach(r => { r.alreadyTracked = true; r.selected = false; });
        this.adding = false;
      },
      error: () => { this.error = 'Failed to add repositories'; this.adding = false; }
    });
  }
}
