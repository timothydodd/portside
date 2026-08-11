using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using k8s.Autorest;

namespace PortsideApi.Common;

/// <summary>
/// Minimal replacement for the KubernetesClient Watcher API, which is excluded from
/// the KubernetesClient.Aot package. Reads the line-delimited watch stream and
/// deserializes each frame's object with source-generated metadata (AOT-safe).
/// </summary>
public static class K8sWatch
{
    public static async Task WatchAsync<TItem, TList>(
        Task<HttpOperationResponse<TList>> responseTask,
        JsonTypeInfo<TItem> typeInfo,
        Func<string, TItem, Task> onEvent,
        Action<Exception>? onError = null,
        CancellationToken token = default)
    {
        using var response = await responseTask;

        Stream stream;
        try
        {
            stream = await response.Response.Content.ReadAsStreamAsync(token);
            if (!stream.CanRead)
            {
                // The response was aborted before streaming began — almost always a
                // cancellation (last dashboard client disconnected) racing the handoff.
                token.ThrowIfCancellationRequested();
                throw new IOException("Watch response stream closed before streaming began.");
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException or ArgumentException && token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }

        using var _ = stream;
        using var reader = new StreamReader(stream);
        string? line;
        while (!token.IsCancellationRequested && (line = await reader.ReadLineAsync(token)) != null)
        {
            if (line.Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var type = NormalizeEventType(doc.RootElement.GetProperty("type").GetString());
                if (!doc.RootElement.TryGetProperty("object", out var el)) continue;
                var item = el.Deserialize(typeInfo);
                if (item is null) continue;
                await onEvent(type, item);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }
    }

    // Raw watch frames use "ADDED"/"MODIFIED"/"DELETED"; the frontend (and the old
    // WatchEventType.ToString() contract) expects "Added"/"Modified"/"Deleted".
    private static string NormalizeEventType(string? raw) => raw switch
    {
        "ADDED" => "Added",
        "MODIFIED" => "Modified",
        "DELETED" => "Deleted",
        "ERROR" => "Error",
        "BOOKMARK" => "Bookmark",
        _ => raw ?? "Unknown",
    };
}
