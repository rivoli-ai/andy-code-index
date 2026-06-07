import { Component, Input, OnInit, OnChanges, SimpleChanges, input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface IndexingRun {
  id: string;
  startedAt: string;
  completedAt?: string;
  durationSeconds?: number;
  status: string;
  snippetsAdded: number;
  snippetsUpdated: number;
  snippetsDeleted: number;
  snippetsUnchanged: number;
  apiDocsGenerated: number;
  commitsScanned: number;
  errorMessage?: string;
}

interface GitLogCommit {
  sha: string;
  abbreviatedSha: string;
  message: string;
  authorName: string;
  authorEmail: string;
  committedAt: string;
  isIndexed: boolean;
  enrichmentCount: number;
}

@Component({
  selector: 'app-repository-history',
  standalone: true,
  imports: [CommonModule],
  template: `
    <!-- Git Commits (filtered by branch) -->
    @if (gitCommits.length > 0) {
      <div class="card" style="margin-bottom:1.5rem">
        <h3 style="margin-bottom:1rem;font-size:1rem">
          Commits
          @if (ref) {
            <span class="badge badge-primary" style="font-size:0.75rem;margin-left:0.5rem">{{ ref }}</span>
          }
        </h3>
        @for (commit of gitCommits; track commit) {
          <div class="commit-item">
            <div style="display:flex;justify-content:space-between;align-items:center">
              <div style="flex:1;min-width:0">
                <div style="display:flex;align-items:center;gap:0.5rem;margin-bottom:0.25rem">
                  <code style="font-size:0.75rem;color:var(--primary);flex-shrink:0">{{ commit.abbreviatedSha }}</code>
                  @if (commit.isIndexed) {
                    <span class="badge badge-success" style="font-size:0.625rem;flex-shrink:0">indexed</span>
                  }
                  @if (commit.enrichmentCount > 0) {
                    <span class="badge badge-muted" style="font-size:0.625rem;flex-shrink:0">{{ commit.enrichmentCount }} enrichments</span>
                  }
                </div>
                <div style="font-size:0.8125rem;white-space:nowrap;overflow:hidden;text-overflow:ellipsis">{{ commit.message }}</div>
                <div class="text-muted" style="font-size:0.75rem;margin-top:0.125rem">
                  {{ commit.authorName }} &middot; {{ commit.committedAt | date:'short' }}
                </div>
              </div>
            </div>
          </div>
        }
        @if (hasMoreCommits) {
          <div style="text-align:center;padding:0.75rem 0">
            <button class="btn btn-secondary btn-sm" (click)="loadMoreCommits()" [disabled]="loadingCommits" style="font-size:0.75rem">
              {{ loadingCommits ? 'Loading...' : 'Load more' }}
            </button>
          </div>
        }
      </div>
    }
    @if (gitCommits.length === 0 && !loadingCommits && ref) {
      <div class="card" style="margin-bottom:1.5rem">
        <p class="text-muted" style="font-size:0.8125rem">No commits found for branch "{{ ref }}"</p>
      </div>
    }
    
    <!-- Indexing History -->
    @if (runs.length > 0) {
      <div class="card">
        <h3 style="margin-bottom:1rem;font-size:1rem">Indexing History</h3>
        @for (run of runs; track run) {
          <div class="run-item">
            <div style="display:flex;justify-content:space-between;align-items:center">
              <div>
                <span class="badge" [ngClass]="run.status === 'completed' ? 'badge-success' : run.status === 'failed' ? 'badge-danger' : 'badge-info'">
                  {{ run.status }}
                </span>
                <span class="text-muted" style="margin-left:0.75rem;font-size:0.8125rem">
                  {{ run.startedAt | date:'short' }}
                </span>
                @if (run.durationSeconds) {
                  <span class="text-muted" style="margin-left:0.5rem;font-size:0.75rem">
                    ({{ run.durationSeconds | number:'1.1-1' }}s)
                  </span>
                }
              </div>
              @if (run.status === 'completed') {
                <div class="stats">
                  @if (run.snippetsAdded) {
                    <span class="stat-badge added">+{{ run.snippetsAdded }}</span>
                  }
                  @if (run.snippetsUpdated) {
                    <span class="stat-badge updated">~{{ run.snippetsUpdated }}</span>
                  }
                  @if (run.snippetsDeleted) {
                    <span class="stat-badge deleted">-{{ run.snippetsDeleted }}</span>
                  }
                  @if (run.snippetsUnchanged) {
                    <span class="stat-badge unchanged">={{ run.snippetsUnchanged }}</span>
                  }
                </div>
              }
            </div>
            @if (run.errorMessage) {
              <div style="color:var(--danger);font-size:0.8125rem;margin-top:0.25rem">
                {{ run.errorMessage }}
              </div>
            }
          </div>
        }
      </div>
    }
    @if (runs.length === 0 && gitCommits.length === 0 && !loading && !loadingCommits) {
      <div class="empty-state card">
        <p class="text-muted">No history yet</p>
      </div>
    }
    `,
  styles: [`
    .run-item { padding: 0.625rem 0; border-bottom: 1px solid var(--border); }
    .run-item:last-child { border-bottom: none; }
    .commit-item { padding: 0.625rem 0; border-bottom: 1px solid var(--border); }
    .commit-item:last-child { border-bottom: none; }
    .stats { display: flex; gap: 0.375rem; }
    .stat-badge { font-size: 0.6875rem; font-weight: 600; padding: 0.125rem 0.5rem; border-radius: 100px; }
    .stat-badge.added { background: rgba(40,167,69,0.1); color: var(--success); }
    .stat-badge.updated { background: rgba(0,164,220,0.1); color: var(--accent); }
    .stat-badge.deleted { background: rgba(220,53,69,0.1); color: var(--danger); }
    .stat-badge.unchanged { background: var(--surface-2); color: var(--text-muted); }
  `]
})
export class RepositoryHistoryComponent implements OnInit, OnChanges {
  private http = inject(HttpClient);

  readonly repositoryId = input.required<string>();
  @Input() ref = '';
  runs: IndexingRun[] = [];
  gitCommits: GitLogCommit[] = [];
  loading = true;
  loadingCommits = false;
  hasMoreCommits = false;
  private nextCursor: string | null = null;

  ngOnInit() {
    this.http.get<IndexingRun[]>(`${environment.apiUrl}/repositories/${this.repositoryId()}/history`)
      .subscribe({
        next: runs => { this.runs = runs; this.loading = false; },
        error: () => this.loading = false
      });
    this.loadGitCommits();
  }

  ngOnChanges(changes: SimpleChanges) {
    if (changes['ref'] && !changes['ref'].firstChange) {
      this.gitCommits = [];
      this.nextCursor = null;
      this.hasMoreCommits = false;
      this.loadGitCommits();
    }
  }

  loadGitCommits() {
    this.loadingCommits = true;
    let params = new HttpParams().set('limit', '50');
    if (this.ref) {
      params = params.set('ref', this.ref);
    }
    if (this.nextCursor) {
      params = params.set('before', this.nextCursor);
    }

    this.http.get<any>(`${environment.apiUrl}/repositories/${this.repositoryId()}/git/log`, { params })
      .subscribe({
        next: res => {
          this.gitCommits = [...this.gitCommits, ...(res.commits || [])];
          this.hasMoreCommits = res.hasMore || false;
          this.nextCursor = res.nextCursor || null;
          this.loadingCommits = false;
        },
        error: () => this.loadingCommits = false
      });
  }

  loadMoreCommits() {
    this.loadGitCommits();
  }
}
