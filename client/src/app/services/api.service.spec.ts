import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ApiService } from './api.service';

describe('ApiService', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ApiService,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('should get repositories', () => {
    service.getRepositories().subscribe(repos => {
      expect(repos.length).toBe(1);
    });
    const req = httpMock.expectOne('/api/v1/repositories');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: '1', name: 'test' }]);
  });

  it('should create repository', () => {
    service.createRepository({ url: 'https://github.com/t/r' }).subscribe(repo => {
      expect(repo.name).toBe('r');
    });
    const req = httpMock.expectOne('/api/v1/repositories');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.url).toBe('https://github.com/t/r');
    req.flush({ id: '1', name: 'r' });
  });

  it('should delete repository', () => {
    service.deleteRepository('123').subscribe();
    const req = httpMock.expectOne('/api/v1/repositories/123');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should sync repository', () => {
    service.syncRepository('123').subscribe();
    const req = httpMock.expectOne('/api/v1/repositories/123/sync');
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('should perform hybrid search', () => {
    service.hybridSearch({ query: 'test' }).subscribe();
    const req = httpMock.expectOne('/api/v1/search');
    expect(req.request.method).toBe('POST');
    expect(req.request.body.query).toBe('test');
    req.flush({ results: [], totalCount: 0 });
  });

  it('should perform semantic search', () => {
    service.semanticSearch('hello', 'csharp').subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/v1/search/semantic');
    expect(req.request.params.get('query')).toBe('hello');
    expect(req.request.params.get('language')).toBe('csharp');
    req.flush({ results: [] });
  });

  it('should perform keyword search', () => {
    service.keywordSearch('hello').subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/v1/search/keyword');
    expect(req.request.params.get('keywords')).toBe('hello');
    req.flush({ results: [] });
  });

  it('should get enrichments with params', () => {
    service.getEnrichments({ type: 'Development', limit: 10 }).subscribe();
    const req = httpMock.expectOne(r => r.url === '/api/v1/enrichments');
    expect(req.request.params.get('type')).toBe('Development');
    expect(req.request.params.get('limit')).toBe('10');
    req.flush({ results: [], totalCount: 0 });
  });

  it('should get tasks', () => {
    service.getTasks().subscribe();
    const req = httpMock.expectOne('/api/v1/queue');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });
});
