import { Component } from '@angular/core';
import { AuthService } from '../../services/auth.service';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-login',
  standalone: true,
  template: `
    <div class="login-container">
      <div class="login-card">
        <div class="brand">
          <i class="bi bi-braces-asterisk"></i>
          <h1>Andy CodeIndex</h1>
        </div>
        <p>Semantic code indexing for the Andy ecosystem</p>
        <button class="btn btn-primary" (click)="login()" *ngIf="authEnabled">
          <i class="bi bi-box-arrow-in-right"></i> Sign in with Andy Auth
        </button>
        <p *ngIf="!authEnabled" class="text-muted">Authentication not configured — running in dev mode</p>
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
      text-align: center; max-width: 400px; width: 100%;
      box-shadow: var(--shadow);
    }
    .brand { margin-bottom: 1.5rem; }
    .brand i { font-size: 3rem; color: var(--primary); display: block; margin-bottom: 0.75rem; }
    .brand h1 { font-size: 1.5rem; }
    p { color: var(--text-muted); margin-bottom: 2rem; }
  `],
  imports: [/* CommonModule if needed */]
})
export class LoginComponent {
  authEnabled = !!environment.authorityUrl;

  constructor(private auth: AuthService) {}

  login() { this.auth.login(); }
}
