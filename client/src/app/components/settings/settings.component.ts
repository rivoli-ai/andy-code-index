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

    <div *ngIf="settings" style="display:grid;grid-template-columns:1fr 1fr;gap:1.5rem">
      <!-- Embedding Section -->
      <div class="card">
        <h3 style="font-size:1.125rem;margin-bottom:0.75rem">Embedding API Key</h3>
        <p class="text-muted" style="font-size:0.8125rem;margin-bottom:1rem">
          Generates <strong>vector representations</strong> of your code for <strong>semantic search</strong> --
          finding code by meaning, not just keywords. Also used for re-embedding when code changes.
          Without this key, only keyword search (BM25) is available.
        </p>

        <div *ngIf="settings.embedding.hasKey" style="display:flex;align-items:center;gap:0.75rem;padding:0.75rem;background:var(--background-alt);border-radius:var(--radius);margin-bottom:1rem">
          <i class="bi bi-key-fill" style="color:var(--success);font-size:1.25rem"></i>
          <div style="flex:1">
            <div style="font-weight:500;font-size:0.875rem">Key configured</div>
            <div class="text-muted" style="font-size:0.8125rem">
              <code>{{ settings.embedding.maskedKey }}</code>
              <span class="badge" [ngClass]="settings.embedding.source === 'user' ? 'badge-primary' : 'badge-muted'" style="margin-left:0.375rem">
                {{ settings.embedding.source }}
              </span>
            </div>
          </div>
          <button class="btn btn-sm btn-secondary" (click)="deleteEmbeddingKey()" *ngIf="settings.embedding.source === 'user'">Remove</button>
        </div>

        <div *ngIf="!settings.embedding.hasKey" style="padding:0.625rem 0.75rem;background:rgba(255,193,7,0.08);border:1px solid rgba(255,193,7,0.2);border-radius:var(--radius);margin-bottom:1rem;font-size:0.8125rem;color:#856404">
          <i class="bi bi-exclamation-triangle"></i> No embedding key configured. Semantic search unavailable.
        </div>

        <div class="form-group">
          <label>API Key</label>
          <input class="form-control" type="password" [(ngModel)]="embeddingKey" placeholder="sk-...">
        </div>
        <div class="form-group">
          <label>Model</label>
          <select class="form-control" [(ngModel)]="embeddingModel">
            <option value="">Default ({{ settings.embedding.model }})</option>
            <option value="text-embedding-3-small">text-embedding-3-small (1536 dims)</option>
            <option value="text-embedding-3-large">text-embedding-3-large (3072 dims)</option>
          </select>
        </div>
        <button class="btn btn-primary btn-sm" (click)="saveEmbedding()" [disabled]="savingEmbed || !embeddingKey">
          {{ savingEmbed ? 'Saving...' : 'Save Embedding Key' }}
        </button>
        <span *ngIf="embedMessage" style="margin-left:0.75rem;color:var(--success);font-size:0.8125rem">{{ embedMessage }}</span>
      </div>

      <!-- LLM / Chat Section -->
      <div class="card">
        <h3 style="font-size:1.125rem;margin-bottom:0.75rem">LLM / Chat Model</h3>
        <p class="text-muted" style="font-size:0.8125rem;margin-bottom:1rem">
          Powers the <strong>Chat</strong> feature (ask questions about your codebase) and generates
          <strong>enrichments</strong>: architecture docs, wiki pages, cookbook guides, database schema docs,
          and code summaries. Uses the same key as embedding if no separate LLM key is set.
        </p>

        <div *ngIf="settings.llm.hasKey" style="display:flex;align-items:center;gap:0.75rem;padding:0.75rem;background:var(--background-alt);border-radius:var(--radius);margin-bottom:1rem">
          <i class="bi bi-key-fill" style="color:var(--success);font-size:1.25rem"></i>
          <div style="flex:1">
            <div style="font-weight:500;font-size:0.875rem">LLM key configured</div>
            <code class="text-muted" style="font-size:0.8125rem">{{ settings.llm.maskedKey }}</code>
          </div>
          <button class="btn btn-sm btn-secondary" (click)="deleteLlmKey()">Remove</button>
        </div>

        <div *ngIf="!settings.llm.hasKey && settings.embedding.hasKey" style="padding:0.625rem 0.75rem;background:rgba(0,164,220,0.08);border:1px solid rgba(0,164,220,0.2);border-radius:var(--radius);margin-bottom:1rem;font-size:0.8125rem;color:var(--accent)">
          <i class="bi bi-info-circle"></i> Using embedding key as fallback for chat and enrichments.
        </div>

        <div *ngIf="!settings.llm.hasKey && !settings.embedding.hasKey" style="padding:0.625rem 0.75rem;background:rgba(255,193,7,0.08);border:1px solid rgba(255,193,7,0.2);border-radius:var(--radius);margin-bottom:1rem;font-size:0.8125rem;color:#856404">
          <i class="bi bi-exclamation-triangle"></i> No LLM key configured. Chat and enrichment generation unavailable.
        </div>

        <div class="form-group">
          <label>LLM API Key (optional, separate from embedding)</label>
          <input class="form-control" type="password" [(ngModel)]="llmKey" placeholder="sk-... (leave empty to use embedding key)">
        </div>
        <div class="form-group">
          <label>Supported Models</label>
          <div class="text-muted" style="font-size:0.8125rem">
            OpenAI: gpt-4o, gpt-4o-mini, gpt-3.5-turbo.
            Azure OpenAI, Ollama, or any OpenAI-compatible API.
            Model is configured server-side via Enrichment:Model.
          </div>
        </div>
        <button class="btn btn-primary btn-sm" (click)="saveLlm()" [disabled]="savingLlm || !llmKey">
          {{ savingLlm ? 'Saving...' : 'Save LLM Key' }}
        </button>
        <span *ngIf="llmMessage" style="margin-left:0.75rem;color:var(--success);font-size:0.8125rem">{{ llmMessage }}</span>
      </div>
    </div>

    <!-- Change History -->
    <div class="card" style="margin-top:1.5rem" *ngIf="history.length > 0">
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
  llmKey = '';
  settings: any = null;
  history: any[] = [];
  savingEmbed = false;
  savingLlm = false;
  embedMessage = '';
  llmMessage = '';

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.embedMessage = '';
    this.llmMessage = '';
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

  saveEmbedding() {
    this.savingEmbed = true;
    this.embedMessage = '';
    const body: any = {};
    if (this.embeddingKey) body.embeddingApiKey = this.embeddingKey;
    if (this.embeddingModel) body.embeddingModel = this.embeddingModel;

    this.http.put(`${environment.apiUrl}/settings`, body).subscribe({
      next: () => { this.embedMessage = 'Saved'; this.savingEmbed = false; this.embeddingKey = ''; this.ngOnInit(); },
      error: () => this.savingEmbed = false
    });
  }

  saveLlm() {
    this.savingLlm = true;
    this.llmMessage = '';
    this.http.put(`${environment.apiUrl}/settings`, { llmApiKey: this.llmKey }).subscribe({
      next: () => { this.llmMessage = 'Saved'; this.savingLlm = false; this.llmKey = ''; this.ngOnInit(); },
      error: () => this.savingLlm = false
    });
  }

  deleteEmbeddingKey() {
    this.http.delete(`${environment.apiUrl}/settings/embedding-key`).subscribe({
      next: () => { this.embedMessage = 'Key removed'; this.ngOnInit(); }
    });
  }

  deleteLlmKey() {
    this.http.put(`${environment.apiUrl}/settings`, { llmApiKey: '' }).subscribe({
      next: () => { this.llmMessage = 'Key removed'; this.ngOnInit(); }
    });
  }
}
