using k8s.Models;
using PortsideApi.Models;

namespace PortsideApi.Services.Interfaces;

public interface IKubernetesService
{
    Task WatchMetrics(Func<string, V1Node, Task> onEvent, Action<Exception>? onError = null, Action? onClosed = null, CancellationToken token = default);
    Task<Cluster> GetMetrics();
}
