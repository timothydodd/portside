import { ChangeDetectionStrategy, Component, OnDestroy, OnInit, inject, signal, computed } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { SelectComponent } from '@rd-ui';
import { Subscription } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from '../../_services/auth-service';
import { Log, SignalRService } from '../../_services/signalr.service';
import { HighlightLogPipe } from '../main-log-page/_services/highlight.directive';

interface LogRow {
  kind: 'log';
  log: Log;
}
interface DateRow {
  kind: 'date';
  label: string;
}
type FeedRow = LogRow | DateRow;

interface PodInfo {
  name: string;
  deployment: string;
  namespace: string;
  logLevel: string;
}

const LOG_LEVELS = ['Error', 'Warning', 'Information', 'Debug', 'Trace'] as const;
type LogLevel = (typeof LOG_LEVELS)[number];

interface TimeRange {
  label: string;
  seconds: number | null; // null = all available
}
const TIME_RANGES: TimeRange[] = [
  { label: 'Last 5 min', seconds: 5 * 60 },
  { label: 'Last 15 min', seconds: 15 * 60 },
  { label: 'Last 1 hour', seconds: 60 * 60 },
  { label: 'Last 6 hours', seconds: 6 * 60 * 60 },
  { label: 'Last 24 hours', seconds: 24 * 60 * 60 },
  { label: 'All available', seconds: null },
];


