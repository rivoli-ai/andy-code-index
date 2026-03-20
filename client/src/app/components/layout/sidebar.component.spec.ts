import { TestBed } from '@angular/core/testing';
import { SidebarComponent } from './sidebar.component';
import { provideRouter } from '@angular/router';

describe('SidebarComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SidebarComponent],
      providers: [provideRouter([])]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render brand name', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.brand-text')?.textContent).toContain('CodeIndex');
  });

  it('should have navigation links', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    const links = fixture.nativeElement.querySelectorAll('.nav-item');
    expect(links.length).toBeGreaterThanOrEqual(4);
  });

  it('should have Repositories link', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Repositories');
  });

  it('should have Search link', () => {
    const fixture = TestBed.createComponent(SidebarComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Search');
  });
});
