using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using k8s.Models;

namespace PortsideApi.Common;

/// <summary>
/// Combined serializer options: AppJsonContext metadata plus the K8sScalarResolver
/// that fills in ResourceQuantity/IntOrString. Use these type infos (not
/// AppJsonContext.Default directly) when serializing K8s object graphs outside the
/// HTTP/SignalR pipelines — e.g. watch-stream frames.
/// </summary>
public static class AppJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static readonly JsonTypeInfo<V1Pod> V1Pod = (JsonTypeInfo<V1Pod>)Options.GetTypeInfo(typeof(V1Pod));
    public static readonly JsonTypeInfo<V1Node> V1Node = (JsonTypeInfo<V1Node>)Options.GetTypeInfo(typeof(V1Node));
    public static readonly JsonTypeInfo<Corev1Event> Corev1Event = (JsonTypeInfo<Corev1Event>)Options.GetTypeInfo(typeof(Corev1Event));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.TypeInfoResolverChain.Add(AppJsonContext.Default);
        options.TypeInfoResolverChain.Add(new K8sScalarResolver());
        options.MakeReadOnly();
        return options;
    }
}
