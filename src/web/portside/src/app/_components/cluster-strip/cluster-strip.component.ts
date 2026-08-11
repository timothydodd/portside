import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { Subject, takeUntil } from 'rxjs';
import { LucideDynamicIcon } from '@lucide/angular';
import { Cluster, KubernetesApiService } from '../../_services/kubernetes.api';
import { SignalRService } from '../../_services/api/signalr.service';

@Component({
  selector: 'app-cluster-strip',
  standalone: true,
  imports: [LucideDynamicIcon],
  templateUrl: './cluster-strip.component.html',
  styleUrls: ['./cluster-strip.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClusterStripComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  private kubernetesApi = inject(KubernetesApiService);
  private signalRService = inject(SignalRService);

  cluster = signal<Cluster | null>(null);

  cpuPct = computed(() => Math.round(this.cluster()?.cpuPercentage ?? 0));
  memPct = computed(() => Math.round(this.cluster()?.memoryPercentage ?? 0));
  nodes = computed(() => this.cluster()?.nodes ?? []);

  ngOnInit() {
    this.kubernetesApi
      .getClusterMetrics()
      .pipe(takeUntil(this.destroy$))
      .subscribe({ next: (c) => this.cluster.set(c), error: () => {} });

    this.signalRService.clusterUpdate.pipe(takeUntil(this.destroy$)).subscribe((data) => {
      if (data) this.cluster.set(data);
    });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  round(n: number | undefined | null): number {
    return Math.round(n ?? 0);
  }

  pctTone(p: number): 'ok' | 'warn' | 'crit' {
    if (p >= 90) return 'crit';
    if (p >= 75) return 'warn';
    return 'ok';
  }
}
