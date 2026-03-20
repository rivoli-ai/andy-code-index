import { TestBed } from '@angular/core/testing';
import { SearchComponent } from './search.component';
import { ApiService } from '../../services/api.service';
import { of } from 'rxjs';

describe('SearchComponent', () => {
  let apiServiceSpy: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    apiServiceSpy = jasmine.createSpyObj('ApiService', ['hybridSearch', 'semanticSearch', 'keywordSearch']);

    await TestBed.configureTestingModule({
      imports: [SearchComponent],
      providers: [{ provide: ApiService, useValue: apiServiceSpy }]
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
    fixture.componentInstance.search();
    expect(apiServiceSpy.hybridSearch).toHaveBeenCalled();
  });

  it('should call semanticSearch for semantic mode', () => {
    apiServiceSpy.semanticSearch.and.returnValue(of({ results: [], totalCount: 0, searchMode: 'semantic', durationMs: 5 }));
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.query = 'test';
    fixture.componentInstance.mode = 'semantic';
    fixture.componentInstance.search();
    expect(apiServiceSpy.semanticSearch).toHaveBeenCalled();
  });

  it('should call keywordSearch for keyword mode', () => {
    apiServiceSpy.keywordSearch.and.returnValue(of({ results: [], totalCount: 0, searchMode: 'keyword', durationMs: 3 }));
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.query = 'test';
    fixture.componentInstance.mode = 'keyword';
    fixture.componentInstance.search();
    expect(apiServiceSpy.keywordSearch).toHaveBeenCalled();
  });

  it('should not search with empty query', () => {
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.query = '  ';
    fixture.componentInstance.search();
    expect(apiServiceSpy.hybridSearch).not.toHaveBeenCalled();
  });

  it('should set results after search', () => {
    const mockResults = { results: [{ enrichmentId: '1', content: 'test', score: 0.9 }], totalCount: 1, searchMode: 'hybrid', durationMs: 5 };
    apiServiceSpy.hybridSearch.and.returnValue(of(mockResults as any));
    const fixture = TestBed.createComponent(SearchComponent);
    fixture.componentInstance.query = 'test';
    fixture.componentInstance.search();
    expect(fixture.componentInstance.results?.totalCount).toBe(1);
  });
});
