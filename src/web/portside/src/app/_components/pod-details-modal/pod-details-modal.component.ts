import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { ModalComponent, ModalContainerService, ModalLayoutComponent } from '@rd-ui';
import { Pod } from '../../_models/kubernetes.interfaces';
import { UserPreferencesService } from '../../_services/user-preferences.service';

@Component({
  selector: 'app-pod-details-modal',
  standalone: true,
  imports: [LucideDynamicIcon, ModalLayoutComponent],
  template: `
    <rd-modal-layout [title]="title()">
      <div slot="body" class="pod-details-body">
        @if (pod(); as p) {
          <section class="section">
            <div class="grid">
              <div class="field">
                <span class="label">Status</span>
                <span class="value status" [class]="'status-' + (p.status?.phase ?? 'unknown').toLowerCase()">
                  {{ p.status?.phase ?? 'Unknown' }}
                </span>
              </div>
              <div class="field">
                <span class="label">Namespace</span>
                <span class="value">{{ p.metadata.namespace }}</span>
              </div>
              <div class="field">
                <span class="label">Node</span>
                <span class="value">{{ p.spec.nodeName ?? 'Unscheduled' }}</span>
              </div>
              <div class="field">
                <span class="label">Pod IP</span>
                <span class="value mono">{{ p.status?.podIP ?? '-' }}</span>
              </div>
              <div class="field">
                <span class="label">Host IP</span>
                <span class="value mono">{{ p.status?.hostIP ?? '-' }}</span>
              </div>
              <div class="field">
                <span class="label">Service Account</span>
                <span class="value">{{ p.spec.serviceAccountName ?? 'default' }}</span>
              </div>
              <div class="field">
                <span class="label">Created</span>
                <span class="value">{{ p.metadata.creationTimestamp ?? '-' }}</span>
              </div>
              <div class="field">
                <span class="label">UID</span>
                <span class="value mono small">{{ p.metadata.uid ?? '-' }}</span>
              </div>
            </div>
          </section>

          @if (p.metrics) {
            <section class="section">
              <h4>Resource usage</h4>
              <div class="grid">
                <div class="field">
                  <span class="label">CPU</span>
                  <span class="value">
                    @if (p.metrics.cpuPercent !== undefined) {
                      {{ p.metrics.cpuPercent }}%
                    } @else { - }
                  </span>
                </div>
                <div class="field">
                  <span class="label">Memory</span>
                  <span class="value">
                    @if (p.metrics.memory !== undefined) {
                      {{ formatMemory(p.metrics.memory) }}
                      @if (p.metrics.memoryPercent !== undefined) {
                        ({{ p.metrics.memoryPercent }}%)
                      }
                    } @else { - }
                  </span>
                </div>
              </div>
            </section>
          }

          @if ((p.status?.containerStatuses?.length ?? 0) > 0) {
            <section class="section">
              <h4>Containers</h4>
              <table class="containers-table">
                <thead>
                  <tr>
                    <th>Name</th>
                    <th>Ready</th>
                    <th>State</th>
                    <th>Restarts</th>
                    <th>Image</th>
                  </tr>
                </thead>
                <tbody>
                  @for (c of p.status?.containerStatuses ?? []; track c.name) {
                    <tr>
                      <td class="mono">{{ c.name }}</td>
                      <td>
                        <svg [lucideIcon]="c.ready ? 'check-circle' : 'x-circle'"
                                     [class.ok]="c.ready"
                                     [class.bad]="!c.ready"></svg>
                      </td>
                      <td>{{ containerStateLabel(c.state) }}</td>
                      <td [class.warn]="c.restartCount > 0">{{ c.restartCount }}</td>
                      <td class="mono small">{{ c.image }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </section>
          }

          <section class="section">
            <h4>Settings</h4>
            <label class="setting-row">
              <span class="setting-text">
                <span class="setting-title">Fetch 24h log counts</span>
                <span class="setting-help">Polls error/warning counts for this pod and shows them in the dashboard's "24h Errors" column.</span>
              </span>
              <input type="checkbox"
                     class="setting-toggle"
                     [checked]="countsEnabled()"
                     (change)="toggleCounts($any($event.target).checked)" />
            </label>
          </section>
        } @else {
          <p class="empty">No pod data.</p>
        }
      </div>

      <div slot="footer">
        <button class="btn btn-secondary" (click)="close()">Close</button>
        @if (pod(); as p) {
          <button class="btn btn-primary" (click)="viewLogs(p)">
            <svg lucideIcon="server"></svg>
            View Logs
          </button>
        }
      </div>
    </rd-modal-layout>
  `,
  styleUrl: './pod-details-modal.component.scss',
})
export class PodDetailsModalComponent implements OnInit {
  private modalContainerService = inject(ModalContainerService);
  private modalComponent = inject(ModalComponent);
  private userPrefs = inject(UserPreferencesService);
  private router = inject(Router);

  pod = signal<Pod | null>(null);

  title = computed(() => {
    const p = this.pod();
    return p ? `Pod · ${p.metadata.name}` : 'Pod details';
  });

  countsEnabled = computed(() => {
    const p = this.pod();
    if (!p) return false;
    const key = `${p.metadata.namespace}/${p.metadata.name}`;
    return this.userPrefs.podCountsEnabled().has(key);
  });

  ngOnInit(): void {
    const data = this.modalComponent.config?.data;
    if (data?.pod) this.pod.set(data.pod as Pod);
  }

  toggleCounts(enabled: boolean) {
    const p = this.pod();
    if (!p) return;
    const key = `${p.metadata.namespace}/${p.metadata.name}`;
    this.userPrefs.setPodCountEnabled(key, enabled);
  }

  containerStateLabel(state: Pod['status'] extends infer _ ? any : never): string {
    if (!state) return '-';
    if (state.running) return 'Running';
    if (state.waiting) return state.waiting.reason ? `Waiting (${state.waiting.reason})` : 'Waiting';
    if (state.terminated) return state.terminated.reason ? `Terminated (${state.terminated.reason})` : 'Terminated';
    return '-';
  }

  formatMemory(bytes: number): string {
    const units = ['B', 'Ki', 'Mi', 'Gi', 'Ti'];
    let size = bytes;
    let i = 0;
    while (size >= 1024 && i < units.length - 1) { size /= 1024; i++; }
    return `${size.toFixed(1)}${units[i]}`;
  }

  viewLogs(p: Pod) {
    this.close();
    this.router.navigate(['/logs'], {
      queryParams: { namespace: p.metadata.namespace, pod: p.metadata.name },
    });
  }

  close() {
    this.modalContainerService.closeAll();
  }
}
