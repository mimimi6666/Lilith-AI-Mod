using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace LilithTextInjector;

// Classification only: this policy cannot send, delay, start a service or change
// the caller's local/autostart and single-attempt ownership boundaries.
internal static class LocalVoiceRetryPolicy
{
    internal static bool ShouldRetry(HttpRequestException exception, int attempt, int maximumAttempts)
        => attempt >= 1 && maximumAttempts is >= 2 and <= 7 && attempt < maximumAttempts
            && RetryReason(exception).Length != 0;

    // The outer reminder queue must not turn a terminal server response into a
    // fresh synthesis attempt. Keep unknown transport/playback recovery separate.
    internal static bool IsTerminalResponse(HttpRequestException exception)
        => exception.StatusCode.HasValue && RetryReason(exception).Length == 0;

    internal static string RetryReason(HttpRequestException exception)
    {
        // A server response takes precedence over any nested transport exception.
        // In particular, GPT-SoVITS' HTTP 400 synthesis/CUDA failures are terminal.
        if (exception.StatusCode.HasValue)
        {
            return exception.StatusCode.Value switch
            {
                HttpStatusCode.RequestTimeout => "http_request_timeout",
                HttpStatusCode.TooManyRequests => "http_busy",
                HttpStatusCode.BadGateway => "http_bad_gateway",
                HttpStatusCode.ServiceUnavailable => "http_service_unavailable",
                HttpStatusCode.GatewayTimeout => "http_gateway_timeout",
                _ => string.Empty
            };
        }

        // A generic HttpRequestException is not evidence of startup. Only known
        // socket failures retain the legacy bounded reconnect behavior. Never
        // classify response text, CUDA messages, or cancellation as readiness.
        Exception? inner = exception.InnerException;
        for (var depth = 0; inner != null && depth < 8; depth++, inner = inner.InnerException)
        {
            if (inner is OperationCanceledException) return string.Empty;
            if (inner is not SocketException socket) continue;
            return socket.SocketErrorCode switch
            {
                SocketError.ConnectionRefused => "connection_refused",
                SocketError.ConnectionReset => "connection_reset",
                SocketError.ConnectionAborted => "connection_aborted",
                SocketError.TimedOut => "connection_timeout",
                SocketError.NetworkDown or SocketError.NetworkUnreachable
                    or SocketError.HostDown or SocketError.HostUnreachable => "network_unavailable",
                _ => string.Empty
            };
        }
        return string.Empty;
    }
}
