import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { environment } from '../../environments/environment';

export const authGuard: CanActivateFn = () => {
  // Skip auth in dev mode when no authority configured
  if (!environment.authorityUrl) return true;

  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) return true;

  router.navigate(['/login']);
  return false;
};
