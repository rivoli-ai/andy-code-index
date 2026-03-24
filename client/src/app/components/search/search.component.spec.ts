import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SearchComponent } from './search.component';
import { ApiService } from '../../services/api.service';
import { of } from 'rxjs';

describe('SearchComponent', () => {
  let apiServiceSpy: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    apiServiceSpy = jasmine.createSpyObj('ApiService', ['hybridSearch', 'semanticSearch', 'keywordSearch']);

    await TestBed.configureTestingModule({
      imports: [SearchComponent],
      providers: [
        { provide: ApiService, useValue: apiServiceSpy },
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([])
      ]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(SearchComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should default to hybrid mode', () => {
    const fixture = TestBed.createComponent(SearchComponent);
    expect(fixture.componentInstance.mode).toBe('hybrid');
  });

  it('should call hybridSearch for hybrid mode', () => {
    apiServiceSpy.hybridSearch.and.returnValue(of({ results: [], totalCount: 0, searchMode: 'hybrid', durationMs: 10 }));
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.query = 'test';
    fixture.componentInstance.mode = 'hybrid';
    fixture.componentInstance.search(true);
    expect(apiServiceSpy.hybridSearch).toHaveBeenCalled();
  });

  it('should call semanticSearch for semantic mode', () => {
    apiServiceSpy.semanticSearch.and.returnValue(of({ results: [], totalCount: 0, searchMode: 'semantic', durationMs: 5 }));
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.query = 'test';
    fixture.componentInstance.mode = 'semantic';
    fixture.componentInstance.search(true);
    expect(apiServiceSpy.semanticSearch).toHaveBeenCalled();
  });

  it('should call keywordSearch for keyword mode', () => {
    apiServiceSpy.keywordSearch.and.returnValue(of({ results: [], totalCount: 0, searchMode: 'keyword', durationMs: 3 }));
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.query = 'test';
    fixture.componentInstance.mode = 'keyword';
    fixture.componentInstance.search(true);
    expect(apiServiceSpy.keywordSearch).toHaveBeenCalled();
  });

  it('should not search with empty query', () => {
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.query = '  ';
    fixture.componentInstance.search(true);
    expect(apiServiceSpy.hybridSearch).not.toHaveBeenCalled();
  });

  it('should set results after search', () => {
    const mockResults = { results: [{ enrichmentId: '1', content: 'test', score: 0.9 }], totalCount: 1, searchMode: 'hybrid', durationMs: 5 };
    apiServiceSpy.hybridSearch.and.returnValue(of(mockResults as any));
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.query = 'test';
    fixture.componentInstance.search(true);
    expect(fixture.componentInstance.results?.totalCount).toBe(1);
  });

  it('should reset offset on new search', () => {
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.offset = 20;
    apiServiceSpy.hybridSearch.and.returnValue(of({ results: [], totalCount: 0, searchMode: 'hybrid', durationMs: 0 }));
    fixture.componentInstance.query = 'test';
    fixture.componentInstance.search(true);
    expect(fixture.componentInstance.offset).toBe(0);
  });

  it('should calculate page numbers', () => {
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.results = { results: [], totalCount: 35, searchMode: 'hybrid', durationMs: 0 };
    fixture.componentInstance.pageSize = 10;
    fixture.componentInstance.offset = 10;
    expect(fixture.componentInstance.currentPage).toBe(2);
    expect(fixture.componentInstance.totalPages).toBe(4);
  });
});
