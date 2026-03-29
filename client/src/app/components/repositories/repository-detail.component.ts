import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ApiService } from '../../services/api.service';
import { Repository } from '../../models/repository.model';
import { RepositoryHistoryComponent } from './repository-history.component';
import { RepositoryAnalyticsComponent } from './repository-analytics.component';
import { environment } from '../../../environments/environment';

interface CommitSummary {
  id: string;
  sha: string;
  message: string;
  authorName?: string;
  committedAt: string;
  isIndexed: boolean;
}

interface CommitComparison {
  from: string;
  to: string;
  added: any[];
  removed: any[];
  changed: any[];
}

@Component({
  selector: 'app-repository-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RepositoryHistoryComponent, RepositoryAnalyticsComponent],
  template: `
    <div *ngIf="loading" style="display:flex;justify-content:center;padding:3rem"><div class="spinner"></div></div>

    <div *ngIf="!loading && repo">
      <div class="page-header">
        <div>
          <h1>{{ repo.name }}</h1>
          <div style="display:flex;gap:0.75rem;align-items:center;margin-top:0.25rem" *ngIf="repo.lastIndexedCommitSha || repo.defaultBranch">
            <span *ngIf="repo.defaultBranch" class="badge badge-primary" style="font-size:0.75rem">
              <i class="bi bi-diagram-2"></i> {{ repo.defaultBranch }}
            </span>
            <code *ngIf="repo.lastIndexedCommitSha" style="font-size:0.75rem;color:var(--text-muted)">
              {{ repo.lastIndexedCommitSha.substring(0, 7) }}
            </code>
          </div>
        </div>
        <div style="display:flex;gap:0.75rem">
          <button class="btn btn-secondary" (click)="sync()" [disabled]="syncing">
            <i class="bi bi-arrow-repeat"></i> Sync
          </button>
          <button class="btn btn-danger" (click)="confirmDelete()">
            <i class="bi bi-trash"></i> Delete
          </button>
        </div>
      </div>

      <div style="display:grid;grid-template-columns:1fr 1fr;gap:1.5rem;margin-bottom:1.5rem">
        <div class="card">
          <h3 style="margin-bottom:1rem;font-size:1rem">Details</h3>
          <div class="detail-row"><span class="label">URL</span><a [href]="repo.url" target="_blank">{{ repo.url }}</a></div>
          <div class="detail-row"><span class="label">Provider</span><span class="badge badge-muted">{{ repo.provider }}</span></div>
          <div class="detail-row"><span class="label">Status</span><span class="badge" [ngClass]="statusClass(repo.status)">{{ repo.status }}</span></div>
          <div class="detail-row"><span class="label">Default Branch</span><span>{{ repo.defaultBranch || '—' }}</span></div>
          <div class="detail-row"><span class="label">Last Synced</span><span>{{ repo.lastSyncedAt ? (repo.lastSyncedAt | date:'medium') : 'Never' }}</span></div>
          <div class="detail-row">
            <span class="label">Sync Interval</span>
            <select [ngModel]="syncIntervalValue" (ngModelChange)="onSyncIntervalChange($event)"
                    style="padding:0.375rem 0.5rem;border:1px solid var(--border);border-radius:var(--radius);background:var(--surface);color:var(--text);font-size:0.8125rem">
              <option value="null">Default</option>
              <option value="0">Manual Only</option>
              <option value="15">15 min</option>
              <option value="30">30 min</option>
              <option value="60">1 hour</option>
              <option value="120">2 hours</option>
              <option value="360">6 hours</option>
              <option value="720">12 hours</option>
              <option value="1440">Daily</option>
            </select>
            <span *ngIf="syncIntervalSaving" style="margin-left:0.5rem;font-size:0.75rem;color:var(--text-muted)">Saving...</span>
            <span *ngIf="syncIntervalSaved" style="margin-left:0.5rem;font-size:0.75rem;color:var(--success)">Saved</span>
          </div>
        </div>
        <div class="card" *ngIf="repo.stats">
          <h3 style="margin-bottom:1rem;font-size:1rem">Statistics</h3>
          <div class="stat-grid">
            <div class="stat"><div class="stat-value">{{ repo.stats.commitCount }}</div><div class="stat-label">Commits</div></div>
            <div class="stat"><div class="stat-value">{{ repo.stats.enrichmentCount }}</div><div class="stat-label">Enrichments</div></div>
            <div class="stat">
              <div class="stat-value" [style.color]="repo.stats.hasEmbeddings ? 'var(--primary)' : 'var(--text-muted)'">
                {{ repo.stats.embeddingCount }}
              </div>
              <div class="stat-label">Embeddings</div>
            </div>
            <div class="stat"><div class="stat-value">{{ repo.stats.pendingTaskCount }}</div><div class="stat-label">Pending Tasks</div></div>
          </div>
          <div *ngIf="!repo.stats.hasEmbeddings && repo.status === 'indexed'"
               style="margin-top:0.75rem;padding:0.5rem 0.75rem;background:rgba(255,193,7,0.08);border-radius:var(--radius);font-size:0.8125rem;color:#856404">
            <i class="bi bi-info-circle"></i> No embeddings -- semantic search unavailable. Configure an embedding API key in Settings.
          </div>
        </div>
      </div>

      <!-- Summary stats from analytics endpoint -->
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:1.5rem;margin-bottom:1.5rem" *ngIf="summary">
        <div class="card">
          <h3 style="margin-bottom:1rem;font-size:1rem">Last Commit</h3>
          <div *ngIf="summary.lastCommit">
            <div style="font-weight:500;margin-bottom:0.25rem">{{ summary.lastCommit.authorName }}</div>
            <div class="text-muted" style="font-size:0.8125rem;margin-bottom:0.5rem">{{ summary.lastCommit.authorEmail }}</div>
            <div style="font-size:0.875rem;margin-bottom:0.5rem">{{ summary.lastCommit.message }}</div>
            <div class="text-muted" style="font-size:0.8125rem">
              <code>{{ summary.lastCommit.sha?.substring(0, 8) }}</code>
              <span style="margin-left:0.5rem">{{ getRelativeTime(summary.lastCommit.committedAt) }}</span>
            </div>
          </div>
          <div *ngIf="!summary.lastCommit" class="text-muted">No commits found</div>
        </div>
        <div class="card">
          <h3 style="margin-bottom:1rem;font-size:1rem">File Breakdown</h3>
          <div class="stat-grid" style="grid-template-columns:repeat(3, 1fr)">
            <div class="stat"><div class="stat-value">{{ summary.stats.totalFiles }}</div><div class="stat-label">Total Files</div></div>
            <div class="stat"><div class="stat-value">{{ summary.stats.testFiles }}</div><div class="stat-label">Test Files</div></div>
            <div class="stat"><div class="stat-value">{{ summary.stats.apiDocs }}</div><div class="stat-label">API Docs</div></div>
          </div>
          <div *ngIf="summary.enrichmentsByType && summary.enrichmentsByType.length > 0" style="margin-top:1rem">
            <div class="text-muted" style="font-size:0.75rem;margin-bottom:0.5rem;font-weight:500;text-transform:uppercase;letter-spacing:0.05em">Enrichments by type</div>
            <div style="display:flex;flex-wrap:wrap;gap:0.375rem">
              <span *ngFor="let et of summary.enrichmentsByType" class="badge badge-muted">{{ et.subtype }} ({{ et.count }})</span>
            </div>
          </div>
        </div>
      </div>

      <div class="card" *ngIf="repo.branches && repo.branches.length > 0">
        <h3 style="margin-bottom:1rem;font-size:1rem">Branches</h3>
        <div class="tag-list">
          <span *ngFor="let branch of repo.branches" class="badge" [ngClass]="branch.isDefault ? 'badge-primary' : 'badge-muted'">
            {{ branch.name }}
          </span>
        </div>
      </div>

      <!-- Commit Comparison -->
      <div class="card" style="margin-top:1.5rem" *ngIf="commits.length >= 2">
        <h3 style="margin-bottom:1rem;font-size:1rem">Compare Commits</h3>
        <div style="display:flex;gap:0.75rem;align-items:flex-end;flex-wrap:wrap">
          <div>
            <label style="font-size:0.75rem;color:var(--text-muted);display:block;margin-bottom:0.25rem">From</label>
            <select [(ngModel)]="compareFrom" style="padding:0.375rem 0.5rem;border:1px solid var(--border);border-radius:var(--radius);background:var(--surface);color:var(--text);font-size:0.8125rem;min-width:200px">
              <option value="">Select commit...</option>
              <option *ngFor="let c of commits" [value]="c.sha">{{ c.sha.substring(0, 7) }} - {{ c.message | slice:0:40 }}</option>
            </select>
          </div>
          <div>
            <label style="font-size:0.75rem;color:var(--text-muted);display:block;margin-bottom:0.25rem">To</label>
            <select [(ngModel)]="compareTo" style="padding:0.375rem 0.5rem;border:1px solid var(--border);border-radius:var(--radius);background:var(--surface);color:var(--text);font-size:0.8125rem;min-width:200px">
              <option value="">Select commit...</option>
              <option *ngFor="let c of commits" [value]="c.sha">{{ c.sha.substring(0, 7) }} - {{ c.message | slice:0:40 }}</option>
            </select>
          </div>
          <button class="btn btn-primary" (click)="compareCommits()" [disabled]="!compareFrom || !compareTo || comparing" style="font-size:0.8125rem">
            <i class="bi bi-arrow-left-right"></i> Compare
          </button>
        </div>
        <div *ngIf="compareError" style="margin-top:0.75rem;color:var(--danger);font-size:0.8125rem">{{ compareError }}</div>

        <!-- Comparison Results -->
        <div *ngIf="comparison" style="margin-top:1rem">
          <div style="display:flex;gap:1rem;margin-bottom:1rem">
            <span class="stat-badge added" style="cursor:pointer" (click)="toggleSection('added')">+ {{ comparison.added.length }} added</span>
            <span class="stat-badge deleted" style="cursor:pointer" (click)="toggleSection('removed')">- {{ comparison.removed.length }} removed</span>
            <span class="stat-badge updated" style="cursor:pointer" (click)="toggleSection('changed')">~ {{ comparison.changed.length }} changed</span>
          </div>

          <div *ngIf="expandedSection === 'added' && comparison.added.length > 0" style="margin-top:0.75rem">
            <h4 style="font-size:0.875rem;margin-bottom:0.5rem;color:var(--success)">Added Enrichments</h4>
            <div *ngFor="let e of comparison.added" class="compare-item">
              <div style="font-weight:500;font-size:0.8125rem">{{ e.filePath || '(no file)' }}</div>
              <span class="badge badge-muted" style="font-size:0.6875rem">{{ e.subtype }}</span>
              <div class="text-muted" style="font-size:0.75rem;margin-top:0.25rem;white-space:pre-wrap;max-height:4rem;overflow:hidden">{{ e.content | slice:0:200 }}</div>
            </div>
          </div>

          <div *ngIf="expandedSection === 'removed' && comparison.removed.length > 0" style="margin-top:0.75rem">
            <h4 style="font-size:0.875rem;margin-bottom:0.5rem;color:var(--danger)">Removed Enrichments</h4>
            <div *ngFor="let e of comparison.removed" class="compare-item">
              <div style="font-weight:500;font-size:0.8125rem">{{ e.filePath || '(no file)' }}</div>
              <span class="badge badge-muted" style="font-size:0.6875rem">{{ e.subtype }}</span>
              <div class="text-muted" style="font-size:0.75rem;margin-top:0.25rem;white-space:pre-wrap;max-height:4rem;overflow:hidden">{{ e.content | slice:0:200 }}</div>
            </div>
          </div>

          <div *ngIf="expandedSection === 'changed' && comparison.changed.length > 0" style="margin-top:0.75rem">
            <h4 style="font-size:0.875rem;margin-bottom:0.5rem;color:var(--accent)">Changed Enrichments</h4>
            <div *ngFor="let c of comparison.changed" class="compare-item">
              <div style="font-weight:500;font-size:0.8125rem">{{ c.to.filePath || '(no file)' }}</div>
              <span class="badge badge-muted" style="font-size:0.6875rem">{{ c.to.subtype }}</span>
            </div>
          </div>
        </div>
      </div>

      <!-- Insights & Report -->
      <div class="card" style="margin-top:1.5rem">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:1rem">
          <h3 style="font-size:1rem;margin:0">Insights & Report</h3>
          <div style="display:flex;gap:0.5rem">
            <button class="btn btn-secondary" (click)="generateInsights()" [disabled]="generatingInsights" style="font-size:var(--font-xs)">
              <i class="bi bi-lightbulb"></i> {{ generatingInsights ? 'Generating...' : 'Generate Insights' }}
            </button>
            <button class="btn btn-secondary" (click)="generateReport()" [disabled]="generatingReport || !insightLayers.length" style="font-size:var(--font-xs)">
              <i class="bi bi-file-earmark-bar-graph"></i> {{ generatingReport ? 'Generating...' : 'Generate Report' }}
            </button>
            <a *ngIf="reportData" [href]="'/api/v1/repositories/' + repo.id + '/report/html'" target="_blank" class="btn btn-secondary" style="font-size:var(--font-xs)">
              <i class="bi bi-download"></i> Export HTML
            </a>
          </div>
        </div>

        <!-- Health Score -->
        <div *ngIf="reportData" style="display:flex;gap:2rem;align-items:center;margin-bottom:1.5rem;padding:1rem;background:var(--background-alt);border-radius:var(--radius)">
          <div style="text-align:center">
            <div style="font-size:2.5rem;font-weight:700" [style.color]="reportData.overallHealthScore >= 70 ? 'var(--success)' : reportData.overallHealthScore >= 40 ? '#e6a700' : 'var(--danger)'">
              {{ reportData.overallHealthScore }}
            </div>
            <div class="text-muted" style="font-size:var(--font-xs)">Health Score</div>
          </div>
          <div *ngIf="reportData.velocity" style="display:flex;gap:1.5rem">
            <div class="stat"><div class="stat-value">{{ reportData.velocity.commitsPerMonth }}</div><div class="stat-label">Commits/Month</div></div>
            <div class="stat"><div class="stat-value">{{ reportData.velocity.activeContributors }}</div><div class="stat-label">Active Contributors</div></div>
          </div>
          <div *ngIf="reportData.top5Improvements?.length" style="flex:1">
            <div class="text-muted" style="font-size:var(--font-xs);font-weight:600;margin-bottom:0.375rem">Top Improvements</div>
            <div *ngFor="let imp of reportData.top5Improvements; let i = index" style="font-size:var(--font-xs);margin-bottom:0.125rem">
              {{ i + 1 }}. {{ imp.title }}
              <span class="badge badge-muted" style="font-size:0.65rem;margin-left:0.25rem">{{ imp.impact }}</span>
            </div>
          </div>
        </div>

        <!-- Insight Layer Tabs -->
        <div *ngIf="insightLayers.length > 0">
          <div style="display:flex;flex-wrap:wrap;gap:0.375rem;margin-bottom:1rem">
            <button *ngFor="let layer of insightLayers" class="badge" style="cursor:pointer;border:none"
                    [ngClass]="selectedInsightLayer === layer.subtype ? 'badge-primary' : 'badge-muted'"
                    (click)="selectedInsightLayer = layer.subtype">
              {{ getInsightLabel(layer.subtype) }}
              <span *ngIf="getLayerRating(layer.subtype)" style="margin-left:0.25rem">{{ getLayerRating(layer.subtype) }}/5</span>
            </button>
          </div>

          <!-- Layer Content -->
          <div *ngFor="let layer of insightLayers">
            <div *ngIf="selectedInsightLayer === layer.subtype">
              <!-- Layer Ratings -->
              <div *ngIf="getLayerReport(layer.subtype) as lr" style="display:flex;gap:1rem;margin-bottom:1rem;flex-wrap:wrap">
                <span class="badge badge-muted"><i class="bi bi-bar-chart"></i> Maturity: {{ lr.maturityRating }}/5</span>
                <span class="badge badge-muted"><i class="bi bi-star"></i> Quality: {{ lr.qualityRating }}/5</span>
                <span class="badge" [ngClass]="lr.riskRating >= 4 ? 'badge-danger' : lr.riskRating >= 3 ? 'badge-warning' : 'badge-success'">
                  <i class="bi bi-shield"></i> Risk: {{ lr.riskRating }}/5
                </span>
              </div>
              <!-- Content -->
              <div style="white-space:pre-wrap;font-size:var(--font-xs);line-height:1.6;max-height:500px;overflow-y:auto;padding:0.5rem;background:var(--background-alt);border-radius:var(--radius)">{{ layer.content }}</div>
            </div>
          </div>
        </div>

        <div *ngIf="insightLayers.length === 0 && !generatingInsights" class="text-muted" style="font-size:var(--font-sm);padding:1rem 0;text-align:center">
          No insights yet. Click "Generate Insights" to analyze this repository.
        </div>
      </div>

      <app-repository-history [repositoryId]="repo.id" style="margin-top:1.5rem;display:block" />
      <app-repository-analytics [repositoryId]="repo.id" />
    </div>

    <div *ngIf="!loading && !repo" class="empty-state card">
      <i class="bi bi-exclamation-circle"></i>
      <h3>Repository not found</h3>
      <a routerLink="/repositories" class="btn btn-primary mt-2">Back to Repositories</a>
    </div>
  `,
  styles: [`
    .detail-row { display: flex; align-items: center; padding: 0.5rem 0; border-bottom: 1px solid var(--border); }
    .detail-row:last-child { border-bottom: none; }
    .detail-row .label { width: 140px; font-size: 0.875rem; color: var(--text-muted); font-weight: 500; }
    .stat-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1rem; }
    .stat { text-align: center; }
    .stat-value { font-size: 1.5rem; font-weight: 700; color: var(--primary); }
    .stat-label { font-size: 0.8125rem; color: var(--text-muted); }
    .tag-list { display: flex; flex-wrap: wrap; gap: 0.5rem; }
    .stat-badge { font-size: 0.75rem; font-weight: 600; padding: 0.25rem 0.625rem; border-radius: 100px; }
    .stat-badge.added { background: rgba(40,167,69,0.1); color: var(--success); }
    .stat-badge.updated { background: rgba(0,164,220,0.1); color: var(--accent); }
    .stat-badge.deleted { background: rgba(220,53,69,0.1); color: var(--danger); }
    .compare-item { padding: 0.5rem 0; border-bottom: 1px solid var(--border); }
    .compare-item:last-child { border-bottom: none; }
  `]
})
export class RepositoryDetailComponent implements OnInit {
  repo: Repository | null = null;
  loading = true;
  syncing = false;
  summary: any = null;
  commits: CommitSummary[] = [];
  compareFrom = '';
  compareTo = '';
  comparing = false;
  comparison: CommitComparison | null = null;
  compareError = '';
  expandedSection: string | null = null;
  syncIntervalValue = 'null';
  syncIntervalSaving = false;
  syncIntervalSaved = false;

