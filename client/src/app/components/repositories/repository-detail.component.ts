import { Component, OnInit, AfterViewChecked, ElementRef, NgZone, ViewEncapsulation } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { ApiService } from '../../services/api.service';
import { Repository } from '../../models/repository.model';
import { RepositoryHistoryComponent } from './repository-history.component';
import { RepositoryAnalyticsComponent } from './repository-analytics.component';
import { environment } from '../../../environments/environment';
import { marked } from 'marked';

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

      <!-- Tab Navigation -->
      <div style="display:flex;gap:0;border-bottom:2px solid var(--border);margin-bottom:1.5rem">
        <button *ngFor="let tab of ['Overview', 'Insights', 'History', 'Analytics']"
                (click)="activeTab = tab"
                [style.border-bottom]="activeTab === tab ? '2px solid var(--primary)' : '2px solid transparent'"
                style="padding:0.75rem 1.25rem;background:none;border:none;border-bottom:2px solid transparent;cursor:pointer;font-size:var(--font-sm);font-weight:500;color:var(--text-muted);margin-bottom:-2px;transition:all 0.15s"
                [style.color]="activeTab === tab ? 'var(--primary)' : 'var(--text-muted)'">
          {{ tab }}
        </button>
      </div>

      <!-- Overview Tab -->
      <div *ngIf="activeTab === 'Overview'">
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

      </div><!-- End Overview Tab -->

      <!-- Insights Tab -->
      <div *ngIf="activeTab === 'Insights'" class="insights-tab">

        <!-- Toolbar -->
        <div class="insights-toolbar">
          <div class="insights-toolbar-left">
            <i class="bi bi-journal-richtext" style="font-size:1.125rem;color:var(--primary)"></i>
            <span style="font-weight:600;font-size:var(--font-sm)">Repository Insights Report</span>
          </div>
          <div class="insights-toolbar-right">
            <button class="btn btn-secondary btn-sm" (click)="generateInsights()" [disabled]="generatingInsights">
              <i class="bi bi-lightbulb"></i> {{ generatingInsights ? 'Generating (' + insightLayers.length + '/10)...' : (insightLayers.length > 0 ? 'Regenerate' : 'Generate Insights') }}
            </button>
            <button class="btn btn-secondary btn-sm" (click)="generateReport()" [disabled]="generatingReport || !insightLayers.length">
              <i class="bi bi-file-earmark-bar-graph"></i> {{ generatingReport ? 'Generating...' : 'Generate Report' }}
            </button>
            <a *ngIf="reportData" [href]="'/api/v1/repositories/' + repo.id + '/report/html'" target="_blank" class="btn btn-secondary btn-sm">
              <i class="bi bi-download"></i> Export HTML
            </a>
            <button *ngIf="insightLayers.length > 0" class="btn btn-secondary btn-sm" (click)="printReport()">
              <i class="bi bi-printer"></i> Print
            </button>
          </div>
        </div>

        <!-- Empty State -->
        <div *ngIf="insightLayers.length === 0 && !generatingInsights" class="insights-empty card">
          <i class="bi bi-lightbulb" style="font-size:2.5rem;color:var(--text-light);margin-bottom:1rem;display:block"></i>
          <h3 style="font-size:var(--font-lg);margin-bottom:0.5rem">No insights yet</h3>
          <p class="text-muted" style="font-size:var(--font-xs);margin-bottom:1.25rem">Click "Generate Insights" to analyze this repository and produce a comprehensive report.</p>
          <button class="btn btn-primary" (click)="generateInsights()" [disabled]="generatingInsights">
            <i class="bi bi-lightbulb"></i> Generate Insights
          </button>
        </div>

        <!-- Report Document -->
        <div *ngIf="insightLayers.length > 0" class="insights-document-wrapper" id="insightsDocumentWrapper">

          <!-- TOC Sidebar -->
          <nav class="insights-toc" id="insightsToc">
            <div class="insights-toc-title">Contents</div>
            <a *ngIf="reportData" class="insights-toc-item"
               [class.active]="activeTocSection === 'report-summary'"
               (click)="scrollToSection('report-summary', $event)">
              <i class="bi bi-speedometer2"></i> Summary
            </a>
            <a *ngFor="let layer of insightLayers; let i = index"
               class="insights-toc-item"
               [class.active]="activeTocSection === 'layer-' + layer.subtype"
               (click)="scrollToSection('layer-' + layer.subtype, $event)">
              <span class="insights-toc-num">{{ i + 1 }}</span>
              {{ getInsightLabel(layer.subtype) }}
              <span *ngIf="getLayerRating(layer.subtype) as rating" class="insights-toc-rating">
                {{ getStarRating(rating) }}
              </span>
            </a>
          </nav>

          <!-- Content Area -->
          <div class="insights-content-area" id="insightsContentArea" (scroll)="onInsightsScroll($event)">

            <!-- Health Score Header -->
            <div *ngIf="reportData" class="insights-section" id="report-summary">
              <div class="insights-health-header">
                <div class="insights-health-score"
                     [style.borderColor]="reportData.overallHealthScore >= 70 ? 'var(--success)' : reportData.overallHealthScore >= 40 ? '#e6a700' : 'var(--danger)'">
                  <div class="insights-health-number"
                       [style.color]="reportData.overallHealthScore >= 70 ? 'var(--success)' : reportData.overallHealthScore >= 40 ? '#e6a700' : 'var(--danger)'">
                    {{ reportData.overallHealthScore }}
                  </div>
                  <div class="insights-health-label">Health Score</div>
                  <div class="insights-health-stars" *ngIf="reportData.overallHealthScore != null">
                    {{ getStarRating(Math.round(reportData.overallHealthScore / 20)) }}
                  </div>
                </div>

                <div class="insights-health-details">
                  <!-- Star Ratings Summary -->
                  <div class="insights-ratings-summary" *ngIf="reportData.layers?.length">
                    <div *ngFor="let lr of reportData.layers" class="insights-rating-row">
                      <span class="insights-rating-label">{{ getInsightLabel(lr.subtype) }}</span>
                      <span class="insights-rating-stars">{{ getStarRating(lr.qualityRating) }}</span>
                      <span class="insights-rating-value">{{ lr.qualityRating }}/5</span>
                    </div>
                  </div>
                </div>
              </div>

              <!-- Velocity Metrics -->
              <div *ngIf="reportData.velocity" class="insights-velocity-row">
                <div class="insights-velocity-item">
                  <div class="insights-velocity-value">{{ reportData.velocity.commitsPerMonth }}</div>
                  <div class="insights-velocity-label">Commits/Month</div>
                </div>
                <div class="insights-velocity-item">
                  <div class="insights-velocity-value">{{ reportData.velocity.activeContributors }}</div>
                  <div class="insights-velocity-label">Active Contributors</div>
                </div>
                <div class="insights-velocity-item" *ngIf="reportData.velocity.averageCommitsPerDay != null">
                  <div class="insights-velocity-value">{{ reportData.velocity.averageCommitsPerDay | number:'1.1-1' }}</div>
                  <div class="insights-velocity-label">Commits/Day</div>
                </div>
                <div class="insights-velocity-item" *ngIf="reportData.velocity.deployFrequency">
                  <div class="insights-velocity-value">{{ reportData.velocity.deployFrequency }}</div>
                  <div class="insights-velocity-label">Deploy Frequency</div>
                </div>
              </div>

              <!-- Top 5 Improvements -->
              <div *ngIf="reportData.top5Improvements?.length" class="insights-improvements">
                <h3 class="insights-improvements-title">
                  <i class="bi bi-arrow-up-circle"></i> Top Improvements
                </h3>
                <div *ngFor="let imp of reportData.top5Improvements; let i = index" class="insights-improvement-item">
                  <span class="insights-improvement-num">{{ i + 1 }}</span>
                  <div class="insights-improvement-body">
                    <div class="insights-improvement-title">{{ imp.title }}</div>
                    <div *ngIf="imp.description" class="insights-improvement-desc">{{ imp.description }}</div>
                  </div>
                  <span class="badge insights-impact-badge"
                        [ngClass]="imp.impact === 'high' || imp.impact === 'critical' ? 'badge-danger' : imp.impact === 'medium' ? 'badge-warning' : 'badge-info'"
                        style="font-size:0.6875rem;text-transform:capitalize">{{ imp.impact }}</span>
                </div>
              </div>
            </div>

            <!-- Each Insight Layer Section -->
            <div *ngFor="let layer of insightLayers; let idx = index"
                 class="insights-section insights-layer-section"
                 [id]="'layer-' + layer.subtype">

              <!-- Section Heading -->
              <div class="insights-layer-heading">
                <div class="insights-layer-heading-left">
                  <span class="insights-layer-num">{{ idx + 1 }}</span>
                  <h2 class="insights-layer-title">{{ getInsightLabel(layer.subtype) }}</h2>
                </div>
                <div class="insights-layer-stars" *ngIf="getLayerRating(layer.subtype) as rating">
                  {{ getStarRating(rating) }}
                </div>
              </div>

              <!-- Ratings Badges -->
              <div *ngIf="getLayerReport(layer.subtype) as lr" class="insights-layer-badges">
                <span class="badge insights-badge-maturity"
                      [ngClass]="lr.maturityRating >= 4 ? 'badge-success' : lr.maturityRating >= 3 ? 'badge-warning' : 'badge-danger'">
                  <i class="bi bi-bar-chart-fill"></i> Maturity {{ lr.maturityRating }}/5
                </span>
                <span class="badge insights-badge-quality"
                      [ngClass]="lr.qualityRating >= 4 ? 'badge-success' : lr.qualityRating >= 3 ? 'badge-warning' : 'badge-danger'">
                  <i class="bi bi-star-fill"></i> Quality {{ lr.qualityRating }}/5
                </span>
                <span class="badge insights-badge-risk"
                      [ngClass]="lr.riskRating <= 2 ? 'badge-success' : lr.riskRating <= 3 ? 'badge-warning' : 'badge-danger'">
                  <i class="bi bi-shield-fill"></i> Risk {{ lr.riskRating }}/5
                </span>
              </div>

              <!-- Strengths / Weaknesses / Recommendations -->
              <div *ngIf="getLayerReport(layer.subtype) as lr" class="insights-layer-meta">
                <div *ngIf="lr.strengths?.length" class="insights-meta-block">
                  <div class="insights-meta-title insights-meta-strengths"><i class="bi bi-check-circle-fill"></i> Strengths</div>
                  <ul class="insights-meta-list">
                    <li *ngFor="let s of lr.strengths" class="insights-strength-item">{{ s }}</li>
                  </ul>
                </div>
                <div *ngIf="lr.weaknesses?.length" class="insights-meta-block">
                  <div class="insights-meta-title insights-meta-weaknesses"><i class="bi bi-exclamation-triangle-fill"></i> Weaknesses</div>
                  <ul class="insights-meta-list">
                    <li *ngFor="let w of lr.weaknesses" class="insights-weakness-item">{{ w }}</li>
                  </ul>
                </div>
                <div *ngIf="lr.recommendations?.length" class="insights-meta-block">
                  <div class="insights-meta-title insights-meta-recommendations"><i class="bi bi-arrow-right-circle-fill"></i> Recommendations</div>
                  <ul class="insights-meta-list">
                    <li *ngFor="let r of lr.recommendations" class="insights-recommendation-item">{{ r }}</li>
                  </ul>
                </div>
              </div>

              <!-- Rendered Markdown Content -->
              <div class="insights-layer-content" [innerHTML]="renderInsightHtml(layer)"></div>
            </div>

          </div><!-- End Content Area -->
        </div><!-- End Document Wrapper -->

      </div><!-- End Insights Tab -->

      <!-- History Tab -->
      <div *ngIf="activeTab === 'History'">
        <app-repository-history [repositoryId]="repo.id" style="display:block" />
      </div>

      <!-- Analytics Tab -->
      <div *ngIf="activeTab === 'Analytics'">
        <app-repository-analytics [repositoryId]="repo.id" />
      </div>
    </div>

    <div *ngIf="!loading && !repo" class="empty-state card">
      <i class="bi bi-exclamation-circle"></i>
      <h3>Repository not found</h3>
      <a routerLink="/repositories" class="btn btn-primary mt-2">Back to Repositories</a>
    </div>
  `,
  encapsulation: ViewEncapsulation.None,
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

    /* ===== Insights Tab Styles ===== */

    .insights-toolbar {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.75rem 1rem; background: var(--surface); border: 1px solid var(--border);
      border-radius: var(--radius-lg); margin-bottom: 1rem; box-shadow: var(--shadow);
    }
    .insights-toolbar-left { display: flex; align-items: center; gap: 0.5rem; }
    .insights-toolbar-right { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; }

    .insights-empty { text-align: center; padding: 3rem 2rem; }

    /* Document Wrapper: TOC + Content side by side */
    .insights-document-wrapper {
      display: flex; gap: 0; min-height: 70vh;
      border: 1px solid var(--border); border-radius: var(--radius-lg);
      background: var(--surface); box-shadow: var(--shadow); overflow: hidden;
    }

    /* TOC Sidebar */
    .insights-toc {
      width: 220px; min-width: 220px; padding: 1.25rem 0;
      border-right: 1px solid var(--border); background: var(--background-alt);
      position: sticky; top: 0; align-self: flex-start; max-height: 85vh; overflow-y: auto;
    }
    .insights-toc-title {
      font-size: 0.6875rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.08em;
      color: var(--text-muted); padding: 0 1rem; margin-bottom: 0.75rem;
    }
    .insights-toc-item {
      display: flex; align-items: center; gap: 0.375rem; padding: 0.5rem 1rem;
      font-size: 0.8125rem; color: var(--text-muted); cursor: pointer;
      text-decoration: none; transition: all 0.15s; border-left: 3px solid transparent;
      line-height: 1.3;
    }
    .insights-toc-item:hover {
      color: var(--text); background: rgba(0, 102, 204, 0.04);
    }
    .insights-toc-item.active {
      color: var(--primary); border-left-color: var(--primary);
      background: rgba(0, 102, 204, 0.06); font-weight: 600;
    }
    .insights-toc-num {
      display: inline-flex; align-items: center; justify-content: center;
      width: 1.25rem; height: 1.25rem; border-radius: 50%; font-size: 0.6875rem;
      background: var(--surface-2); color: var(--text-muted); font-weight: 600; flex-shrink: 0;
    }
    .insights-toc-item.active .insights-toc-num {
      background: var(--primary); color: white;
    }
    .insights-toc-rating {
      margin-left: auto; font-size: 0.625rem; letter-spacing: -0.05em; flex-shrink: 0;
    }

    /* Content Area */
    .insights-content-area {
      flex: 1; padding: 2rem 2.5rem; overflow-y: auto; max-height: 85vh;
      scroll-behavior: smooth;
    }

    .insights-section {
      margin-bottom: 2.5rem; padding-bottom: 2rem;
      border-bottom: 1px solid var(--border);
    }
    .insights-section:last-child { border-bottom: none; margin-bottom: 0; }

    /* Health Score Header */
    .insights-health-header {
      display: flex; gap: 2.5rem; align-items: flex-start; margin-bottom: 1.5rem;
    }
    .insights-health-score {
      text-align: center; padding: 1.5rem 2rem; border: 3px solid;
      border-radius: var(--radius-lg); background: var(--background-alt); min-width: 140px;
    }
    .insights-health-number { font-size: 3rem; font-weight: 800; line-height: 1; }
    .insights-health-label {
      font-size: 0.75rem; font-weight: 600; text-transform: uppercase;
      letter-spacing: 0.05em; color: var(--text-muted); margin-top: 0.25rem;
    }
    .insights-health-stars { font-size: 0.875rem; margin-top: 0.5rem; letter-spacing: 0.05em; }

    .insights-health-details { flex: 1; }
    .insights-ratings-summary {
      display: grid; grid-template-columns: 1fr 1fr; gap: 0.25rem 1.5rem;
    }
    .insights-rating-row {
      display: flex; align-items: center; gap: 0.5rem; padding: 0.375rem 0;
      font-size: 0.8125rem;
    }
    .insights-rating-label {
      flex: 1; color: var(--text); font-weight: 500;
    }
    .insights-rating-stars { font-size: 0.75rem; letter-spacing: 0.02em; color: #e6a700; }
    .insights-rating-value { font-size: 0.75rem; color: var(--text-muted); width: 2rem; text-align: right; }

    /* Velocity Metrics */
    .insights-velocity-row {
      display: flex; gap: 1px; margin-bottom: 1.5rem;
      background: var(--border); border-radius: var(--radius); overflow: hidden;
    }
    .insights-velocity-item {
      flex: 1; text-align: center; padding: 1rem; background: var(--background-alt);
    }
    .insights-velocity-value {
      font-size: 1.5rem; font-weight: 700; color: var(--primary); line-height: 1.2;
    }
    .insights-velocity-label {
      font-size: 0.6875rem; font-weight: 600; text-transform: uppercase;
      letter-spacing: 0.05em; color: var(--text-muted); margin-top: 0.25rem;
    }

    /* Top 5 Improvements */
    .insights-improvements {
      background: var(--background-alt); border-radius: var(--radius);
      padding: 1.25rem 1.5rem; margin-bottom: 0.5rem;
    }
    .insights-improvements-title {
      font-size: 0.9375rem; font-weight: 600; margin-bottom: 1rem; display: flex;
      align-items: center; gap: 0.5rem; color: var(--primary);
    }
    .insights-improvement-item {
      display: flex; align-items: flex-start; gap: 0.75rem; padding: 0.625rem 0;
      border-bottom: 1px solid var(--border);
    }
    .insights-improvement-item:last-child { border-bottom: none; }
    .insights-improvement-num {
      display: inline-flex; align-items: center; justify-content: center;
      width: 1.5rem; height: 1.5rem; border-radius: 50%; font-size: 0.75rem;
      background: var(--primary); color: white; font-weight: 700; flex-shrink: 0; margin-top: 0.125rem;
    }
    .insights-improvement-body { flex: 1; }
    .insights-improvement-title { font-size: 0.875rem; font-weight: 500; color: var(--text); }
    .insights-improvement-desc { font-size: 0.8125rem; color: var(--text-muted); margin-top: 0.125rem; }
    .insights-impact-badge { flex-shrink: 0; margin-top: 0.125rem; }

    /* Layer Section Heading */
    .insights-layer-heading {
      display: flex; align-items: center; justify-content: space-between;
      margin-bottom: 1rem; padding-bottom: 0.75rem; border-bottom: 2px solid var(--primary);
    }
    .insights-layer-heading-left { display: flex; align-items: center; gap: 0.75rem; }
    .insights-layer-num {
      display: inline-flex; align-items: center; justify-content: center;
      width: 2rem; height: 2rem; border-radius: 50%; font-size: 0.875rem;
      background: var(--primary); color: white; font-weight: 700;
    }
    .insights-layer-title { font-size: 1.25rem; font-weight: 700; margin: 0; color: var(--text); }
    .insights-layer-stars { font-size: 1.125rem; letter-spacing: 0.02em; color: #e6a700; }

    /* Layer Badges */
    .insights-layer-badges {
      display: flex; gap: 0.5rem; margin-bottom: 1.25rem; flex-wrap: wrap;
    }
    .insights-layer-badges .badge {
      font-size: 0.75rem; padding: 0.3rem 0.75rem; font-weight: 600;
    }
    .insights-layer-badges .badge i { margin-right: 0.25rem; }

    /* Strengths / Weaknesses / Recommendations */
    .insights-layer-meta {
      display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 1rem; margin-bottom: 1.5rem;
    }
    .insights-meta-block {
      background: var(--background-alt); border-radius: var(--radius); padding: 1rem;
    }
    .insights-meta-title {
      font-size: 0.75rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em;
      margin-bottom: 0.625rem; display: flex; align-items: center; gap: 0.375rem;
    }
    .insights-meta-strengths { color: var(--success); }
    .insights-meta-weaknesses { color: #b8860b; }
    .insights-meta-recommendations { color: var(--primary); }

    .insights-meta-list {
      list-style: none; padding: 0; margin: 0;
    }
    .insights-meta-list li {
      font-size: 0.8125rem; padding: 0.25rem 0; padding-left: 1.25rem;
      position: relative; color: var(--text); line-height: 1.4;
    }
    .insights-strength-item::before {
      content: '\\2713'; position: absolute; left: 0; color: var(--success); font-weight: 700;
    }
    .insights-weakness-item::before {
      content: '\\26A0'; position: absolute; left: 0; color: #b8860b; font-size: 0.75rem;
    }
    .insights-recommendation-item::before {
      content: '\\2192'; position: absolute; left: 0; color: var(--primary); font-weight: 700;
    }

    /* Layer Content (rendered markdown) */
    .insights-layer-content {
      font-size: 0.9375rem; line-height: 1.7; color: var(--text);
    }
    .insights-layer-content h1 { font-size: 1.375rem; margin: 1.5rem 0 0.75rem; }
    .insights-layer-content h2 { font-size: 1.1875rem; margin: 1.25rem 0 0.625rem; }
    .insights-layer-content h3 { font-size: 1.0625rem; margin: 1rem 0 0.5rem; }
    .insights-layer-content h4 { font-size: 0.9375rem; margin: 0.875rem 0 0.375rem; }
    .insights-layer-content p { margin-bottom: 0.75rem; }
    .insights-layer-content ul, .insights-layer-content ol {
      margin: 0.5rem 0 0.75rem; padding-left: 1.5rem;
    }
    .insights-layer-content li { margin-bottom: 0.25rem; }
    .insights-layer-content table {
      width: 100%; border-collapse: collapse; margin: 1rem 0; font-size: 0.875rem;
    }
    .insights-layer-content th {
      background: var(--background-alt); font-weight: 600; text-align: left;
      padding: 0.625rem 0.75rem; border: 1px solid var(--border); font-size: 0.8125rem;
    }
    .insights-layer-content td {
      padding: 0.5rem 0.75rem; border: 1px solid var(--border); font-size: 0.8125rem;
    }
    .insights-layer-content blockquote {
      border-left: 3px solid var(--primary); padding: 0.5rem 1rem; margin: 0.75rem 0;
      background: rgba(0, 102, 204, 0.04); font-style: italic; color: var(--text-muted);
    }
    .insights-layer-content pre {
      margin: 0.75rem 0; font-size: 0.8125rem;
    }
    .insights-layer-content code {
      font-size: 0.85em;
    }
    .insights-layer-content img {
      max-width: 100%; height: auto; border-radius: var(--radius);
    }

    /* Mermaid Diagram Wrapper */
    .insights-mermaid-wrapper {
      margin: 1.25rem 0; padding: 1.25rem; background: var(--background-alt);
      border: 1px solid var(--border); border-radius: var(--radius);
      text-align: center; overflow-x: auto;
    }
    .insights-mermaid-wrapper .mermaid-diagram-label {
      font-size: 0.6875rem; font-weight: 600; text-transform: uppercase;
      letter-spacing: 0.06em; color: var(--text-muted); margin-bottom: 0.75rem;
      display: flex; align-items: center; gap: 0.375rem; justify-content: center;
    }
    .insights-mermaid-wrapper svg {
      max-width: 100%; height: auto;
    }

    /* Mermaid error fallback */
    .mermaid-error {
      padding: 0.75rem; background: rgba(220,53,69,0.05); border: 1px solid rgba(220,53,69,0.2);
      border-radius: var(--radius); font-size: 0.8125rem; color: var(--danger); text-align: left;
    }
    .mermaid-error pre {
      background: var(--surface-2); padding: 0.5rem; margin-top: 0.5rem;
      border-radius: 4px; font-size: 0.75rem; color: var(--text-muted); white-space: pre-wrap;
    }

    /* ===== Print Styles ===== */
    @media print {
      .insights-toolbar, .page-header, nav.insights-toc,
      [style*="border-bottom:2px solid"] { display: none !important; }

      .insights-document-wrapper {
        display: block !important; border: none !important;
        box-shadow: none !important; overflow: visible !important;
      }
      .insights-content-area {
        max-height: none !important; overflow: visible !important;
        padding: 0 !important;
      }
      .insights-section {
        page-break-inside: avoid; break-inside: avoid;
      }
      .insights-layer-section { page-break-before: auto; }
      .insights-health-header { flex-direction: column; gap: 1rem; }
      .insights-velocity-row { flex-wrap: wrap; }
      .insights-mermaid-wrapper { page-break-inside: avoid; break-inside: avoid; }

      body { font-size: 12pt; }
      .insights-layer-content { font-size: 11pt; }
    }
  `]
})
export class RepositoryDetailComponent implements OnInit, AfterViewChecked {
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
  activeTab = 'Overview';

