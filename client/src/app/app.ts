import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { SidebarComponent } from './components/layout/sidebar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, SidebarComponent],
  template: `
    <app-sidebar />
    <main class="main-content">
      <router-outlet />
    </main>
  `,
  styles: [`
    .main-content {
      margin-left: var(--sidebar-width);
      padding: 2rem;
      min-height: 100vh;
    }
  `]
})
export class AppComponent {}
