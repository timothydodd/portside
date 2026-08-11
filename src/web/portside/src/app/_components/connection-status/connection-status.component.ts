import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';
import { SignalRService as ClusterHub } from '../../_services/api/signalr.service';
import { SignalRService as PodLogHub } from '../../_services/signalr.service';
import { AuthService } from '../../_services/auth-service';

@Component({
  selector: 'app-connection-status',
  standalone: true,
  imports: [LucideDynamicIcon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (visible()) {
      <div class="status" [class.online]="anyConnected()" [class.offline]="!anyConnected()" [title]="tooltip()">
        <span class="dot"></span>
        <svg [lucideIcon]="anyConnected() ? 'activity' : 'alert-circle'"></svg>
        <span class="label">{{ anyConnected() ? 'Live' : 'Offline' }}</span>
      </div>
    }
  `,
  styleUrl: './connection-status.component.scss',
})
export class ConnectionStatusComponent {
  private cluster = inject(ClusterHub);
  private podLog = inject(PodLogHub);
  private auth = inject(AuthService);

  clusterConnected = signal(false);
  podLogConnected = signal(false);
  loggedIn = signal(false);

  constructor() {
    this.cluster.connected$.subscribe((c) => this.clusterConnected.set(c));
    this.podLog.connected$.subscribe((c) => this.podLogConnected.set(c));
    this.auth.isLoggedIn.subscribe((v) => this.loggedIn.set(v));
  }

  visible = computed(() => this.loggedIn());
  anyConnected = computed(() => this.clusterConnected() || this.podLogConnected());
  tooltip = computed(() =>
    `Cluster hub: ${this.clusterConnected() ? 'connected' : 'offline'}\n` +
    `Pod log hub: ${this.podLogConnected() ? 'connected' : 'offline'}`,
  );
}
