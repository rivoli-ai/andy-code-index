import { Component, OnInit, OnDestroy } from '@angular/core';

import { RouterOutlet, RouterLink, Router, NavigationEnd } from '@angular/router';
import { SidebarComponent } from './components/layout/sidebar.component';
import { ApiService } from './services/api.service';
import { AuthService } from './services/auth.service';
import { filter } from 'rxjs/operators';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, SidebarComponent],
  template: `
    @if (showChrome) {
      <app-sidebar />
    }
    <main [class.main-content]="showChrome" [class.main-content-full]="!showChrome">
      @if (showChrome && llmWarning) {
        <div class="health-banner health-banner-warning">
          <i class="bi bi-exclamation-triangle-fill"></i>
          {{ llmWarning }}
          <a routerLink="/settings">Check Settings</a>
        </div>
      }
      @if (showChrome && embeddingWarning) {
        <div class="health-banner health-banner-warning">
          <i class="bi bi-exclamation-triangle-fill"></i>
          {{ embeddingWarning }}
          <a routerLink="/settings">Check Settings</a>
        </div>
      }
      <router-outlet />
    </main>
    `,
  styles: [`
    .main-content {
      margin-left: var(--sidebar-width);
      padding: 2rem;
      min-height: 100vh;
    }
    .health-banner {
      padding: 0.625rem 1rem;
      border-radius: var(--radius);
      margin-bottom: 1rem;
      font-size: 0.875rem;
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .health-banner a {
      margin-left: auto;
      font-weight: 500;
    }
    .health-banner-warning {
      background: rgba(255,193,7,0.1);
      border: 1px solid rgba(255,193,7,0.3);
      color: #856404;
    }
    .main-content-full {
      min-height: 100vh;
    }
  `]
})
export class AppComponent implements OnInit, OnDestroy {
  llmWarning: string | null = null;
  embeddingWarning: string | null = null;
  showChrome = false;
  private healthTimer: any;
  private readonly chromeExcludedRoutes = ['/login', '/callback'];

  constructor(private api: ApiService, private auth: AuthService, private router: Router) {}

  ngOnInit() {
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd)
    ).subscribe(e => {
      this.showChrome = !this.chromeExcludedRoutes.some(r => e.urlAfterRedirects.startsWith(r));
      if (this.showChrome && this.auth.isAuthenticated() && !this.healthTimer) {
        this.checkHealth();
        this.healthTimer = setInterval(() => this.checkHealth(), 5 * 60 * 1000);
      }
    });
  }

  ngOnDestroy() {
    if (this.healthTimer) clearInterval(this.healthTimer);
  }

  private checkHealth() {
    if (!this.auth.isAuthenticated()) return;
    this.api.getKeyHealth().subscribe({
      next: (h) => {
        if (!h.lastChecked) return;
        this.llmWarning = !h.llmKeyValid && h.llmError
          ? `LLM API key is invalid \u2014 enrichments and insights will not work.` : null;
        this.embeddingWarning = !h.embeddingKeyValid && h.embeddingError
          ? `Embedding API key is invalid \u2014 search will not work.` : null;
      },
      error: () => {}
    });
  }
}
