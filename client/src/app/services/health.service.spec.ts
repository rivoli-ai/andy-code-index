import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { HealthService } from './health.service';

describe('HealthService', () => {
  let service: HealthService;
  let httpSpy: jasmine.SpyObj<HttpClient>;

  beforeEach(() => {
    httpSpy = jasmine.createSpyObj('HttpClient', ['get']);
    httpSpy.get.and.returnValue(of('ok'));

    TestBed.configureTestingModule({
      providers: [
        { provide: HttpClient, useValue: httpSpy }
      ]
    });

    service = TestBed.inject(HealthService);
  });

  afterEach(() => {
    service.ngOnDestroy();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should default to connected', () => {
    expect(service.isConnected$.value).toBe(true);
  });

  it('checkHealth should return true on success', (done) => {
    httpSpy.get.and.returnValue(of('ok'));
    service.checkHealth().subscribe(result => {
      expect(result).toBe(true);
      done();
    });
  });

  it('checkHealth should return false on HTTP error', (done) => {
    httpSpy.get.and.returnValue(throwError(() => new Error('Server Error')));
    service.checkHealth().subscribe(result => {
      expect(result).toBe(false);
      done();
    });
  });

  it('checkHealth should return false on network error', (done) => {
    httpSpy.get.and.returnValue(throwError(() => new ProgressEvent('error')));
    service.checkHealth().subscribe(result => {
      expect(result).toBe(false);
      done();
    });
  });

  it('checkHealth should call the correct URL', (done) => {
    httpSpy.get.calls.reset();
    httpSpy.get.and.returnValue(of('ok'));
    service.checkHealth().subscribe(() => {
      expect(httpSpy.get).toHaveBeenCalled();
      const url = httpSpy.get.calls.mostRecent().args[0];
      expect(url).toBe('/api/v1/health');
      done();
    });
  });
});
