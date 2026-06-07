import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { SettingsComponent } from './settings.component';
import { environment } from '../../../environments/environment';

describe('SettingsComponent', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<SettingsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SettingsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SettingsComponent);
  });

  afterEach(() => httpMock.verify());

  it('should load settings, history, and health on init', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiUrl}/settings`).flush({
      embedding: { model: 'text-embedding-3-small', baseUrl: 'https://api' },
      llm: { model: 'gpt-4', baseUrl: 'https://llm' },
    });
    httpMock.expectOne(`${environment.apiUrl}/settings/history`).flush([{ id: 1 }]);
    httpMock.expectOne(`${environment.apiUrl}/settings/health`).flush({ ok: true });

    const c = fixture.componentInstance;
    expect(c.embeddingModel).toBe('text-embedding-3-small');
    expect(c.llmModel).toBe('gpt-4');
    expect(c.history.length).toBe(1);
    expect(c.health).toEqual({ ok: true });
  });

  it('should POST a connection test and store the result', () => {
    // No detectChanges → ngOnInit (and its three GETs) does not run.
    fixture.componentInstance.testConnection('embedding');

    const req = httpMock.expectOne(`${environment.apiUrl}/settings/test-connection`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ type: 'embedding' });
    req.flush({ success: true });

    expect(fixture.componentInstance.embedTestResult).toEqual({ success: true });
    expect(fixture.componentInstance.testingEmbed).toBeFalse();
  });
});
