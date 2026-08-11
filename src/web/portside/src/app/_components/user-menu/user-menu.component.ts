import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';

import { Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { take } from 'rxjs';
import { AuthService, User } from '../../_services/auth-service';
import { ClickOutsideDirective } from '../../_services/click-outside.directive';

@Component({
  selector: 'app-user-menu',
  standalone: true,
  imports: [LucideDynamicIcon, ClickOutsideDirective],
  template: `
    <div class="user-menu-container">
      <button type="button" class="user-menu-trigger" (click)="isOpen.set(!isOpen())" title="User menu">
        <div class="user-avatar">
          <svg lucideIcon="user" size="18"></svg>
        </div>
        <span class="user-name">{{ user()?.userName ?? username() ?? 'Account' }}</span>
      </button>

      @if (isOpen()) {
        <div class="user-menu-dropdown" appClickOutside (clickOutside)="isOpen.set(false)" [delayTime]="200">
          <div class="dropdown-header">
            <div class="user-info">
              <div class="large-avatar">
                <svg lucideIcon="user" size="24"></svg>
              </div>
              <div class="user-details">
                <div class="username">{{ user()?.userName ?? username() ?? 'Account' }}</div>
                <div class="user-role">Signed in</div>
              </div>
            </div>
          </div>

          <div class="dropdown-divider"></div>

          <div class="dropdown-menu-items">
            <button class="dropdown-item" (click)="openSettings()">
              <svg lucideIcon="settings" size="16"></svg>
              <span>Monitoring Settings</span>
            </button>
            <button class="dropdown-item" (click)="changePassword()">
              <svg lucideIcon="shield" size="16"></svg>
              <span>Change Password</span>
            </button>
            <button class="dropdown-item logout" (click)="logOut()">
              <svg lucideIcon="log-out" size="16"></svg>
              <span>Sign Out</span>
            </button>
          </div>
        </div>
      }
    </div>
  `,
  styleUrl: './user-menu.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserMenuComponent {
  authService = inject(AuthService);
  router = inject(Router);
  user = signal<User | null>(null);
  username = signal<string | null>(null);
  isOpen = signal(false);

  constructor() {
    this.username.set(this.authService.getCurrentUsername());
    const userObs = this.authService.getUser();
    if (userObs && typeof userObs.pipe === 'function') {
      userObs.pipe(take(1)).subscribe({
        next: (u) => this.user.set(u),
        error: () => { /* fall back to username from token */ },
      });
    }
  }

  changePassword() {
    this.isOpen.set(false);
    this.router.navigate(['/change-password']);
  }

  openSettings() {
    this.isOpen.set(false);
    this.router.navigate(['/settings']);
  }

  logOut() {
    this.isOpen.set(false);
    this.authService.logout();
  }
}
