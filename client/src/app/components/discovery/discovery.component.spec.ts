import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { DiscoveryComponent } from './discovery.component';

describe('DiscoveryComponent', () => {
  let httpMock: HttpTestingController;
  let fixture: ComponentFixture<DiscoveryComponent>;
  let component: DiscoveryComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DiscoveryComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DiscoveryComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => httpMock.verify());

  it('should count selected (excluding already-tracked) and tracked repos', () => {
    component.repos = [
      { selected: true, alreadyTracked: false } as any,
      { selected: true, alreadyTracked: true } as any,
      { selected: false, alreadyTracked: false } as any,
    ];
    expect(component.selectedCount).toBe(1);
    expect(component.trackedCount).toBe(1);
  });

  it('should populate repos on discover()', () => {
    component.org = 'acme';
    component.discover();

    const req = httpMock.expectOne(r => r.url.includes('/discover/github'));
    expect(req.request.url).toContain('org=acme');
    req.flush([{ name: 'r1', cloneUrl: 'u1', selected: false, alreadyTracked: false }]);

    expect(component.repos.length).toBe(1);
    expect(component.searched).toBeTrue();
    expect(component.discovering).toBeFalse();
  });

  it('should set an error message when discovery fails', () => {
    component.discover();
    httpMock.expectOne(r => r.url.includes('/discover/'))
      .flush({ message: 'nope' }, { status: 500, statusText: 'Server Error' });

    expect(component.error).toContain('Discovery failed');
    expect(component.discovering).toBeFalse();
  });

  it('should POST the selected repos on addSelected()', () => {
    component.repos = [{ selected: true, alreadyTracked: false, cloneUrl: 'u1' } as any];
    component.addSelected();

    const req = httpMock.expectOne(r => r.url.endsWith('/discover/sync'));
    expect(req.request.method).toBe('POST');
    expect(req.request.body.repositoryUrls).toEqual(['u1']);
    req.flush({ added: ['u1'], skipped: [] });

    expect(component.addMessage).toContain('Added 1');
    expect(component.adding).toBeFalse();
  });
});