  // Insights & Report
  insightLayers: any[] = [];
  selectedInsightLayer = '';
  generatingInsights = false;
  generatingReport = false;
  reportData: any = null;

  private insightLabels: Record<string, string> = {
    'FeatureMap': 'Features', 'ArchitectureAnalysis': 'Architecture', 'DesignAnalysis': 'Design',
    'ImplementationAnalysis': 'Implementation', 'DependencyAnalysis': 'Dependencies',
    'TestAnalysis': 'Testing', 'SecurityAnalysis': 'Security', 'DeploymentAnalysis': 'Deployment',
    'OperationsAnalysis': 'Operations', 'LocalSetupGuide': 'Local Setup'
  };

  constructor(private api: ApiService, private route: ActivatedRoute, private router: Router, private http: HttpClient) {}

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id')!;
    this.api.getRepository(id).subscribe({
      next: repo => {
        this.repo = repo;
        this.syncIntervalValue = repo.syncIntervalMinutes != null ? String(repo.syncIntervalMinutes) : 'null';
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
    this.http.get(`${environment.apiUrl}/repositories/${id}/analytics/summary`).subscribe({
      next: (s: any) => this.summary = s,
      error: () => {}
    });
    this.http.get<CommitSummary[]>(`${environment.apiUrl}/repositories/${id}/commits?limit=100`).subscribe({
      next: (commits) => this.commits = commits,
      error: () => {}
    });
    // Load insights and report
    this.http.get<any[]>(`${environment.apiUrl}/repositories/${id}/insights`).subscribe({
      next: (layers) => {
        this.insightLayers = layers;
        if (layers.length > 0) this.selectedInsightLayer = layers[0].subtype;
      },
      error: () => {}
    });
    this.http.get<any>(`${environment.apiUrl}/repositories/${id}/report`).subscribe({
      next: (report) => this.reportData = report,
      error: () => {}
    });
  }

  sync() {
    if (!this.repo) return;
    this.syncing = true;
    this.api.syncRepository(this.repo.id).subscribe({
      next: () => this.syncing = false,
      error: () => this.syncing = false
    });
  }

  confirmDelete() {
    if (!this.repo || !confirm(`Delete ${this.repo.name}? This will remove all indexed data.`)) return;
    this.api.deleteRepository(this.repo.id).subscribe({
      next: () => this.router.navigate(['/repositories'])
    });
  }

  compareCommits() {
    if (!this.repo || !this.compareFrom || !this.compareTo) return;
    this.comparing = true;
    this.comparison = null;
    this.compareError = '';
    this.expandedSection = null;
    this.http.get<CommitComparison>(
      `${environment.apiUrl}/repositories/${this.repo.id}/commits/compare`,
      { params: { from: this.compareFrom, to: this.compareTo } }
    ).subscribe({
      next: (result) => { this.comparison = result; this.comparing = false; },
      error: (err) => {
        this.compareError = err.error?.error || 'Failed to compare commits.';
        this.comparing = false;
      }
    });
  }

  onSyncIntervalChange(value: string) {
    if (!this.repo) return;
    this.syncIntervalValue = value;
    this.syncIntervalSaving = true;
    this.syncIntervalSaved = false;
    const syncIntervalMinutes = value === 'null' ? null : parseInt(value, 10);
    this.api.updateRepository(this.repo.id, { syncIntervalMinutes }).subscribe({
      next: (updated) => {
        this.repo = updated;
        this.syncIntervalSaving = false;
        this.syncIntervalSaved = true;
        setTimeout(() => this.syncIntervalSaved = false, 2000);
      },
      error: () => {
        this.syncIntervalSaving = false;
      }
    });
  }

  toggleSection(section: string) {
    this.expandedSection = this.expandedSection === section ? null : section;
  }

  getRelativeTime(dateStr: string): string {
    if (!dateStr) return '';
    const now = new Date();
    const date = new Date(dateStr);
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    if (diffMins < 1) return 'just now';
    if (diffMins < 60) return `${diffMins} minute${diffMins > 1 ? 's' : ''} ago`;
    const diffHours = Math.floor(diffMins / 60);
    if (diffHours < 24) return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`;
    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 30) return `${diffDays} day${diffDays > 1 ? 's' : ''} ago`;
    const diffMonths = Math.floor(diffDays / 30);
    return `${diffMonths} month${diffMonths > 1 ? 's' : ''} ago`;
  }

  statusClass(status: string): string {
    switch (status) {
      case 'indexed': return 'badge-success';
      case 'indexing': case 'cloning': case 'cloned': return 'badge-info';
      case 'error': return 'badge-danger';
      default: return 'badge-muted';
    }
  }

  loadInsights() {
    if (!this.repo) return;
    this.http.get<any[]>(`${environment.apiUrl}/repositories/${this.repo.id}/insights`).subscribe({
      next: (layers) => {
        this.insightLayers = layers;
        if (layers.length > 0 && !this.selectedInsightLayer) {
          this.selectedInsightLayer = layers[0].subtype;
        }
      },
      error: () => {}
    });
    this.http.get<any>(`${environment.apiUrl}/repositories/${this.repo.id}/report`).subscribe({
      next: (report) => this.reportData = report,
      error: () => {}
    });
  }

  generateInsights() {
    if (!this.repo) return;
    this.generatingInsights = true;
    this.http.post(`${environment.apiUrl}/repositories/${this.repo.id}/insights/generate`, {}).subscribe({
      next: () => { this.generatingInsights = false; this.loadInsights(); },
      error: () => { this.generatingInsights = false; }
    });
  }

  generateReport() {
    if (!this.repo) return;
    this.generatingReport = true;
    this.http.get<any>(`${environment.apiUrl}/repositories/${this.repo.id}/report`).subscribe({
      next: (report) => { this.reportData = report; this.generatingReport = false; },
      error: () => { this.generatingReport = false; }
    });
  }

  getInsightLabel(subtype: string): string {
    return this.insightLabels[subtype] || subtype;
  }

  getLayerRating(subtype: string): number | null {
    if (!this.reportData?.layers) return null;
    const layer = this.reportData.layers.find((l: any) => l.subtype === subtype);
    return layer?.qualityRating || null;
  }

  getLayerReport(subtype: string): any {
    if (!this.reportData?.layers) return null;
    return this.reportData.layers.find((l: any) => l.subtype === subtype);
  }
}
