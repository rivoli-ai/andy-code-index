import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

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
        </div>
      </nav>
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
      font-size: 1.125rem; font-weight: 700; color: var(--primary);
      border-bottom: 1px solid var(--border);
    }
    .sidebar-brand i { font-size: 1.5rem; }
    .sidebar-nav { padding: 1rem 0; flex: 1; }
    .nav-section { margin-bottom: 1.5rem; }
    .nav-section-title {
      padding: 0 1.5rem; margin-bottom: 0.5rem;
      font-size: 0.6875rem; font-weight: 600; text-transform: uppercase;
      letter-spacing: 0.08em; color: var(--text-light);
    }
    .nav-item {
      display: flex; align-items: center; gap: 0.75rem;
      padding: 0.625rem 1.5rem; color: var(--text-muted);
      font-size: 0.875rem; font-weight: 500; transition: all var(--transition);
    }
    .nav-item:hover { color: var(--text); background: var(--background-alt); }
    .nav-item.active { color: var(--primary); background: rgba(0, 102, 204, 0.08); }
    .nav-item i { font-size: 1.125rem; width: 1.25rem; text-align: center; }
  `]
})
export class SidebarComponent {}
