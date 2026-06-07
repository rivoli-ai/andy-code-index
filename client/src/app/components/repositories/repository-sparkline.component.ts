import { Component, Input, OnChanges } from '@angular/core';

import { WeeklyActivity } from '../../models/repository.model';

interface DayCell {
  date: Date;
  commitCount: number;
  col: number;
  row: number;
}

@Component({
  selector: 'app-repository-sparkline',
  standalone: true,
  imports: [],
  template: `
    @if (cells.length > 0) {
      <svg [attr.width]="svgWidth" [attr.height]="svgHeight"
        style="display:block" role="img" aria-label="Activity grid">
        @for (cell of cells; track cell) {
          <rect
            [attr.x]="cell.col * (cellSize + gap)"
            [attr.y]="cell.row * (cellSize + gap)"
            [attr.width]="cellSize"
            [attr.height]="cellSize"
            [attr.fill]="getColor(cell.commitCount)"
            [attr.rx]="1">
            <title>{{ formatDate(cell.date) }}: {{ cell.commitCount }} commits</title>
          </rect>
        }
      </svg>
    }
    @if (cells.length === 0) {
      <span class="text-muted" style="font-size:0.75rem">--</span>
    }
    `,
  styles: [`
    :host { display: inline-block; }
  `]
})
export class RepositorySparklineComponent implements OnChanges {
  @Input() weeklyData: WeeklyActivity[] = [];

  cells: DayCell[] = [];
  cellSize = 4;
  gap = 1;
  numWeeks = 52;

  get svgWidth(): number {
    return this.numWeeks * (this.cellSize + this.gap);
  }

  get svgHeight(): number {
    return 7 * (this.cellSize + this.gap);
  }

  private maxCommits = 1;

  ngOnChanges() {
    this.buildGrid();
  }

  private buildGrid() {
    // Take the last numWeeks of weekly data and expand into daily cells
    const weeks = this.weeklyData.slice(-this.numWeeks);
    if (weeks.length === 0) {
      this.cells = [];
      return;
    }

    // Find max for color scaling
    this.maxCommits = Math.max(...weeks.map(w => w.commitCount), 1);

    const cells: DayCell[] = [];
    weeks.forEach((week, colIndex) => {
      const weekStart = new Date(week.weekStart);
      // Distribute commits across 7 days (simplified: show week total on all days)
      // For a compact sparkline, we show per-week intensity on all 7 day rows
      for (let dayOfWeek = 0; dayOfWeek < 7; dayOfWeek++) {
        const date = new Date(weekStart);
        date.setDate(date.getDate() + dayOfWeek);
        cells.push({
          date,
          commitCount: week.commitCount,
          col: colIndex,
          row: dayOfWeek
        });
      }
    });

    this.cells = cells;
  }

  getColor(commitCount: number): string {
    if (commitCount === 0) return '#ebedf0';
    const ratio = commitCount / this.maxCommits;
    if (ratio <= 0.25) return '#c6e48b';
    if (ratio <= 0.5) return '#7bc96f';
    if (ratio <= 0.75) return '#239a3b';
    return '#196127';
  }

  formatDate(date: Date): string {
    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  }
}
