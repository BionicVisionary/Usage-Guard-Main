using System.Text.Json;

namespace CodexUsageGuard.Core;

public static class AppServerProtocol
{
    public const long InitializeRequestId = 1;
    public const long RateLimitsRequestId = 2;

    public static string InitializeRequest =>
        "{\"method\":\"initialize\",\"id\":1," +
        "\"params\":{\"clientInfo\":{" +
        "\"name\":\"codex_usage_guard_feasibility\"," +
        "\"title\":\"Usage Guard\"," +
        "\"version\":\"0.1.0\"}}}";

    public static string InitializedNotification =>
        "{\"method\":\"initialized\",\"params\":{}}";

    public static string RateLimitsRequest =>
        "{\"method\":\"account/rateLimits/read\",\"id\":2}";

    public static ProtocolResponseKind ClassifyResponse(
        string json,
        long expectedId)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return ProtocolResponseKind.Invalid;
            }

            if (root.TryGetProperty("method", out var method) &&
                method.ValueKind == JsonValueKind.String &&
                method.ValueEquals("account/chatgptAuthTokens/refresh"))
            {
                return ProtocolResponseKind.AuthenticationRefreshRequested;
            }

            if (!root.TryGetProperty("id", out var id))
            {
                return ProtocolResponseKind.Notification;
            }

            return id.TryGetInt64(out var value) && value == expectedId
                ? ProtocolResponseKind.ExpectedResponse
                : ProtocolResponseKind.Invalid;
        }
        catch (JsonException)
        {
            return ProtocolResponseKind.Invalid;
        }
    }

    public static bool InitializeAccepted(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                !root.TryGetProperty("error", out _) &&
                root.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public enum ProtocolResponseKind
{
    ExpectedResponse,
    Notification,
    AuthenticationRefreshRequested,
    Invalid
}
