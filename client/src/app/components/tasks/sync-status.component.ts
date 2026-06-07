import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

interface SyncStatus {
  enabled: boolean;
  intervalSeconds: number;
  lastRunAt?: string;
  nextRunAt?: string;
  repositoriesTracked: number;
}

@Component({
  selector: 'app-sync-status',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (status) {
      <div class="card mb-2">
        <div style="display:flex;justify-content:space-between;align-items:center">
          <div>
            <strong>Periodic Sync</strong>
            <span class="badge" [ngClass]="status.enabled ? 'badge-success' : 'badge-muted'" style="margin-left:0.5rem">
              {{ status.enabled ? 'Enabled' : 'Disabled' }}
            </span>
          </div>
          <div class="text-muted" style="font-size:0.8125rem">
            {{ status.repositoriesTracked }} repositories tracked
          </div>
        </div>
        @if (status.enabled) {
          <div style="display:flex;gap:2rem;margin-top:0.75rem;font-size:0.8125rem">
            <div>
              <span class="text-muted">Interval:</span>
              {{ status.intervalSeconds / 60 | number:'1.0-0' }} min
            </div>
            @if (status.lastRunAt) {
              <div>
                <span class="text-muted">Last sync:</span>
                {{ status.lastRunAt | date:'short' }}
              </div>
            }
            @if (status.nextRunAt) {
              <div>
                <span class="text-muted">Next sync:</span>
                {{ status.nextRunAt | date:'short' }}
              </div>
            }
          </div>
        }
      </div>
    }
    `
})
export class SyncStatusComponent implements OnInit {
  private http = inject(HttpClient);

  status: SyncStatus | null = null;

  ngOnInit() {
    this.http.get<SyncStatus>(`${environment.apiUrl}/sync/status`).subscribe({
      next: s => this.status = s
    });
  }
}
