import { TestBed, ComponentFixture } from '@angular/core/testing';
import { Router } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthService } from '../../services/auth.service';

describe('LoginComponent', () => {
  let authSpy: jasmine.SpyObj<AuthService>;
  let routerSpy: jasmine.SpyObj<Router>;

  function setup(opts: { authEnabled?: boolean; authenticated?: boolean; userName?: string | null } = {})
    : ComponentFixture<LoginComponent> {
    authSpy = jasmine.createSpyObj<AuthService>(
      'AuthService',
      ['isAuthenticated', 'getUserName', 'signIn', 'signOut'],
      { authEnabled: opts.authEnabled ?? true },
    );
    authSpy.isAuthenticated.and.returnValue(opts.authenticated ?? false);
    authSpy.getUserName.and.returnValue(opts.userName ?? null);
    authSpy.signIn.and.returnValue(Promise.resolve());

    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        { provide: AuthService, useValue: authSpy },
        { provide: Router, useValue: routerSpy },
      ],
    });
    return TestBed.createComponent(LoginComponent);
  }

  it('should reflect the auth state from the service', () => {
    const fixture = setup({ authEnabled: true, authenticated: true, userName: 'Ada' });
    const c = fixture.componentInstance;
    expect(c.authEnabled).toBeTrue();
    expect(c.authenticated).toBeTrue();
    expect(c.userName).toBe('Ada');
  });

  it('should call auth.signIn() on signIn()', async () => {
    const fixture = setup();
    await fixture.componentInstance.signIn();
    expect(authSpy.signIn).toHaveBeenCalled();
  });

  it('should surface an error and reset signingIn when signIn fails', async () => {
    const fixture = setup();
    authSpy.signIn.and.returnValue(Promise.reject(new Error('boom')));
    await fixture.componentInstance.signIn();
    expect(fixture.componentInstance.error).toBe('boom');
    expect(fixture.componentInstance.signingIn).toBeFalse();
  });

  it('should navigate to /repositories from goToApp()', () => {
    const fixture = setup();
    fixture.componentInstance.goToApp();
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/repositories']);
  });

  it('should call auth.signOut() on signOut()', () => {
    const fixture = setup();
    fixture.componentInstance.signOut();
    expect(authSpy.signOut).toHaveBeenCalled();
  });
});
