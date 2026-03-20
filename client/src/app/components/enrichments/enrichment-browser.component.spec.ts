import { TestBed } from '@angular/core/testing';
import { EnrichmentBrowserComponent } from './enrichment-browser.component';
import { ApiService } from '../../services/api.service';
import { of } from 'rxjs';

describe('EnrichmentBrowserComponent', () => {
  let apiServiceSpy: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    apiServiceSpy = jasmine.createSpyObj('ApiService', ['getEnrichments']);
    apiServiceSpy.getEnrichments.and.returnValue(of({ results: [], totalCount: 0, offset: 0, limit: 20 }));

    await TestBed.configureTestingModule({
      imports: [EnrichmentBrowserComponent],
      providers: [{ provide: ApiService, useValue: apiServiceSpy }]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(EnrichmentBrowserComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should load enrichments on init', () => {
    const fixture = TestBed.createComponent(EnrichmentBrowserComponent);
    fixture.detectChanges();
    expect(apiServiceSpy.getEnrichments).toHaveBeenCalled();
  });

  it('should show empty state when no enrichments', () => {
    const fixture = TestBed.createComponent(EnrichmentBrowserComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No enrichments found');
  });

  it('should display enrichments', () => {
    apiServiceSpy.getEnrichments.and.returnValue(of({
      results: [{ id: '1', repositoryId: 'r1', type: 'Development', subtype: 'Chunk', content: 'test code', createdAt: '' }],
      totalCount: 1, offset: 0, limit: 20
    }));
    const fixture = TestBed.createComponent(EnrichmentBrowserComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Development');
  });

  it('should toggle expand', () => {
    const fixture = TestBed.createComponent(EnrichmentBrowserComponent);
    fixture.componentInstance.toggleExpand('123');
    expect(fixture.componentInstance.expandedId).toBe('123');
    fixture.componentInstance.toggleExpand('123');
    expect(fixture.componentInstance.expandedId).toBeNull();
  });

  it('should reload on filter change', () => {
    const fixture = TestBed.createComponent(EnrichmentBrowserComponent);
    fixture.detectChanges();
    apiServiceSpy.getEnrichments.calls.reset();
    fixture.componentInstance.typeFilter = 'Usage';
    fixture.componentInstance.loadEnrichments();
    expect(apiServiceSpy.getEnrichments).toHaveBeenCalled();
  });
});
