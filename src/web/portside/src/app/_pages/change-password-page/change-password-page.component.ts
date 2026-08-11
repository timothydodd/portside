import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { AuthService } from '../../_services/auth-service';

@Component({
  selector: 'app-change-password-page',
  standalone: true,
  imports: [FormsModule, LucideDynamicIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './change-password-page.component.scss',
  template: `
    <div class="change-password-page">
      <form class="card" (submit)="submit($event)" autocomplete="off">
        <div class="title">
          <svg lucideIcon="settings"></svg>
          <h2>Change Password</h2>
        </div>

        <label>
          <span>Current password</span>
          <input type="password"
                 [(ngModel)]="oldPassword"
                 name="oldPassword"
                 autocomplete="current-password"
                 required />
        </label>

        <label>
          <span>New password</span>
          <input type="password"
                 [(ngModel)]="newPassword"
                 name="newPassword"
                 autocomplete="new-password"
                 required
                 minlength="8" />
          <small>At least 8 chars with upper, lower, digit, and a special character.</small>
        </label>

        <label>
          <span>Confirm new password</span>
          <input type="password"
                 [(ngModel)]="confirmPassword"
                 name="confirmPassword"
                 autocomplete="new-password"
                 required />
        </label>

        @if (error()) {
          <div class="msg error"><svg lucideIcon="alert-circle"></svg> {{ error() }}</div>
        }
        @if (success()) {
          <div class="msg ok"><svg lucideIcon="check-circle"></svg> {{ success() }}</div>
        }

        <div class="actions">
          <button type="button" class="btn ghost" (click)="cancel()">Cancel</button>
          <button type="submit" class="btn primary" [disabled]="saving()">
            @if (saving()) { <svg lucideIcon="loader-2"></svg> Saving... }
            @else { Save }
          </button>
        </div>
      </form>
    </div>
  `,
})
export class ChangePasswordPageComponent {
  private auth = inject(AuthService);
  private router = inject(Router);

  oldPassword = signal('');
  newPassword = signal('');
  confirmPassword = signal('');
  saving = signal(false);
  error = signal<string | null>(null);
  success = signal<string | null>(null);

  submit(e: Event) {
    e.preventDefault();
    this.error.set(null);
    this.success.set(null);

    if (!this.oldPassword() || !this.newPassword()) {
      this.error.set('All fields are required');
      return;
    }
    if (this.newPassword() !== this.confirmPassword()) {
      this.error.set('New passwords do not match');
      return;
    }

    this.saving.set(true);
    this.auth.changePassword({
      oldPassword: this.oldPassword(),
      newPassword: this.newPassword(),
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.success.set('Password updated. Please sign in again.');
        setTimeout(() => this.auth.logout(), 1200);
      },
      error: (err) => {
        this.saving.set(false);
        const msg = err?.error?.Error || err?.error?.error || err?.message || 'Failed to change password';
        this.error.set(typeof msg === 'string' ? msg : 'Failed to change password');
      },
    });
  }

  cancel() {
    this.router.navigate(['/dashboard']);
  }
}
