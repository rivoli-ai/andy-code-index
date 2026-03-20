import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'repositories', pathMatch: 'full' },
  { path: 'repositories', loadComponent: () => import('./components/repositories/repository-list.component').then(m => m.RepositoryListComponent) },
  { path: 'repositories/add', loadComponent: () => import('./components/repositories/repository-add.component').then(m => m.RepositoryAddComponent) },
  { path: 'repositories/:id', loadComponent: () => import('./components/repositories/repository-detail.component').then(m => m.RepositoryDetailComponent) },
  { path: 'search', loadComponent: () => import('./components/search/search.component').then(m => m.SearchComponent) },
  { path: 'enrichments', loadComponent: () => import('./components/enrichments/enrichment-browser.component').then(m => m.EnrichmentBrowserComponent) },
  { path: 'tasks', loadComponent: () => import('./components/tasks/task-dashboard.component').then(m => m.TaskDashboardComponent) },
];
