import { Component, Input, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { ActivityHeatmap, DailyActivity, ActivityStats } from '../../models/repository.model';

interface HeatmapCell {
  date: string;
  commitCount: number;
  col: number;
  row: number;
}

@Component({
  selector: 'app-repository-analytics',
  standalone: true,
  imports: [CommonModule],
  template: `
    <!-- Git Activity Heatmap -->
    <div class="card" style="margin-top:1.5rem;padding:1.25rem">
      <h3 style="font-size:1rem;margin-bottom:1rem">Git Activity</h3>

      <div *ngIf="heatmapLoading" style="display:flex;justify-content:center;padding:2rem">
        <div class="spinner"></div>
      </div>

      <div *ngIf="!heatmapLoading && heatmapCells.length > 0" class="heatmap-container">
        <div class="heatmap-scroll">
          <div class="day-labels">
            <span></span>
            <span>Mon</span>
            <span></span>
            <span>Wed</span>
            <span></span>
            <span>Fri</span>
            <span></span>
          </div>
          <svg [attr.width]="heatmapWidth" [attr.height]="heatmapHeight"
               style="display:block" role="img" aria-label="Contribution heatmap">
            <!-- Month labels -->
            <text *ngFor="let label of monthLabels"
                  [attr.x]="label.x" y="10"
                  fill="var(--text-muted)" font-size="10" font-family="inherit">
              {{ label.text }}
            </text>
            <!-- Day cells -->
            <rect *ngFor="let cell of heatmapCells"
                  [attr.x]="cell.col * (cellSize + cellGap)"
                  [attr.y]="18 + cell.row * (cellSize + cellGap)"
                  [attr.width]="cellSize"
                  [attr.height]="cellSize"
                  [attr.fill]="getHeatmapColor(cell.commitCount)"
                  rx="2"
                  style="cursor:default">
              <title>{{ cell.date }}: {{ cell.commitCount }} commit{{ cell.commitCount !== 1 ? 's' : '' }}</title>
            </rect>
          </svg>
        </div>

        <!-- Legend -->
        <div class="heatmap-legend">
          <span class="text-muted" style="font-size:0.75rem">Less</span>
          <span class="legend-cell" [style.background]="'#ebedf0'"></span>
          <span class="legend-cell" [style.background]="'#c6e48b'"></span>
          <span class="legend-cell" [style.background]="'#7bc96f'"></span>
          <span class="legend-cell" [style.background]="'#239a3b'"></span>
          <span class="legend-cell" [style.background]="'#196127'"></span>
          <span class="text-muted" style="font-size:0.75rem">More</span>
        </div>
      </div>

      <div *ngIf="!heatmapLoading && heatmapCells.length === 0" class="text-muted" style="font-size:0.875rem">
        No commit activity data available.
      </div>

      <!-- Stats -->
      <div *ngIf="heatmapStats" class="heatmap-stats">
        <div class="stat-item">
          <span class="stat-value">{{ heatmapStats.totalCommits }}</span>
          <span class="stat-label">Total commits</span>
        </div>
        <div class="stat-item">
          <span class="stat-value">{{ heatmapStats.uniqueAuthors }}</span>
          <span class="stat-label">Contributors</span>
        </div>
        <div class="stat-item">
          <span class="stat-value">{{ heatmapStats.avgPerDay | number:'1.1-1' }}</span>
          <span class="stat-label">Avg/day</span>
        </div>
        <div class="stat-item">
          <span class="stat-value">{{ heatmapStats.maxCommitsInDay }}</span>
          <span class="stat-label">Max in a day</span>
        </div>
        <div class="stat-item">
          <span class="stat-value">{{ heatmapStats.mostActiveDay }}</span>
          <span class="stat-label">Most active day</span>
        </div>
        <div class="stat-item" *ngIf="heatmapStats.longestInactiveStreak > 0">
          <span class="stat-value">{{ heatmapStats.longestInactiveStreak }}d</span>
          <span class="stat-label">Longest streak</span>
        </div>
      </div>
    </div>

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

    .heatmap-container { overflow: visible; }
    .heatmap-scroll { display: flex; gap: 0.25rem; overflow-x: auto; padding-bottom: 0.5rem; }
    .day-labels { display: flex; flex-direction: column; justify-content: flex-start; padding-top: 18px; gap: 0px; min-width: 30px; }
    .day-labels span { height: 13px; font-size: 9px; color: var(--text-muted); line-height: 13px; }
    .heatmap-legend { display: flex; align-items: center; gap: 3px; margin-top: 0.75rem; }
    .legend-cell { display: inline-block; width: 11px; height: 11px; border-radius: 2px; }
    .heatmap-stats { display: flex; flex-wrap: wrap; gap: 1.5rem; margin-top: 1.25rem; padding-top: 1rem; border-top: 1px solid var(--border); }
    .stat-item { display: flex; flex-direction: column; }
    .stat-value { font-size: 1.25rem; font-weight: 600; color: var(--text); }
    .stat-label { font-size: 0.75rem; color: var(--text-muted); }
  `]
})
export class RepositoryAnalyticsComponent implements OnInit {
  @Input() repositoryId!: string;
  languages: any[] = [];
  fileTypes: any[] = [];
  topTerms: any[] = [];
  complexFiles: any[] = [];

