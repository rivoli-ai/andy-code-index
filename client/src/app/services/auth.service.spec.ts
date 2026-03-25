import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AuthService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(AuthService);
    localStorage.clear();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should report authEnabled based on environment', () => {
    expect(typeof service.authEnabled).toBe('boolean');
  });

  it('should return null user name when not authenticated', () => {
    expect(service.getUserName()).toBeNull();
  });

  it('should return null user email when not authenticated', () => {
    expect(service.getUserEmail()).toBeNull();
  });

  it('should have ensureInitialized method', async () => {
    await expectAsync(service.ensureInitialized()).toBeResolved();
  });
});