  // Insights & Report
  insightLayers: any[] = [];
  selectedInsightLayer = '';
  generatingInsights = false;
  generatingReport = false;
  reportData: any = null;
  activeTocSection = '';
  Math = Math;

  // Mermaid rendering tracking
  private mermaidLoaded = false;
  private mermaidModule: any = null;
  private pendingMermaidRender = false;
  private renderedMermaidIds = new Set<string>();
  private mermaidIdCounter = 0;
  private insightHtmlCache = new Map<string, SafeHtml>();
  private insightContentVersions = new Map<string, string>();

  private insightLabels: Record<string, string> = {
    'FeatureMap': 'Features', 'ArchitectureAnalysis': 'Architecture', 'DesignAnalysis': 'Design',
    'ImplementationAnalysis': 'Implementation', 'DependencyAnalysis': 'Dependencies',
    'TestAnalysis': 'Testing', 'SecurityAnalysis': 'Security', 'DeploymentAnalysis': 'Deployment',
    'OperationsAnalysis': 'Operations', 'LocalSetupGuide': 'Local Setup',
    'featuremap': 'Features', 'architectureanalysis': 'Architecture', 'designanalysis': 'Design',
    'implementationanalysis': 'Implementation', 'dependencyanalysis': 'Dependencies',
    'testanalysis': 'Testing', 'securityanalysis': 'Security', 'deploymentanalysis': 'Deployment',
    'operationsanalysis': 'Operations', 'localsetupguide': 'Local Setup'
  };

