import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';

import { ApiService } from '../../services/api.service';

@Component({
  selector: 'app-repository-add',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="page-header">
      <h1>Add Repository</h1>
    </div>
    <div class="card" style="max-width: 600px">
      <form (ngSubmit)="submit()">
        <div class="form-group">
          <label for="url">Repository URL *</label>
          <input id="url" class="form-control" [(ngModel)]="url" name="url"
            placeholder="https://github.com/owner/repo" required
            (ngModelChange)="onUrlChange()">
        </div>
        <div class="form-group">
          <label for="pat">Personal Access Token (optional)</label>
          <input id="pat" type="password" class="form-control" [(ngModel)]="pat" name="pat"
            placeholder="For private repositories">
        </div>
        @if (duplicateRepoId) {
          <div class="duplicate-warning">
            <i class="bi bi-exclamation-triangle"></i>
            This repository is already being tracked.
            <a [routerLink]="['/repositories', duplicateRepoId]">View existing repository</a>
          </div>
        }
        @if (error) {
          <div class="error-message">{{ error }}</div>
        }
        <div style="display:flex;gap:0.75rem;margin-top:1.5rem">
          <button type="submit" class="btn btn-primary" [disabled]="submitting || !url || !!duplicateRepoId">
            {{ submitting ? 'Adding...' : 'Add Repository' }}
          </button>
          <button type="button" class="btn btn-secondary" (click)="cancel()">Cancel</button>
        </div>
      </form>
    </div>
    `,
  styles: [`
    .error-message { color: var(--danger); margin-top: 0.5rem; padding: 0.75rem; background: rgba(220,53,69,0.1); border-radius: var(--radius); }
    .duplicate-warning { color: #856404; margin-top: 0.5rem; padding: 0.75rem; background: rgba(255,193,7,0.15); border: 1px solid rgba(255,193,7,0.3); border-radius: var(--radius); font-size: var(--font-sm); }
    .duplicate-warning a { color: var(--primary); margin-left: 0.25rem; }
  `]
})
export class RepositoryAddComponent {
  private api = inject(ApiService);
  private router = inject(Router);

  url = '';
  pat = '';
  submitting = false;
  error = '';
  duplicateRepoId: string | null = null;
  private checkTimeout: any;

  onUrlChange() {
    this.duplicateRepoId = null;
    this.error = '';

    clearTimeout(this.checkTimeout);
    if (!this.url || this.url.length < 10) return;

    this.checkTimeout = setTimeout(() => this.checkDuplicate(), 500);
  }

  private checkDuplicate() {
    this.api.checkRepositoryUrl(this.url).subscribe({
      next: result => {
        this.duplicateRepoId = result?.existingRepositoryId || null;
      },
      error: () => { /* ignore check errors */ }
    });
  }

  submit() {
    this.submitting = true;
    this.error = '';
    this.api.createRepository({ url: this.url, personalAccessToken: this.pat || undefined }).subscribe({
      next: repo => this.router.navigate(['/repositories', repo.id]),
      error: err => {
        this.submitting = false;
        if (err.status === 409 && err.error?.existingRepositoryId) {
          this.duplicateRepoId = err.error.existingRepositoryId;
        } else {
          this.error = err.error?.error || 'Failed to add repository';
        }
      }
    });
  }

  cancel() { this.router.navigate(['/repositories']); }
}
