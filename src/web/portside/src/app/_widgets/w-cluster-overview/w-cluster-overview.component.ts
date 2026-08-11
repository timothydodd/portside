import { Component, OnInit, OnDestroy, signal, computed, inject } from '@angular/core';
import { Subject, takeUntil } from 'rxjs';
import { KubernetesApiService, Cluster } from '../../_services/kubernetes.api';
import { SignalRService } from '../../_services/api/signalr.service';
import { LoadingSpinnerComponent } from '../../_components/loading-spinner/loading-spinner.component';
import { ProgressBarComponent } from '../../_components/progress-bar/progress-bar.component';
import { FlashLabelComponent } from '../../_components/flash-label/flash-label.component';
import { LucideDynamicIcon } from '@lucide/angular';

@Component({
  selector: 'app-w-cluster-overview',
  standalone: true,
  imports: [LoadingSpinnerComponent, ProgressBarComponent, FlashLabelComponent, LucideDynamicIcon],
  templateUrl: './w-cluster-overview.component.html',
  styleUrls: ['./w-cluster-overview.component.scss'],
})
export class WClusterOverviewComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  private kubernetesApi = inject(KubernetesApiService);
  private signalRService = inject(SignalRService);

  loading = signal(true);
  error = signal<string | null>(null);
  cluster = signal<Cluster | null>(null);

  cpuUsagePercent = computed(() => {
    const c = this.cluster();
    return c ? Math.round(c.cpuPercentage) : 0;
  });

  memoryUsagePercent = computed(() => {
    const c = this.cluster();
    return c ? Math.round(c.memoryPercentage) : 0;
  });

  nodeCount = computed(() => {
    const c = this.cluster();
    return c?.nodes?.length || 0;
  });

  ngOnInit() {
    this.loadClusterMetrics();
    this.setupSignalRSubscriptions();
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadClusterMetrics() {
    this.error.set(null);

    this.kubernetesApi
      .getClusterMetrics()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (metrics) => {
          this.cluster.set(metrics);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load cluster metrics');
          this.loading.set(false);
        },
      });
  }

  private setupSignalRSubscriptions() {
    this.signalRService.clusterUpdate.pipe(takeUntil(this.destroy$)).subscribe((data) => {
      if (data) {
        this.cluster.set(data);
        this.loading.set(false);
      }
    });
  }

  retry() {
    this.loadClusterMetrics();
  }

  formatMemory(bytes: number): string {
    const gb = bytes / (1024 * 1024 * 1024);
    return gb.toFixed(1) + ' GB';
  }

  formatCPU(cores: number): string {
    return cores.toFixed(1) + ' cores';
  }

  round(n: number | undefined | null): number {
    return Math.round(n ?? 0);
  }
}
