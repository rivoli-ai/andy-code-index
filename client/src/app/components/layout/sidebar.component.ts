import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

import { environment } from '../../../environments/environment';
import { AuthService } from '../../services/auth.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  template: `
    <aside class="sidebar">
      <div class="sidebar-brand">
        <i class="bi bi-braces-asterisk"></i>
        <span class="brand-text">CodeIndex</span>
      </div>
      <nav class="sidebar-nav">
        <div class="nav-section">
          <div class="nav-section-title">Overview</div>
          <a routerLink="/dashboard" routerLinkActive="active" class="nav-item">
            <i class="bi bi-speedometer2"></i><span>Dashboard</span>
          </a>
          <a routerLink="/repositories" routerLinkActive="active" class="nav-item">
            <i class="bi bi-folder2-open"></i><span>Repositories</span>
          </a>
          <a routerLink="/search" routerLinkActive="active" class="nav-item">
            <i class="bi bi-search"></i><span>Search</span>
          </a>
          <a routerLink="/discover" routerLinkActive="active" class="nav-item">
            <i class="bi bi-globe"></i><span>Discover</span>
          </a>
        </div>
        <div class="nav-section">
          <div class="nav-section-title">Intelligence</div>
          <a routerLink="/chat" routerLinkActive="active" class="nav-item">
            <i class="bi bi-chat-dots"></i><span>Chat</span>
          </a>
          <a routerLink="/enrichments" routerLinkActive="active" class="nav-item">
            <i class="bi bi-file-earmark-text"></i><span>Enrichments</span>
          </a>
          <a routerLink="/tasks" routerLinkActive="active" class="nav-item">
            <i class="bi bi-list-task"></i><span>Tasks</span>
          </a>
        </div>
        <div class="nav-section">
          <div class="nav-section-title">Account</div>
          <a routerLink="/settings" routerLinkActive="active" class="nav-item">
            <i class="bi bi-gear"></i><span>Settings</span>
          </a>
          <a routerLink="/docs" routerLinkActive="active" class="nav-item">
            <i class="bi bi-book"></i><span>Docs</span>
          </a>
        </div>
      </nav>
      <div class="sidebar-footer">
        @if (authService.authEnabled && authService.isAuthenticated()) {
          <div class="user-section">
            <div class="user-info">
              <svg class="user-avatar" fill="currentColor" viewBox="0 0 24 24">
                <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm0 14.2c-2.5 0-4.71-1.28-6-3.22.03-1.99 4-3.08 6-3.08 1.99 0 5.97 1.09 6 3.08-1.29 1.94-3.5 3.22-6 3.22z"/>
              </svg>
              <div class="user-details">
                <span class="user-name">{{ authService.getUserName() || 'User' }}</span>
                @if (authService.getUserEmail()) {
                  <span class="user-email">{{ authService.getUserEmail() }}</span>
                }
              </div>
            </div>
            <button class="btn btn-sm btn-secondary sign-out-btn" (click)="signOut()">
              <i class="bi bi-box-arrow-right"></i> Sign Out
            </button>
          </div>
        }
        @if (isDevMode && !authService.authEnabled) {
          <div class="dev-indicator">
            <span class="dev-dot"></span>
            <span>Development Mode</span>
          </div>
        }
      </div>
    </aside>
    `,
  styles: [`
    .sidebar {
      position: fixed; left: 0; top: 0; bottom: 0;
      width: var(--sidebar-width); background: var(--surface);
      border-right: 1px solid var(--border); z-index: 100;
      display: flex; flex-direction: column; overflow-y: auto;
    }
    .sidebar-brand {
      padding: 1.25rem 1.5rem; display: flex; align-items: center; gap: 0.75rem;
      font-size: var(--font-xl); font-weight: 700; color: var(--primary);
      border-bottom: 1px solid var(--border);
    }
    .sidebar-brand i { font-size: var(--font-xl); }
    .sidebar-nav { padding: 1rem 0; flex: 1; }
    .nav-section { margin-bottom: 1.5rem; }
    .nav-section-title {
      padding: 0 1.5rem; margin-bottom: 0.5rem;
      font-size: var(--font-xs); font-weight: 600; text-transform: uppercase;
      letter-spacing: 0.08em; color: var(--text-light);
    }
    .nav-item {
      display: flex; align-items: center; gap: 0.75rem;
      padding: 0.625rem 1.5rem; color: var(--text-muted);
      font-size: var(--font-base); font-weight: 500; transition: all var(--transition);
    }
    .nav-item:hover { color: var(--text); background: var(--background-alt); }
    .nav-item.active { color: var(--primary); background: rgba(0, 102, 204, 0.08); }
    .nav-item i { font-size: var(--font-lg); width: 1.25rem; text-align: center; }
    .sidebar-footer {
      border-top: 1px solid var(--border);
      padding: 1rem 1.5rem;
      margin-top: auto;
    }
    .user-section {
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }
    .user-info {
      display: flex;
      align-items: center;
      gap: 0.625rem;
      color: var(--text);
    }
    .user-avatar {
      width: 2rem;
      height: 2rem;
      color: var(--primary);
      flex-shrink: 0;
    }
    .user-details {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }
    .user-name {
      font-size: var(--font-sm);
      font-weight: 500;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .user-email {
      font-size: var(--font-xs);
      color: var(--text-muted);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .sign-out-btn {
      width: 100%;
      justify-content: center;
    }
    .dev-indicator {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: var(--font-xs);
      color: var(--text-muted);
      font-weight: 500;
    }
    .dev-dot {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: var(--success, #22c55e);
      display: inline-block;
    }
  `]
})
export class SidebarComponent {
  authService = inject(AuthService);

  isDevMode = !environment.production;

  signOut() {
    this.authService.signOut();
  }
}