@Component({
  selector: 'app-pod-logs-page',
  standalone: true,
  imports: [FormsModule, LucideDynamicIcon, HighlightLogPipe, SelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './pod-logs-page.component.scss',
  template: `
    <div class="pod-logs-page">
      <div class="toolbar">
        <div class="control flex-grow">
          <label>Pod</label>
          <rd-select
            [items]="podOptions()"
            [searchable]="true"
            searchPlaceholder="Search pods..."
            placeholder="Select a pod"
            [minWidth]="240"
            size="compact"
            [ngModel]="selectedPod()"
            (ngModelChange)="onPodSelect($event)"
          ></rd-select>
        </div>
        <div class="control">
          <label>Time range</label>
          <rd-select
            [items]="timeRanges"
            placeholder="Time range"
            [minWidth]="160"
            size="compact"
            [ngModel]="selectedRange()"
            (ngModelChange)="onRangeSelect($event)"
          ></rd-select>
        </div>
        <div class="control">
          <label>Levels</label>
          <rd-select
            [items]="levelOptions"
            [multiple]="true"
            [showSelectAll]="true"
            placeholder="All levels"
            [minWidth]="160"
            size="compact"
            [ngModel]="selectedLevelValues()"
            (ngModelChange)="onLevelsChange($event)"
          ></rd-select>
        </div>
        <div class="control">
          <label>Search</label>
          <input type="text" [(ngModel)]="search" placeholder="Filter lines..." />
        </div>
        <button class="btn" (click)="clear()" title="Clear logs">
          <svg lucideIcon="trash-2"></svg> Clear
        </button>
        <button class="btn" (click)="reload()" [disabled]="!selectedPod()" title="Reload tail">
          <svg lucideIcon="refresh-cw"></svg> Reload
        </button>
      </div>

      @if (error()) {
        <div class="error-banner">
          <svg lucideIcon="alert-circle"></svg> {{ error() }}
        </div>
      }

      <div class="log-stream">
        @if (feed().length === 0) {
          <div class="empty">
            @if (selectedPod()) {
              <span>Waiting for log output...</span>
            } @else {
              <span>Select a pod above to start streaming.</span>
            }
          </div>
        } @else {
          @for (row of feed(); track $index) {
            @if (row.kind === 'date') {
              <div class="date-divider"><span>{{ row.label }}</span></div>
            } @else {
              <div class="log-line" [attr.data-level]="row.log.logLevel.toLowerCase()">
                <span class="ts">{{ formatTs(row.log.timeStamp) }}</span>
                <span class="lvl">{{ shortLevel(row.log.logLevel) }}</span>
                <span class="msg" [innerHTML]="row.log.line | highlightLog: search()"></span>
              </div>
            }
          }

          @if (selectedPod() && logs().length > 0) {
            <div class="pager">
              @if (noMoreOlder()) {
                <span class="pager-msg">No older logs available</span>
              } @else {
                <button class="btn" (click)="loadOlder()" [disabled]="loadingMore()">
                  @if (loadingMore()) {
                    <svg lucideIcon="loader-2"></svg> Loading...
                  } @else {
                    <svg lucideIcon="rotate-ccw"></svg> Load older
                  }
                </button>
              }
            </div>
          }
        }
      </div>
    </div>
  `,
})
export class PodLogsPageComponent implements OnInit, OnDestroy {
  private http = inject(HttpClient);
  private auth = inject(AuthService);
  private signalr = inject(SignalRService);
  private route = inject(ActivatedRoute);

  pods = signal<PodInfo[]>([]);
  selectedNamespace = signal<string>('');
  selectedPod = signal<PodInfo | null>(null);
  search = signal<string>('');
  logs = signal<Log[]>([]);
  error = signal<string | null>(null);
  selectedLevels = signal<Set<LogLevel>>(new Set(LOG_LEVELS));
  selectedRange = signal<TimeRange>(TIME_RANGES[2]); // default Last 1 hour
  tailLineCount = signal<number>(0); // 0 = use sinceSeconds; >0 = paginated tailLines mode
  loadingMore = signal<boolean>(false);
  noMoreOlder = signal<boolean>(false);

  private readonly INITIAL_TAIL = 2000;
  private readonly LOAD_MORE_CHUNK = 2000;
  private readonly MAX_TAIL = 50000;

  readonly levels = LOG_LEVELS;
  readonly timeRanges = TIME_RANGES.map((r) => ({ label: r.label, value: r }));
  readonly levelOptions = LOG_LEVELS.map((l) => ({ label: l, value: l }));

  podOptions = computed(() =>
    this.pods().map((p) => ({
      label: `${p.namespace} / ${p.name}`,
      value: p,
    })),
  );

  selectedLevelValues = computed(() => Array.from(this.selectedLevels()));

  private subs?: Subscription;
  private idSeed = 0;
  private maxLines = 5000;

  namespaces = computed(() => {
    const set = new Set(this.pods().map((p) => p.namespace));
    return Array.from(set).sort();
  });

  filteredPods = computed(() => {
    const ns = this.selectedNamespace();
    return ns ? this.pods().filter((p) => p.namespace === ns) : this.pods();
  });

  visibleLogs = computed(() => {
    const q = this.search().trim().toLowerCase();
    const levels = this.selectedLevels();
    const range = this.selectedRange();
    const cutoff = range.seconds ? Date.now() - range.seconds * 1000 : 0;
    return this.logs().filter((l) => {
      if (!levels.has(l.logLevel as LogLevel)) return false;
      if (cutoff) {
        const t = l.timeStamp instanceof Date ? l.timeStamp.getTime() : new Date(l.timeStamp as any).getTime();
        if (t < cutoff) return false;
      }
      if (q && !l.line.toLowerCase().includes(q)) return false;
      return true;
    });
  });

  onLevelsChange(levels: LogLevel[] | null) {
    const next = new Set<LogLevel>(levels ?? []);
    if (next.size === 0) LOG_LEVELS.forEach((l) => next.add(l));
    this.selectedLevels.set(next);
  }

  onRangeSelect(range: TimeRange | null) {
    if (range) {
      this.selectedRange.set(range);
      // Changing the range resets pagination back to time-range mode.
      this.tailLineCount.set(0);
      this.noMoreOlder.set(false);
      const pod = this.selectedPod();
      if (pod) this.fetchTail(pod);
    }
  }

  onPodSelect(pod: PodInfo | null) {
    this.selectedPod.set(pod);
    this.onPodChange();
  }

  feed = computed<FeedRow[]>(() => {
    // Render newest first; date dividers sit above the most-recent line of each day.
    const source = this.visibleLogs();
    const rows: FeedRow[] = [];
    let lastDay = '';
    for (let i = source.length - 1; i >= 0; i--) {
      const log = source[i];
      const d = log.timeStamp instanceof Date ? log.timeStamp : new Date(log.timeStamp as any);
      const dayKey = d.toDateString();
      if (dayKey !== lastDay) {
        rows.push({ kind: 'date', label: this.formatDateLabel(d) });
        lastDay = dayKey;
      }
      rows.push({ kind: 'log', log });
    }
    return rows;
  });

  private formatDateLabel(d: Date): string {
    const now = new Date();
    const today = now.toDateString();
    const yesterday = new Date(now.getTime() - 86400_000).toDateString();
    const key = d.toDateString();
    if (key === today) return `Today  ${d.toLocaleDateString('en-US')}`;
    if (key === yesterday) return `Yesterday  ${d.toLocaleDateString('en-US')}`;
    return d.toLocaleDateString('en-US', { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' });
  }

  constructor() {}

  ngOnInit() {
    this.loadPods();
    const token = this.auth.getToken();
    if (token) {
      this.signalr.startConnection(token).catch(() => { /* surfaced via connected$ */ });
    }
    this.subs = this.signalr.logsReceived.subscribe((batch) => this.appendLogs(batch));
  }

  ngOnDestroy() {
    this.subs?.unsubscribe();
    const pod = this.selectedPod();
    if (pod) {
      this.signalr.unsubscribeFromPod(pod.namespace, pod.name);
    }
  }

  private loadPods() {
    this.http.get<PodInfo[]>(`${environment.apiUrl}/api/log/pods`).subscribe({
      next: (pods) => {
        this.pods.set(pods);
        this.applyQueryParams();
      },
      error: (err) => this.error.set(err?.error?.error || 'Failed to load pod list'),
    });
  }

  private applyQueryParams() {
    const params = this.route.snapshot.queryParamMap;
    const ns = params.get('namespace');
    const name = params.get('pod');
    const levelsParam = params.get('levels');

    if (levelsParam) {
      const requested = levelsParam.split(',').map((s) => s.trim());
      const valid = requested.filter((l): l is LogLevel =>
        (LOG_LEVELS as readonly string[]).includes(l),
      );
      if (valid.length) this.selectedLevels.set(new Set(valid));
    }

    if (name) {
      const match = this.pods().find((p) => p.name === name && (!ns || p.namespace === ns));
      if (match) {
        if (ns) this.selectedNamespace.set(ns);
        this.selectedPod.set(match);
        this.onPodChange();
      }
    }
  }

  onNamespaceChange() {
    const pod = this.selectedPod();
    if (pod && pod.namespace !== this.selectedNamespace() && this.selectedNamespace() !== '') {
      this.unsubscribeCurrent();
      this.selectedPod.set(null);
      this.logs.set([]);
    }
  }

  onPodChange() {
    this.unsubscribeCurrent();
    this.logs.set([]);
    this.tailLineCount.set(0);
    this.noMoreOlder.set(false);
    const pod = this.selectedPod();
    if (!pod) return;
    this.error.set(null);
    this.fetchTail(pod);
    this.signalr.subscribeToPod(pod.namespace, pod.name).catch((err) => {
      this.error.set('Failed to subscribe to pod stream: ' + (err?.message || err));
    });
  }

  loadOlder() {
    const pod = this.selectedPod();
    if (!pod || this.loadingMore() || this.noMoreOlder()) return;

    // First click: switch from time-range mode to tail-line mode based on current size.
    let next = this.tailLineCount();
    if (next === 0) {
      next = Math.max(this.logs().length + this.LOAD_MORE_CHUNK, this.INITIAL_TAIL);
    } else {
      next = next + this.LOAD_MORE_CHUNK;
    }
    if (next > this.MAX_TAIL) next = this.MAX_TAIL;
    this.tailLineCount.set(next);
    this.fetchTail(pod, /* loadOlder */ true);
  }

  private fetchTail(pod: PodInfo, loadOlder = false) {
    const params = new URLSearchParams({ namespace: pod.namespace, pod: pod.name });
    const tail = this.tailLineCount();
    const range = this.selectedRange();

    if (tail > 0) {
      params.set('tailLines', String(tail));
    } else if (range.seconds) {
      params.set('sinceSeconds', String(range.seconds));
    } else {
      params.set('tailLines', String(this.INITIAL_TAIL));
    }

    const previousLength = this.logs().length;
    if (loadOlder) this.loadingMore.set(true);

    const url = `${environment.apiUrl}/api/log/tail?${params.toString()}`;
    this.http.get<{ lines: string[] }>(url).subscribe({
      next: (resp) => {
        const seed: Log[] = resp.lines.map((raw) => this.lineToLog(pod, raw));
        this.logs.set(seed);
        if (loadOlder) {
          this.loadingMore.set(false);
          // If we asked for more lines but got the same count back, kubelet has no older logs.
          if (seed.length <= previousLength) this.noMoreOlder.set(true);
          if (tail >= this.MAX_TAIL) this.noMoreOlder.set(true);
        } else {
          this.noMoreOlder.set(false);
        }
      },
      error: (err) => {
        this.loadingMore.set(false);
        this.error.set(err?.error?.error || 'Failed to fetch logs');
      },
    });
  }

  private lineToLog(pod: PodInfo, raw: string): Log {
    let ts = new Date();
    let content = raw;
    const space = raw.indexOf(' ');
    if (space > 0) {
      const parsed = new Date(raw.slice(0, space));
      if (!isNaN(parsed.getTime())) {
        ts = parsed;
        content = raw.slice(space + 1);
      }
    }
    const level = this.guessLevel(content);
    return {
      id: ++this.idSeed,
      deployment: pod.deployment,
      pod: pod.name,
      line: content,
      view: '',
      logLevel: level,
      timeStamp: ts,
      podColor: '',
      sequenceNumber: this.idSeed,
    };
  }

  private guessLevel(content: string): string {
    const u = content.toUpperCase();
    if (u.includes('ERROR') || u.includes('EXCEPTION') || u.includes('FATAL')) return 'Error';
    if (u.includes('WARN')) return 'Warning';
    if (u.includes('DEBUG')) return 'Debug';
    if (u.includes('TRACE')) return 'Trace';
    return 'Information';
  }

  private appendLogs(batch: Log[]) {
    if (!batch?.length) return;
    const merged = [...this.logs(), ...batch];
    if (merged.length > this.maxLines) merged.splice(0, merged.length - this.maxLines);
    this.logs.set(merged);
  }

  private unsubscribeCurrent() {
    const pod = this.selectedPod();
    if (pod) {
      this.signalr.unsubscribeFromPod(pod.namespace, pod.name);
    }
  }

  clear() { this.logs.set([]); }

  reload() {
    const pod = this.selectedPod();
    if (pod) this.fetchTail(pod);
  }

  shortLevel(level: string): string {
    switch (level) {
      case 'Error': return 'ERR';
      case 'Warning': return 'WARN';
      case 'Information': return 'INFO';
      case 'Debug': return 'DEBUG';
      case 'Trace': return 'TRACE';
      default: return level.toUpperCase().slice(0, 5);
    }
  }

  formatTs(d: Date | string) {
    const date = typeof d === 'string' ? new Date(d) : d;
    return date.toLocaleTimeString('en-US', { hour12: false });
  }
}
