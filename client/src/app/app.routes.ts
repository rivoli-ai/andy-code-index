import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  { path: 'dashboard', loadComponent: () => import('./components/dashboard/dashboard.component').then(m => m.DashboardComponent), canActivate: [authGuard] },
  { path: 'login', loadComponent: () => import('./components/auth/login.component').then(m => m.LoginComponent) },
  { path: 'callback', loadComponent: () => import('./components/auth/callback.component').then(m => m.CallbackComponent) },
  { path: 'repositories', loadComponent: () => import('./components/repositories/repository-list.component').then(m => m.RepositoryListComponent), canActivate: [authGuard] },
  { path: 'repositories/add', loadComponent: () => import('./components/repositories/repository-add.component').then(m => m.RepositoryAddComponent), canActivate: [authGuard] },
  { path: 'repositories/:id', loadComponent: () => import('./components/repositories/repository-detail.component').then(m => m.RepositoryDetailComponent), canActivate: [authGuard] },
  { path: 'search', loadComponent: () => import('./components/search/search.component').then(m => m.SearchComponent), canActivate: [authGuard] },
  { path: 'enrichments', loadComponent: () => import('./components/enrichments/enrichment-browser.component').then(m => m.EnrichmentBrowserComponent), canActivate: [authGuard] },
  { path: 'tasks', loadComponent: () => import('./components/tasks/task-dashboard.component').then(m => m.TaskDashboardComponent), canActivate: [authGuard] },
  { path: 'discover', loadComponent: () => import('./components/discovery/discovery.component').then(m => m.DiscoveryComponent), canActivate: [authGuard] },
  { path: 'settings', loadComponent: () => import('./components/settings/settings.component').then(m => m.SettingsComponent), canActivate: [authGuard] },
  { path: 'chat', loadComponent: () => import('./components/chat/chat.component').then(m => m.ChatComponent), canActivate: [authGuard] },
  { path: 'docs', loadComponent: () => import('./components/docs/docs.component').then(m => m.DocsComponent) },
  { path: 'docs/:page', loadComponent: () => import('./components/docs/docs.component').then(m => m.DocsComponent) },
];