  // Heatmap
  heatmapCells: HeatmapCell[] = [];
  heatmapStats: ActivityStats | null = null;
  heatmapLoading = true;
  monthLabels: { text: string; x: number }[] = [];
  cellSize = 11;
  cellGap = 2;

  get maxLangCount() { return this.languages[0]?.count || 1; }
  get maxFileTypeCount() { return this.fileTypes[0]?.count || 1; }
  get maxTermCount() { return this.topTerms[0]?.count || 1; }
  get maxChunkCount() { return this.complexFiles[0]?.chunkCount || 1; }

  get heatmapWidth(): number {
    const numCols = this.heatmapCells.length > 0
      ? Math.max(...this.heatmapCells.map(c => c.col)) + 1
      : 0;
    return numCols * (this.cellSize + this.cellGap);
  }

  get heatmapHeight(): number {
    return 18 + 7 * (this.cellSize + this.cellGap);
  }

  private maxCommitsInDay = 1;

  constructor(private http: HttpClient) {}

  ngOnInit() {
    const base = `${environment.apiUrl}/repositories/${this.repositoryId}/analytics`;
    this.http.get<any[]>(`${base}/languages`).subscribe(d => this.languages = d);
    this.http.get<any[]>(`${base}/file-types`).subscribe(d => this.fileTypes = d);
    this.http.get<any[]>(`${base}/top-terms?limit=40`).subscribe(d => this.topTerms = d);
    this.http.get<any[]>(`${base}/complex-files`).subscribe(d => this.complexFiles = d);
    this.loadHeatmap();
  }

  private loadHeatmap() {
    this.heatmapLoading = true;
    const url = `${environment.apiUrl}/repositories/${this.repositoryId}/analytics/activity-heatmap?weeksBack=52`;
    this.http.get<ActivityHeatmap>(url).subscribe({
      next: data => {
        this.heatmapStats = data.stats;
        this.buildHeatmapGrid(data.dailyData);
        this.heatmapLoading = false;
      },
      error: () => {
        this.heatmapLoading = false;
      }
    });
  }

  private buildHeatmapGrid(dailyData: DailyActivity[]) {
    if (!dailyData || dailyData.length === 0) {
      this.heatmapCells = [];
      return;
    }

    // Build a map of date -> commit count
    const dateMap = new Map<string, number>();
    dailyData.forEach(d => {
      const dateStr = typeof d.date === 'string' ? d.date.substring(0, 10) : new Date(d.date).toISOString().substring(0, 10);
      dateMap.set(dateStr, d.commitCount);
    });

    this.maxCommitsInDay = Math.max(...dailyData.map(d => d.commitCount), 1);

    // Determine the range: last 52 weeks ending today
    const today = new Date();
    const endDate = new Date(today);
    // Align to end of week (Saturday)
    const endDow = endDate.getDay(); // 0=Sun
    endDate.setDate(endDate.getDate() + (6 - endDow));

    const startDate = new Date(endDate);
    startDate.setDate(startDate.getDate() - 52 * 7 + 1);

    const cells: HeatmapCell[] = [];
    const months: { text: string; x: number }[] = [];
    let lastMonth = -1;
    const current = new Date(startDate);
    const col0Dow = current.getDay(); // should be Sunday (0)

    while (current <= endDate) {
      const daysSinceStart = Math.floor((current.getTime() - startDate.getTime()) / (1000 * 60 * 60 * 24));
      const col = Math.floor(daysSinceStart / 7);
      const row = current.getDay(); // 0=Sun, 6=Sat
      const dateStr = current.toISOString().substring(0, 10);

      cells.push({
        date: dateStr,
        commitCount: dateMap.get(dateStr) || 0,
        col,
        row
      });

      // Month label on first occurrence
      if (current.getMonth() !== lastMonth && row === 0) {
        lastMonth = current.getMonth();
        const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
        months.push({
          text: monthNames[current.getMonth()],
          x: col * (this.cellSize + this.cellGap)
        });
      }

      current.setDate(current.getDate() + 1);
    }

    this.heatmapCells = cells;
    this.monthLabels = months;
  }

  getHeatmapColor(commitCount: number): string {
    if (commitCount === 0) return '#ebedf0';
    const ratio = commitCount / this.maxCommitsInDay;
    if (ratio <= 0.25) return '#c6e48b';
    if (ratio <= 0.5) return '#7bc96f';
    if (ratio <= 0.75) return '#239a3b';
    return '#196127';
  }

  shortPath(path: string): string {
    const parts = path.split('/');
    return parts.length > 2 ? '.../' + parts.slice(-2).join('/') : path;
  }
}
