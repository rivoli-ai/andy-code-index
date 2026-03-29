import { Component, OnInit, ViewChild, ElementRef, AfterViewChecked, ViewEncapsulation } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { marked } from 'marked';
import { environment } from '../../../environments/environment';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged } from 'rxjs/operators';

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

interface SuggestionDimension {
  id: string;
  name: string;
  questions: string[];
}

interface Conversation {
  id: string;
  title: string;
  updatedAt: string;
  messageCount: number;
  isPinned: boolean;
  pinnedAt: string | null;
}

interface ConversationGroup {
  label: string;
  conversations: Conversation[];
}

@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [CommonModule, FormsModule],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="chat-layout">
      <!-- Left Panel: Quick Questions -->
      <aside class="chat-sidebar">
        <div class="sidebar-section">
          <h3 class="sidebar-title">Quick Questions</h3>
          <input class="form-control sidebar-search" [(ngModel)]="searchQuery"
                 placeholder="Search questions..." (input)="filterQuestions()">
        </div>

        <div class="sidebar-section">
          <div class="category-grid">
            <button *ngFor="let cat of allCategories" class="category-tile"
                    [class.active]="activeCategory === cat.name"
                    (click)="selectCategory(cat.name)">
              <span class="category-name">{{ cat.name }}</span>
              <span class="category-count">{{ cat.questions.length }}</span>
            </button>
          </div>
        </div>

        <div class="sidebar-section question-list">
          <button *ngFor="let q of visibleQuestions" class="question-item" (click)="askSuggestion(q)">
            {{ q }}
          </button>
          <div *ngIf="visibleQuestions.length === 0" class="text-muted" style="padding:0.5rem;font-size:var(--font-xs)">
            No matching questions.
          </div>
        </div>

        <div class="sidebar-section conversations-section">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:0.5rem">
            <h3 class="sidebar-title" style="margin:0">Conversations</h3>
            <button class="btn-icon" (click)="newChat()" title="New Chat">
              <i class="bi bi-plus-lg"></i>
            </button>
          </div>
          <input class="form-control sidebar-search" [(ngModel)]="convSearchQuery"
                 placeholder="Search conversations..." (input)="onConvSearchInput()"
                 style="margin-bottom:0.5rem">
          <div class="conversation-list">
            <ng-container *ngFor="let group of groupedConversations">
              <div class="conv-group-header" *ngIf="group.conversations.length > 0">{{ group.label }}</div>
              <div *ngFor="let conv of group.conversations" class="conversation-item"
                   [class.active]="conversationId === conv.id"
                   (click)="resumeConversation(conv.id)">
                <div class="conv-title-row">
                  <div class="conv-title" *ngIf="editingConvId !== conv.id"
                       (dblclick)="startRename(conv, $event)">{{ conv.title }}</div>
                  <input *ngIf="editingConvId === conv.id" class="conv-title-input"
                         [(ngModel)]="editingTitle"
                         (keydown.enter)="saveRename(conv)"
                         (keydown.escape)="cancelRename()"
                         (blur)="saveRename(conv)"
                         (click)="$event.stopPropagation()">
                  <button class="btn-icon-sm" (click)="togglePin(conv, $event)"
                          [title]="conv.isPinned ? 'Unpin' : 'Pin'">
                    <i class="bi" [ngClass]="conv.isPinned ? 'bi-pin-fill' : 'bi-pin'"></i>
                  </button>
                </div>
                <div class="conv-meta">
                  <span>{{ formatTimeAgo(conv.updatedAt) }}</span>
                  <button class="btn-icon-sm" (click)="deleteConversation(conv.id, $event)" title="Delete">
                    <i class="bi bi-trash3"></i>
                  </button>
                </div>
              </div>
            </ng-container>
            <div *ngIf="conversations.length === 0" class="text-muted" style="font-size:var(--font-xs);padding:0.25rem">
              No conversations yet.
            </div>
          </div>
        </div>
      </aside>

      <!-- Right Panel: Chat -->
      <main class="chat-main">
        <div class="chat-header">
          <h1>Chat with Code</h1>
          <div style="display:flex;gap:0.75rem;align-items:center">
            <select class="form-control" [(ngModel)]="selectedRepo" style="width:180px">
              <option value="">All Repositories</option>
              <option *ngFor="let r of repos" [value]="r.id">{{ r.name }}</option>
            </select>
            <span class="badge badge-muted" *ngIf="!chatAvailable">LLM not configured</span>
          </div>
        </div>

        <div class="chat-messages" #messagesContainer>
          <div *ngIf="messages.length === 0" class="empty-chat">
            <i class="bi bi-chat-dots"></i>
            <h3>Ask about your codebase</h3>
            <p class="text-muted">Select a question from the left panel, or type your own below.</p>
          </div>

          <div *ngFor="let msg of messages" class="message" [ngClass]="msg.role">
            <div class="message-bubble">
              <div class="message-content" [innerHTML]="formatContent(msg.content)"></div>
              <div *ngIf="msg.sources && msg.sources.length > 0" class="sources-toggle">
                <button class="btn btn-sm btn-secondary" (click)="msg.showSources = !msg.showSources" style="font-size:var(--font-xs)">
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
      </main>
    </div>
  `,
  styles: [`
    /* --- Two-panel layout --- */
    .chat-layout { display: flex; height: calc(100vh - 4rem); gap: 0; }

    /* --- Left sidebar --- */
    .chat-sidebar {
      width: 300px; min-width: 300px;
      border-right: 1px solid var(--border);
      display: flex; flex-direction: column;
      overflow-y: auto;
      background: var(--background-alt);
    }
    .sidebar-section { padding: 0.75rem 1rem; border-bottom: 1px solid var(--border); }
    .sidebar-title { font-size: var(--font-base); font-weight: 600; margin: 0 0 0.5rem 0; }
    .sidebar-search { font-size: var(--font-xs); padding: 0.375rem 0.625rem; }

    .category-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0.375rem; padding: 0; }
    .category-tile {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.375rem 0.5rem; border: 1px solid var(--border); border-radius: var(--radius);
      background: var(--surface); font-size: var(--font-xs); cursor: pointer;
      transition: all var(--transition); color: var(--text); text-align: left;
      min-height: 2rem;
    }
    .category-tile:hover { border-color: var(--primary); color: var(--primary); background: rgba(0,102,204,0.04); }
    .category-tile.active { background: var(--primary); color: white; border-color: var(--primary); }
    .category-name { font-weight: 500; line-height: 1.2; }
    .category-count { font-size: var(--font-xs); opacity: 0.7; flex-shrink: 0; margin-left: 0.25rem; }

    .question-list { flex: 1; overflow-y: auto; padding: 0.5rem 0; }
    .question-item {
      display: block; width: 100%; text-align: left;
      padding: 0.5rem 1rem; margin-bottom: 0.25rem;
      border: 1px solid transparent; border-radius: var(--radius); background: none;
      font-size: var(--font-sm); color: var(--text); cursor: pointer;
      transition: all var(--transition); line-height: 1.4;
    }
    .question-item:hover {
      background: var(--surface); color: var(--primary);
      border-color: var(--primary-light);
    }

    .conversations-section { margin-top: auto; border-top: 1px solid var(--border); border-bottom: none; max-height: 40%; overflow-y: auto; }
    .conversation-list { display: flex; flex-direction: column; gap: 0.125rem; padding: 0.25rem 0; }
    .conversation-item {
      padding: 0.5rem 1rem; border-radius: var(--radius); cursor: pointer;
      transition: all var(--transition); border: 1px solid transparent;
    }
    .conversation-item:hover { background: var(--surface); border-color: var(--border); }
    .conversation-item.active { background: rgba(0,102,204,0.06); border-color: var(--primary-light); }
    .conv-title-row { display: flex; align-items: center; gap: 0.25rem; }
    .conv-title { font-size: var(--font-xs); font-weight: 500; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; flex: 1; min-width: 0; }
    .conv-title-input {
      font-size: var(--font-xs); font-weight: 500; flex: 1; min-width: 0;
      border: 1px solid var(--primary); border-radius: var(--radius);
      padding: 0.125rem 0.375rem; background: var(--surface); color: var(--text);
      outline: none;
    }
    .conv-meta { display: flex; justify-content: space-between; align-items: center; font-size: var(--font-xs); color: var(--text-muted); margin-top: 0.125rem; }
    .conv-group-header {
      font-size: var(--font-xs); font-weight: 600; color: var(--text-muted);
      padding: 0.375rem 1rem 0.125rem; text-transform: uppercase; letter-spacing: 0.03em;
    }
    .btn-icon {
      background: none; border: 1px solid var(--border); border-radius: var(--radius);
      cursor: pointer; padding: 0.25rem 0.5rem; color: var(--text-muted);
      transition: all var(--transition); font-size: var(--font-xs);
    }
    .btn-icon:hover { color: var(--primary); border-color: var(--primary); }
    .btn-icon-sm {
      background: none; border: none; cursor: pointer; padding: 0.125rem;
      color: var(--text-light); transition: all var(--transition); font-size: var(--font-xs);
    }
    .btn-icon-sm:hover { color: var(--danger); }
    .btn-icon-sm .bi-pin-fill { color: var(--primary); }
    .btn-icon-sm .bi-pin:hover { color: var(--primary); }

    /* --- Right chat area --- */
    .chat-main { flex: 1; display: flex; flex-direction: column; min-width: 0; }
    .chat-header {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.75rem 1.5rem; border-bottom: 1px solid var(--border);
    }
    .chat-header h1 { font-size: var(--font-xl); margin: 0; }
    .chat-messages { flex: 1; overflow-y: auto; padding: 1.5rem; }
    .chat-input { display: flex; gap: 0.75rem; padding: 0.75rem 1.5rem; border-top: 1px solid var(--border); }
    .chat-input textarea { resize: none; min-height: 44px; max-height: 120px; flex: 1; }
    .chat-input .btn { align-self: flex-end; padding: 0.625rem 1rem; }

    .empty-chat { text-align: center; padding: 4rem 2rem; color: var(--text-muted); }
    .empty-chat i { font-size: 3rem; display: block; margin-bottom: 1rem; color: var(--primary); }
    .empty-chat h3 { margin-bottom: 0.5rem; color: var(--text); }

    /* --- Messages --- */
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

    /* --- Responsive --- */
    @media (max-width: 768px) {
      .chat-layout { flex-direction: column; }
      .chat-sidebar { width: 100%; min-width: 100%; max-height: 40vh; border-right: none; border-bottom: 1px solid var(--border); }
      .question-list { max-height: 120px; }
    }
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
  activeCategory = '';
  searchQuery = '';
  convSearchQuery = '';

  // Inline rename state
  editingConvId: string | null = null;
  editingTitle = '';

  allCategories: SuggestionDimension[] = [];
  visibleQuestions: string[] = [];
  conversations: Conversation[] = [];
  groupedConversations: ConversationGroup[] = [];

  private convSearchSubject = new Subject<string>();

  private searchAliases: Record<string, string[]> = {
    'db': ['database', 'schema', 'table', 'migration', 'entity'],
    'auth': ['authentication', 'authorization', 'login', 'sign in', 'oauth', 'jwt'],
    'deps': ['dependency', 'dependencies', 'package', 'nuget', 'npm'],
    'ci': ['ci/cd', 'pipeline', 'github actions', 'jenkins', 'deploy'],
    'k8s': ['kubernetes', 'container', 'docker', 'helm'],
    'api': ['endpoint', 'route', 'controller', 'rest', 'swagger'],
    'test': ['testing', 'unit test', 'integration test', 'coverage', 'spec'],
    'perf': ['performance', 'latency', 'throughput', 'benchmark'],
    'config': ['configuration', 'settings', 'environment', 'env'],
    'repo': ['repository', 'codebase', 'project'],
    'infra': ['infrastructure', 'terraform', 'cloud', 'deployment'],
    'sec': ['security', 'secrets', 'encryption', 'vulnerability'],
    'ops': ['operations', 'monitoring', 'alerting', 'logging'],
    'doc': ['documentation', 'readme', 'wiki', 'guide'],
  };

  constructor(private http: HttpClient) {}

  ngOnInit() {
    this.http.get<Repository[]>(`${environment.apiUrl}/repositories`).subscribe({
      next: r => this.repos = r
    });
    this.http.get<any>(`${environment.apiUrl}/chat/suggestions`).subscribe({
      next: res => {
        this.allCategories = (res.dimensions || []).map((d: any) => ({
          id: d.id,
          name: d.label,
          questions: d.questions.map((q: any) => q.text)
        }));
        if (this.allCategories.length > 0) {
          this.activeCategory = this.allCategories[0].name;
          this.filterQuestions();
        }
      }
    });
    this.http.get<any>(`${environment.apiUrl}/chat/status`).subscribe({
      next: s => this.chatAvailable = s.available
    });
    this.loadConversations();

    // Debounced conversation search
    this.convSearchSubject.pipe(
      debounceTime(300),
      distinctUntilChanged()
    ).subscribe(term => {
      this.loadConversations(term || undefined);
    });
  }

  ngAfterViewChecked() {
    this.scrollToBottom();
  }

  selectCategory(name: string) {
    this.activeCategory = name;
    this.searchQuery = '';
    this.filterQuestions();
  }

  filterQuestions() {
    if (this.searchQuery.trim()) {
      const q = this.searchQuery.toLowerCase().trim();
      // Expand query with aliases
      const expandedTerms = [q];
      for (const [alias, expansions] of Object.entries(this.searchAliases)) {
        if (q === alias || q.includes(alias)) {
          expandedTerms.push(...expansions);
        }
        // Also reverse: if query matches an expansion, include the alias targets
        if (expansions.some(e => e.includes(q) || q.includes(e))) {
          expandedTerms.push(...expansions);
        }
      }
      const uniqueTerms = [...new Set(expandedTerms)];

      this.visibleQuestions = this.allCategories
        .flatMap(c => c.questions)
        .filter(question => {
          const lower = question.toLowerCase();
          return uniqueTerms.some(term => lower.includes(term));
        });
    } else {
      const cat = this.allCategories.find(c => c.name === this.activeCategory);
      this.visibleQuestions = cat?.questions || [];
    }
  }

  onConvSearchInput() {
    this.convSearchSubject.next(this.convSearchQuery);
  }

  loadConversations(search?: string) {
    let url = `${environment.apiUrl}/chat/conversations?limit=50`;
    if (search) {
      url += `&search=${encodeURIComponent(search)}`;
    }
    this.http.get<any>(url).subscribe({
      next: res => {
        this.conversations = (res.conversations || []).map((c: any) => ({
          ...c,
          isPinned: c.isPinned || false,
          pinnedAt: c.pinnedAt || null
        }));
        this.groupedConversations = this.groupConversations(this.conversations);
      }
    });
  }

  groupConversations(conversations: Conversation[]): ConversationGroup[] {
    const now = new Date();
    const startOfToday = new Date(now.getFullYear(), now.getMonth(), now.getDate());
    const startOfYesterday = new Date(startOfToday.getTime() - 86400000);
    const startOf7Days = new Date(startOfToday.getTime() - 7 * 86400000);
    const startOf30Days = new Date(startOfToday.getTime() - 30 * 86400000);

    const groups: ConversationGroup[] = [
      { label: 'Pinned', conversations: [] },
      { label: 'Today', conversations: [] },
      { label: 'Yesterday', conversations: [] },
      { label: 'Previous 7 Days', conversations: [] },
      { label: 'Previous 30 Days', conversations: [] },
      { label: 'Older', conversations: [] },
    ];

    for (const conv of conversations) {
      if (conv.isPinned) {
        groups[0].conversations.push(conv);
        continue;
      }

      const date = new Date(conv.updatedAt);
      if (date >= startOfToday) {
        groups[1].conversations.push(conv);
      } else if (date >= startOfYesterday) {
        groups[2].conversations.push(conv);
      } else if (date >= startOf7Days) {
        groups[3].conversations.push(conv);
      } else if (date >= startOf30Days) {
        groups[4].conversations.push(conv);
      } else {
        groups[5].conversations.push(conv);
      }
    }

    return groups;
  }

  newChat() {
    this.messages = [];
    this.conversationId = null;
    this.input = '';
  }

  resumeConversation(id: string) {
    this.http.get<any>(`${environment.apiUrl}/chat/conversations/${id}`).subscribe({
      next: res => {
        this.conversationId = res.id;
        this.messages = (res.messages || []).map((m: any) => ({
          role: m.role,
          content: m.content,
          sources: m.sourcesJson ? JSON.parse(m.sourcesJson) : undefined,
          showSources: false
        }));
      }
    });
  }

  deleteConversation(id: string, event: Event) {
    event.stopPropagation();
    this.http.delete(`${environment.apiUrl}/chat/conversations/${id}`).subscribe({
      next: () => {
        this.conversations = this.conversations.filter(c => c.id !== id);
        this.groupedConversations = this.groupConversations(this.conversations);
        if (this.conversationId === id) this.newChat();
      }
    });
  }

  // --- Inline rename ---
  startRename(conv: Conversation, event: Event) {
    event.stopPropagation();
    this.editingConvId = conv.id;
    this.editingTitle = conv.title;
    // Focus the input after Angular renders it
    setTimeout(() => {
      const input = document.querySelector('.conv-title-input') as HTMLInputElement;
      if (input) { input.focus(); input.select(); }
    }, 0);
  }

  saveRename(conv: Conversation) {
    if (this.editingConvId !== conv.id) return;
    const newTitle = this.editingTitle.trim();
    this.editingConvId = null;
    if (!newTitle || newTitle === conv.title) return;

    this.http.patch<any>(`${environment.apiUrl}/chat/conversations/${conv.id}`, { title: newTitle }).subscribe({
      next: res => {
        conv.title = res.title;
      }
    });
  }

  cancelRename() {
    this.editingConvId = null;
  }

  // --- Pin/Unpin ---
  togglePin(conv: Conversation, event: Event) {
    event.stopPropagation();
    const newPinned = !conv.isPinned;
    this.http.patch<any>(`${environment.apiUrl}/chat/conversations/${conv.id}`, { isPinned: newPinned }).subscribe({
      next: res => {
        conv.isPinned = res.isPinned;
        conv.pinnedAt = res.pinnedAt;
        this.groupedConversations = this.groupConversations(this.conversations);
      }
    });
  }

  formatTimeAgo(dateStr: string): string {
    const date = new Date(dateStr);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    const diffHours = Math.floor(diffMins / 60);
    if (diffHours < 24) return `${diffHours}h ago`;
    const diffDays = Math.floor(diffHours / 24);
    if (diffDays < 7) return `${diffDays}d ago`;
    return date.toLocaleDateString();
  }

  send() {
    if (!this.input.trim() || this.sending) return;

    const message = this.input.trim();
    const isNewConversation = !this.conversationId;
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

        // Auto-title: if this was a new conversation, update title with first ~50 chars
        if (isNewConversation && message.length > 0) {
          const autoTitle = message.length > 50 ? message.substring(0, 47) + '...' : message;
          this.http.patch<any>(`${environment.apiUrl}/chat/conversations/${res.conversationId}`, { title: autoTitle }).subscribe();
        }

        this.loadConversations();
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
