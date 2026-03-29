import { Injectable, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Subscription, timer, of } from 'rxjs';
import { catchError, timeout, map, switchMap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

@Injectable({ providedIn: 'root' })
export class HealthService implements OnDestroy {
  private readonly POLL_INTERVAL = 30_000;
  private readonly TIMEOUT = 5_000;
  private readonly healthUrl = '/health';

  isConnected$ = new BehaviorSubject<boolean>(true);
  private pollSubscription?: Subscription;

  constructor(private http: HttpClient) {
    this.startPolling();
  }

  startPolling(): void {
    this.pollSubscription?.unsubscribe();
    this.pollSubscription = timer(0, this.POLL_INTERVAL)
      .pipe(switchMap(() => this.checkHealth()))
      .subscribe(connected => this.isConnected$.next(connected));
  }

  checkHealth() {
    return this.http.get(this.healthUrl, { responseType: 'text' }).pipe(
      timeout(this.TIMEOUT),
      map(() => true),
      catchError(() => of(false))
    );
  }

  ngOnDestroy(): void {
    this.pollSubscription?.unsubscribe();
  }
}
