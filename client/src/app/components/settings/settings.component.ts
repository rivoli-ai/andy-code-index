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

    <div class="card" style="max-width:600px">
      <h3 style="font-size:1rem;margin-bottom:1rem">Embedding Configuration</h3>
      <div class="form-group">
        <label>Embedding API Key</label>
        <input class="form-control" type="password" [(ngModel)]="embeddingKey"
               [placeholder]="currentSettings?.embeddingKeyMasked || 'Not set'">
        <div class="text-muted" style="font-size:0.75rem;margin-top:0.25rem" *ngIf="currentSettings?.hasEmbeddingKey">
          Current key: {{ currentSettings.embeddingKeyMasked }}
        </div>
      </div>
      <div class="form-group">
        <label>Embedding Model</label>
        <select class="form-control" [(ngModel)]="embeddingModel">
          <option value="">Default (text-embedding-3-small)</option>
          <option value="text-embedding-3-small">text-embedding-3-small (1536 dims)</option>
          <option value="text-embedding-3-large">text-embedding-3-large (3072 dims)</option>
        </select>
      </div>
      <div style="display:flex;gap:0.75rem;margin-top:1.5rem">
        <button class="btn btn-primary" (click)="save()" [disabled]="saving">
          {{ saving ? 'Saving...' : 'Save Settings' }}
        </button>
        <button class="btn btn-secondary" (click)="deleteKey()" *ngIf="currentSettings?.hasEmbeddingKey">
          Remove Key
        </button>
      </div>
      <div *ngIf="message" style="margin-top:1rem;color:var(--success)">{{ message }}</div>
    </div>
  `
})
export class SettingsComponent implements OnInit {
  embeddingKey = '';
  embeddingModel = '';
  currentSettings: any = null;
  saving = false;
  message = '';

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.http.get(`${environment.apiUrl}/settings`).subscribe({
      next: (s: any) => { this.currentSettings = s; this.embeddingModel = s.embeddingModel || ''; }
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
