import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideDynamicIcon } from '@lucide/angular';

@Component({
  selector: 'app-widget-toggle-item',
  standalone: true,
  imports: [FormsModule, LucideDynamicIcon],
  template: `
    <div
      class="widget-item bg-glass rounded-card p-widget-sm"
      (click)="toggleValue()"
      role="button"
      tabindex="0"
      (keydown.space)="toggleValue($event)"
      (keydown.enter)="toggleValue($event)"
    >
      <div class="widget-header">
        <div class="widget-info">
          <svg [lucideIcon]="icon()" size="20" class="widget-icon"></svg>
          <div>
            <h6 class="widget-name">{{ title() }}</h6>
            <p class="widget-description">{{ description() }}</p>
          </div>
        </div>
        <div class="form-check form-switch">
          <input
            class="form-check-input"
            type="checkbox"
            [id]="'widget-toggle-' + id()"
            [ngModel]="checked()"
            (ngModelChange)="checked.set($event)"
            (click)="$event.stopPropagation()"
          />
        </div>
      </div>
    </div>
  `,
  styleUrl: './widget-toggle-item.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WidgetToggleItemComponent {
  id = input.required<string>();
  title = input.required<string>();
  description = input.required<string>();
  icon = input.required<string>();
  checked = model.required<boolean>();

  toggleValue(event?: any) {
    event?.preventDefault();
    this.checked.update((value) => !value);
  }
}
