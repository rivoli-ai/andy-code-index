import { Component, OnInit } from '@angular/core';

import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-callback',
  standalone: true,
  imports: [],
  template: `
    <div style="display:flex;align-items:center;justify-content:center;min-height:100vh">
      <div style="text-align:center">
        @if (!error) {
          <div>
            <div class="spinner" style="margin:0 auto 1rem"></div>
            <p class="text-muted">Completing sign in...</p>
          </div>
        }
        @if (error) {
          <div>
            <p style="color:var(--danger)">{{ error }}</p>
            <button class="btn btn-primary" (click)="retry()">Try Again</button>
          </div>
        }
      </div>
    </div>
    `
})
export class CallbackComponent implements OnInit {
  error = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private auth: AuthService
  ) {}

  async ngOnInit() {
    const code = this.route.snapshot.queryParamMap.get('code');
    const state = this.route.snapshot.queryParamMap.get('state');
    const errorParam = this.route.snapshot.queryParamMap.get('error');

    if (errorParam) {
      this.error = this.route.snapshot.queryParamMap.get('error_description') || errorParam;
      return;
    }

    if (code) {
      const success = await this.auth.handleCallback(code, state ?? undefined);
      if (success) {
        const returnUrl = localStorage.getItem('auth_return_url') || '/repositories';
        localStorage.removeItem('auth_return_url');
        this.router.navigateByUrl(returnUrl);
      } else {
        this.error = 'Authentication failed. Please try again.';
      }
    } else {
      this.router.navigate(['/login']);
    }
  }

  retry() {
    this.router.navigate(['/login']);
  }
}
