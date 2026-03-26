import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ChatComponent } from './chat.component';

describe('ChatComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ChatComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function flushInitRequests() {
    httpMock.match('/api/v1/repositories').forEach(r => r.flush([]));
    httpMock.match('/api/v1/chat/suggestions').forEach(r => r.flush({
      dimensions: [
        { id: 'structure', label: 'Structure', questions: [{ id: 'q1', text: 'How is this organized?' }, { id: 'q2', text: 'What are the modules?' }] },
        { id: 'data', label: 'Data', questions: [{ id: 'q3', text: 'What database schemas exist?' }] },
      ]
    }));
    httpMock.match('/api/v1/chat/status').forEach(r => r.flush({ available: true }));
  }

  it('should create', () => {
    const fixture = TestBed.createComponent(ChatComponent);
    flushInitRequests();
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should load suggestion categories on init', () => {
    const fixture = TestBed.createComponent(ChatComponent);
    fixture.detectChanges();
    flushInitRequests();
    fixture.detectChanges();

    expect(fixture.componentInstance.allCategories.length).toBe(2);
    expect(fixture.componentInstance.activeCategory).toBe('Structure');
  });

  it('should show questions for selected category', () => {
    const fixture = TestBed.createComponent(ChatComponent);
    fixture.detectChanges();
    flushInitRequests();
    fixture.detectChanges();

    expect(fixture.componentInstance.visibleQuestions).toContain('How is this organized?');
  });

  it('should filter questions by search query', () => {
    const fixture = TestBed.createComponent(ChatComponent);
    fixture.detectChanges();
    flushInitRequests();
    fixture.detectChanges();

    fixture.componentInstance.searchQuery = 'database';
    fixture.componentInstance.filterQuestions();

    expect(fixture.componentInstance.visibleQuestions).toContain('What database schemas exist?');
    expect(fixture.componentInstance.visibleQuestions.length).toBe(1);
  });

  it('should expand search aliases', () => {
    const fixture = TestBed.createComponent(ChatComponent);
    fixture.detectChanges();
    flushInitRequests();
    fixture.detectChanges();

    fixture.componentInstance.searchQuery = 'db';
    fixture.componentInstance.filterQuestions();

    // "db" should expand to match "database"
    expect(fixture.componentInstance.visibleQuestions).toContain('What database schemas exist?');
  });

  it('should switch categories on tile click', () => {
    const fixture = TestBed.createComponent(ChatComponent);
    fixture.detectChanges();
    flushInitRequests();
    fixture.detectChanges();

    fixture.componentInstance.selectCategory('Data');
    expect(fixture.componentInstance.activeCategory).toBe('Data');
    expect(fixture.componentInstance.visibleQuestions).toContain('What database schemas exist?');
  });

  it('should clear search when switching categories', () => {
    const fixture = TestBed.createComponent(ChatComponent);
    fixture.detectChanges();
    flushInitRequests();
    fixture.detectChanges();

    fixture.componentInstance.searchQuery = 'test';
    fixture.componentInstance.selectCategory('Structure');
    expect(fixture.componentInstance.searchQuery).toBe('');
  });

  it('should render two-panel layout', () => {
    const fixture = TestBed.createComponent(ChatComponent);
    fixture.detectChanges();
    flushInitRequests();
    fixture.detectChanges();

    const el = fixture.nativeElement;
    expect(el.querySelector('.chat-layout')).toBeTruthy();
    expect(el.querySelector('.chat-sidebar')).toBeTruthy();
    expect(el.querySelector('.chat-main')).toBeTruthy();
  });
});
