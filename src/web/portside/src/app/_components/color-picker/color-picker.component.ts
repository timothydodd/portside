import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { LucideDynamicIcon } from '@lucide/angular';
import { ColorLight, PRESET_COLORS } from '../../_services/color-light.model';
import { PALETTE_DEFINITIONS } from '../../_services/palette-definitions';
import { ColorWheelPickerComponent } from '../color-wheel-picker/color-wheel-picker.component';
import { PaletteDropdownComponent } from '../palette-dropdown/palette-dropdown.component';
import { SliderComponent } from '../slider/slider.component';

@Component({
  selector: 'app-color-picker',
  standalone: true,
  imports: [FormsModule, LucideDynamicIcon, SliderComponent, ColorWheelPickerComponent, PaletteDropdownComponent],
  template: `
    <div class="color-picker-container">
      <!-- Light Effects Dropdown (if supported) -->
      @if (device().supportsEffects()) {
        <div class="control-group">
          <label class="control-label">
            <svg lucideIcon="sparkles" size="16"></svg>
            <span>Light Effect</span>
          </label>
          <select
            class="form-control effect-dropdown"
            [ngModel]="selectedEffectKey()"
            (ngModelChange)="onEffectChange($event)"
          >
            @for (effect of effectOptions(); track effect.key) {
              <option [value]="effect.key">{{ effect.name }}</option>
            }
          </select>
        </div>
      }

      <!-- Light Palettes Dropdown (if supported and effect is not Solid) -->
      @if (device().supportsPalettes() && device().shouldShowEffectControls()) {
        <div class="control-group">
          <label class="control-label">
            <svg lucideIcon="palette" size="16"></svg>
            <span>Color Palette</span>
          </label>
          <app-palette-dropdown
            [palettes]="device().lightPalettes()"
            [selectedKey]="selectedPaletteKey()"
            [selectedPaletteName]="device().paletteName()"
            (paletteSelect)="onPaletteChange($event)"
          />
          <!-- Palette Gradient Display -->
          @if (currentPaletteGradient()) {
            <div class="palette-gradient-display" [style.background]="currentPaletteGradient()"></div>
          }
        </div>
      }

      <!-- Level Control (moved to top) -->
      <div class="control-group">
        <label class="control-label">
          <svg lucideIcon="sun" size="16"></svg>
          <span>Brightness</span>
          <span class="value">{{ brightness() }}%</span>
        </label>
        <app-slider [min]="0" [max]="100" [ngModel]="brightness()" (ngModelChange)="updateBrightness($event)" />
      </div>

      <!-- Effect Speed Slider (if supported and effect is not Solid) -->
      @if (device().supportsEffectSpeed() && device().shouldShowEffectControls()) {
        <div class="control-group">
          <label class="control-label">
            <svg lucideIcon="zap" size="16"></svg>
            <span>Effect Speed</span>
            <span class="value">{{ effectSpeed() }}%</span>
          </label>
          <app-slider [min]="0" [max]="100" [ngModel]="effectSpeed()" (ngModelChange)="updateEffectSpeed($event)" />
        </div>
      }

      <!-- Color Wheel (visible only when not using a non-custom palette) -->
      @if (shouldShowColorPicker()) {
        <div class="color-controls-row">
          <!-- Color Wheel -->
          <div class="wheel-section">
            <app-color-wheel-picker
              [hue]="hue() * 3.6"
              [saturation]="saturation()"
              [brightness]="brightness()"
              [size]="240"
              (colorChange)="onWheelColorChange($event)"
            />
          </div>

          <!-- Color Info Panel with Preview -->
          <div class="color-info-panel">
            <div class="info-and-preview">
              <div class="info-grid">
                <div class="info-item">
                  <svg lucideIcon="palette" size="16"></svg>
                  <span class="label">Hue</span>
                  <span class="value">{{ hueDegrees() }}°</span>
                </div>
                <div class="info-item">
                  <svg lucideIcon="droplets" size="16"></svg>
                  <span class="label">Saturation</span>
                  <span class="value">{{ saturation() }}%</span>
                </div>
                <div class="info-item">
                  <svg lucideIcon="sun" size="16"></svg>
                  <span class="label">Level</span>
                  <span class="value">{{ brightness() }}%</span>
                </div>
                <div class="info-item">
                  <svg lucideIcon="hash" size="16"></svg>
                  <span class="label">Hex</span>
                  <span class="value">{{ currentHex() }}</span>
                </div>
                <div class="info-item">
                  <svg lucideIcon="square" size="16"></svg>
                  <span class="label">RGB</span>
                  <span class="value">{{ currentRgb() }}</span>
                </div>
              </div>

              <!-- Color Preview Box -->
              <div class="color-preview-section">
                <div
                  class="current-color-box"
                  [style.background-color]="previewColor()"
                  [class.off]="!device().checked()"
                ></div>
              </div>
            </div>
          </div>
        </div>
      }

      <!-- Preset Colors (visible only when not using a non-custom palette) -->
      @if (shouldShowColorPicker()) {
        <div class="preset-colors">
          <h4 class="preset-header">Quick Colors</h4>
          <div class="preset-grid">
            @for (preset of presets; track preset.name) {
              <button
                class="preset-button"
                [title]="preset.name"
                [style.background-color]="getPresetColor(preset)"
                (click)="applyPreset(preset)"
              ></button>
            }
          </div>
        </div>
      }
    </div>
  `,
  styleUrl: './color-picker.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ColorPickerComponent {
  device = input.required<ColorLight>();

  // Output for color changes (RGB only)
  colorChange = output<{ hue: number; saturation: number; brightness: number }>();

  // Output for effect changes
  effectChange = output<string>();

  // Output for palette changes
  paletteChange = output<string>();

  // Output for effect speed changes
  effectSpeedChange = output<number>();

  // Local state for real-time preview
  hue = signal(0);
  saturation = signal(100);
  brightness = signal(100);
  effectSpeed = signal(50);

  presets = PRESET_COLORS;

  // Computed properties for effects
  effectOptions = computed(() => {
    const effects = this.device().lightEffects();
    if (!effects) return [];

    const options = Object.entries(effects).map(([key, name]) => ({
      key,
      name,
    }));

    // Sort by name, but keep "Solid" first
    return options.sort((a, b) => {
      if (a.name === 'Solid') return -1;
      if (b.name === 'Solid') return 1;
      return a.name.localeCompare(b.name);
    });
  });

  selectedEffectKey = computed(() => {
    const effects = this.device().lightEffects();
    const currentEffect = this.device().effectName();

    if (!effects || !currentEffect) return '0'; // Default to first option (usually "Solid")

    // Find the key for the current effect name
    for (const [key, name] of Object.entries(effects)) {
      if (name === currentEffect) return key;
    }

    return '0'; // Fallback to first option
  });

  currentEffectName = computed(() => {
    return this.device().effectName() || 'Unknown Effect';
  });

  // Computed properties for palettes
  paletteOptions = computed(() => {
    const palettes = this.device().lightPalettes();
    if (!palettes) return [];

    return Object.entries(palettes).map(([key, name]) => ({
      key,
      name,
    }));
  });

  selectedPaletteKey = computed(() => {
    const palettes = this.device().lightPalettes();
    const currentPalette = this.device().paletteName();

    if (!palettes || !currentPalette) return '0'; // Default to first option

    // Find the key for the current palette name
    for (const [key, name] of Object.entries(palettes)) {
      if (name === currentPalette) return key;
    }

    return '0'; // Fallback to first option
  });

  // Get the gradient for the currently selected palette
  currentPaletteGradient = computed(() => {
    const paletteName = this.device().paletteName();
    if (!paletteName) return null;

    const palette = PALETTE_DEFINITIONS[paletteName];
    return palette ? palette.gradient : null;
  });

  // Check if we should show the color picker
  shouldShowColorPicker = computed(() => {
    const effectName = this.device().effectName();
    const paletteName = this.device().paletteName();

    // Show color picker if:
    // 1. Effect is Solid
    // 2. Effect is not Solid but palette starts with * (custom palette)
    // 3. No effect selected
    return !effectName || effectName === 'Solid' || (paletteName && paletteName.startsWith('*'));
  });

  // Convert hue from 0-100 scale to degrees
  hueDegrees = computed(() => Math.round(this.hue() * 3.6));

  // Current RGB values
  currentRgbValues = computed(() => {
    return this.device().hsvToRgb(this.hue(), this.saturation(), this.brightness());
  });

  // Current RGB string for display
  currentRgb = computed(() => {
    const rgb = this.currentRgbValues();
    return `${rgb.r}, ${rgb.g}, ${rgb.b}`;
  });

  // Current hex value
  currentHex = computed(() => {
    const rgb = this.currentRgbValues();
    return this.device().rgbToHex(rgb.r, rgb.g, rgb.b);
  });

  // Preview color for the color swatch
  previewColor = computed(() => {
    if (!this.device().checked()) {
      return 'transparent';
    }

    const rgb = this.currentRgbValues();
    return `rgb(${rgb.r}, ${rgb.g}, ${rgb.b})`;
  });

  constructor() {
    // Initialize from device values using effect
    effect(() => {
      const device = this.device();
      this.hue.set(device.hue());
      this.saturation.set(device.saturation());
      this.brightness.set(device.level());
      this.effectSpeed.set(device.effectSpeed());
    });
  }

  updateBrightness(value: number) {
    this.brightness.set(value);
    this.emitColorChange();
  }

  updateEffectSpeed(value: number) {
    this.effectSpeed.set(value);
    this.effectSpeedChange.emit(value);
  }

  applyPreset(preset: (typeof PRESET_COLORS)[0]) {
    // All presets are now RGB-only
    this.hue.set(preset.hue);
    this.saturation.set(preset.saturation);
    this.brightness.set(preset.level);
    this.emitColorChange();
  }

  getPresetColor(preset: (typeof PRESET_COLORS)[0]): string {
    // All presets use HSV to match the device's color model
    const rgb = this.device().hsvToRgb(preset.hue, preset.saturation, preset.level);
    return `rgb(${rgb.r}, ${rgb.g}, ${rgb.b})`;
  }

  private emitColorChange() {
    this.colorChange.emit({
      hue: this.hue(),
      saturation: this.saturation(),
      brightness: this.brightness(),
    });
  }

  onWheelColorChange(event: { hue: number; saturation: number }) {
    // Convert hue from degrees (0-360) to Hubitat scale (0-100)
    this.hue.set(event.hue / 3.6);
    this.saturation.set(event.saturation);
    this.emitColorChange();
  }

  onEffectChange(effectKey: string) {
    const effects = this.device().lightEffects();
    if (!effects || !effects[effectKey]) return;

    const effectName = effects[effectKey];

    // Update local device state
    this.device().effectName.set(effectName);

    // Emit effect change
    this.effectChange.emit(effectName);
  }

  onPaletteChange(paletteKey: string) {
    const palettes = this.device().lightPalettes();
    if (!palettes || !palettes[paletteKey]) return;

    const paletteName = palettes[paletteKey];

    // Update local device state
    this.device().paletteName.set(paletteName);

    // Emit palette change
    this.paletteChange.emit(paletteName);
  }
}
