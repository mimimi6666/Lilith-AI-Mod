using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed record LilithDailyNoteNativeOutcome(string Status, string Code, LilithNoteInboxResult? Result);

internal interface ILilithDailyNoteNativePort : IAsyncDisposable
{
    event Action<LilithDailyNoteCommand>? ToolRequested;
    Task<LilithDailyNoteReadiness> PrepareAsync(LilithDailyNotePreparation request, CancellationToken cancellationToken);
    Task<bool> CancelPreparationAsync(LilithDailyNotePreparation request, CancellationToken cancellationToken);
    Task<LilithDailyNoteNativeOutcome> EvaluateAsync(LilithDailyNoteScope scope, string locale, Guid preparationToken, CancellationToken cancellationToken);
    Task<bool> CompleteAsync(LilithDailyNoteScope scope, LilithNoteInboxResult result, CancellationToken cancellationToken);
    Task<bool> CancelAsync(LilithDailyNoteScope scope, CancellationToken cancellationToken);
}

// This adapter transports the existing authenticated native approval path.
// It never selects a provider, grants approval, submits chat, or emits speech.
internal sealed class LilithDailyNoteNativeAdapter : ILilithDailyNoteNativePort
{
    private readonly ILilithGameNotesGateway _gateway;
    private bool _disposed;
    public event Action<LilithDailyNoteCommand>? ToolRequested;

    internal LilithDailyNoteNativeAdapter(ILilithGameNotesGateway gateway)
    { _gateway = gateway; _gateway.GatewayEvent += OnEvent; }

    public async Task<LilithDailyNoteReadiness> PrepareAsync(LilithDailyNotePreparation request, CancellationToken cancellationToken)
    {
        var value = await _gateway.RequestAsync(LilithDailyNoteReadinessProtocol.PrepareMethod,
            LilithDailyNoteReadinessProtocol.EncodeRequest(request), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(35), cancellationToken).ConfigureAwait(false);
        return LilithDailyNoteReadinessProtocol.ReadReadiness(value, request);
    }

    public async Task<bool> CancelPreparationAsync(LilithDailyNotePreparation request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var value = await _gateway.RequestAsync(LilithDailyNoteReadinessProtocol.CancelMethod,
            new { profile_id = request.ProfileId, runtime_generation = request.RuntimeGeneration.ToString("N"),
                preparation_id = request.PreparationId.ToString("N") }, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        LilithNoteInboxProtocol.Exact(value, "profile_id", "runtime_generation", "preparation_id", "accepted");
        return LilithNoteInboxProtocol.String(value, "profile_id") == request.ProfileId
            && LilithNoteInboxProtocol.ParseId(LilithNoteInboxProtocol.String(value, "runtime_generation")) == request.RuntimeGeneration
            && LilithNoteInboxProtocol.ParseId(LilithNoteInboxProtocol.String(value, "preparation_id")) == request.PreparationId
            && value.GetProperty("accepted").ValueKind == JsonValueKind.True;
    }

    public async Task<LilithDailyNoteNativeOutcome> EvaluateAsync(LilithDailyNoteScope scope, string locale, Guid preparationToken, CancellationToken cancellationToken)
    {
        if (preparationToken == Guid.Empty) throw new ArgumentException("Daily preparation token required.");
        var value = await _gateway.RequestAsync(LilithDailyNoteProtocol.EvaluateMethod,
            new { scope = LilithDailyNoteProtocol.EncodeScope(scope), locale, preparation_token = preparationToken.ToString("N") }, cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(65), cancellationToken).ConfigureAwait(false);
        LilithNoteInboxProtocol.Exact(value, "scope", "status", "code", "result");
        if (LilithDailyNoteProtocol.ReadScope(value.GetProperty("scope")) != scope)
            throw new InvalidDataException("Daily evaluation identity changed.");
        var status = LilithNoteInboxProtocol.String(value, "status");
        if (status is not ("no_effect" or "unknown" or "completed"))
            throw new InvalidDataException("Daily evaluation status rejected.");
        var code = LilithNoteInboxProtocol.String(value, "code");
        if (string.IsNullOrEmpty(code) || code.Length > 96 || code.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_')))
            throw new InvalidDataException("Daily evaluation code rejected.");
        var raw = value.GetProperty("result");
        var result = raw.ValueKind == JsonValueKind.Null ? null : LilithNoteInboxProtocol.ReadResult(raw);
        if (result is not null) LilithDailyNoteProtocol.RequireResult(scope, result);
        if ((status == "completed") != (result is not null))
            throw new InvalidDataException("Daily evaluation receipt shape rejected.");
        return new(status, code, result);
    }

    public Task<bool> CompleteAsync(LilithDailyNoteScope scope, LilithNoteInboxResult result, CancellationToken cancellationToken)
    {
        LilithDailyNoteProtocol.RequireResult(scope, result);
        return AcknowledgeAsync(LilithDailyNoteProtocol.CompleteMethod, scope,
            new { scope = LilithDailyNoteProtocol.EncodeScope(scope), result = LilithNoteInboxProtocol.EncodeResult(result) }, cancellationToken);
    }

    public Task<bool> CancelAsync(LilithDailyNoteScope scope, CancellationToken cancellationToken)
        => AcknowledgeAsync(LilithDailyNoteProtocol.CancelMethod, scope,
            new { scope = LilithDailyNoteProtocol.EncodeScope(scope) }, cancellationToken);

    private async Task<bool> AcknowledgeAsync(string method, LilithDailyNoteScope scope, object parameters, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var value = await _gateway.RequestAsync(method, parameters, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        LilithNoteInboxProtocol.Exact(value, "scope", "accepted");
        return LilithDailyNoteProtocol.ReadScope(value.GetProperty("scope")) == scope
            && value.GetProperty("accepted").ValueKind == JsonValueKind.True;
    }

    private void OnEvent(HermesGatewayEvent value)
    {
        if (_disposed || value.Type != "lilith.daily_note.tool") return;
        try
        {
            LilithNoteInboxProtocol.Exact(value.Payload, "scope", "arguments");
            var scope = LilithDailyNoteProtocol.ReadScope(value.Payload.GetProperty("scope"));
            var arguments = value.Payload.GetProperty("arguments");
            var action = LilithNoteInboxProtocol.String(arguments, "action");
            LilithNoteInboxProtocol.Exact(arguments, action == "deliver"
                ? new[] { "action", "locale", "text" } : new[] { "action", "locale" });
            if (action is not ("deliver" or "status")) return;
            var command = new LilithDailyNoteCommand(scope,
                action == "deliver" ? LilithNoteInboxAction.Deliver : LilithNoteInboxAction.Status,
                action == "deliver" ? LilithNoteInboxProtocol.String(arguments, "text") : null,
                LilithNoteInboxProtocol.String(arguments, "locale"));
            ToolRequested?.Invoke(command);
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException or FormatException)
        { /* Malformed native traffic cannot acquire a delivery lease. */ }
    }

    public ValueTask DisposeAsync()
    { _disposed = true; _gateway.GatewayEvent -= OnEvent; return ValueTask.CompletedTask; }
}
