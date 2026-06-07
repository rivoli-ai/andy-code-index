import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { DocsComponent } from './docs.component';

describe('DocsComponent', () => {
  let httpMock: HttpTestingController;

  function setup(params: Record<string, string> = {}): ComponentFixture<DocsComponent> {
    TestBed.configureTestingModule({
      imports: [DocsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap(params)) } },
      ],
    });
    httpMock = TestBed.inject(HttpTestingController);
    return TestBed.createComponent(DocsComponent);
  }

  afterEach(() => httpMock.verify());

  it('should load the page named in the route and render its markdown', () => {
    const fixture = setup({ page: 'search' });
    fixture.detectChanges();

    const req = httpMock.expectOne('docs/search.md');
    expect(req.request.responseType).toBe('text');
    req.flush('# Search\n\nFind things.');

    expect(fixture.componentInstance.renderedHtml).toContain('Search');
    expect(fixture.componentInstance.loading).toBeFalse();
  });

  it('should default to getting-started when no page param is present', () => {
    const fixture = setup();
    fixture.detectChanges();

    httpMock.expectOne('docs/getting-started.md').flush('# Getting Started');
    expect(fixture.componentInstance.renderedHtml).toContain('Getting Started');
  });

  it('should set an error when the page cannot be loaded', () => {
    const fixture = setup({ page: 'missing' });
    fixture.detectChanges();

    httpMock.expectOne('docs/missing.md').flush('nope', { status: 404, statusText: 'Not Found' });
    expect(fixture.componentInstance.error).toContain('missing');
  });
});
