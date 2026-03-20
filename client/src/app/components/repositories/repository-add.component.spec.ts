import { TestBed } from '@angular/core/testing';
import { RepositoryAddComponent } from './repository-add.component';
import { ApiService } from '../../services/api.service';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';

describe('RepositoryAddComponent', () => {
  let apiServiceSpy: jasmine.SpyObj<ApiService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    apiServiceSpy = jasmine.createSpyObj('ApiService', ['createRepository']);
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    await TestBed.configureTestingModule({
      imports: [RepositoryAddComponent],
      providers: [
        { provide: ApiService, useValue: apiServiceSpy },
        { provide: Router, useValue: routerSpy }
      ]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(RepositoryAddComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should submit and navigate on success', () => {
    apiServiceSpy.createRepository.and.returnValue(of({ id: '123', name: 'test', url: '', provider: '', status: '', createdAt: '', updatedAt: '' } as any));
    const fixture = TestBed.createComponent(RepositoryAddComponent);
    fixture.componentInstance.url = 'https://github.com/test/repo';
    fixture.componentInstance.submit();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/repositories', '123']);
  });

  it('should show error on failure', () => {
    apiServiceSpy.createRepository.and.returnValue(throwError(() => ({ error: { error: 'Already exists' } })));
    const fixture = TestBed.createComponent(RepositoryAddComponent);
    fixture.componentInstance.url = 'https://github.com/test/repo';
    fixture.componentInstance.submit();
    expect(fixture.componentInstance.error).toBe('Already exists');
    expect(fixture.componentInstance.submitting).toBeFalse();
  });

  it('should navigate on cancel', () => {
    const fixture = TestBed.createComponent(RepositoryAddComponent);
    fixture.componentInstance.cancel();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/repositories']);
  });
});
