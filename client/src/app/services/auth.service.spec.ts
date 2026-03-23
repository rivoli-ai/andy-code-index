import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(() => {
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    TestBed.configureTestingModule({
      providers: [
        AuthService,
        { provide: Router, useValue: routerSpy }
      ]
    });
    service = TestBed.inject(AuthService);
    sessionStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should return null token when not authenticated', () => {
    expect(service.getToken()).toBeNull();
  });

  it('should return false for isAuthenticated when no token', () => {
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('should return true for isAuthenticated with valid token', () => {
    // Create a mock JWT with future expiry
    const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + 3600 }));
    const mockToken = `header.${payload}.signature`;
    sessionStorage.setItem('code_index_token', mockToken);
    expect(service.isAuthenticated()).toBeTrue();
  });

  it('should return false for isAuthenticated with expired token', () => {
    const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) - 3600 }));
    const mockToken = `header.${payload}.signature`;
    sessionStorage.setItem('code_index_token', mockToken);
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('should clear token on logout', () => {
    sessionStorage.setItem('code_index_token', 'test-token');
    service.logout();
    expect(sessionStorage.getItem('code_index_token')).toBeNull();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/login']);
  });
});
