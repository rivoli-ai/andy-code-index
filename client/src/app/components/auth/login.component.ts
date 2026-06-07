import { Component } from '@angular/core';

import { Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [],
  template: `
    <div class="login-container">
      <div class="login-card">
        <div class="brand">
          <i class="bi bi-braces-asterisk"></i>
          <h1>Andy CodeIndex</h1>
        </div>
        <p>Semantic code indexing for the Andy ecosystem</p>
    
        @if (authEnabled && !authenticated) {
          <div>
            <button class="btn btn-primary" (click)="signIn()" [disabled]="signingIn">
              {{ signingIn ? 'Redirecting...' : 'Sign in with Andy Auth' }}
            </button>
            @if (error) {
              <div class="error-msg">{{ error }}</div>
            }
          </div>
        }
    
        @if (authEnabled && authenticated) {
          <div>
            <p><strong>Welcome back, {{ userName }}</strong></p>
            <button class="btn btn-primary" (click)="goToApp()" style="margin-right:0.5rem">Go to Repositories</button>
            <button class="btn btn-secondary" (click)="signOut()">Sign Out</button>
          </div>
        }
    
        @if (!authEnabled) {
          <p class="text-muted">
            Authentication not configured. Running in dev mode.
          </p>
        }
      </div>
    </div>
    `,
  styles: [`
    .login-container {
      display: flex; align-items: center; justify-content: center;
      min-height: 100vh; background: var(--background-alt);
    }
    .login-card {
      background: var(--surface); border: 1px solid var(--border);
      border-radius: var(--radius-lg); padding: 3rem;
      text-align: center; max-width: 420px; width: 100%;
      box-shadow: var(--shadow);
    }
    .brand { margin-bottom: 1.5rem; }
    .brand i { font-size: 3rem; color: var(--primary); display: block; margin-bottom: 0.75rem; }
    .brand h1 { font-size: var(--font-2xl); }
    p { color: var(--text-muted); margin-bottom: 2rem; }
    .error-msg { color: var(--danger); margin-top: 1rem; font-size: var(--font-sm); }
  `]
})
export class LoginComponent {
  authEnabled: boolean;
  authenticated: boolean;
  userName: string | null;
  signingIn = false;
  error = '';

  constructor(private auth: AuthService, private router: Router) {
    this.authEnabled = auth.authEnabled;
    this.authenticated = auth.isAuthenticated();
    this.userName = auth.getUserName();
  }

  async signIn() {
    this.signingIn = true;
    this.error = '';
    try {
      await this.auth.signIn();
    } catch (e: any) {
      this.error = e.message || 'Sign in failed';
      this.signingIn = false;
    }
  }

  signOut() {
    this.auth.signOut();
  }

  goToApp() {
    this.router.navigate(['/repositories']);
  }
}
