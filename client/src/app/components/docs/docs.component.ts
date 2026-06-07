import { Component, OnInit, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink, RouterLinkActive } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { marked } from 'marked';

interface TocEntry {
  id: string;
  text: string;
  level: number;
}

interface DocPage {
  slug: string;
  title: string;
  icon: string;
}

@Component({
  selector: 'app-docs',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  template: `
    <div class="docs-layout">
      <!-- Sidebar TOC -->
      <aside class="docs-sidebar">
        <div class="docs-sidebar-header">
          <i class="bi bi-book"></i>
          <span>Documentation</span>
        </div>
        <nav class="docs-nav">
          @for (page of pages; track page) {
            <a
              [routerLink]="['/docs', page.slug]"
              routerLinkActive="active"
              class="docs-nav-item">
              <i class="bi" [ngClass]="page.icon"></i>
              <span>{{ page.title }}</span>
            </a>
          }
        </nav>
      </aside>
    
      <!-- Main content -->
      <main class="docs-content">
        @if (loading) {
          <div class="docs-loading">
            <i class="bi bi-arrow-repeat spin"></i> Loading...
          </div>
        }
        @if (error) {
          <div class="docs-error">
            <i class="bi bi-exclamation-triangle"></i> {{ error }}
          </div>
        }
        @if (!loading && !error) {
          <article
            class="docs-article"
            [innerHTML]="renderedHtml">
          </article>
        }
      </main>
    
      <!-- Page TOC (right sidebar) -->
      @if (pageToc.length > 0) {
        <aside class="docs-page-toc">
          <div class="page-toc-header">On this page</div>
          <nav class="page-toc-nav">
            @for (entry of pageToc; track entry) {
              <a
                [href]="'#' + entry.id"
                class="page-toc-item"
                [class.level-3]="entry.level === 3"
                (click)="scrollToHeading($event, entry.id)">
                {{ entry.text }}
              </a>
            }
          </nav>
        </aside>
      }
    </div>
    `,
  styles: [`
    .docs-layout {
      display: grid;
      grid-template-columns: 240px 1fr 200px;
      min-height: 100vh;
      gap: 0;
    }

    /* Sidebar */
    .docs-sidebar {
      border-right: 1px solid var(--border);
      background: var(--background-alt);
      padding: 1.5rem 0;
      position: sticky;
      top: 0;
      height: 100vh;
      overflow-y: auto;
    }
    .docs-sidebar-header {
      padding: 0 1.25rem 1rem;
      font-size: var(--font-lg);
      font-weight: 600;
      color: var(--primary);
      display: flex;
      align-items: center;
      gap: 0.5rem;
      border-bottom: 1px solid var(--border);
      margin-bottom: 0.75rem;
    }
    .docs-nav {
      display: flex;
      flex-direction: column;
    }
    .docs-nav-item {
      display: flex;
      align-items: center;
      gap: 0.625rem;
      padding: 0.5rem 1.25rem;
      font-size: var(--font-xs);
      color: var(--text-muted);
      font-weight: 500;
      transition: all var(--transition);
      text-decoration: none;
    }
    .docs-nav-item:hover {
      color: var(--text);
      background: var(--surface-2);
    }
    .docs-nav-item.active {
      color: var(--primary);
      background: rgba(0, 102, 204, 0.08);
      border-right: 2px solid var(--primary);
    }
    .docs-nav-item i {
      font-size: var(--font-sm);
      width: 1.125rem;
      text-align: center;
    }

    /* Main content */
    .docs-content {
      padding: 2rem 3rem;
      max-width: 100%;
      overflow-x: hidden;
    }
    .docs-loading, .docs-error {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      padding: 2rem;
      color: var(--text-muted);
      font-size: var(--font-base);
    }
    .docs-error { color: var(--danger); }

    .spin {
      animation: spin 1s linear infinite;
    }
    @keyframes spin {
      from { transform: rotate(0deg); }
      to { transform: rotate(360deg); }
    }

    /* Page TOC */
    .docs-page-toc {
      border-left: 1px solid var(--border);
      padding: 1.5rem 1rem;
      position: sticky;
      top: 0;
      height: 100vh;
      overflow-y: auto;
    }
    .page-toc-header {
      font-size: var(--font-xs);
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--text-light);
      margin-bottom: 0.75rem;
    }
    .page-toc-nav {
      display: flex;
      flex-direction: column;
    }
    .page-toc-item {
      font-size: 0.8125rem;
      color: var(--text-muted);
      padding: 0.25rem 0;
      text-decoration: none;
      border-left: 2px solid transparent;
      padding-left: 0.75rem;
      transition: all var(--transition);
    }
    .page-toc-item:hover {
      color: var(--primary);
      border-left-color: var(--primary);
    }
    .page-toc-item.level-3 {
      padding-left: 1.5rem;
      font-size: 0.75rem;
    }

    /* Article styles */
    :host ::ng-deep .docs-article {
      line-height: 1.7;
      color: var(--text);
    }
    :host ::ng-deep .docs-article h1 {
      font-size: var(--font-3xl);
      font-weight: 700;
      margin-bottom: 1.5rem;
      padding-bottom: 0.75rem;
      border-bottom: 2px solid var(--border);
      color: var(--text);
    }
    :host ::ng-deep .docs-article h2 {
      font-size: var(--font-xl);
      font-weight: 600;
      margin-top: 2.5rem;
      margin-bottom: 1rem;
      padding-bottom: 0.5rem;
      border-bottom: 1px solid var(--border);
      color: var(--text);
    }
    :host ::ng-deep .docs-article h3 {
      font-size: var(--font-lg);
      font-weight: 600;
      margin-top: 1.75rem;
      margin-bottom: 0.75rem;
      color: var(--text);
    }
    :host ::ng-deep .docs-article p {
      margin-bottom: 1rem;
      font-size: var(--font-sm);
    }
    :host ::ng-deep .docs-article ul,
    :host ::ng-deep .docs-article ol {
      margin-bottom: 1rem;
      padding-left: 1.5rem;
    }
    :host ::ng-deep .docs-article li {
      margin-bottom: 0.375rem;
      font-size: var(--font-sm);
    }
    :host ::ng-deep .docs-article a {
      color: var(--primary);
      text-decoration: none;
    }
    :host ::ng-deep .docs-article a:hover {
      text-decoration: underline;
    }
    :host ::ng-deep .docs-article code {
      background: var(--background-alt);
      border: 1px solid var(--border);
      border-radius: 4px;
      padding: 0.125rem 0.375rem;
      font-size: 0.875em;
      font-family: 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, monospace;
    }
    :host ::ng-deep .docs-article pre {
      background: #1e1e2e;
      color: #cdd6f4;
      border-radius: var(--radius);
      padding: 1rem 1.25rem;
      margin-bottom: 1.25rem;
      overflow-x: auto;
      font-size: 0.8125rem;
      line-height: 1.6;
    }
    :host ::ng-deep .docs-article pre code {
      background: none;
      border: none;
      padding: 0;
      font-size: inherit;
      color: inherit;
    }
    :host ::ng-deep .docs-article strong {
      font-weight: 600;
    }
    :host ::ng-deep .docs-article table {
      width: 100%;
      border-collapse: collapse;
      margin-bottom: 1.25rem;
      font-size: var(--font-xs);
    }
    :host ::ng-deep .docs-article th,
    :host ::ng-deep .docs-article td {
      border: 1px solid var(--border);
      padding: 0.5rem 0.75rem;
      text-align: left;
    }
    :host ::ng-deep .docs-article th {
      background: var(--background-alt);
      font-weight: 600;
    }
    :host ::ng-deep .docs-article blockquote {
      border-left: 4px solid var(--primary);
      padding: 0.75rem 1rem;
      margin-bottom: 1rem;
      background: var(--background-alt);
      color: var(--text-muted);
    }
    :host ::ng-deep .docs-article input[type="checkbox"] {
      margin-right: 0.5rem;
    }
    :host ::ng-deep .docs-article hr {
      border: none;
      border-top: 1px solid var(--border);
      margin: 2rem 0;
    }
  `]
})
export class DocsComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private http = inject(HttpClient);

  pages: DocPage[] = [
    { slug: 'getting-started', title: 'Getting Started', icon: 'bi-rocket-takeoff' },
    { slug: 'repositories', title: 'Repositories', icon: 'bi-folder2-open' },
    { slug: 'search', title: 'Search', icon: 'bi-search' },
    { slug: 'enrichments', title: 'Enrichments', icon: 'bi-file-earmark-text' },
    { slug: 'insights', title: 'Insights', icon: 'bi-lightbulb' },
    { slug: 'chat', title: 'Chat', icon: 'bi-chat-dots' },
    { slug: 'api-reference', title: 'API Reference', icon: 'bi-braces' },
    { slug: 'mcp', title: 'MCP Server', icon: 'bi-plug' },
    { slug: 'settings', title: 'Settings', icon: 'bi-gear' },
    { slug: 'deployment', title: 'Deployment', icon: 'bi-cloud-upload' },
  ];

  renderedHtml = '';
  pageToc: TocEntry[] = [];
  loading = false;
  error = '';
  private routeSub!: Subscription;

  ngOnInit(): void {
    this.routeSub = this.route.paramMap.subscribe(params => {
      const page = params.get('page') || 'getting-started';
      this.loadPage(page);
    });
  }

  ngOnDestroy(): void {
    if (this.routeSub) {
      this.routeSub.unsubscribe();
    }
  }

  loadPage(slug: string): void {
    this.loading = true;
    this.error = '';
    this.renderedHtml = '';
    this.pageToc = [];

    this.http.get(`docs/${slug}.md`, { responseType: 'text' }).subscribe({
      next: (markdown) => {
        this.pageToc = this.extractToc(markdown);
        this.renderedHtml = this.renderMarkdown(markdown);
        this.loading = false;
      },
      error: () => {
        this.error = `Could not load documentation page: ${slug}`;
        this.loading = false;
      }
    });
  }

  private extractToc(markdown: string): TocEntry[] {
    const toc: TocEntry[] = [];
    const lines = markdown.split('\n');
    for (const line of lines) {
      const match = line.match(/^(#{2,3})\s+(.+)$/);
      if (match) {
        const level = match[1].length;
        const text = match[2].trim();
        const id = text
          .toLowerCase()
          .replace(/[^\w\s-]/g, '')
          .replace(/\s+/g, '-');
        toc.push({ id, text, level });
      }
    }
    return toc;
  }

  private renderMarkdown(markdown: string): string {
    const renderer = new marked.Renderer();

    // Add IDs to headings for anchor links
    renderer.heading = ({ tokens, depth }: { tokens: any; depth: number }) => {
      const text = this.getPlainText(tokens);
      const id = text
        .toLowerCase()
        .replace(/[^\w\s-]/g, '')
        .replace(/\s+/g, '-');
      return `<h${depth} id="${id}">${this.renderInlineTokens(tokens)}</h${depth}>`;
    };

    // Convert relative links to docs routes
    renderer.link = ({ href, tokens }: { href: string; tokens: any }) => {
      const text = this.renderInlineTokens(tokens);
      if (href && !href.startsWith('http') && !href.startsWith('#')) {
        return `<a href="/docs/${href}">${text}</a>`;
      }
      return `<a href="${href}">${text}</a>`;
    };

    return marked.parse(markdown, { async: false, renderer }) as string;
  }

  private getPlainText(tokens: any[]): string {
    return tokens.map((t: any) => t.raw || t.text || '').join('');
  }

  private renderInlineTokens(tokens: any[]): string {
    return tokens.map((t: any) => {
      if (t.type === 'codespan') return `<code>${t.text}</code>`;
      if (t.type === 'strong') return `<strong>${t.text}</strong>`;
      if (t.type === 'em') return `<em>${t.text}</em>`;
      return t.text || t.raw || '';
    }).join('');
  }

  scrollToHeading(event: Event, id: string): void {
    event.preventDefault();
    const el = document.getElementById(id);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  }
}
