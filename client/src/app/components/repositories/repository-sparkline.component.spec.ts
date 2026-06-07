import { TestBed, ComponentFixture } from '@angular/core/testing';
import { RepositorySparklineComponent } from './repository-sparkline.component';

describe('RepositorySparklineComponent', () => {
  let fixture: ComponentFixture<RepositorySparklineComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositorySparklineComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(RepositorySparklineComponent);
  });

  it('should render a placeholder when there is no activity', () => {
    fixture.componentRef.setInput('weeklyData', []);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('svg')).toBeNull();
    expect(el.textContent).toContain('--');
  });

  it('should expand each week into 7 day-cells', () => {
    fixture.componentRef.setInput('weeklyData', [
      { weekStart: '2026-01-05', commitCount: 4 },
      { weekStart: '2026-01-12', commitCount: 0 },
    ]);
    fixture.detectChanges();

    expect(fixture.componentInstance.cells.length).toBe(14);
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('rect').length).toBe(14);
  });

  it('should color cells by commit intensity', () => {
    fixture.componentRef.setInput('weeklyData', [{ weekStart: '2026-01-05', commitCount: 10 }]);
    fixture.detectChanges();

    const c = fixture.componentInstance;
    expect(c.getColor(0)).toBe('#ebedf0'); // no commits
    expect(c.getColor(10)).toBe('#196127'); // max intensity
  });
});
