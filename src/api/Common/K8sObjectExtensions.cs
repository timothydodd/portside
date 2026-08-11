using k8s;
using k8s.Models;

namespace PortsideApi.Common;

/// <summary>
/// Drops metadata.managedFields — server-side apply bookkeeping the UI never reads,
/// and frequently the single largest chunk of every K8s object. Stripping it before
/// objects are cached or serialized keeps the heap and response payloads small.
/// </summary>
public static class K8sObjectExtensions
{
    public static T StripManagedFields<T>(this T obj) where T : IKubernetesObject<V1ObjectMeta>
    {
        if (obj.Metadata is not null) obj.Metadata.ManagedFields = null;
        return obj;
    }

    public static IList<T> StripManagedFields<T>(this IList<T> items) where T : IKubernetesObject<V1ObjectMeta>
    {
        foreach (var item in items) item.StripManagedFields();
        return items;
    }
}
