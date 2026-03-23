import { Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly storageKey = 'code_index_token';
  private readonly authorityUrl = environment.authorityUrl;
  private readonly clientId = environment.clientId;
  private readonly redirectUri = environment.redirectUri;

  constructor(private router: Router) {}

  isAuthenticated(): boolean {
    const token = this.getToken();
    if (!token) return false;
    // Basic JWT expiry check
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return payload.exp * 1000 > Date.now();
    } catch {
      return false;
    }
  }

  getToken(): string | null {
    return sessionStorage.getItem(this.storageKey);
  }

  login(): void {
    if (!this.authorityUrl) return; // Dev mode — no auth

    const state = this.generateRandomString(32);
    const codeVerifier = this.generateRandomString(64);
    sessionStorage.setItem('oauth_state', state);
    sessionStorage.setItem('oauth_code_verifier', codeVerifier);

    this.generateCodeChallenge(codeVerifier).then(codeChallenge => {
      const params = new URLSearchParams({
        response_type: 'code',
        client_id: this.clientId,
        redirect_uri: this.redirectUri,
        scope: 'openid profile email',
        state,
        code_challenge: codeChallenge,
        code_challenge_method: 'S256',
      });
      window.location.href = `${this.authorityUrl}/connect/authorize?${params}`;
    });
  }

  async handleCallback(code: string, state: string): Promise<boolean> {
    const savedState = sessionStorage.getItem('oauth_state');
    if (state !== savedState) return false;

    const codeVerifier = sessionStorage.getItem('oauth_code_verifier');
    if (!codeVerifier) return false;

    try {
      const response = await fetch(`${this.authorityUrl}/connect/token`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: new URLSearchParams({
          grant_type: 'authorization_code',
          client_id: this.clientId,
          code,
          redirect_uri: this.redirectUri,
          code_verifier: codeVerifier,
        }),
      });

      if (!response.ok) return false;

      const data = await response.json();
      sessionStorage.setItem(this.storageKey, data.access_token);
      if (data.refresh_token) {
        sessionStorage.setItem('code_index_refresh', data.refresh_token);
      }

      sessionStorage.removeItem('oauth_state');
      sessionStorage.removeItem('oauth_code_verifier');
      return true;
    } catch {
      return false;
    }
  }

  logout(): void {
    sessionStorage.removeItem(this.storageKey);
    sessionStorage.removeItem('code_index_refresh');
    this.router.navigate(['/login']);
  }

  private generateRandomString(length: number): string {
    const array = new Uint8Array(length);
    crypto.getRandomValues(array);
    return Array.from(array, b => b.toString(16).padStart(2, '0')).join('').slice(0, length);
  }

  private async generateCodeChallenge(verifier: string): Promise<string> {
    const encoder = new TextEncoder();
    const data = encoder.encode(verifier);
    const digest = await crypto.subtle.digest('SHA-256', data);
    return btoa(String.fromCharCode(...new Uint8Array(digest)))
      .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }
}
