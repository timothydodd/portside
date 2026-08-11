import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LucideDynamicIcon } from '@lucide/angular';
import { debounceTime, Subject } from 'rxjs';
import { DeviceApiService } from '../../_services/api/device.api';
import { ThermoStatControl } from '../../_services/device-manager.service';

@Component({
  selector: 'app-thermostat',
  imports: [LucideDynamicIcon],
  template: `<div class="container-t">
    <div class="thermostat-header">
      <h3 class="room-name">{{ this.data().room }}</h3>
      <div class="current-status">
        <span class="current-temp">
          <svg lucideIcon="thermometer" size="18"></svg>
          {{ this.data().tempDisplay() }}
        </span>
        <span class="system-state" [class.active]="this.data().state() !== 'idle'">{{ this.data().state() }}</span>
      </div>
    </div>

    <div class="temperature-section">
      <div class="temp-display">
        <span class="set-temp">{{ this.data().setPointDisplay() }}</span>
        <span class="target-label">Target Temperature</span>
      </div>

      <div class="temp-controls">
        <button class="temp-btn decrease" (click)="adjustTemp(-1)" [disabled]="isDisabled()">
          <svg lucideIcon="minus" size="24"></svg>
        </button>
        <button class="temp-btn increase" (click)="adjustTemp(1)" [disabled]="isDisabled()">
          <svg lucideIcon="plus" size="24"></svg>
        </button>
      </div>
    </div>

    <div class="controls-section">
      <div class="control-group">
        <label class="control-label">Mode</label>
        <div class="mode-buttons">
          <button class="mode-btn" [class.active]="data().mode() === 'heat'" (click)="setMode('heat')">
            <svg lucideIcon="flame" size="20"></svg>
            <span>Heat</span>
          </button>
          <button class="mode-btn" [class.active]="data().mode() === 'cool'" (click)="setMode('cool')">
            <svg lucideIcon="snowflake" size="20"></svg>
            <span>Cool</span>
          </button>
          <button class="mode-btn" [class.active]="data().mode() === 'off'" (click)="setMode('off')">
            <svg lucideIcon="power" size="20"></svg>
            <span>Off</span>
          </button>
        </div>
      </div>

      <div class="control-group">
        <label class="control-label">Fan</label>
        <div class="fan-buttons">
          <button class="fan-btn" [class.active]="data().fanMode() === 'auto'" (click)="setFanMode('auto')">
            Auto
          </button>
          <button class="fan-btn" [class.active]="data().fanMode() === 'circulate'" (click)="setFanMode('circulate')">
            Circulate
          </button>
          <button class="fan-btn" [class.active]="data().fanMode() === 'on'" (click)="setFanMode('on')">On</button>
        </div>
      </div>
    </div>
  </div>`,
  styleUrl: './thermostat.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThermostatComponent {
  api = inject(DeviceApiService);
  data = input.required<ThermoStatControl>();
  lowest = 30;
  highest = 100;
  isEnabled = computed(() => {
    return this.data()?.isEnabled() ?? false;
  });
  isDisabled = computed(() => {
    return !this.isEnabled();
  });
  debouncer: Subject<number> = new Subject<number>();

  constructor() {
    this.debouncer.pipe(debounceTime(1000), takeUntilDestroyed()).subscribe((temp) => {
      const d = this.data();
      if (d.mode() === 'heat') {
        this.api.sendCommand(d.id, 'setHeatingSetpoint', temp.toString()).subscribe();
      } else if (d.mode() === 'cool') {
        this.api.sendCommand(d.id, 'setCoolingSetpoint', temp.toString()).subscribe();
      }
    });
  }
  setMode(mode: string) {
    this.data().mode.set(mode);
    this.api.sendCommand(this.data().id, 'setThermostatMode', mode).subscribe();
  }
  setFanMode(mode: string) {
    this.data().fanMode.set(mode);
    this.api.sendCommand(this.data().id, 'setThermostatFanMode', mode).subscribe();
  }

  adjustTemp(delta: number) {
    const currentTemp = this.data().setPoint();
    const newTemp = Math.max(this.lowest, Math.min(this.highest, currentTemp + delta));
    this.data().setPoint.set(newTemp);
    this.debouncer.next(newTemp);
  }
}
