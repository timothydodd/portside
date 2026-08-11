import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { LoadingSpinnerComponent } from '../../_components/loading-spinner/loading-spinner.component';
import { Pod } from '../../_models/kubernetes.interfaces';
import { KubernetesApiService } from '../../_services/kubernetes.api';
import { MonitorSettings, MonitorSettingsApiService } from '../../_services/monitor-settings.service';

interface PodOption {
  key: string;
  namespace: string;
  name: string;
  enabled: boolean;
}

@Component({
  selector: 'app-settings-page',
  standalone: true,
  imports: [FormsModule, LucideDynamicIcon, LoadingSpinnerComponent, CommonModule],
  template: `
    <div class="settings-page">
      <header class="settings-header">
        <button class="back-btn" (click)="goBack()" title="Back">
          <svg lucideIcon="chevron-right" class="back-icon"></svg>
          Back
        </button>
        <h1>Monitoring Settings</h1>
      </header>

      @if (loading()) {
        <div class="loading-state">
          <app-loading-spinner />
          <p>Loading settings…</p>
        </div>
      } @else if (settings()) {
        <section class="card">
          <h2>Pod monitoring</h2>
          <p class="hint">
            When disabled, the backend stops scanning pod logs and polling CPU/memory. The 24h error/warning counts and
            per-pod resource columns will go blank until re-enabled.
          </p>
          <label class="toggle-row">
            <input type="checkbox" [(ngModel)]="enabled" />
            <span>Enable pod monitoring</span>
          </label>
        </section>

        <section class="card" [class.disabled]="!enabled()">
          <h2>Intervals</h2>
          <p class="hint">
            Lower values produce fresher data but more load on the cluster API. The 24-hour error/warning counts in
            particular involve fetching pod logs and should not run too aggressively.
          </p>

          <div class="form-row">
            <label>
              <span>24h error/warning scan interval</span>
              <input type="number" min="30" step="30" [(ngModel)]="logScanSeconds" />
              <span class="unit">seconds (≈ {{ logScanSeconds() | number: '1.1-1' }} × the prior value)</span>
            </label>
            <p class="help">Default: 300s (5 minutes). Minimum: 30s.</p>
          </div>

          <div class="form-row">
            <label>
              <span>CPU / memory poll interval</span>
              <input type="number" min="15" step="15" [(ngModel)]="metricsSeconds" />
              <span class="unit">seconds</span>
            </label>
            <p class="help">Default: 60s. Minimum: 15s. Backend pulls from metrics-server.</p>
          </div>

          <div class="form-row">
            <label>
              <span>Error/warning lookback window</span>
              <input type="number" min="60" step="60" [(ngModel)]="logWindowSeconds" />
              <span class="unit">seconds ({{ logWindowSeconds() / 3600 | number: '1.1-1' }}h)</span>
            </label>
            <p class="help">Default: 86400s (24 hours).</p>
          </div>
        </section>

        <section class="card" [class.disabled]="!enabled()">
          <h2>Monitored Pods</h2>
          <p class="hint">
            All pods are monitored by default. Uncheck a pod to opt it out of both log scanning and CPU/memory polling.
          </p>

          <div class="pods-toolbar">
            <div class="search">
              <svg lucideIcon="search"></svg>
              <input type="text" placeholder="Filter pods…" [(ngModel)]="podFilter" />
            </div>
            <span class="count">{{ enabledCount() }} of {{ podOptions().length }} enabled</span>
            <button class="link-btn" (click)="enableAll()">Enable all</button>
            <button class="link-btn" (click)="disableAll()">Disable all</button>
          </div>

          <div class="pods-list">
            @for (p of filteredPods(); track p.key) {
              <label class="pod-row">
                <input type="checkbox" [checked]="p.enabled" (change)="togglePod(p.key, $any($event.target).checked)" />
                <span class="ns">{{ p.namespace }}</span>
                <span class="sep">/</span>
                <span class="name">{{ p.name }}</span>
              </label>
            }
            @if (filteredPods().length === 0) {
              <div class="empty">No pods match the filter.</div>
            }
          </div>
        </section>

        <footer class="actions">
          @if (saveError()) {
            <span class="error">{{ saveError() }}</span>
          }
          @if (saved()) {
            <span class="saved">Saved.</span>
          }
          <button class="primary" [disabled]="saving()" (click)="save()">
            <svg lucideIcon="save"></svg>
            {{ saving() ? 'Saving…' : 'Save changes' }}
          </button>
        </footer>
      }
    </div>
  `,
  styleUrls: ['./settings-page.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPageComponent implements OnInit {
  private api = inject(MonitorSettingsApiService);
  private k8s = inject(KubernetesApiService);
  private router = inject(Router);

  loading = signal(true);
  saving = signal(false);
  saved = signal(false);
  saveError = signal<string | null>(null);
  settings = signal<MonitorSettings | null>(null);

  enabled = signal(true);
  logScanSeconds = signal(300);
  metricsSeconds = signal(60);
  logWindowSeconds = signal(86400);

  pods = signal<Pod[]>([]);
  excluded = signal<Set<string>>(new Set());
  podFilter = signal('');

  podOptions = computed<PodOption[]>(() => {
    const excluded = this.excluded();
    const all = this.pods().map((p) => {
      const namespace = p.metadata.namespace ?? '';
      const name = p.metadata.name ?? '';
      const key = `${namespace}/${name}`;
      return { key, namespace, name, enabled: !excluded.has(key) };
    });
    all.sort((a, b) => a.key.localeCompare(b.key));
    return all;
  });

  filteredPods = computed<PodOption[]>(() => {
    const q = this.podFilter().trim().toLowerCase();
    if (!q) return this.podOptions();
    return this.podOptions().filter((p) => p.key.toLowerCase().includes(q));
  });

  enabledCount = computed(() => this.podOptions().filter((p) => p.enabled).length);

  ngOnInit() {
    this.api.load().subscribe({
      next: (s) => {
        this.settings.set(s);
        this.enabled.set(s.enabled);
        this.logScanSeconds.set(s.logScanIntervalSeconds);
        this.metricsSeconds.set(s.metricsPollIntervalSeconds);
        this.logWindowSeconds.set(s.logWindowSeconds);
        this.excluded.set(new Set(s.excludedPods));
        this.loadPods();
      },
      error: () => {
        this.loading.set(false);
        this.saveError.set('Failed to load settings');
      },
    });
  }

  private loadPods() {
    this.k8s.getPods().subscribe({
      next: (pods) => {
        this.pods.set(pods);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
      },
    });
  }

  togglePod(key: string, enabled: boolean) {
    const next = new Set(this.excluded());
    if (enabled) next.delete(key);
    else next.add(key);
    this.excluded.set(next);
    this.saved.set(false);
  }

  enableAll() {
    this.excluded.set(new Set());
    this.saved.set(false);
  }

  disableAll() {
    this.excluded.set(new Set(this.podOptions().map((p) => p.key)));
    this.saved.set(false);
  }

  save() {
    const payload: MonitorSettings = {
      enabled: this.enabled(),
      logScanIntervalSeconds: Number(this.logScanSeconds()),
      metricsPollIntervalSeconds: Number(this.metricsSeconds()),
      logWindowSeconds: Number(this.logWindowSeconds()),
      excludedPods: Array.from(this.excluded()).sort(),
    };
    this.saving.set(true);
    this.saveError.set(null);
    this.saved.set(false);
    this.api.save(payload).subscribe({
      next: (s) => {
        this.settings.set(s);
        this.enabled.set(s.enabled);
        this.logScanSeconds.set(s.logScanIntervalSeconds);
        this.metricsSeconds.set(s.metricsPollIntervalSeconds);
        this.logWindowSeconds.set(s.logWindowSeconds);
        this.excluded.set(new Set(s.excludedPods));
        this.saving.set(false);
        this.saved.set(true);
      },
      error: (err) => {
        this.saving.set(false);
        this.saveError.set(err?.message || 'Save failed');
      },
    });
  }

  goBack() {
    this.router.navigate(['/dashboard']);
  }
}
