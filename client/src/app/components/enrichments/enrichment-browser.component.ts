import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ApiService } from '../../services/api.service';
import { Enrichment, EnrichmentListResponse } from '../../models/enrichment.model';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-enrichment-browser',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-header">
      <h1>Enrichments</h1>
    </div>

    <div class="card mb-2">
      <h3 style="font-size:var(--font-sm);margin-bottom:0.5rem">What are enrichments?</h3>
      <p class="text-muted" style="font-size:var(--font-xs);margin-bottom:0.75rem">
        Enrichments are structured knowledge extracted from your repositories. They power MCP agents, semantic search,
        and chat -- giving AI tools deep understanding of your codebase beyond raw source files.
      </p>
      <h4 style="font-size:var(--font-sm);font-weight:600;margin:0.75rem 0 0.5rem 0;color:var(--text)">Type descriptions</h4>
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:0.5rem 1.5rem;font-size:var(--font-xs)">
        <div><strong>Architecture</strong> -- High-level component overview, responsibilities, interactions, and design patterns</div>
        <div><strong>DB Schema</strong> -- Database tables, columns, relationships, indexes, and constraints from entity classes and migrations</div>
        <div><strong>Dependencies</strong> -- Package dependencies extracted from manifest files</div>
        <div><strong>Chunk</strong> -- Parsed code segments with file path and line ranges</div>
        <div><strong>Snippet</strong> -- Key code snippets extracted from the repository</div>
        <div><strong>Snippet Summary</strong> -- Natural language summaries of code snippets grouped by file</div>
        <div><strong>Example</strong> -- Usage examples found in tests and documentation</div>
        <div><strong>Example Summary</strong> -- Natural language summaries of usage examples</div>
        <div><strong>Commit Desc</strong> -- LLM-generated summary of development history, features, and project purpose</div>
        <div><strong>Commit History</strong> -- Full commit log with authors, dates, and tags</div>
        <div><strong>API Docs</strong> -- Auto-generated documentation for public classes, interfaces, functions, and enums</div>
        <div><strong>Cookbook</strong> -- Getting-started guide with setup instructions, usage patterns, and best practices</div>
        <div><strong>Wiki</strong> -- Comprehensive documentation covering architecture, API reference, configuration, deployment, and troubleshooting</div>
        <div><strong>Ownership</strong> -- Code ownership, team structure, maintainer areas, and contribution workflows</div>
        <div><strong>Security</strong> -- Authentication, authorization, secrets management, input validation, and encryption analysis</div>
        <div><strong>Operations</strong> -- CI/CD pipelines, containerization, deployment, infrastructure, monitoring, and environment management</div>
        <div><strong>Quality</strong> -- Testing strategy, test frameworks, coverage analysis, quality tooling, and CI quality gates</div>
        <div><strong>Document Text</strong> -- Text content extracted from binary documents (PDF) with metadata and page structure</div>
        <div><strong>Feature Map</strong> -- Structured feature inventory with stable IDs, status, and complexity</div>
        <div><strong>Architecture Analysis</strong> -- Deep architecture analysis with Mermaid component diagrams</div>
        <div><strong>Design Analysis</strong> -- Design patterns, domain model, and API surface with Mermaid class diagrams</div>
        <div><strong>Implementation Analysis</strong> -- Code patterns, smells, and improvement suggestions with effort/impact</div>
        <div><strong>Dependency Analysis</strong> -- Dependency tree, freshness, licenses, and security advisories</div>
        <div><strong>Test Analysis</strong> -- Test pyramid, coverage gaps, frameworks, and improvement suggestions</div>
        <div><strong>Security Analysis</strong> -- Auth patterns, OWASP Top 10 exposure, secrets handling, risk ratings</div>
        <div><strong>Deployment Analysis</strong> -- CI/CD pipelines, environments, and release process with Mermaid flowcharts</div>
        <div><strong>Operations Analysis</strong> -- Logging audit, monitoring, health checks, alerting, and graceful shutdown</div>
        <div><strong>Local Setup Guide</strong> -- Step-by-step guide to run the project locally with prerequisites and common issues</div>
      </div>
    </div>

    <!-- Summary bar -->
    <div class="card mb-2" *ngIf="!loading && typeCounts.length > 0" style="padding:0.75rem 1rem">
      <div style="display:flex;gap:1rem;flex-wrap:wrap;align-items:center;font-size:var(--font-xs)">
        <span class="text-muted">{{ totalCount }} total</span>
        <span *ngFor="let tc of typeCounts" style="display:inline-flex;align-items:center;gap:0.25rem">
          <span class="badge badge-muted">{{ getSubtypeLabel(tc.subtype) }}</span>
          <span>{{ tc.count }}</span>
        </span>
      </div>
    </div>

    <!-- Filters -->
    <div class="card mb-2">
      <div style="display:flex;gap:1rem;flex-wrap:wrap">
        <div class="form-group" style="margin-bottom:0">
          <select class="form-control" [(ngModel)]="typeFilter" (change)="onTypeChange()" style="width:180px">
            <option value="">All Types</option>
            <option *ngFor="let t of typeOptions" [value]="t">{{ t }}</option>
          </select>
        </div>
        <div class="form-group" style="margin-bottom:0">
          <select class="form-control" [(ngModel)]="subtypeFilter" (change)="loadEnrichments()" style="width:180px">
            <option value="">All Subtypes</option>
            <option *ngFor="let st of availableSubtypes" [value]="st.value">{{ st.label }}</option>
          </select>
        </div>
        <div class="form-group" style="margin-bottom:0">
          <select class="form-control" [(ngModel)]="repoFilter" (change)="loadEnrichments()" style="width:200px">
            <option value="">All Repositories</option>
            <option *ngFor="let r of repos" [value]="r.id">{{ r.name }}</option>
          </select>
        </div>
      </div>
    </div>

    <div *ngIf="loading" style="display:flex;justify-content:center;padding:2rem"><div class="spinner"></div></div>

    <div *ngIf="!loading && enrichments.length > 0">
      <div class="card" *ngFor="let e of enrichments" style="margin-bottom:1rem;cursor:pointer" (click)="toggleExpand(e.id)">
        <div style="display:flex;justify-content:space-between;align-items:flex-start">
          <div>
            <div style="margin-bottom:0.375rem">
              <span class="badge badge-primary">{{ getTypeLabel(e.type) }}</span>
              <span class="badge badge-muted" style="margin-left:0.25rem">{{ getSubtypeLabel(e.subtype) }}</span>
              <span class="badge badge-muted" style="margin-left:0.25rem" *ngIf="e.language">{{ e.language }}</span>
              <span class="badge" style="margin-left:0.25rem" [ngClass]="qualityClass(e.quality)">{{ qualityLabel(e.quality) }}</span>
            </div>
            <strong>{{ e.title || e.filePath || 'Untitled' }}</strong>
            <div class="text-muted" style="font-size:var(--font-xs);margin-top:0.25rem" *ngIf="e.filePath || getRepoName(e.repositoryId)">
              <span *ngIf="getRepoName(e.repositoryId)">{{ getRepoName(e.repositoryId) }}</span>
              <span *ngIf="getRepoName(e.repositoryId) && e.filePath"> / </span>
              <code *ngIf="e.filePath">{{ e.filePath }}</code>
            </div>
          </div>
        </div>
        <div *ngIf="expandedId === e.id" style="margin-top:1rem">
          <pre><code>{{ e.content }}</code></pre>
        </div>
      </div>
      <div style="display:flex;justify-content:center;gap:0.75rem;margin-top:1rem" *ngIf="totalCount > enrichments.length">
        <button class="btn btn-secondary" (click)="loadMore()">Load More</button>
      </div>
    </div>

    <div *ngIf="!loading && enrichments.length === 0" class="empty-state card">
      <i class="bi bi-file-earmark-text"></i>
      <h3>No enrichments found</h3>
      <p>Index a repository to generate enrichments.</p>
    </div>
  `
})
export class EnrichmentBrowserComponent implements OnInit {
  enrichments: Enrichment[] = [];
  totalCount = 0;
  loading = true;
  typeFilter = '';
  subtypeFilter = '';
  repoFilter = '';
  expandedId: string | null = null;
  offset = 0;
  repos: { id: string; name: string }[] = [];
  typeCounts: { subtype: string; count: number }[] = [];

  private typeLabels: Record<string, string> = {
    'Architecture': 'Architecture',
    'Development': 'Development',
    'History': 'History',
    'Usage': 'Usage',
    'Insights': 'Insights',
  };

  private subtypeLabels: Record<string, string> = {
    'Chunk': 'Chunk',
    'Snippet': 'Snippet',
    'SnippetSummary': 'Snippet Summary',
    'Example': 'Example',
    'ExampleSummary': 'Example Summary',
    'APIDocs': 'API Docs',
    'Cookbook': 'Cookbook',
    'Wiki': 'Wiki',
    'Physical': 'Architecture',
    'DatabaseSchema': 'DB Schema',
    'CommitDescription': 'Commit Desc',
    'CommitHistory': 'Commit History',
    'Dependencies': 'Dependencies',
    'Ownership': 'Ownership',
    'Security': 'Security',
    'Operations': 'Operations',
    'Quality': 'Quality',
    'DocumentText': 'Document Text',
    'FeatureMap': 'Feature Map',
    'ArchitectureAnalysis': 'Architecture Analysis',
    'DesignAnalysis': 'Design Analysis',
    'ImplementationAnalysis': 'Implementation Analysis',
    'DependencyAnalysis': 'Dependency Analysis',
    'TestAnalysis': 'Test Analysis',
    'SecurityAnalysis': 'Security Analysis',
    'DeploymentAnalysis': 'Deployment Analysis',
    'OperationsAnalysis': 'Operations Analysis',
    'LocalSetupGuide': 'Local Setup Guide',
  };

  typeOptions = ['Architecture', 'Development', 'History', 'Usage', 'Insights'];

  private typeToSubtypes: Record<string, { value: string; label: string }[]> = {
    'Architecture': [
      { value: 'Physical', label: 'Architecture' },
      { value: 'DatabaseSchema', label: 'DB Schema' },
      { value: 'Dependencies', label: 'Dependencies' },
      { value: 'Ownership', label: 'Ownership' },
      { value: 'Security', label: 'Security' },
    ],
    'Development': [
      { value: 'Chunk', label: 'Chunk' },
      { value: 'Snippet', label: 'Snippet' },
      { value: 'SnippetSummary', label: 'Snippet Summary' },
      { value: 'Example', label: 'Example' },
      { value: 'ExampleSummary', label: 'Example Summary' },
      { value: 'Quality', label: 'Quality' },
      { value: 'DocumentText', label: 'Document Text' },
    ],
    'History': [
      { value: 'CommitDescription', label: 'Commit Desc' },
      { value: 'CommitHistory', label: 'Commit History' },
    ],
    'Usage': [
      { value: 'Cookbook', label: 'Cookbook' },
      { value: 'APIDocs', label: 'API Docs' },
      { value: 'Wiki', label: 'Wiki' },
      { value: 'Operations', label: 'Operations' },
    ],
    'Insights': [
      { value: 'FeatureMap', label: 'Feature Map' },
      { value: 'ArchitectureAnalysis', label: 'Architecture Analysis' },
      { value: 'DesignAnalysis', label: 'Design Analysis' },
      { value: 'ImplementationAnalysis', label: 'Implementation Analysis' },
      { value: 'DependencyAnalysis', label: 'Dependency Analysis' },
      { value: 'TestAnalysis', label: 'Test Analysis' },
      { value: 'SecurityAnalysis', label: 'Security Analysis' },
      { value: 'DeploymentAnalysis', label: 'Deployment Analysis' },
      { value: 'OperationsAnalysis', label: 'Operations Analysis' },
      { value: 'LocalSetupGuide', label: 'Local Setup Guide' },
    ],
  };

  private allSubtypes: { value: string; label: string }[] = [
    ...this.typeToSubtypes['Architecture'],
    ...this.typeToSubtypes['Development'],
    ...this.typeToSubtypes['History'],
    ...this.typeToSubtypes['Usage'],
    ...this.typeToSubtypes['Insights'],
  ];

  get availableSubtypes(): { value: string; label: string }[] {
    if (!this.typeFilter) return this.allSubtypes;
    return this.typeToSubtypes[this.typeFilter] || this.allSubtypes;
  }

  onTypeChange() {
    // Reset subtype if it doesn't belong to the new type
    if (this.subtypeFilter && this.typeFilter) {
      const valid = this.availableSubtypes.some(s => s.value === this.subtypeFilter);
      if (!valid) this.subtypeFilter = '';
    }
    this.loadEnrichments();
  }

  constructor(private api: ApiService, private http: HttpClient) {}

  ngOnInit() {
    this.http.get<any[]>(`${environment.apiUrl}/repositories`).subscribe({
      next: r => this.repos = r.map((repo: any) => ({ id: repo.id, name: repo.name }))
    });
    this.loadEnrichments();
  }

  loadEnrichments() {
    this.offset = 0;
    this.loading = true;
    const params: Record<string, string | number> = { offset: 0, limit: 20 };
    if (this.typeFilter) params['type'] = this.typeFilter;
    if (this.subtypeFilter) params['subtype'] = this.subtypeFilter;
    if (this.repoFilter) params['repositoryId'] = this.repoFilter;

    this.api.getEnrichments(params).subscribe({
      next: res => {
        this.enrichments = res.results;
        this.totalCount = res.totalCount;
        this.loading = false;
      },
      error: () => this.loading = false
    });

    // Fetch accurate per-subtype counts from backend
    const countParams: Record<string, string> = {};
    if (this.typeFilter) countParams['type'] = this.typeFilter;
    if (this.repoFilter) countParams['repositoryId'] = this.repoFilter;
    this.api.getEnrichmentCounts(countParams).subscribe({
      next: counts => {
        this.typeCounts = Object.entries(counts).map(([subtype, count]) => ({ subtype, count }));
        this.totalCount = Object.values(counts).reduce((sum, c) => sum + c, 0);
      }
    });
  }

  loadMore() {
    this.offset += 20;
    const params: Record<string, string | number> = { offset: this.offset, limit: 20 };
    if (this.typeFilter) params['type'] = this.typeFilter;
    if (this.subtypeFilter) params['subtype'] = this.subtypeFilter;
    if (this.repoFilter) params['repositoryId'] = this.repoFilter;

    this.api.getEnrichments(params).subscribe({
      next: res => this.enrichments.push(...res.results)
    });
  }

  toggleExpand(id: string) { this.expandedId = this.expandedId === id ? null : id; }

  getTypeLabel(type: string): string {
    return this.typeLabels[type] || type;
  }

  getSubtypeLabel(subtype: string): string {
    return this.subtypeLabels[subtype] || subtype;
  }

  getRepoName(repositoryId: string): string {
    const repo = this.repos.find(r => r.id === repositoryId);
    return repo?.name || '';
  }

  qualityLabel(quality: number): string {
    if (quality >= 0.8) return 'High';
    if (quality >= 0.5) return 'Medium';
    return 'Low';
  }

  qualityClass(quality: number): string {
    if (quality >= 0.8) return 'badge-success';
    if (quality >= 0.5) return 'badge-warning';
    return 'badge-danger';
  }
}
