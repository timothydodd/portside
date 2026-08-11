import { Component, OnInit, OnDestroy, effect, signal, computed, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { KubernetesApiService } from '../../_services/kubernetes.api';
import { SignalRService } from '../../_services/api/signalr.service';
import { LucideDynamicIcon } from '@lucide/angular';
import { Pod, PodMetrics } from '../../_models/kubernetes.interfaces';
import { ModalContainerService } from '@rd-ui';
import { PodDetailsModalComponent } from '../../_components/pod-details-modal/pod-details-modal.component';
import { ColumnFilterComponent, ColumnFilterItem } from '../../_components/column-filter/column-filter.component';

interface LogCounts {
  error: number;
  warning: number;
}

type SortKey = 'name' | 'namespace' | 'status' | 'cpu' | 'memory' | 'age' | 'errors';
type SortDir = 'asc' | 'desc';

const PODS_FILTER_KEY = 'portside:pods-widget-filters';
interface PersistedPodsFilters {
  search?: string;
  excludedNamespaces?: string[];
  excludedStatuses?: string[];
  sortKey?: SortKey;
  sortDir?: SortDir;
}

const KNOWN_STATUSES = ['Running', 'Pending', 'Failed', 'Succeeded', 'Unknown'];

@Component({
  selector: 'app-w-pods',
  standalone: true,
  imports: [LucideDynamicIcon, RouterLink, FormsModule, ColumnFilterComponent],
  template: `
    <div class="pods-widget">
      @if (loading() && pods().length === 0) {
        <div class="pods-content skeleton" aria-busy="true" aria-label="Loading pods">
          <div class="pods-toolbar">
            <div class="search skeleton-bar skeleton-bar-wide"></div>
            <span class="skeleton-bar skeleton-bar-short"></span>
          </div>
          <div class="pods-table-container">
            <table class="pods-table">
              <thead>
                <tr>
                  @for (col of skeletonColumns; track col) {
                    <th><span class="skeleton-bar skeleton-bar-th"></span></th>
                  }
                </tr>
              </thead>
              <tbody>
                @for (row of skeletonRows; track row) {
                  <tr class="pod-row skeleton-row">
                    <td><span class="skeleton-bar skeleton-bar-name"></span></td>
                    <td><span class="skeleton-bar skeleton-bar-pill"></span></td>
                    <td><span class="skeleton-bar skeleton-bar-pill"></span></td>
                    <td><span class="skeleton-bar skeleton-bar-tiny"></span></td>
                    <td><span class="skeleton-bar skeleton-bar-metric"></span></td>
                    <td><span class="skeleton-bar skeleton-bar-metric"></span></td>
                    <td><span class="skeleton-bar skeleton-bar-medium"></span></td>
                    <td><span class="skeleton-bar skeleton-bar-tiny"></span></td>
                    <td><span class="skeleton-bar skeleton-bar-short"></span></td>
                    <td><span class="skeleton-bar skeleton-bar-button"></span></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      } @else if (error()) {
        <div class="error-state">
          <svg lucideIcon="alert-circle" class="error-icon"></svg>
          <p>{{ error() }}</p>
          <button (click)="retry()" class="retry-btn">
            <svg lucideIcon="refresh-cw"></svg>
            Retry
          </button>
        </div>
      } @else {
        <div class="pods-content">
          <div class="pods-toolbar">
            <div class="search">
              <svg lucideIcon="search"></svg>
              <input type="text"
                     [(ngModel)]="searchText"
                     placeholder="Search pods..."
                     aria-label="Search pods" />
            </div>
            <span class="result-count">{{ filteredPods().length }} of {{ pods().length }}</span>
          </div>

          @if (filteredPods().length === 0) {
            <div class="empty-state">
              <svg lucideIcon="package"></svg>
              <p>{{ pods().length === 0 ? 'No pods found' : 'No pods match the current filters' }}</p>
            </div>
          } @else {
            <div class="pods-table-container">
              <table class="pods-table">
                <thead>
                  <tr>
                    <th class="sortable" (click)="setSort('name')">
                      <span class="th-label">Name {{ sortIndicator('name') }}</span>
                    </th>
                    <th class="sortable" (click)="setSort('namespace')">
                      <span class="th-label">Namespace {{ sortIndicator('namespace') }}</span>
                      <app-column-filter
                        label="Namespace"
                        searchPlaceholder="Search namespaces..."
                        [items]="namespaceItems()"
                        [selected]="namespaceSelected()"
                        (selectionChange)="onNamespaceFilter($event)" />
                    </th>
                    <th class="sortable" (click)="setSort('status')">
                      <span class="th-label">Status {{ sortIndicator('status') }}</span>
                      <app-column-filter
                        label="Status"
                        [searchable]="false"
                        [items]="statusItems()"
                        [selected]="statusSelected()"
                        (selectionChange)="onStatusFilter($event)" />
                    </th>
                    <th><span class="th-label">Ready</span></th>
                    <th class="sortable" (click)="setSort('cpu')">
                      <span class="th-label">CPU {{ sortIndicator('cpu') }}</span>
                    </th>
                    <th class="sortable" (click)="setSort('memory')">
                      <span class="th-label">Memory {{ sortIndicator('memory') }}</span>
                    </th>
                    <th><span class="th-label">Node</span></th>
                    <th class="sortable" (click)="setSort('age')">
                      <span class="th-label">Age {{ sortIndicator('age') }}</span>
                    </th>
                    <th class="sortable" (click)="setSort('errors')">
                      <span class="th-label">24h Errors {{ sortIndicator('errors') }}</span>
                      <a class="settings-link"
                         [routerLink]="['/settings']"
                         title="Configure monitoring (backend pushes these counts; opt pods out in Settings)"
                         (click)="$event.stopPropagation()">
                        <svg lucideIcon="settings"></svg>
                      </a>
                    </th>
                    <th><span class="th-label">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (pod of filteredPods(); track pod.metadata.namespace + '/' + pod.metadata.name) {
                    <tr class="pod-row clickable" [class]="getPodStatusClass(pod)"
                        title="Open logs"
                        (click)="goToLogs(pod)">
                      <td class="pod-name-cell">
                        <span class="pod-name">{{ pod.metadata.name }}</span>
                      </td>
                      <td>
                        <span class="namespace-badge">{{ pod.metadata.namespace }}</span>
                      </td>
                      <td class="status-cell">
                        <div class="status-indicator">
                          <svg [lucideIcon]="getPodStatusIcon(pod)" class="status-icon"></svg>
                          <span class="status-text">{{ pod.status?.phase || 'Unknown' }}</span>
                        </div>
                      </td>
                      <td class="ready-cell">
                        @if (pod.status?.containerStatuses) {
                          <div class="container-info">
                            <svg lucideIcon="box"></svg>
                            <span>{{ getReadyContainers(pod) }}/{{ pod.status?.containerStatuses?.length || 0 }}</span>
                          </div>
                        } @else {
                          <span class="muted">-</span>
                        }
                      </td>
                      <td class="cpu-cell">
                        @let m = getMetrics(pod);
                        @if (m && m.cpuPercent !== undefined && m.cpuPercent !== null) {
                          <div class="resource-info">
                            <svg lucideIcon="cpu"></svg>
                            <span>{{ m.cpuPercent }}%</span>
                            <svg class="sparkline" viewBox="0 0 60 20" preserveAspectRatio="none"
                                 [attr.aria-label]="'CPU history for ' + pod.metadata.name">
                              @let hist = cpuSparkline(pod);
                              @if (hist.area) {
                                <path class="spark-area" [attr.d]="hist.area" />
                              }
                              @if (hist.line) {
                                <path class="spark-line" [attr.d]="hist.line" />
                              }
                            </svg>
                          </div>
                        } @else {
                          <span class="muted">-</span>
                        }
                      </td>
                      <td class="memory-cell">
                        @let mm = getMetrics(pod);
                        @if (mm && mm.memory !== undefined && mm.memory !== null) {
                          <div class="resource-info">
                            <svg lucideIcon="memory-stick"></svg>
                            <span>{{ formatMemory(mm.memory) }}</span>
                            @if (mm.memoryPercent !== undefined && mm.memoryPercent !== null) {
                              <span class="resource-percent">({{ mm.memoryPercent }}%)</span>
                            }
                          </div>
                        } @else {
                          <span class="muted">-</span>
                        }
                      </td>
                      <td class="node-cell">
                        <div class="node-info">
                          <svg lucideIcon="server"></svg>
                          <span>{{ pod.spec.nodeName ?? 'Unscheduled' }}</span>
                        </div>
                      </td>
                      <td class="age-cell">
                        @if (pod.metadata.creationTimestamp) {
                          <div class="age-info">
                            <svg lucideIcon="clock"></svg>
                            <span>{{ getAge(pod.metadata.creationTimestamp) }}</span>
                          </div>
                        } @else {
                          <span class="muted">-</span>
                        }
                      </td>
                      <td class="counts-cell">
                        @let counts = getCounts(pod);
                        @if (counts && (counts.error > 0 || counts.warning > 0)) {
                          <div class="counts-info">
                            @if (counts.error > 0) {
                              <button type="button"
                                      class="count-pill error"
                                      title="View errors in logs"
                                      (click)="goToLogs(pod, 'Error'); $event.stopPropagation()">
                                <svg lucideIcon="x-circle"></svg> {{ counts.error }}
                              </button>
                            }
                            @if (counts.warning > 0) {
                              <button type="button"
                                      class="count-pill warning"
                                      title="View warnings in logs"
                                      (click)="goToLogs(pod, 'Warning'); $event.stopPropagation()">
                                <svg lucideIcon="alert-circle"></svg> {{ counts.warning }}
                              </button>
                            }
                          </div>
                        }
                      </td>
                      <td class="actions-cell">
                        <button type="button"
                                class="details-btn"
                                title="View pod details"
                                (click)="openPodDetails(pod); $event.stopPropagation()">
                          <svg lucideIcon="info"></svg>
                          <span>Details</span>
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </div>
      }
    </div>
  `,
  styleUrls: ['./w-pods.component.scss'],
})
export class WPodsComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  private kubernetesApi = inject(KubernetesApiService);
  private signalRService = inject(SignalRService);
  private modalService = inject(ModalContainerService);
  private router = inject(Router);

  loading = signal(true);
  error = signal<string | null>(null);
  pods = signal<Pod[]>([]);

  readonly skeletonRows = Array.from({ length: 8 }, (_, i) => i);
  readonly skeletonColumns = Array.from({ length: 10 }, (_, i) => i);

  // Filters / sort state
  searchText = signal<string>('');
  excludedNamespaces = signal<Set<string>>(new Set());
  excludedStatuses = signal<Set<string>>(new Set());
  sortKey = signal<SortKey>('name');
  sortDir = signal<SortDir>('asc');


  namespaceItems = computed<ColumnFilterItem[]>(() => {
    const set = new Set<string>();
    this.pods().forEach((p) => p.metadata.namespace && set.add(p.metadata.namespace));
    return Array.from(set).sort().map((ns) => ({ label: ns, value: ns }));
  });

  statusItems = computed<ColumnFilterItem[]>(() => {
    const set = new Set<string>(KNOWN_STATUSES);
    this.pods().forEach((p) => p.status?.phase && set.add(p.status.phase));
    return Array.from(set).sort().map((s) => ({ label: s, value: s }));
  });

  // Selected sets shown by the column-filter (items minus excluded)
  namespaceSelected = computed<Set<string>>(() => {
    const excluded = this.excludedNamespaces();
    return new Set(this.namespaceItems().map((i) => i.value).filter((v) => !excluded.has(v)));
  });

  statusSelected = computed<Set<string>>(() => {
    const excluded = this.excludedStatuses();
    return new Set(this.statusItems().map((i) => i.value).filter((v) => !excluded.has(v)));
  });

  filteredPods = computed(() => {
    const q = this.searchText().trim().toLowerCase();
    const excludedNs = this.excludedNamespaces();
    const excludedSt = this.excludedStatuses();
    let list = this.pods().filter((p) => {
      const ns = p.metadata.namespace ?? '';
      const phase = p.status?.phase ?? 'Unknown';
      if (excludedNs.has(ns)) return false;
      if (excludedSt.has(phase)) return false;
      if (q) {
        const hay = `${p.metadata.name ?? ''} ${ns} ${p.spec?.nodeName ?? ''}`.toLowerCase();
        if (!hay.includes(q)) return false;
      }
      return true;
    });

    const key = this.sortKey();
    const dir = this.sortDir() === 'asc' ? 1 : -1;
    list = [...list].sort((a, b) => this.compare(a, b, key) * dir);
    return list;
  });

  onNamespaceFilter(selected: Set<string>) {
    const all = this.namespaceItems().map((i) => i.value);
    this.excludedNamespaces.set(new Set(all.filter((v) => !selected.has(v))));
  }

  onStatusFilter(selected: Set<string>) {
    const all = this.statusItems().map((i) => i.value);
    this.excludedStatuses.set(new Set(all.filter((v) => !selected.has(v))));
  }

  setSort(key: SortKey) {
    if (this.sortKey() === key) {
      this.sortDir.set(this.sortDir() === 'asc' ? 'desc' : 'asc');
    } else {
      this.sortKey.set(key);
      this.sortDir.set(key === 'cpu' || key === 'memory' || key === 'errors' || key === 'age' ? 'desc' : 'asc');
    }
  }

  sortIndicator(key: SortKey): string {
    if (this.sortKey() !== key) return '';
    return this.sortDir() === 'asc' ? '▲' : '▼';
  }

  private compare(a: Pod, b: Pod, key: SortKey): number {
    switch (key) {
      case 'name':
        return (a.metadata.name ?? '').localeCompare(b.metadata.name ?? '');
      case 'namespace':
        return (a.metadata.namespace ?? '').localeCompare(b.metadata.namespace ?? '');
      case 'status':
        return (a.status?.phase ?? '').localeCompare(b.status?.phase ?? '');
      case 'cpu': {
        const ma = this.getMetrics(a);
        const mb = this.getMetrics(b);
        return (ma?.cpuPercent ?? -1) - (mb?.cpuPercent ?? -1);
      }
      case 'memory': {
        const ma = this.getMetrics(a);
        const mb = this.getMetrics(b);
        return ((ma?.memoryPercent ?? ma?.memory ?? -1) as number) -
               ((mb?.memoryPercent ?? mb?.memory ?? -1) as number);
      }
      case 'age':
        return new Date(a.metadata.creationTimestamp ?? 0).getTime() -
               new Date(b.metadata.creationTimestamp ?? 0).getTime();
      case 'errors': {
        const ac = this.getCounts(a)?.error ?? -1;
        const bc = this.getCounts(b)?.error ?? -1;
        return ac - bc;
      }
    }
  }

  getCounts(pod: Pod): LogCounts | null {
    return pod.counts ?? null;
  }

  cpuSparkline(pod: Pod): { line: string | null; area: string | null } {
    const series = pod.metrics?.history;
    if (!series || series.length < 2) return { line: null, area: null };

    const w = 60;
    const h = 20;
    const pad = 1;
    // Scale Y to the visible range so even small fluctuations are readable,
    // but pin the floor at 0 so flat-low pods don't appear noisy.
    const max = Math.max(...series, 1);
    const min = 0;
    const range = max - min || 1;
    const stepX = series.length > 1 ? (w - pad * 2) / (series.length - 1) : 0;

    const points = series.map((v, i) => {
      const x = pad + i * stepX;
      const y = pad + (h - pad * 2) * (1 - (v - min) / range);
      return `${x.toFixed(2)},${y.toFixed(2)}`;
    });
    const line = `M${points.join(' L')}`;
    const first = points[0].split(',')[0];
    const last = points[points.length - 1].split(',')[0];
    const area = `M${first},${h} L${points.join(' L')} L${last},${h} Z`;
    return { line, area };
  }

  getMetrics(pod: Pod): PodMetrics | null {
    return pod.metrics ?? null;
  }

  // Computed properties for statistics
  runningCount = computed(() => 
    this.pods().filter(pod => pod.status?.phase === 'Running').length
  );

  pendingCount = computed(() => 
    this.pods().filter(pod => pod.status?.phase === 'Pending').length
  );

  failedCount = computed(() => 
    this.pods().filter(pod => pod.status?.phase === 'Failed').length
  );

  constructor() {
    this.restoreFilters();
    effect(() => this.persistFilters());
  }

  private restoreFilters() {
    try {
      const raw = localStorage.getItem(PODS_FILTER_KEY);
      if (!raw) return;
      const f = JSON.parse(raw) as PersistedPodsFilters;
      if (typeof f.search === 'string') this.searchText.set(f.search);
      if (Array.isArray(f.excludedNamespaces)) this.excludedNamespaces.set(new Set(f.excludedNamespaces));
      if (Array.isArray(f.excludedStatuses)) this.excludedStatuses.set(new Set(f.excludedStatuses));
      if (f.sortKey) this.sortKey.set(f.sortKey);
      if (f.sortDir) this.sortDir.set(f.sortDir);
    } catch {
      /* ignore */
    }
  }

  private persistFilters() {
    const f: PersistedPodsFilters = {
      search: this.searchText(),
      excludedNamespaces: Array.from(this.excludedNamespaces()),
      excludedStatuses: Array.from(this.excludedStatuses()),
      sortKey: this.sortKey(),
      sortDir: this.sortDir(),
    };
    try {
      localStorage.setItem(PODS_FILTER_KEY, JSON.stringify(f));
    } catch {
      /* ignore */
    }
  }

  ngOnInit() {
    this.loadPods();
    this.setupSignalRSubscriptions();
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private loadPods() {
    this.error.set(null);

    this.kubernetesApi
      .getPods()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (pods) => {
          this.pods.set(pods);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load pods');
          this.loading.set(false);
        },
      });
  }

  private setupSignalRSubscriptions() {
    // Pod lifecycle (Added/Modified/Deleted) keeps the list fresh; counts and metrics
    // ride along on the pod object, refreshed each time getPods() runs.
    this.signalRService.podUpdate.pipe(takeUntil(this.destroy$)).subscribe((data) => {
      if (!data?.pod) return;
      this.applyPodEvent(data.eventType, data.pod as Pod);
    });
  }

  private applyPodEvent(eventType: string, pod: Pod) {
    const uid = pod.metadata?.uid;
    if (!uid) return;
    const current = this.pods();
    const idx = current.findIndex((p) => p.metadata?.uid === uid);

    switch (eventType) {
      case 'Added':
      case 'Modified': {
        const next = idx >= 0 ? [...current] : [...current, pod];
        if (idx >= 0) next[idx] = pod;
        this.pods.set(next);
        break;
      }
      case 'Deleted': {
        if (idx >= 0) {
          const next = current.filter((_, i) => i !== idx);
          this.pods.set(next);
        }
        break;
      }
      default:
        break;
    }
  }

  retry() {
    this.loadPods();
  }

  getPodStatusClass(pod: Pod): string {
    const phase = pod.status?.phase?.toLowerCase();
    switch (phase) {
      case 'running':
        return 'status-running';
      case 'pending':
        return 'status-pending';
      case 'failed':
        return 'status-failed';
      case 'succeeded':
        return 'status-succeeded';
      default:
        return 'status-unknown';
    }
  }

  getPodStatusIcon(pod: Pod): string {
    const phase = pod.status?.phase?.toLowerCase();
    switch (phase) {
      case 'running':
        return 'check-circle';
      case 'pending':
        return 'clock';
      case 'failed':
        return 'x-circle';
      case 'succeeded':
        return 'check-circle';
      default:
        return 'help-circle';
    }
  }

  getReadyContainers(pod: Pod): number {
    if (!pod.status?.containerStatuses) return 0;
    return pod.status.containerStatuses.filter(container => container.ready).length;
  }

  getAge(creationTimestamp: string): string {
    const now = new Date();
    const created = new Date(creationTimestamp);
    const diffMs = now.getTime() - created.getTime();
    
    const minutes = Math.floor(diffMs / (1000 * 60));
    const hours = Math.floor(minutes / 60);
    const days = Math.floor(hours / 24);

    if (days > 0) return `${days}d`;
    if (hours > 0) return `${hours}h`;
    return `${minutes}m`;
  }

  formatCPU(millicores: number): string {
    if (millicores >= 1000) {
      return `${(millicores / 1000).toFixed(1)}`;
    }
    return `${millicores}m`;
  }

  openPodDetails(pod: Pod) {
    this.modalService.openComponent(PodDetailsModalComponent, { data: { pod } });
  }

  goToLogs(pod: Pod, level?: 'Error' | 'Warning') {
    const queryParams: Record<string, string> = {
      namespace: pod.metadata.namespace ?? '',
      pod: pod.metadata.name ?? '',
    };
    if (level) queryParams['levels'] = level;
    this.router.navigate(['/logs'], { queryParams });
  }

  formatMemory(bytes: number): string {
    const units = ['B', 'Ki', 'Mi', 'Gi', 'Ti'];
    let size = bytes;
    let unitIndex = 0;
    
    while (size >= 1024 && unitIndex < units.length - 1) {
      size /= 1024;
      unitIndex++;
    }
    
    return `${size.toFixed(1)}${units[unitIndex]}`;
  }
}