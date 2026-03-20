import { TestBed } from '@angular/core/testing';
import { RepositoryListComponent } from './repository-list.component';
import { ApiService } from '../../services/api.service';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';

describe('RepositoryListComponent', () => {
  let apiServiceSpy: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    apiServiceSpy = jasmine.createSpyObj('ApiService', ['getRepositories', 'syncRepository']);
    apiServiceSpy.getRepositories.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [RepositoryListComponent],
      providers: [
        { provide: ApiService, useValue: apiServiceSpy },
        provideRouter([])
      ]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(RepositoryListComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should load repositories on init', () => {
    const fixture = TestBed.createComponent(RepositoryListComponent);
    fixture.detectChanges();
    expect(apiServiceSpy.getRepositories).toHaveBeenCalled();
  });

  it('should show empty state when no repos', () => {
    const fixture = TestBed.createComponent(RepositoryListComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No repositories yet');
  });

  it('should display repositories in table', () => {
    apiServiceSpy.getRepositories.and.returnValue(of([
      { id: '1', name: 'test-repo', url: 'https://github.com/t/r', provider: 'GitHub', status: 'indexed', createdAt: '', updatedAt: '' }
    ]));
    const fixture = TestBed.createComponent(RepositoryListComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('test-repo');
  });

  it('should show error on load failure', () => {
    apiServiceSpy.getRepositories.and.returnValue(throwError(() => new Error('fail')));
    const fixture = TestBed.createComponent(RepositoryListComponent);
    fixture.detectChanges();
    expect(fixture.componentInstance.error).toContain('Failed');
  });

  it('should return correct status class', () => {
    const fixture = TestBed.createComponent(RepositoryListComponent);
    expect(fixture.componentInstance.statusClass('indexed')).toBe('badge-success');
    expect(fixture.componentInstance.statusClass('indexing')).toBe('badge-info');
    expect(fixture.componentInstance.statusClass('error')).toBe('badge-danger');
    expect(fixture.componentInstance.statusClass('pending')).toBe('badge-muted');
  });

  it('should sync repository', () => {
    apiServiceSpy.syncRepository.and.returnValue(of(undefined));
    const fixture = TestBed.createComponent(RepositoryListComponent);
    const repo = { id: '1', name: 'test', url: '', provider: '', status: 'indexed', createdAt: '', updatedAt: '' };
    fixture.componentInstance.sync(repo as any);
    expect(apiServiceSpy.syncRepository).toHaveBeenCalledWith('1');
  });
});
