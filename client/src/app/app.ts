import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterOutlet, RouterLink } from '@angular/router';
import { SidebarComponent } from './components/layout/sidebar.component';
import { ApiService } from './services/api.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, SidebarComponent],
  template: `
    <app-sidebar />
    <main class="main-content">
      <div *ngIf="llmWarning" class="health-banner health-banner-warning">
        <i class="bi bi-exclamation-triangle-fill"></i>
        {{ llmWarning }}
        <a routerLink="/settings">Check Settings</a>
      </div>
      <div *ngIf="embeddingWarning" class="health-banner health-banner-warning">
        <i class="bi bi-exclamation-triangle-fill"></i>
        {{ embeddingWarning }}
        <a routerLink="/settings">Check Settings</a>
      </div>
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
  `]
})
export class AppComponent implements OnInit, OnDestroy {
  llmWarning: string | null = null;
  embeddingWarning: string | null = null;
  private healthTimer: any;

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.checkHealth();
    this.healthTimer = setInterval(() => this.checkHealth(), 5 * 60 * 1000);
  }

  ngOnDestroy() {
    if (this.healthTimer) clearInterval(this.healthTimer);
  }

  private checkHealth() {
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
