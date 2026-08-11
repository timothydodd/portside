import { ChangeDetectionStrategy, Component } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';

@Component({
  selector: 'app-loading-spinner',
  imports: [LucideDynamicIcon],
  template: `
    <div class="loading-overlay">
      <div class="loading-container">
        <div class="spinner-container">
          <svg lucideIcon="loader-2" size="48" class="spinner"></svg>
        </div>
        <div class="loading-text">Loading Dashboard...</div>
        <div class="loading-dots">
          <span></span>
          <span></span>
          <span></span>
        </div>
      </div>
    </div>
  `,
  styleUrls: ['./loading-spinner.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoadingSpinnerComponent {}
