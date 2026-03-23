import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-callback',
  standalone: true,
  template: `
    <div style="display:flex;align-items:center;justify-content:center;min-height:100vh">
      <div style="text-align:center">
        <div class="spinner" style="margin:0 auto 1rem"></div>
        <p class="text-muted">Completing sign in...</p>
      </div>
    </div>
  `
})
export class CallbackComponent implements OnInit {
  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private auth: AuthService
  ) {}

  async ngOnInit() {
    const code = this.route.snapshot.queryParamMap.get('code');
    const state = this.route.snapshot.queryParamMap.get('state');

    if (code && state) {
      const success = await this.auth.handleCallback(code, state);
      this.router.navigate(success ? ['/repositories'] : ['/login']);
    } else {
      this.router.navigate(['/login']);
    }
  }
}
