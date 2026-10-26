using System.Text.Json;
using System.Collections.Immutable;
using LilithGameBridgeContracts;
using LilithRuntimeContracts;

namespace LilithRuntime;

internal sealed partial class LilithGameHermesSessionHost
{
    internal void AttachLettersObserver(Action<LilithLetterTurnReference> observer)
    {
        if (_lettersCompleted is not null) throw new InvalidOperationException("Letters observer already attached.");
        _lettersCompleted = observer;
    }

    internal async Task WithLettersScopeAsync(Func<ILilithLettersHostScope, CancellationToken, Task> action,
        Func<bool> productCurrent, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _noteDraftShutdown.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        if (!await _turnGate.WaitAsync(0, deadline.Token).ConfigureAwait(false)) return;
        var phase = "scope_model_authority";
        try
        {
            using var authority = await _runtime.ModelAuthority.AcquireAsync(_snapshot.GenerationId, deadline.Token).ConfigureAwait(false);
            RequireUsableModelAuthority(authority.Snapshot);
            if (!productCurrent() || Volatile.Read(ref _foregroundTurnDemand) != 0) return;
            // Reuse the ordinary durable-handle activation, never fabricate a
            // player turn just to wake letters after restart.
            phase = "scope_session_activation";
            await EnsureSessionAsync(authority, deadline.Token).ConfigureAwait(false);
            var sessionId = _router.ActiveSessionId;
            if (sessionId is null || !_router.IsSessionAttested(sessionId)) return;
            phase = "scope_lineage";
            var lineage = RequireExactActiveDurableLineage(sessionId);
            bool Current() => !_disposed && !deadline.IsCancellationRequested && productCurrent()
                && Volatile.Read(ref _foregroundTurnDemand) == 0
                && Volatile.Read(ref _nativeAssistantContinuationRecoveryRequired) == 0
                && Volatile.Read(ref _nativeAssistantCommitQuarantined) == 0 && _quarantinedTurn is null
                && _runtime.ModelAuthority.Snapshot().Revision == authority.Snapshot.Revision
                && IsExactActiveLineage(sessionId, lineage);
            if (!Current()) return;
            using var watcherStop = new CancellationTokenSource();
            var watch = Watch();
            try
            {
                var scope = new LilithLettersHostScope(sessionId, lineage, _runtime.RequestAsync, Current);
                phase = "scope_action";
                await action(scope, deadline.Token).ConfigureAwait(false);
            }
            finally { watcherStop.Cancel(); await watch.ConfigureAwait(false); }
            async Task Watch()
            {
                try
                {
                    while (!watcherStop.IsCancellationRequested)
                    {
                        await Task.Delay(100, watcherStop.Token).ConfigureAwait(false);
                        if (!Current()) { deadline.Cancel(); return; }
                    }
                }
                catch (OperationCanceledException) { }
            }
        }
        catch (LilithLettersValidationException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { throw new LilithLettersValidationException(LettersScopeFailureCode(phase, error)); }
        finally { _turnGate.Release(); }
    }

    internal static string LettersScopeFailureCode(string phase, Exception error)
    {
        // Expose only our own finite failure taxonomy, never exception text or
        // arbitrary provider error codes in the player-visible diagnostic.
        var detail = error is LilithGameBridgeOperationException operation ? operation.Code switch
        {
            "runtime.native_surface_attestation_required" => "attestation_required",
            "runtime.game_session_lineage_rejected" => "lineage_rejected",
            "runtime.game_model_selection_required" => "model_selection_required",
            "runtime.game_session_rejected" => "session_rejected",
            "runtime.game_session_persistence_failed" => "session_persistence_failed",
            "runtime.game_agent_initialization_failed" => "agent_initialization_failed",
            "runtime.session_activation_not_sent" => "activation_not_sent",
            "runtime.session_activation_quarantined" => "activation_quarantined",
            "runtime.native_config_quarantined" => "config_quarantined",
            "runtime.game_approval_bypass_rejected" => "session_approval_rejected",
            _ => "operation_rejected"
        } : error switch
        {
            InvalidDataException => "invalid_receipt",
            TimeoutException => "timeout",
            HermesRpcException => "native_rpc_rejected",
            _ => "unavailable"
        };
        return phase + "_" + detail;
    }
}

internal interface ILilithLettersHostScope
{
    bool IsCurrent { get; }
    ILilithLetterTextModel Model(LilithLettersWritingPacket packet);
    Task<LilithLettersHistoryRead?> ReadAsync(LilithLettersSnapshot state, CancellationToken token);
}

// Exclusions are host-owned letter-index metadata, never model output or a
// request to forget/delete player conversation or long-term memory. A null read
// has no authority to reconcile anything (revoked scope is not empty history).
internal sealed record LilithLettersHistoryRead(IReadOnlyList<LilithLetterMaterial> Material,
    ImmutableArray<Guid> UnattestedKeys);

internal sealed class LilithLettersHostScope(string session, LilithGameSessionLineageAttestation lineage,
    Func<string, object?, CancellationToken, Task<JsonElement>> request, Func<bool> current) : ILilithLettersHostScope
{
    private readonly string tip = lineage.ResolvedStoredSessionId;
    private readonly string root = lineage.RequestedStoredSessionId;
    private readonly HashSet<string> attestedSessions = lineage.SessionIds.ToHashSet(StringComparer.Ordinal);

    public bool IsCurrent => current();

    public ILilithLetterTextModel Model(LilithLettersWritingPacket packet)
        => new LilithLettersNativeTextModel(packet, session, tip, root, (method, data, token) => request(method, data, token), current);

    public async Task<LilithLettersHistoryRead?> ReadAsync(LilithLettersSnapshot state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!state.Settings.Enabled || !current()) return null;
        var now = DateTimeOffset.UtcNow;
        // A durable restart can attest only the persisted compression tip and
        // its descendants. Older indexed ancestors are not authority to read
        // that history. Exclude them before applying the request bound, so one
        // stale reference cannot reject or crowd out the currently valid set.
        var excluded = state.Turns.Where(t => !t.Used && !t.Withdrawn
                && !attestedSessions.Contains(t.Source.StoredSessionId)).Select(t => t.Source.Key).ToImmutableArray();
        var refs = state.Turns.Where(t => attestedSessions.Contains(t.Source.StoredSessionId)
                && !t.Used && !t.Withdrawn && t.Kind != LilithLetterConversationKind.ToolOnly
                && now - t.Source.CompletedAtUtc < LilithLettersPolicy.MaximumContextAge)
            .OrderBy(t => t.Source.CompletedAtUtc).Take(32).Select(t => t.Source).ToArray();
        if (refs.Length == 0) return current() ? new([], excluded) : null;
        var response = await request("prompt.letters_history", new
        {
            session_id = session, expected_stored_session_id = tip, expected_lineage_root = root,
            references = refs.Select(r => new { key = r.Key.ToString("N"), stored_session_id = r.StoredSessionId, turn_id = r.NativeTurnId })
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!current()) return null;
        LilithNoteInboxProtocol.Exact(response, "stored_session_id", "status", "rows");
        if (response.GetProperty("status").GetString() != "ready" || response.GetProperty("stored_session_id").GetString() != tip)
            throw new LilithLettersValidationException("history_not_ready");
        var raw = response.GetProperty("rows");
        if (raw.ValueKind != JsonValueKind.Array || raw.GetArrayLength() > 64) throw new InvalidDataException("Letters history bound exceeded.");
        var rows = raw.EnumerateArray().Select(r =>
        {
            LilithNoteInboxProtocol.Exact(r, "role", "text", "message_id", "turn_id", "message_kind", "timestamp_ms");
            return new LilithGameBridgeHistoryEntry(r.GetProperty("message_id").GetString()!, r.GetProperty("turn_id").GetString()!,
                r.GetProperty("message_kind").GetString()!, r.GetProperty("role").GetString()!, r.GetProperty("text").GetString()!,
                r.GetProperty("timestamp_ms").GetInt64());
        }).ToArray();
        // Native attests every referenced session in the exact compression chain;
        // existing assembler still checks completed-reference hashes and clocks.
        return new(refs.GroupBy(r => r.StoredSessionId).SelectMany(group =>
            LilithLettersHistory.Assemble(state.ProfileId, group.Key, group, rows, now)).ToArray(), excluded);
    }
}
