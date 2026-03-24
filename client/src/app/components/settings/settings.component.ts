import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-header">
      <h1>Settings</h1>
    </div>

    <div class="card" style="max-width:640px" *ngIf="settings">
      <h3 style="font-size:1rem;margin-bottom:1rem">Embedding Configuration</h3>

      <!-- Key status indicator -->
      <div class="key-status" *ngIf="settings.embedding.hasKey">
        <div style="display:flex;align-items:center;gap:0.75rem;padding:0.75rem;background:var(--background-alt);border-radius:var(--radius);margin-bottom:1rem">
          <i class="bi bi-key-fill" style="color:var(--success);font-size:1.25rem"></i>
          <div style="flex:1">
            <div style="font-weight:500">API key configured</div>
            <div class="text-muted" style="font-size:0.8125rem">
              <code>{{ settings.embedding.maskedKey }}</code>
              <span class="badge" [ngClass]="settings.embedding.source === 'user' ? 'badge-primary' : 'badge-muted'" style="margin-left:0.5rem">
                {{ settings.embedding.source }}
              </span>
            </div>
          </div>
          <button class="btn btn-sm btn-secondary" (click)="deleteKey()" *ngIf="settings.embedding.source === 'user'">
            Remove
          </button>
        </div>
      </div>

      <div class="key-status" *ngIf="!settings.embedding.hasKey" style="padding:0.75rem;background:rgba(255,193,7,0.08);border:1px solid rgba(255,193,7,0.2);border-radius:var(--radius);margin-bottom:1rem">
        <div style="display:flex;align-items:center;gap:0.75rem">
          <i class="bi bi-exclamation-triangle" style="color:#856404;font-size:1.25rem"></i>
          <div>
            <div style="font-weight:500;color:#856404">No embedding key configured</div>
            <div class="text-muted" style="font-size:0.8125rem">Semantic search and code embeddings require an API key.</div>
          </div>
        </div>
      </div>

      <div class="form-group">
        <label>{{ settings.embedding.hasKey && settings.embedding.source === 'user' ? 'Update' : 'Set' }} Embedding API Key</label>
        <input class="form-control" type="password" [(ngModel)]="embeddingKey"
               placeholder="sk-...">
      </div>
      <div class="form-group">
        <label>Model</label>
        <select class="form-control" [(ngModel)]="embeddingModel">
          <option value="">Default ({{ settings.embedding.model }})</option>
          <option value="text-embedding-3-small">text-embedding-3-small (1536 dims)</option>
          <option value="text-embedding-3-large">text-embedding-3-large (3072 dims)</option>
        </select>
      </div>
      <div style="margin-top:1.5rem">
        <button class="btn btn-primary" (click)="save()" [disabled]="saving || !embeddingKey">
          {{ saving ? 'Saving...' : 'Save' }}
        </button>
      </div>
      <div *ngIf="message" style="margin-top:1rem;color:var(--success);font-size:0.875rem">{{ message }}</div>
    </div>

    <!-- Change History -->
    <div class="card" style="max-width:640px;margin-top:1.5rem" *ngIf="history.length > 0">
      <h3 style="font-size:1rem;margin-bottom:1rem">Change History</h3>
      <div class="history-item" *ngFor="let entry of history">
        <div style="display:flex;justify-content:space-between;align-items:center">
          <div>
            <span class="badge" [ngClass]="entry.action === 'removed' ? 'badge-danger' : entry.action === 'set' ? 'badge-success' : 'badge-info'">
              {{ entry.action }}
            </span>
            <strong style="margin-left:0.5rem">{{ entry.field }}</strong>
          </div>
          <span class="text-muted" style="font-size:0.75rem">{{ entry.createdAt | date:'short' }}</span>
        </div>
        <div class="text-muted" style="font-size:0.8125rem;margin-top:0.25rem" *ngIf="entry.oldValue || entry.newValue">
          <span *ngIf="entry.oldValue"><code>{{ entry.oldValue }}</code> &rarr; </span>
          <code *ngIf="entry.newValue">{{ entry.newValue }}</code>
          <span *ngIf="!entry.newValue && entry.action === 'removed'">(removed)</span>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .history-item { padding: 0.625rem 0; border-bottom: 1px solid var(--border); }
    .history-item:last-child { border-bottom: none; }
  `]
})
export class SettingsComponent implements OnInit {
  embeddingKey = '';
  embeddingModel = '';
  settings: any = null;
  history: any[] = [];
  saving = false;
  message = '';

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.http.get(`${environment.apiUrl}/settings`).subscribe({
      next: (s: any) => {
        this.settings = s;
        this.embeddingModel = s.embedding?.model || '';
      }
    });
    this.http.get<any[]>(`${environment.apiUrl}/settings/history`).subscribe({
      next: h => this.history = h
    });
  }

  save() {
    this.saving = true;
    this.message = '';
    const body: any = {};
    if (this.embeddingKey) body.embeddingApiKey = this.embeddingKey;
    if (this.embeddingModel) body.embeddingModel = this.embeddingModel;

    this.http.put(`${environment.apiUrl}/settings`, body).subscribe({
      next: () => { this.message = 'Settings saved'; this.saving = false; this.embeddingKey = ''; this.ngOnInit(); },
      error: () => { this.saving = false; }
    });
  }

  deleteKey() {
    this.http.delete(`${environment.apiUrl}/settings/embedding-key`).subscribe({
      next: () => { this.message = 'Key removed'; this.ngOnInit(); }
    });
  }
}
