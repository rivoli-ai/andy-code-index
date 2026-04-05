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
        <h3 style="font-size:1.125rem;margin-bottom:0.75rem">Embedding Provider</h3>
        <p class="text-muted" style="font-size:0.8125rem;margin-bottom:1rem">
          Generates <strong>vector representations</strong> of your code for <strong>semantic search</strong> --
          finding code by meaning, not just keywords. Also used for re-embedding when code changes.
          Without this key, only keyword search (BM25) is available.
        </p>

        <div *ngIf="settings.embedding.hasKey" style="display:flex;align-items:center;gap:0.75rem;padding:0.75rem;background:var(--background-alt);border-radius:var(--radius);margin-bottom:1rem">
          <i class="bi bi-key-fill" style="color:var(--success);font-size:1.25rem"></i>
          <div style="flex:1">
            <div style="font-weight:500;font-size:0.875rem">
              Key configured
              <i *ngIf="health && health.embeddingKeyValid" class="bi bi-check-circle-fill" style="color:var(--success);margin-left:0.375rem" title="Key is valid"></i>
              <i *ngIf="health && !health.embeddingKeyValid && health.embeddingError" class="bi bi-x-circle-fill" style="color:var(--danger);margin-left:0.375rem" [title]="health.embeddingError"></i>
            </div>
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
          <label>Server URL</label>
          <input class="form-control" type="text" [(ngModel)]="embeddingBaseUrl" placeholder="https://api.openai.com/v1" style="margin-bottom:0.375rem">
          <div style="display:flex;gap:0.375rem;flex-wrap:wrap;margin-bottom:0.5rem">
            <button class="btn btn-xs btn-outline" (click)="embeddingBaseUrl = 'https://api.openai.com/v1'">OpenAI</button>
            <button class="btn btn-xs btn-outline" (click)="embeddingBaseUrl = 'http://localhost:11434/v1'">Ollama</button>
            <button class="btn btn-xs btn-outline" (click)="embeddingBaseUrl = 'https://api.groq.com/openai/v1'">Groq</button>
          </div>
        </div>
        <div class="form-group">
          <label>API Key</label>
          <div style="display:flex;gap:0.5rem">
            <input class="form-control" type="password" [(ngModel)]="embeddingKey" placeholder="sk-..." (keydown.enter)="embeddingKey && saveEmbedding()" style="flex:1">
            <button class="btn btn-sm btn-secondary" (click)="testConnection('embedding')" [disabled]="testingEmbed" title="Test embedding connection">
              <span *ngIf="testingEmbed">...</span>
              <span *ngIf="!testingEmbed">Test</span>
            </button>
          </div>
          <div *ngIf="embedTestResult" style="margin-top:0.375rem;font-size:0.8125rem">
            <span *ngIf="embedTestResult.success" style="color:var(--success)">
              <i class="bi bi-check-circle-fill"></i> Connected ({{ embedTestResult.latencyMs }}ms)
            </span>
            <span *ngIf="!embedTestResult.success" style="color:var(--danger)">
              <i class="bi bi-x-circle-fill"></i> {{ embedTestResult.error }}
            </span>
          </div>
        </div>
        <div class="form-group">
          <label>Model</label>
          <input class="form-control" [(ngModel)]="embeddingModel" name="embeddingModel" list="embedding-models" placeholder="e.g., text-embedding-3-small">
          <datalist id="embedding-models">
            <option value="text-embedding-3-small">
            <option value="text-embedding-3-large">
            <option value="text-embedding-ada-002">
          </datalist>
        </div>
        <button class="btn btn-primary btn-sm" (click)="saveEmbedding()" [disabled]="savingEmbed || (!embeddingKey && !embeddingModel && !embeddingBaseUrl)">
          {{ savingEmbed ? 'Saving...' : 'Save Embedding Settings' }}
        </button>
        <span *ngIf="embedMessage" style="margin-left:0.75rem;color:var(--success);font-size:0.8125rem">{{ embedMessage }}</span>
      </div>

      <!-- LLM / Chat Section -->
      <div class="card">
        <h3 style="font-size:1.125rem;margin-bottom:0.75rem">LLM / Chat Provider</h3>
        <p class="text-muted" style="font-size:0.8125rem;margin-bottom:1rem">
          Powers the <strong>Chat</strong> feature (ask questions about your codebase) and generates
          <strong>enrichments</strong>: architecture docs, wiki pages, cookbook guides, database schema docs,
          and code summaries. Uses the same key as embedding if no separate LLM key is set.
        </p>

        <div *ngIf="settings.llm.hasKey" style="display:flex;align-items:center;gap:0.75rem;padding:0.75rem;background:var(--background-alt);border-radius:var(--radius);margin-bottom:1rem">
          <i class="bi bi-key-fill" style="color:var(--success);font-size:1.25rem"></i>
          <div style="flex:1">
            <div style="font-weight:500;font-size:0.875rem">
              LLM key configured
              <i *ngIf="health && health.llmKeyValid" class="bi bi-check-circle-fill" style="color:var(--success);margin-left:0.375rem" title="Key is valid"></i>
              <i *ngIf="health && !health.llmKeyValid && health.llmError" class="bi bi-x-circle-fill" style="color:var(--danger);margin-left:0.375rem" [title]="health.llmError"></i>
            </div>
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
          <label>Server URL</label>
          <input class="form-control" type="text" [(ngModel)]="llmBaseUrl" placeholder="https://api.openai.com/v1" style="margin-bottom:0.375rem">
          <div style="display:flex;gap:0.375rem;flex-wrap:wrap;margin-bottom:0.5rem">
            <button class="btn btn-xs btn-outline" (click)="llmBaseUrl = 'https://api.openai.com/v1'">OpenAI</button>
            <button class="btn btn-xs btn-outline" (click)="llmBaseUrl = 'http://localhost:11434/v1'">Ollama</button>
            <button class="btn btn-xs btn-outline" (click)="llmBaseUrl = 'https://api.groq.com/openai/v1'">Groq</button>
          </div>
        </div>
        <div class="form-group">
          <label>LLM API Key (optional, separate from embedding)</label>
          <div style="display:flex;gap:0.5rem">
            <input class="form-control" type="password" [(ngModel)]="llmKey" placeholder="sk-... (leave empty to use embedding key)" (keydown.enter)="llmKey && saveLlm()" style="flex:1">
            <button class="btn btn-sm btn-secondary" (click)="testConnection('llm')" [disabled]="testingLlm" title="Test LLM connection">
              <span *ngIf="testingLlm">...</span>
              <span *ngIf="!testingLlm">Test</span>
            </button>
          </div>
          <div *ngIf="llmTestResult" style="margin-top:0.375rem;font-size:0.8125rem">
            <span *ngIf="llmTestResult.success" style="color:var(--success)">
              <i class="bi bi-check-circle-fill"></i> Connected ({{ llmTestResult.latencyMs }}ms)
            </span>
            <span *ngIf="!llmTestResult.success" style="color:var(--danger)">
              <i class="bi bi-x-circle-fill"></i> {{ llmTestResult.error }}
            </span>
          </div>
        </div>
        <div class="form-group">
          <label>LLM Model</label>
          <input class="form-control" [(ngModel)]="llmModel" name="llmModel" list="llm-models" placeholder="e.g., gpt-4o-mini">
          <datalist id="llm-models">
            <option value="gpt-4o-mini">
            <option value="gpt-4o">
            <option value="gpt-4.1-mini">
            <option value="gpt-4.1">
            <option value="gpt-5">
            <option value="o3-mini">
            <option value="claude-sonnet-4-20250514">
          </datalist>
        </div>
        <button class="btn btn-primary btn-sm" (click)="saveLlm()" [disabled]="savingLlm || (!llmKey && !llmModel && !llmBaseUrl)">
          {{ savingLlm ? 'Saving...' : 'Save LLM Settings' }}
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
          <span class="text-muted" style="font-size:0.75rem">
            <span *ngIf="entry.userEmail" style="margin-right:0.5rem">{{ entry.userEmail }}</span>{{ entry.createdAt | date:'short' }}
          </span>
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
    .btn-xs { padding: 0.125rem 0.5rem; font-size: 0.75rem; }
    .btn-outline { background: transparent; border: 1px solid var(--border); color: var(--text-secondary); cursor: pointer; border-radius: var(--radius); }
    .btn-outline:hover { background: var(--background-alt); }
  `]
})
export class SettingsComponent implements OnInit {
  embeddingKey = '';
  embeddingModel = '';
  embeddingBaseUrl = '';
  llmKey = '';
  llmModel = '';
  llmBaseUrl = '';
  settings: any = null;
  history: any[] = [];
  savingEmbed = false;
  savingLlm = false;
  embedMessage = '';
  llmMessage = '';
  testingEmbed = false;
  testingLlm = false;
  embedTestResult: any = null;
  llmTestResult: any = null;
  health: any = null;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.embedMessage = '';
    this.llmMessage = '';
    this.embedTestResult = null;
    this.llmTestResult = null;
    this.http.get(`${environment.apiUrl}/settings`).subscribe({
      next: (s: any) => {
        this.settings = s;
        this.embeddingModel = s.embedding?.model || '';
        this.embeddingBaseUrl = s.embedding?.baseUrl || '';
        this.llmModel = s.llm?.model || '';
        this.llmBaseUrl = s.llm?.baseUrl || '';
      }
    });
    this.http.get<any[]>(`${environment.apiUrl}/settings/history`).subscribe({
      next: h => this.history = h
    });
    this.http.get(`${environment.apiUrl}/settings/health`).subscribe({
      next: (h: any) => this.health = h,
      error: () => {}
    });
  }

  saveEmbedding() {
    this.savingEmbed = true;
    this.embedMessage = '';
    const body: any = {};
    if (this.embeddingKey) body.embeddingApiKey = this.embeddingKey;
    if (this.embeddingModel) body.embeddingModel = this.embeddingModel;
    if (this.embeddingBaseUrl) body.embeddingBaseUrl = this.embeddingBaseUrl;

    this.http.put(`${environment.apiUrl}/settings`, body).subscribe({
      next: () => { this.embedMessage = 'Saved'; this.savingEmbed = false; this.embeddingKey = ''; this.ngOnInit(); },
      error: () => this.savingEmbed = false
    });
  }

  saveLlm() {
    this.savingLlm = true;
    this.llmMessage = '';
    const body: any = {};
    if (this.llmKey) body.llmApiKey = this.llmKey;
    if (this.llmModel) body.llmModel = this.llmModel;
    if (this.llmBaseUrl) body.llmBaseUrl = this.llmBaseUrl;

    this.http.put(`${environment.apiUrl}/settings`, body).subscribe({
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

  testConnection(type: 'embedding' | 'llm') {
    if (type === 'embedding') {
      this.testingEmbed = true;
      this.embedTestResult = null;
    } else {
      this.testingLlm = true;
      this.llmTestResult = null;
    }

    this.http.post<any>(`${environment.apiUrl}/settings/test-connection`, { type }).subscribe({
      next: (result) => {
        if (type === 'embedding') {
          this.embedTestResult = result;
          this.testingEmbed = false;
        } else {
          this.llmTestResult = result;
          this.testingLlm = false;
        }
      },
      error: (err) => {
        const errorResult = { success: false, error: err.message || 'Request failed' };
        if (type === 'embedding') {
          this.embedTestResult = errorResult;
          this.testingEmbed = false;
        } else {
          this.llmTestResult = errorResult;
          this.testingLlm = false;
        }
      }
    });
  }
}
