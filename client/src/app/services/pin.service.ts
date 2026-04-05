import { Injectable } from '@angular/core';

const STORAGE_KEY = 'codeindex_pinned_repos';

@Injectable({ providedIn: 'root' })
export class PinService {
  getPinnedIds(): string[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? JSON.parse(raw) : [];
    } catch {
      return [];
    }
  }

  isPinned(repoId: string): boolean {
    return this.getPinnedIds().includes(repoId);
  }

  pin(repoId: string): void {
    const ids = this.getPinnedIds();
    if (!ids.includes(repoId)) {
      ids.push(repoId);
      localStorage.setItem(STORAGE_KEY, JSON.stringify(ids));
    }
  }

  unpin(repoId: string): void {
    const ids = this.getPinnedIds().filter(id => id !== repoId);
    localStorage.setItem(STORAGE_KEY, JSON.stringify(ids));
  }

  toggle(repoId: string): boolean {
    if (this.isPinned(repoId)) {
      this.unpin(repoId);
      return false;
    } else {
      this.pin(repoId);
      return true;
    }
  }
}
