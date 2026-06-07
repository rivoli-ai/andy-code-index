import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ApiService } from '../../services/api.service';
import { IndexingTask } from '../../models/task.model';
import { SyncStatusComponent } from './sync-status.component';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-task-dashboard',
  standalone: true,
  imports: [CommonModule, SyncStatusComponent],
  template: `
    <div class="page-header">
      <h1>Task Queue</h1>
    </div>
    
    <app-sync-status />
    
    @if (loading) {
      <div style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>
    }
    
    @if (!loading) {
      <div>
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
        @for (task of currentTasks; track task) {
          <div class="card" style="margin-bottom:0.75rem">
            <div style="display:flex;justify-content:space-between;align-items:center">
              <div>
                <span class="badge" [ngClass]="statusClass(task.status)">{{ task.status }}</span>
                <strong style="margin-left:0.75rem">{{ operationLabel(task.operation) }}</strong>
                @if (getRepoName(task.repositoryId)) {
                  <span class="text-muted" style="margin-left:0.5rem;font-size:0.8125rem">
                    on {{ getRepoName(task.repositoryId) }}
                  </span>
                }
              </div>
              <div style="display:flex;align-items:center;gap:0.75rem">
                <span class="text-muted" style="font-size:0.8125rem">{{ task.createdAt | date:'short' }}</span>
                @if (task.startedAt && task.completedAt) {
                  <span class="text-muted" style="font-size:0.75rem">
                    {{ getDuration(task.startedAt, task.completedAt) }}
                  </span>
                }
                @if (task.status === 'Pending') {
                  <button class="btn btn-sm" style="font-size:0.75rem;padding:0.25rem 0.5rem;color:var(--danger);border:1px solid var(--danger);background:none" (click)="cancelTask(task.id)">
                    Cancel
                  </button>
                }
                @if (task.status === 'Running') {
                  <button class="btn btn-sm" style="font-size:0.75rem;padding:0.25rem 0.5rem;color:var(--danger);border:1px solid var(--danger);background:none" (click)="forceCancelTask(task.id)">
                    Force Cancel
                  </button>
                }
              </div>
            </div>
            @if (task.status === 'Running' && (task.progress > 0 || task.progressMessage)) {
              <div style="margin-top:0.75rem">
                <div class="progress"><div class="progress-bar" [style.width.%]="task.progress || 2"></div></div>
                <div style="display:flex;justify-content:space-between;margin-top:0.25rem">
                  <span class="text-muted" style="font-size:0.75rem">{{ task.progressMessage || '' }}</span>
                  <span class="text-muted" style="font-size:0.75rem">{{ task.progress }}%</span>
                </div>
                @if (task.chainStepIndex != null && task.chainTotalSteps) {
                  <div class="text-muted" style="font-size:0.75rem;margin-top:0.25rem">
                    Step {{ task.chainStepIndex! + 1 }} of {{ task.chainTotalSteps }}
                  </div>
                }
              </div>
            }
            @if (task.errorMessage) {
              <div style="margin-top:0.5rem;color:var(--danger);font-size:0.8125rem">
                {{ task.errorMessage }}
              </div>
            }
          </div>
        }
        @if (currentTasks.length === 0) {
          <div class="empty-state card">
            <i class="bi bi-check-circle"></i>
            <h3>No {{ tab }} tasks</h3>
          </div>
        }
      </div>
    }
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
    'ExtractCommitHistory': 'Extract Commit History',
    'CreateOwnershipDocs': 'Generate Ownership Docs',
    'CreateSecurityDocs': 'Generate Security Docs',
    'CreateOperationsDocs': 'Generate Operations Docs',
    'CreateQualityDocs': 'Generate Quality Docs',
  };

  private baseUrl = environment.apiUrl;

  constructor(private api: ApiService, private http: HttpClient) {}

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

  cancelTask(taskId: string) {
    this.http.delete(`${this.baseUrl}/queue/${taskId}`).subscribe({
      next: () => this.loadTasks(),
      error: (err: any) => alert(err?.error?.error || 'Failed to cancel task')
    });
  }

  forceCancelTask(taskId: string) {
    if (!confirm('Force cancel this running task? It may leave the repository in an incomplete state.')) return;
    this.http.post(`${this.baseUrl}/queue/${taskId}/cancel`, {}).subscribe({
      next: () => this.loadTasks(),
      error: (err: any) => alert(err?.error?.error || 'Failed to cancel task')
    });
  }
}
