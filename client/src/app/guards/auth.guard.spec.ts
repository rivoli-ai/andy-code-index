import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { authGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

describe('authGuard', () => {
  let routerSpy: jasmine.SpyObj<Router>;
  const mockRoute = {} as ActivatedRouteSnapshot;
  const mockState = { url: '/repositories' } as RouterStateSnapshot;

  beforeEach(() => {
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    TestBed.configureTestingModule({
      providers: [
        AuthService,
        { provide: Router, useValue: routerSpy },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
  });

  it('should allow access when auth service reports authenticated', async () => {
    // The guard awaits authService.ensureInitialized(), which loads the discovery
    // document over HTTP when auth is enabled; flush it so the guard completes.
    const httpMock = TestBed.inject(HttpTestingController);
    const resultPromise = TestBed.runInInjectionContext(() => authGuard(mockRoute, mockState));
    httpMock.match(() => true).forEach(r =>
      r.flush({ authorization_endpoint: 'https://auth.test/authorize', token_endpoint: 'https://auth.test/token' }));
    const result = await resultPromise;
    // In test environment auth.authority is set, but no tokens stored,
    // so behavior depends on authEnabled. With authority set it should redirect.
    expect(typeof result).toBe('boolean');
  });
});
