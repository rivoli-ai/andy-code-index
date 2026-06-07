import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { CallbackComponent } from './callback.component';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { AuthService } from '../../services/auth.service';

describe('CallbackComponent', () => {
  let authServiceSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['handleCallback'], { authEnabled: true });
    routerSpy = jasmine.createSpyObj('Router', ['navigate', 'navigateByUrl']);

    await TestBed.configureTestingModule({
      imports: [CallbackComponent],
      providers: [
        { provide: AuthService, useValue: authServiceSpy },
        { provide: Router, useValue: routerSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap({ code: 'test-code', state: 'test-state' })
            }
          }
        },
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(CallbackComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should call handleCallback with code and state', async () => {
    authServiceSpy.handleCallback.and.returnValue(Promise.resolve(true));
    const fixture = TestBed.createComponent(CallbackComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(authServiceSpy.handleCallback).toHaveBeenCalledWith('test-code', 'test-state');
  });

  it('should navigate to repositories on success', async () => {
    authServiceSpy.handleCallback.and.returnValue(Promise.resolve(true));
    const fixture = TestBed.createComponent(CallbackComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/repositories');
  });

  it('should show error on failure', async () => {
    authServiceSpy.handleCallback.and.returnValue(Promise.resolve(false));
    const fixture = TestBed.createComponent(CallbackComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.componentInstance.error).toBeTruthy();
  });
});
