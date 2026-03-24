import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { IndexingTask } from '../../models/task.model';
import { SyncStatusComponent } from './sync-status.component';

@Component({
  selector: 'app-task-dashboard',
  standalone: true,
  imports: [CommonModule, SyncStatusComponent],
  template: `
    <div class="page-header">
      <h1>Task Queue</h1>
    </div>

    <app-sync-status />

    <div *ngIf="loading" style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>

    <div *ngIf="!loading">
      <div style="display:flex;gap:0.5rem;margin-bottom:1.5rem">
        <button class="btn" [ngClass]="tab === 'active' ? 'btn-primary' : 'btn-secondary'" (click)="tab='active'">
          Active ({{ activeTasks.length }})
        </button>
        <button class="btn" [ngClass]="tab === 'pending' ? 'btn-primary' : 'btn-secondary'" (click)="tab='pending'">
          Pending ({{ pendingTasks.length }})
        </button>
        <button class="btn" [ngClass]="tab === 'completed' ? 'btn-primary' : 'btn-secondary'" (click)="tab='completed'">
          Completed ({{ completedTasks.length }})
        </button>
        <button class="btn" [ngClass]="tab === 'failed' ? 'btn-primary' : 'btn-secondary'" (click)="tab='failed'">
          Failed ({{ failedTasks.length }})
        </button>
      </div>

      <div class="card" *ngFor="let task of currentTasks" style="margin-bottom:0.75rem">
        <div style="display:flex;justify-content:space-between;align-items:center">
          <div>
            <span class="badge" [ngClass]="statusClass(task.status)">{{ task.status }}</span>
            <strong style="margin-left:0.75rem">{{ operationLabel(task.operation) }}</strong>
            <span class="text-muted" style="margin-left:0.5rem;font-size:0.8125rem" *ngIf="getRepoName(task.repositoryId)">
              on {{ getRepoName(task.repositoryId) }}
            </span>
          </div>
          <div style="text-align:right">
            <span class="text-muted" style="font-size:0.8125rem">{{ task.createdAt | date:'short' }}</span>
            <div *ngIf="task.startedAt && task.completedAt" class="text-muted" style="font-size:0.75rem">
              {{ getDuration(task.startedAt, task.completedAt) }}
            </div>
          </div>
        </div>
        <div *ngIf="task.status === 'Running' && task.progress > 0" style="margin-top:0.75rem">
          <div class="progress"><div class="progress-bar" [style.width.%]="task.progress"></div></div>
          <span class="text-muted" style="font-size:0.75rem">{{ task.progress }}%</span>
        </div>
        <div *ngIf="task.errorMessage" style="margin-top:0.5rem;color:var(--danger);font-size:0.8125rem">
          {{ task.errorMessage }}
        </div>
      </div>

      <div *ngIf="currentTasks.length === 0" class="empty-state card">
        <i class="bi bi-check-circle"></i>
        <h3>No {{ tab }} tasks</h3>
      </div>
    </div>
  `
})
export class TaskDashboardComponent implements OnInit, OnDestroy {
  tasks: IndexingTask[] = [];
  repos: { id: string; name: string }[] = [];
  loading = true;
  tab = 'active';
  private pollInterval: any;

  private operationLabels: Record<string, string> = {
    'CloneRepository': 'Clone Repository',
    'SyncRepository': 'Sync Repository',
    'DeleteRepository': 'Delete Repository',
    'ScanCommit': 'Scan Commit',
    'RescanCommit': 'Rescan Commit',
    'ExtractSnippets': 'Extract Snippets',
    'CreateBM25Index': 'Build Keyword Index',
    'CreateCodeEmbeddings': 'Generate Embeddings',
    'CreateSummaryEnrichments': 'Generate Summaries',
    'CreateSummaryEmbeddings': 'Embed Summaries',
    'CreatePublicAPIDocs': 'Generate API Docs',
    'CreateArchitectureDocs': 'Generate Architecture Docs',
    'CreateDatabaseSchema': 'Generate DB Schema',
    'CreateCommitDescription': 'Generate Commit Descriptions',
    'CreateCookbook': 'Generate Cookbook',
    'CreateWiki': 'Generate Wiki',
    'ExtractDependencies': 'Extract Dependencies',
  };

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.loadTasks();
    this.pollInterval = setInterval(() => this.loadTasks(), 5000);
    this.api.getRepositories().subscribe({
      next: repos => this.repos = repos.map((r: any) => ({ id: r.id, name: r.name }))
    });
  }

  ngOnDestroy() { clearInterval(this.pollInterval); }

  loadTasks() {
    this.api.getTasks().subscribe({
      next: tasks => { this.tasks = tasks; this.loading = false; },
      error: () => this.loading = false
    });
  }

  get activeTasks() { return this.tasks.filter(t => t.status === 'Running'); }
  get pendingTasks() { return this.tasks.filter(t => t.status === 'Pending'); }
  get completedTasks() { return this.tasks.filter(t => t.status === 'Completed'); }
  get failedTasks() { return this.tasks.filter(t => t.status === 'Failed'); }

  get currentTasks(): IndexingTask[] {
    switch (this.tab) {
      case 'active': return this.activeTasks;
      case 'pending': return this.pendingTasks;
      case 'completed': return this.completedTasks;
      case 'failed': return this.failedTasks;
      default: return [];
    }
  }

  operationLabel(operation: string): string {
    return this.operationLabels[operation] || operation;
  }

  getRepoName(repositoryId: string): string {
    return this.repos.find(r => r.id === repositoryId)?.name || '';
  }

  getDuration(start: string, end: string): string {
    const ms = new Date(end).getTime() - new Date(start).getTime();
    if (ms < 1000) return `${ms}ms`;
    const seconds = Math.floor(ms / 1000);
    if (seconds < 60) return `${seconds}s`;
    const minutes = Math.floor(seconds / 60);
    const remaining = seconds % 60;
    return `${minutes}m ${remaining}s`;
  }

  statusClass(status: string): string {
    switch (status) {
      case 'Running': return 'badge-info';
      case 'Completed': return 'badge-success';
      case 'Failed': return 'badge-danger';
      case 'Cancelled': return 'badge-warning';
      default: return 'badge-muted';
    }
  }
}
