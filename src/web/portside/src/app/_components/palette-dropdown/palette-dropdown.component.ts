import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';
import { PALETTE_DEFINITIONS } from '../../_services/palette-definitions';

@Component({
  selector: 'app-palette-dropdown',
  standalone: true,
  imports: [LucideDynamicIcon],
  template: `
    <div class="custom-palette-dropdown" [class.open]="dropdownOpen()">
      <button class="palette-dropdown-trigger" (click)="toggleDropdown()" type="button">
        <div class="selected-palette">
          <div class="palette-gradient-preview" [style.background]="selectedPaletteGradient()"></div>
          <span>{{ selectedPaletteName() || 'Select Palette' }}</span>
        </div>
        <svg lucideIcon="chevron-down" size="16"></svg>
      </button>
      @if (dropdownOpen()) {
        <div class="palette-dropdown-menu">
          @for (option of paletteOptionsWithGradients(); track option.key) {
            <button
              class="palette-option"
              [class.selected]="option.key === selectedKey()"
              (click)="selectPalette(option.key)"
              type="button"
            >
              <div class="palette-gradient-preview" [style.background]="option.gradient"></div>
              <span>{{ option.name }}</span>
            </button>
          }
        </div>
      }
    </div>
  `,
  styleUrl: './palette-dropdown.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaletteDropdownComponent {
  // Input for available palettes (key-value pairs from device)
  palettes = input.required<Record<string, string> | null>();

  // Input for currently selected palette key
  selectedKey = input.required<string>();

  // Input for currently selected palette name
  selectedPaletteName = input<string | null>();

  // Output when palette is selected
  paletteSelect = output<string>();

  // Local state
  dropdownOpen = signal(false);

  // Get gradient for selected palette
  selectedPaletteGradient = computed(() => {
    const paletteName = this.selectedPaletteName();
    if (!paletteName) return 'linear-gradient(to right, #333, #666)';

    const palette = PALETTE_DEFINITIONS[paletteName];
    return palette ? palette.gradient : 'linear-gradient(to right, #333, #666)';
  });

  // Get palette options with gradients
  paletteOptionsWithGradients = computed(() => {
    const palettes = this.palettes();
    if (!palettes) return [];

    const options = Object.entries(palettes).map(([key, name]) => {
      const palette = PALETTE_DEFINITIONS[name];
      return {
        key,
        name,
        gradient: palette ? palette.gradient : 'linear-gradient(to right, #333, #666)',
      };
    });

    // Sort by name, but keep "Default" first
    return options.sort((a, b) => {
      if (a.name === 'Default') return -1;
      if (b.name === 'Default') return 1;
      return a.name.localeCompare(b.name);
    });
  });

  toggleDropdown() {
    this.dropdownOpen.set(!this.dropdownOpen());
  }

  selectPalette(key: string) {
    this.paletteSelect.emit(key);
    this.dropdownOpen.set(false);
  }
}
