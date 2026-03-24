import { Component, OnInit, ViewChild, ElementRef, AfterViewChecked, ViewEncapsulation } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { marked } from 'marked';
import { environment } from '../../../environments/environment';

interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  sources?: ChatSource[];
  showSources?: boolean;
}

interface ChatSource {
  filePath: string;
  repositoryName?: string;
  startLine?: number;
  endLine?: number;
  language?: string;
  content: string;
}

interface Repository {
  id: string;
  name: string;
}

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="chat-container">
      <div class="chat-header">
        <h1 style="font-size:var(--font-2xl);margin:0">Chat with Code</h1>
        <div style="display:flex;gap:0.75rem;align-items:center">
          <select class="form-control" [(ngModel)]="selectedRepo" style="width:180px;padding:0.375rem 0.75rem">
            <option value="">All Repositories</option>
            <option *ngFor="let r of repos" [value]="r.id">{{ r.name }}</option>
          </select>
          <span class="badge badge-muted" *ngIf="!chatAvailable">LLM not configured</span>
        </div>
      </div>

      <div class="chat-messages" #messagesContainer>
        <div *ngIf="messages.length === 0" class="empty-state" style="padding:3rem 3rem 1rem">
          <i class="bi bi-chat-dots" style="font-size:2.5rem;display:block;margin-bottom:1rem;color:var(--primary)"></i>
          <h3>Ask about your codebase</h3>
          <p class="text-muted">Select a category below, or type your own question.</p>
        </div>

        <div class="quick-queries" style="padding:0.75rem 0;border-bottom:1px solid var(--border);margin-bottom:1rem">
          <div class="suggestion-tabs">
            <button *ngFor="let cat of suggestionCategories" class="suggestion-tab"
                    [class.active]="activeCategory === cat.name"
                    (click)="activeCategory = cat.name">
              {{ cat.name }}
            </button>
          </div>
          <div class="suggestions" *ngFor="let cat of suggestionCategories">
            <ng-container *ngIf="activeCategory === cat.name">
              <button class="suggestion" *ngFor="let q of cat.questions" (click)="askSuggestion(q)">{{ q }}</button>
            </ng-container>
          </div>
        </div>

        <div *ngFor="let msg of messages" class="message" [ngClass]="msg.role">
          <div class="message-bubble">
            <div class="message-content" [innerHTML]="formatContent(msg.content)"></div>
            <div *ngIf="msg.sources && msg.sources.length > 0" class="sources-toggle">
              <button class="btn btn-sm btn-secondary" (click)="msg.showSources = !msg.showSources" style="font-size:0.75rem">
                <i class="bi" [ngClass]="msg.showSources ? 'bi-chevron-up' : 'bi-chevron-down'"></i>
                {{ msg.sources.length }} sources
              </button>
              <div *ngIf="msg.showSources" class="sources-list">
                <div *ngFor="let s of msg.sources" class="source-item">
                  <code>{{ s.repositoryName }}/{{ s.filePath }}</code>
                  <span class="text-muted" *ngIf="s.startLine"> :{{ s.startLine }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        <div *ngIf="sending" class="message assistant">
          <div class="message-bubble"><div class="spinner" style="width:1.5rem;height:1.5rem"></div></div>
        </div>
      </div>

      <div class="chat-input">
        <textarea class="form-control" [(ngModel)]="input" placeholder="Ask about your code..."
                  (keydown.enter)="onEnter($event)" rows="1"
                  [disabled]="sending"></textarea>
        <button class="btn btn-primary" (click)="send()" [disabled]="sending || !input.trim()">
          <i class="bi bi-send"></i>
        </button>
      </div>
    </div>
  `,
  styles: [`
    .chat-container { display: flex; flex-direction: column; height: calc(100vh - 4rem); }
    .chat-header { display: flex; justify-content: space-between; align-items: center; padding: 1rem 0; border-bottom: 1px solid var(--border); margin-bottom: 1rem; }
    .chat-messages { flex: 1; overflow-y: auto; padding-bottom: 1rem; }
    .chat-input { display: flex; gap: 0.75rem; padding: 1rem 0; border-top: 1px solid var(--border); }
    .chat-input textarea { resize: none; min-height: 44px; max-height: 120px; }
    .chat-input .btn { align-self: flex-end; padding: 0.625rem 1rem; }
    .message { display: flex; margin-bottom: 1rem; }
    .message.user { justify-content: flex-end; }
    .message.assistant { justify-content: flex-start; }
    .message-bubble { max-width: 80%; padding: 0.75rem 1rem; border-radius: var(--radius-lg); overflow: hidden; }
    .message.user .message-bubble { background: var(--primary); color: white; border-bottom-right-radius: 4px; }
    .message.assistant .message-bubble { background: var(--surface); border: 1px solid var(--border); border-bottom-left-radius: 4px; }
    .message-content { word-wrap: break-word; font-size: var(--font-base); line-height: 1.6; }
    .message-content p { margin: 0 0 0.5rem 0; }
    .message-content p:last-child { margin-bottom: 0; }
    .message-content ul,
    .message-content ol { margin: 0.25rem 0 0.5rem 0; padding: 0; list-style: none; }
    .message-content ol { counter-reset: item; }
    .message-content li { margin-bottom: 0.375rem; padding-left: 1.5rem; position: relative; }
    .message-content ol > li { counter-increment: item; }
    .message-content ol > li::before { content: counter(item) "."; position: absolute; left: 0; color: var(--text-muted); font-weight: 500; }
    .message-content ul > li::before { content: "\\2022"; position: absolute; left: 0.375rem; color: var(--text-muted); font-size: 1.1em; }
    .message-content h1 { font-size: var(--font-xl); font-weight: 600; margin: 0.75rem 0 0.25rem 0; }
    .message-content h2 { font-size: var(--font-lg); font-weight: 600; margin: 0.75rem 0 0.25rem 0; }
    .message-content h3 { font-size: var(--font-md); font-weight: 600; margin: 0.75rem 0 0.25rem 0; }
    .message-content code { background: rgba(0,0,0,0.06); padding: 0.125rem 0.375rem; border-radius: 4px; font-size: 0.9em; }
    .message-content pre { background: #1e1e1e; color: #d4d4d4; padding: 0.75rem; border-radius: 8px; overflow-x: auto; margin: 0.5rem 0; font-size: var(--font-xs); }
    .message-content pre code { background: none; padding: 0; color: inherit; }
    .message-content strong { font-weight: 600; }
    .message-content blockquote { border-left: 3px solid var(--border); margin: 0.5rem 0; padding-left: 0.75rem; color: var(--text-muted); }
    .message.user .message-content code { background: rgba(255,255,255,0.2); }
    .message.user .message-content pre { background: rgba(0,0,0,0.2); color: white; }
    .sources-toggle { margin-top: 0.5rem; }
    .sources-list { margin-top: 0.5rem; }
    .source-item { font-size: var(--font-xs); padding: 0.25rem 0; color: var(--text-muted); }
    .suggestions { display: flex; flex-wrap: wrap; gap: 0.5rem; margin-top: 1rem; justify-content: center; }
    .suggestion { padding: 0.5rem 1rem; border: 1px solid var(--border); border-radius: 100px; background: var(--surface); font-size: var(--font-sm); cursor: pointer; transition: all var(--transition); color: var(--text); }
    .suggestion:hover { border-color: var(--primary); color: var(--primary); }
    .suggestion-tabs { display: flex; flex-wrap: wrap; gap: 0.375rem; margin-bottom: 1rem; justify-content: center; }
    .suggestion-tab { padding: 0.375rem 0.875rem; border: 1px solid var(--border); border-radius: 100px; background: var(--surface); font-size: var(--font-xs); cursor: pointer; transition: all var(--transition); color: var(--text-muted); font-weight: 500; }
    .suggestion-tab:hover { border-color: var(--primary); color: var(--primary); }
    .suggestion-tab.active { background: var(--primary); color: white; border-color: var(--primary); }
  `]
})
export class ChatComponent implements OnInit, AfterViewChecked {
  @ViewChild('messagesContainer') messagesContainer!: ElementRef;

  messages: ChatMessage[] = [];
  input = '';
  sending = false;
  repos: Repository[] = [];
  selectedRepo = '';
  conversationId: string | null = null;
  chatAvailable = false;
  activeCategory = 'Structure';
  suggestionCategories = [
    { name: 'Structure', questions: ['How is this repo organized?', 'What are the main modules?', 'Show me the dependency graph'] },
    { name: 'Patterns', questions: ['What design patterns are used?', 'How is dependency injection configured?', 'What\'s the error handling strategy?'] },
    { name: 'Testing', questions: ['What test frameworks are used?', 'What\'s the test coverage like?', 'Show me example test patterns'] },
    { name: 'Dependencies', questions: ['What external packages are used?', 'What are the key NuGet/npm dependencies?', 'Are there any outdated dependencies?'] },
    { name: 'Architecture', questions: ['Explain the architecture', 'How does data flow through the system?', 'What databases are used?'] },
    { name: 'Migration', questions: ['How would I add a new feature?', 'What would it take to migrate to Python?', 'How to add a new API endpoint?'] },
  ];

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.http.get<Repository[]>(`${environment.apiUrl}/repositories`).subscribe({
      next: r => this.repos = r
    });
    this.http.get<any>(`${environment.apiUrl}/chat/status`).subscribe({
      next: s => this.chatAvailable = s.available
    });
  }

  ngAfterViewChecked() {
    this.scrollToBottom();
  }

  send() {
    if (!this.input.trim() || this.sending) return;

    const message = this.input.trim();
    this.messages.push({ role: 'user', content: message });
    this.input = '';
    this.sending = true;

    const body: any = { message, conversationId: this.conversationId };
    if (this.selectedRepo) body.repositoryId = this.selectedRepo;

    this.http.post<any>(`${environment.apiUrl}/chat`, body).subscribe({
      next: res => {
        this.conversationId = res.conversationId;
        this.messages.push({
          role: 'assistant',
          content: res.reply,
          sources: res.sources,
          showSources: false
        });
        this.sending = false;
      },
      error: () => {
        this.messages.push({ role: 'assistant', content: 'Failed to get a response. Check your LLM configuration in Settings.' });
        this.sending = false;
      }
    });
  }

  askSuggestion(question: string) {
    this.input = question;
    this.send();
  }

  onEnter(event: Event) {
    const ke = event as KeyboardEvent;
    if (!ke.shiftKey) {
      ke.preventDefault();
      this.send();
    }
  }

  formatContent(content: string): string {
    return marked.parse(content, { async: false }) as string;
  }

  private scrollToBottom() {
    try {
      this.messagesContainer.nativeElement.scrollTop = this.messagesContainer.nativeElement.scrollHeight;
    } catch {}
  }
}
