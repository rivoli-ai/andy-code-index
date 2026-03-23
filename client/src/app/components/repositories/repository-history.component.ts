import { Component, Input, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
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

@Component({
  selector: 'app-repository-history',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="card" *ngIf="runs.length > 0">
      <h3 style="margin-bottom:1rem;font-size:1rem">Indexing History</h3>
      <div class="run-item" *ngFor="let run of runs">
        <div style="display:flex;justify-content:space-between;align-items:center">
          <div>
            <span class="badge" [ngClass]="run.status === 'completed' ? 'badge-success' : run.status === 'failed' ? 'badge-danger' : 'badge-info'">
              {{ run.status }}
            </span>
            <span class="text-muted" style="margin-left:0.75rem;font-size:0.8125rem">
              {{ run.startedAt | date:'short' }}
            </span>
            <span class="text-muted" style="margin-left:0.5rem;font-size:0.75rem" *ngIf="run.durationSeconds">
              ({{ run.durationSeconds | number:'1.1-1' }}s)
            </span>
          </div>
          <div class="stats" *ngIf="run.status === 'completed'">
            <span class="stat-badge added" *ngIf="run.snippetsAdded">+{{ run.snippetsAdded }}</span>
            <span class="stat-badge updated" *ngIf="run.snippetsUpdated">~{{ run.snippetsUpdated }}</span>
            <span class="stat-badge deleted" *ngIf="run.snippetsDeleted">-{{ run.snippetsDeleted }}</span>
            <span class="stat-badge unchanged" *ngIf="run.snippetsUnchanged">={{ run.snippetsUnchanged }}</span>
          </div>
        </div>
        <div *ngIf="run.errorMessage" style="color:var(--danger);font-size:0.8125rem;margin-top:0.25rem">
          {{ run.errorMessage }}
        </div>
      </div>
    </div>
    <div class="empty-state card" *ngIf="runs.length === 0 && !loading">
      <p class="text-muted">No indexing history yet</p>
    </div>
  `,
  styles: [`
    .run-item { padding: 0.625rem 0; border-bottom: 1px solid var(--border); }
    .run-item:last-child { border-bottom: none; }
    .stats { display: flex; gap: 0.375rem; }
    .stat-badge { font-size: 0.6875rem; font-weight: 600; padding: 0.125rem 0.5rem; border-radius: 100px; }
    .stat-badge.added { background: rgba(40,167,69,0.1); color: var(--success); }
    .stat-badge.updated { background: rgba(0,164,220,0.1); color: var(--accent); }
    .stat-badge.deleted { background: rgba(220,53,69,0.1); color: var(--danger); }
    .stat-badge.unchanged { background: var(--surface-2); color: var(--text-muted); }
  `]
})
export class RepositoryHistoryComponent implements OnInit {
  @Input() repositoryId!: string;
  runs: IndexingRun[] = [];
  loading = true;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.http.get<IndexingRun[]>(`${environment.apiUrl}/repositories/${this.repositoryId}/history`)
      .subscribe({
        next: runs => { this.runs = runs; this.loading = false; },
        error: () => this.loading = false
      });
  }
}
