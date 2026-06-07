import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { RepositoryAnalyticsComponent } from './repository-analytics.component';

describe('RepositoryAnalyticsComponent', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<RepositoryAnalyticsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositoryAnalyticsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RepositoryAnalyticsComponent);
    fixture.componentRef.setInput('repositoryId', 'repo-1');
  });

  afterEach(() => httpMock.verify());

  // Some analytics URLs embed a query string (e.g. top-terms?limit=40), which
  // lands in HttpRequest.url, so match by substring rather than suffix.
  function flushEndingWith(suffix: string, body: any) {
    httpMock.expectOne(r => r.url.includes(suffix)).flush(body);
  }

  it('should load every analytics section on init', () => {
    fixture.detectChanges();

    flushEndingWith('/analytics/languages', [{ language: 'C#', count: 10 }]);
    flushEndingWith('/analytics/file-types', [{ ext: '.cs', count: 8 }]);
    flushEndingWith('/analytics/top-terms', [{ term: 'service', count: 5 }]);
    flushEndingWith('/analytics/complex-files', [{ path: 'a.cs', chunkCount: 3 }]);
    httpMock.expectOne(r => r.url.includes('/activity-heatmap'))
      .flush({ stats: { totalCommits: 1 }, dailyData: [] });

    const c = fixture.componentInstance;
    expect(c.languages.length).toBe(1);
    expect(c.fileTypes.length).toBe(1);
    expect(c.topTerms.length).toBe(1);
    expect(c.complexFiles.length).toBe(1);
    expect(c.maxLangCount).toBe(10);
    expect(c.heatmapLoading).toBeFalse();
  });

  it('should stop the heatmap spinner on error', () => {
    fixture.detectChanges();

    flushEndingWith('/analytics/languages', []);
    flushEndingWith('/analytics/file-types', []);
    flushEndingWith('/analytics/top-terms', []);
    flushEndingWith('/analytics/complex-files', []);
    httpMock.expectOne(r => r.url.includes('/activity-heatmap'))
      .flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });

    expect(fixture.componentInstance.heatmapLoading).toBeFalse();
  });
});
