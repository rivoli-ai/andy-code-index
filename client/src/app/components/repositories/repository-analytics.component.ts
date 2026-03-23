import { Component, Input, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-repository-analytics',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div style="display:grid;grid-template-columns:1fr 1fr;gap:1.5rem;margin-top:1.5rem">
      <!-- Language Breakdown -->
      <div class="card" *ngIf="languages.length > 0">
        <h3 style="font-size:1rem;margin-bottom:1rem">Languages</h3>
        <div class="bar-chart">
          <div class="bar-row" *ngFor="let lang of languages">
            <span class="bar-label">{{ lang.language }}</span>
            <div class="bar-track">
              <div class="bar-fill" [style.width.%]="(lang.count / maxLangCount) * 100"></div>
            </div>
            <span class="bar-value">{{ lang.count }}</span>
          </div>
        </div>
      </div>

      <!-- File Types -->
      <div class="card" *ngIf="fileTypes.length > 0">
        <h3 style="font-size:1rem;margin-bottom:1rem">File Types</h3>
        <div class="bar-chart">
          <div class="bar-row" *ngFor="let ft of fileTypes.slice(0, 10)">
            <span class="bar-label">{{ ft.extension }}</span>
            <div class="bar-track">
              <div class="bar-fill" [style.width.%]="(ft.count / maxFileTypeCount) * 100" style="background:var(--accent)"></div>
            </div>
            <span class="bar-value">{{ ft.count }}</span>
          </div>
        </div>
      </div>

      <!-- Top Terms -->
      <div class="card" *ngIf="topTerms.length > 0">
        <h3 style="font-size:1rem;margin-bottom:1rem">Top Terms</h3>
        <div class="term-cloud">
          <span *ngFor="let term of topTerms" class="term-tag"
                [style.fontSize.rem]="0.7 + (term.count / maxTermCount) * 0.8"
                [style.opacity]="0.5 + (term.count / maxTermCount) * 0.5">
            {{ term.term }}
          </span>
        </div>
      </div>

      <!-- Complex Files -->
      <div class="card" *ngIf="complexFiles.length > 0">
        <h3 style="font-size:1rem;margin-bottom:1rem">Largest Files (by chunks)</h3>
        <div class="bar-chart">
          <div class="bar-row" *ngFor="let f of complexFiles">
            <span class="bar-label" style="font-size:0.75rem" [title]="f.filePath">{{ shortPath(f.filePath) }}</span>
            <div class="bar-track">
              <div class="bar-fill" [style.width.%]="(f.chunkCount / maxChunkCount) * 100" style="background:var(--accent-secondary)"></div>
            </div>
            <span class="bar-value">{{ f.chunkCount }}</span>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .bar-chart { display: flex; flex-direction: column; gap: 0.5rem; }
    .bar-row { display: flex; align-items: center; gap: 0.5rem; }
    .bar-label { width: 80px; font-size: 0.8125rem; color: var(--text-muted); text-align: right; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .bar-track { flex: 1; height: 1.25rem; background: var(--surface-2); border-radius: 100px; overflow: hidden; }
    .bar-fill { height: 100%; background: var(--primary); border-radius: 100px; transition: width 0.3s ease; min-width: 2px; }
    .bar-value { width: 40px; font-size: 0.75rem; color: var(--text-muted); }
    .term-cloud { display: flex; flex-wrap: wrap; gap: 0.5rem; align-items: baseline; }
    .term-tag { color: var(--primary); font-weight: 500; cursor: default; }
  `]
})
export class RepositoryAnalyticsComponent implements OnInit {
  @Input() repositoryId!: string;
  languages: any[] = [];
  fileTypes: any[] = [];
  topTerms: any[] = [];
  complexFiles: any[] = [];

  get maxLangCount() { return this.languages[0]?.count || 1; }
  get maxFileTypeCount() { return this.fileTypes[0]?.count || 1; }
  get maxTermCount() { return this.topTerms[0]?.count || 1; }
  get maxChunkCount() { return this.complexFiles[0]?.chunkCount || 1; }

  constructor(private http: HttpClient) {}

  ngOnInit() {
    const base = `${environment.apiUrl}/repositories/${this.repositoryId}/analytics`;
    this.http.get<any[]>(`${base}/languages`).subscribe(d => this.languages = d);
    this.http.get<any[]>(`${base}/file-types`).subscribe(d => this.fileTypes = d);
    this.http.get<any[]>(`${base}/top-terms?limit=40`).subscribe(d => this.topTerms = d);
    this.http.get<any[]>(`${base}/complex-files`).subscribe(d => this.complexFiles = d);
  }

  shortPath(path: string): string {
    const parts = path.split('/');
    return parts.length > 2 ? '.../' + parts.slice(-2).join('/') : path;
  }
}
