import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideDynamicIcon } from '@lucide/angular';
import { ClickOutsideDirective } from '../../_services/click-outside.directive';

export interface ColumnFilterItem {
  label: string;
  value: string;
}

/**
 * Excel-style column filter button + popover.
 * - `items` is the full set of distinct values for the column
 * - `selected` is the set currently CHECKED (visible). If equal in size to items,
 *   the filter is considered inactive.
 * - Emits `selectionChange` with the new selected set.
 */
@Component({
  selector: 'app-column-filter',
  standalone: true,
  imports: [FormsModule, LucideDynamicIcon, ClickOutsideDirective],
  template: `
    <span class="col-filter" appClickOutside (clickOutside)="close()">
      <button type="button"
              class="filter-btn"
              [class.active]="isActive()"
              (click)="toggle($event)"
              [attr.aria-label]="'Filter ' + label()">
        <svg lucideIcon="filter"></svg>
      </button>
      @if (open()) {
        <div class="filter-panel" [class.align-end]="align() === 'end'" (click)="$event.stopPropagation()">
          @if (searchable()) {
            <div class="filter-header">
              <input type="text"
                     [ngModel]="search()"
                     (ngModelChange)="search.set($event)"
                     [placeholder]="searchPlaceholder()"
                     aria-label="Search filter values" />
            </div>
          }
          <div class="filter-actions">
            <button type="button" (click)="selectAllVisible()">All</button>
            <button type="button" (click)="selectNoneVisible()">None</button>
            <button type="button" (click)="invertVisible()">Invert</button>
          </div>
          <div class="filter-list">
            @for (item of visibleItems(); track item.value) {
              <label class="filter-item">
                <input type="checkbox"
                       [checked]="selected().has(item.value)"
                       (change)="toggleItem(item.value, $any($event.target).checked)" />
                <span class="filter-item-label">{{ item.label }}</span>
              </label>
            } @empty {
              <div class="filter-empty">No matches</div>
            }
          </div>
        </div>
      }
    </span>
  `,
  styleUrl: './column-filter.component.scss',
})
export class ColumnFilterComponent {
  items = input<ColumnFilterItem[]>([]);
  selected = input<Set<string>>(new Set());
  searchable = input(true);
  searchPlaceholder = input('Search...');
  label = input('');
  align = input<'start' | 'end'>('start');

  selectionChange = output<Set<string>>();

  open = signal(false);
  search = signal('');

  visibleItems = computed(() => {
    const q = this.search().trim().toLowerCase();
    if (!q) return this.items();
    return this.items().filter((i) => i.label.toLowerCase().includes(q));
  });

  isActive = computed(() => {
    const sel = this.selected();
    const all = this.items();
    if (all.length === 0) return false;
    if (sel.size !== all.length) return true;
    for (const i of all) if (!sel.has(i.value)) return true;
    return false;
  });

  toggle(event: MouseEvent) {
    event.stopPropagation();
    this.open.update((v) => !v);
  }

  close() {
    this.open.set(false);
  }

  toggleItem(value: string, checked: boolean) {
    const next = new Set(this.selected());
    if (checked) next.add(value);
    else next.delete(value);
    this.selectionChange.emit(next);
  }

  selectAllVisible() {
    const visible = this.visibleItems();
    const sel = this.selected();
    const allSelected = visible.length > 0 && visible.every((i) => sel.has(i.value));
    const next = new Set(sel);
    if (allSelected) {
      for (const i of visible) next.delete(i.value);
    } else {
      for (const i of visible) next.add(i.value);
    }
    this.selectionChange.emit(next);
  }

  selectNoneVisible() {
    const next = new Set(this.selected());
    for (const i of this.visibleItems()) next.delete(i.value);
    this.selectionChange.emit(next);
  }

  invertVisible() {
    const next = new Set(this.selected());
    for (const i of this.visibleItems()) {
      if (next.has(i.value)) next.delete(i.value);
      else next.add(i.value);
    }
    this.selectionChange.emit(next);
  }
}
