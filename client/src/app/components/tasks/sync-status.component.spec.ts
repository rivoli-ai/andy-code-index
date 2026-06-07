import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { SyncStatusComponent } from './sync-status.component';
import { environment } from '../../../environments/environment';

describe('SyncStatusComponent', () => {
  let httpMock: HttpTestingController;
  const url = `${environment.apiUrl}/sync/status`;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SyncStatusComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create', () => {
    const fixture = TestBed.createComponent(SyncStatusComponent);
    fixture.detectChanges();
    httpMock.expectOne(url).flush({ enabled: false, intervalSeconds: 0, repositoriesTracked: 0 });
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render nothing before the status loads', () => {
    const fixture = TestBed.createComponent(SyncStatusComponent);
    // ngOnInit runs on first detectChanges; status is still null.
    expect((fixture.nativeElement as HTMLElement).querySelector('.card')).toBeNull();
    fixture.detectChanges();
    httpMock.expectOne(url).flush({ enabled: false, intervalSeconds: 0, repositoriesTracked: 0 });
  });

  it('should load and render the sync status on init', () => {
    const fixture = TestBed.createComponent(SyncStatusComponent);
    fixture.detectChanges();
    httpMock.expectOne(url).flush({
      enabled: true,
      intervalSeconds: 600,
      repositoriesTracked: 3,
    });
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Periodic Sync');
    expect(text).toContain('Enabled');
    expect(text).toContain('3 repositories tracked');
  });
});
