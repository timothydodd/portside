import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterModule, RouterOutlet } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { ClusterStripComponent } from './_components/cluster-strip/cluster-strip.component';
import { ConnectionStatusComponent } from './_components/connection-status/connection-status.component';
import { UserMenuComponent } from './_components/user-menu/user-menu.component';
import { DynamicBackgroundDirective } from './_directives/dynamic-background';
import { EffectBackgroundDirective } from './_directives/effect-background';
import { SignalRService } from './_services/api/signalr.service';
import { AuthService } from './_services/auth-service';

@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet,
    RouterModule,
    RouterLink,
    RouterLinkActive,
    LucideDynamicIcon,
    DynamicBackgroundDirective,
    EffectBackgroundDirective,
    UserMenuComponent,
    ConnectionStatusComponent,
    ClusterStripComponent,
  ],
  template: `
    <div class="app-container" appDynamicBackground>
      <div class="content-wrapper" appEffectBackground>
        <header class="app-header">
          <span class="app-logo" aria-hidden="true">
            <img src="assets/logo-64.png" alt="" width="32" height="32" />
          </span>

          <span class="subtitle">Kubernetes Dashboard</span>
          <nav class="app-nav">
            <a routerLink="/dashboard" routerLinkActive="active" class="nav-link">
              <svg lucideIcon="layout-grid"></svg>
              <span>Dashboard</span>
            </a>
            <a routerLink="/logs" routerLinkActive="active" class="nav-link">
              <svg lucideIcon="server"></svg>
              <span>Pod Logs</span>
            </a>
          </nav>
          @if (auth.isAuthenticated()) {
            <app-cluster-strip />
            <app-user-menu />
          }
        </header>
        <main class="app-main">
          <router-outlet />
        </main>
      </div>
      <app-connection-status />
    </div>
  `,
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit {
  title = 'Portside';
  auth = inject(AuthService);
  private signalRService = inject(SignalRService);

  ngOnInit() {
    // Connect to the cluster hub once we have a token; reconnect on subsequent
    // logins, disconnect on logout.
    this.auth.isLoggedIn.subscribe((loggedIn) => {
      if (loggedIn) this.signalRService.startConnection();
      else this.signalRService.stopConnection();
    });
  }
}
