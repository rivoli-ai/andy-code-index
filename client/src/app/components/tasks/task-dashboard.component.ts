import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../services/api.service';
import { IndexingTask } from '../../models/task.model';

@Component({
  selector: 'app-task-dashboard',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="page-header">
      <h1>Task Queue</h1>
    </div>

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
            <strong style="margin-left:0.75rem">{{ task.operation }}</strong>
          </div>
          <span class="text-muted" style="font-size:0.8125rem">{{ task.createdAt | date:'short' }}</span>
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
  loading = true;
  tab = 'active';
  private pollInterval: any;

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.loadTasks();
    this.pollInterval = setInterval(() => this.loadTasks(), 5000);
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
