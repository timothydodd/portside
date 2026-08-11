using System.Text.Json;
using PortsideApi.Common;
using PortsideApi.Data;

namespace PortsideApi.Services;

public class MonitorSettings
{
    /// <summary>Master switch — when false, all backend pod monitoring is paused.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the backend rescans pod logs to refresh 24h error/warning counts.</summary>
    public int LogScanIntervalSeconds { get; set; } = 300;

    /// <summary>How often the backend polls pod CPU/memory metrics.</summary>
    public int MetricsPollIntervalSeconds { get; set; } = 60;

    /// <summary>Window (in seconds) for the rolling log error/warning count. Default 24h.</summary>
    public int LogWindowSeconds { get; set; } = 86400;

    /// <summary>Pod keys ("namespace/name") opted out of monitoring. All other pods are monitored.</summary>
    public List<string> ExcludedPods { get; set; } = new();
}

public sealed class MonitorSettingsService
{
    private const string SettingKey = "monitor";
    private readonly SystemSettingStore _store;
    private readonly ILogger<MonitorSettingsService> _logger;
    private readonly object _gate = new();
    private MonitorSettings _cached = new();
    private bool _loaded;

    public event Action<MonitorSettings>? Changed;

    public MonitorSettingsService(SystemSettingStore store, ILogger<MonitorSettingsService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public MonitorSettings Get()
    {
        lock (_gate)
        {
            if (_loaded) return Clone(_cached);
        }

        MonitorSettings loaded;
        try
        {
            var json = _store.GetJson(SettingKey);
            loaded = json != null
                ? (JsonSerializer.Deserialize(json, AppJsonContext.Default.MonitorSettings) ?? new MonitorSettings())
                : new MonitorSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load monitor settings; using defaults");
            loaded = new MonitorSettings();
        }

        Normalize(loaded);

        lock (_gate)
        {
            _cached = loaded;
            _loaded = true;
            return Clone(_cached);
        }
    }

    public async Task<MonitorSettings> Save(MonitorSettings settings)
    {
        Normalize(settings);
        var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.MonitorSettings);
        await _store.UpsertJsonAsync(SettingKey, json);

        lock (_gate)
        {
            _cached = settings;
            _loaded = true;
        }
        Changed?.Invoke(Clone(settings));
        return Clone(settings);
    }

    private static void Normalize(MonitorSettings s)
    {
        if (s.LogScanIntervalSeconds < 30) s.LogScanIntervalSeconds = 30;
        if (s.LogScanIntervalSeconds > 24 * 60 * 60) s.LogScanIntervalSeconds = 24 * 60 * 60;
        if (s.MetricsPollIntervalSeconds < 15) s.MetricsPollIntervalSeconds = 15;
        if (s.MetricsPollIntervalSeconds > 60 * 60) s.MetricsPollIntervalSeconds = 60 * 60;
        if (s.LogWindowSeconds < 60) s.LogWindowSeconds = 60;
        s.ExcludedPods = s.ExcludedPods?.Distinct().OrderBy(x => x).ToList() ?? new List<string>();
    }

    private static MonitorSettings Clone(MonitorSettings s) => new()
    {
        Enabled = s.Enabled,
        LogScanIntervalSeconds = s.LogScanIntervalSeconds,
        MetricsPollIntervalSeconds = s.MetricsPollIntervalSeconds,
        LogWindowSeconds = s.LogWindowSeconds,
        ExcludedPods = s.ExcludedPods.ToList(),
    };
}
