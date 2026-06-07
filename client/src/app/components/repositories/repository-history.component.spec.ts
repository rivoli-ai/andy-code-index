import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { RepositoryHistoryComponent } from './repository-history.component';

describe('RepositoryHistoryComponent', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<RepositoryHistoryComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepositoryHistoryComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RepositoryHistoryComponent);
    fixture.componentRef.setInput('repositoryId', 'repo-1');
  });

  afterEach(() => httpMock.verify());

  it('should load indexing history and git commits on init', () => {
    fixture.detectChanges();

    httpMock.expectOne(r => r.url.endsWith('/repositories/repo-1/history'))
      .flush([{ id: 'run-1' }, { id: 'run-2' }]);
    httpMock.expectOne(r => r.url.endsWith('/repositories/repo-1/git/log'))
      .flush({ commits: [{ sha: 'abc' }], hasMore: true, nextCursor: 'cur-1' });

    const c = fixture.componentInstance;
    expect(c.runs.length).toBe(2);
    expect(c.gitCommits.length).toBe(1);
    expect(c.hasMoreCommits).toBeTrue();
    expect(c.loading).toBeFalse();
  });

  it('should append more commits on loadMoreCommits()', () => {
    fixture.detectChanges();
    httpMock.expectOne(r => r.url.endsWith('/repositories/repo-1/history')).flush([]);
    httpMock.expectOne(r => r.url.endsWith('/repositories/repo-1/git/log'))
      .flush({ commits: [{ sha: 'a' }], hasMore: true, nextCursor: 'cur-1' });

    fixture.componentInstance.loadMoreCommits();
    const second = httpMock.expectOne(r => r.url.endsWith('/repositories/repo-1/git/log'));
    expect(second.request.params.get('before')).toBe('cur-1');
    second.flush({ commits: [{ sha: 'b' }], hasMore: false });

    expect(fixture.componentInstance.gitCommits.map(c => c.sha)).toEqual(['a', 'b']);
  });
});
