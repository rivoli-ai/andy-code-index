import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { WeeklyActivity } from '../../models/repository.model';

@Component({
  selector: 'app-repository-sparkline',
  standalone: true,
  imports: [CommonModule],
  template: `
    <svg [attr.width]="width" [attr.height]="height" *ngIf="weeklyData.length > 0"
         style="display:block" role="img" aria-label="Activity sparkline">
      <rect *ngFor="let week of weeklyData; let i = index"
            [attr.x]="i * barWidth"
            [attr.y]="height - getBarHeight(week.commitCount)"
            [attr.width]="Math.max(barWidth - 1, 1)"
            [attr.height]="getBarHeight(week.commitCount)"
            [attr.fill]="getColor(week.commitCount)"
            rx="1">
        <title>{{ week.weekStart | date:'MMM d' }}: {{ week.commitCount }} commits</title>
      </rect>
    </svg>
    <span *ngIf="weeklyData.length === 0" class="text-muted" style="font-size:0.75rem">No data</span>
  `,
  styles: [`
    :host { display: inline-block; }
  `]
})
export class RepositorySparklineComponent {
  @Input() weeklyData: WeeklyActivity[] = [];
  @Input() width = 200;
  @Input() height = 32;

  Math = Math;

  get barWidth(): number {
    return this.weeklyData.length > 0 ? this.width / this.weeklyData.length : 4;
  }

  get maxCommits(): number {
    return Math.max(...this.weeklyData.map(w => w.commitCount), 1);
  }

  getBarHeight(commitCount: number): number {
    if (commitCount === 0) return 0;
    const proportional = (commitCount / this.maxCommits) * this.height;
    return Math.max(proportional, 2);
  }

  getColor(commitCount: number): string {
    if (commitCount === 0) return '#ebedf0';
    const ratio = commitCount / this.maxCommits;
    if (ratio <= 0.25) return '#c6e48b';
    if (ratio <= 0.5) return '#7bc96f';
    if (ratio <= 0.75) return '#239a3b';
    return '#196127';
  }
}