  constructor(
    private api: ApiService,
    private route: ActivatedRoute,
    private router: Router,
    private http: HttpClient,
    private sanitizer: DomSanitizer,
    private el: ElementRef,
    private zone: NgZone
  ) {}

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
    this.http.get<any>(`${environment.apiUrl}/repositories/${id}/insights`).subscribe({
      next: (data) => {
        if (data?.layers) {
          this.insightLayers = Object.entries(data.layers)
            .filter(([_, v]) => v !== null)
            .map(([k, v]: any) => ({ subtype: k, ...v }));
          if (this.insightLayers.length > 0) this.selectedInsightLayer = this.insightLayers[0].subtype;
        }
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
    this.http.get<any>(`${environment.apiUrl}/repositories/${this.repo.id}/insights`).subscribe({
      next: (data) => {
        if (data?.layers) {
          this.insightLayers = Object.entries(data.layers)
            .filter(([_, v]) => v !== null)
            .map(([k, v]: any) => ({ subtype: k, ...v }));
          if (this.insightLayers.length > 0 && !this.selectedInsightLayer) {
            this.selectedInsightLayer = this.insightLayers[0].subtype;
          }
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
      next: () => {
        // Task is queued — poll for results every 5 seconds
        const pollInterval = setInterval(() => {
          this.http.get<any>(`${environment.apiUrl}/repositories/${this.repo!.id}/insights`).subscribe({
            next: (data) => {
              const layers = data.layers ? Object.entries(data.layers).filter(([_, v]) => v !== null).map(([k, v]: any) => ({ subtype: k, ...v })) : [];
              if (layers.length > 0) {
                this.insightLayers = layers;
                if (!this.selectedInsightLayer) this.selectedInsightLayer = layers[0].subtype;
              }
              // Stop polling when we have all 10 layers or after 3 minutes
              if (layers.length >= 10) {
                clearInterval(pollInterval);
                this.generatingInsights = false;
              }
            }
          });
        }, 5000);
        // Safety timeout: stop polling after 3 minutes
        setTimeout(() => { clearInterval(pollInterval); this.generatingInsights = false; }, 180000);
      },
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

  ngAfterViewChecked() {
    if (this.pendingMermaidRender && this.activeTab === 'Insights') {
      this.pendingMermaidRender = false;
      this.renderMermaidDiagrams();
    }
  }

  renderMarkdown(content: string): string {
    if (!content) return '';
    return marked.parse(content, { async: false }) as string;
  }

  /**
   * Render insight layer content to SafeHtml, extracting mermaid blocks
   * for post-render processing. Uses a cache to avoid re-parsing unchanged content.
   */
  renderInsightHtml(layer: any): SafeHtml {
    const content = layer.content || layer.Content || '';
    if (!content) return '';

    const key = layer.subtype;
    const cached = this.insightContentVersions.get(key);
    if (cached === content && this.insightHtmlCache.has(key)) {
      return this.insightHtmlCache.get(key)!;
    }

    // Extract mermaid blocks before marked parsing
    const mermaidBlocks: { id: string; code: string }[] = [];
    let processedContent = content.replace(
      /```mermaid\s*([\s\S]*?)```/g,
      (_: string, code: string) => {
        const id = `insight-mermaid-${++this.mermaidIdCounter}`;
        mermaidBlocks.push({ id, code: code.trim() });
        return `<div class="insights-mermaid-wrapper" id="wrap-${id}">
          <div class="mermaid-diagram-label"><i class="bi bi-diagram-3"></i> Diagram</div>
          <div class="mermaid-render-target" id="${id}"></div>
        </div>`;
      }
    );

    let html = marked.parse(processedContent, { async: false }) as string;

    const result = this.sanitizer.bypassSecurityTrustHtml(html);
    this.insightHtmlCache.set(key, result);
    this.insightContentVersions.set(key, content);

    // Schedule mermaid rendering
    if (mermaidBlocks.length > 0) {
      this.pendingMermaidRender = true;
      // Store mermaid blocks for rendering
      (this as any)['_pendingMermaidBlocks'] = [
        ...((this as any)['_pendingMermaidBlocks'] || []),
        ...mermaidBlocks
      ];
    }

    return result;
  }

  /**
   * Dynamically load and render mermaid diagrams.
   * Uses dynamic import to load mermaid from CDN if not available via npm.
   */
  private async renderMermaidDiagrams() {
    const blocks: { id: string; code: string }[] = (this as any)['_pendingMermaidBlocks'] || [];
    if (blocks.length === 0) return;

    // Clear pending
    (this as any)['_pendingMermaidBlocks'] = [];

    // Filter to only blocks not yet rendered
    const toRender = blocks.filter(b => !this.renderedMermaidIds.has(b.id));
    if (toRender.length === 0) return;

    try {
      if (!this.mermaidLoaded) {
        try {
          // Try npm import first
          const mod = await import('mermaid');
          this.mermaidModule = mod.default || mod;
        } catch {
          // Fallback: load from CDN
          await this.loadMermaidFromCDN();
        }

        if (this.mermaidModule) {
          this.mermaidModule.initialize({
            startOnLoad: false,
            theme: 'default',
            themeVariables: {
              primaryColor: '#0066CC',
              primaryTextColor: '#212529',
              primaryBorderColor: '#0052A3',
              lineColor: '#6C757D',
              secondaryColor: '#F8F9FA',
              tertiaryColor: '#FFFFFF',
              background: '#FFFFFF',
              mainBkg: '#F8F9FA',
              secondBkg: '#F1F3F5',
              nodeBorder: '#0052A3',
              clusterBkg: '#F8F9FA',
              clusterBorder: '#DEE2E6',
              titleColor: '#212529',
              edgeLabelBackground: '#F8F9FA',
              nodeTextColor: '#212529',
            },
            flowchart: { curve: 'basis', padding: 15 },
            securityLevel: 'loose',
          });
          this.mermaidLoaded = true;
        }
      }

      if (!this.mermaidModule) return;

      // Wait a tick for DOM to settle
      await new Promise(resolve => setTimeout(resolve, 100));

      for (const block of toRender) {
        const element = document.getElementById(block.id);
        if (!element) continue;
        if (this.renderedMermaidIds.has(block.id)) continue;

        try {
          const { svg } = await this.mermaidModule.render(block.id + '-svg', block.code);
          element.innerHTML = svg;
          this.renderedMermaidIds.add(block.id);
        } catch (e: any) {
          console.warn('Mermaid rendering failed for', block.id, e);
          element.innerHTML = `<div class="mermaid-error">
            <strong>Diagram could not be rendered</strong>
            <pre>${this.escapeHtml(block.code)}</pre>
          </div>`;
          this.renderedMermaidIds.add(block.id);
        }
      }
    } catch (e) {
      console.warn('Mermaid loading/rendering error:', e);
    }
  }

  /**
   * Load mermaid.js from CDN as a fallback
   */
  private loadMermaidFromCDN(): Promise<void> {
    return new Promise((resolve, reject) => {
      if ((window as any).mermaid) {
        this.mermaidModule = (window as any).mermaid;
        resolve();
        return;
      }
      const script = document.createElement('script');
      script.src = 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js';
      script.onload = () => {
        this.mermaidModule = (window as any).mermaid;
        resolve();
      };
      script.onerror = () => reject(new Error('Failed to load mermaid from CDN'));
      document.head.appendChild(script);
    });
  }

  private escapeHtml(text: string): string {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  /**
   * Generate star rating string: filled stars + empty stars
   */
  getStarRating(rating: number | null): string {
    if (rating == null) return '';
    const r = Math.max(0, Math.min(5, Math.round(rating)));
    return '\u2605'.repeat(r) + '\u2606'.repeat(5 - r);
  }

  /**
   * Scroll to a section in the insights document and update active TOC
   */
  scrollToSection(sectionId: string, event: Event) {
    event.preventDefault();
    const contentArea = document.getElementById('insightsContentArea');
    const target = document.getElementById(sectionId);
    if (contentArea && target) {
      const offset = target.offsetTop - contentArea.offsetTop;
      contentArea.scrollTo({ top: offset, behavior: 'smooth' });
      this.activeTocSection = sectionId;
    }
  }

  /**
   * Track scroll position to highlight active TOC entry
   */
  onInsightsScroll(event: Event) {
    const container = event.target as HTMLElement;
    if (!container) return;

    const sections = container.querySelectorAll('.insights-section');
    let currentSection = '';

    sections.forEach((section) => {
      const el = section as HTMLElement;
      const sectionTop = el.offsetTop - container.offsetTop;
      if (container.scrollTop >= sectionTop - 100) {
        currentSection = el.id;
      }
    });

    if (currentSection && currentSection !== this.activeTocSection) {
      this.zone.run(() => {
        this.activeTocSection = currentSection;
      });
    }
  }

  /**
   * Print the report
   */
  printReport() {
    window.print();
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
