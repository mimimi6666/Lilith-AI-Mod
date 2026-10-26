using System;
using System.Collections.Generic;
using LilithRuntime.Configuration;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

// A temporary input producer, not a Runtime client or a second chat engine.
// Every mutation is dispatched by DialogueManager.Update through the same
// functions as F7/F8; receipts are restricted to this process's test inputs.
internal static partial class DialogueManagerUpdatePatch
{
    private static readonly object GameChatTestGate = new();
    private static readonly Dictionary<Guid, GameChatTestObservation> GameChatTests = new();
    private static GameChatTestEndpoint? _gameChatTestEndpoint;
    private static bool _gameChatTestInitialized;
    private static Guid _gameChatTestSubmitting;

    private sealed class GameChatTestObservation
    {
        internal PlayerTurnOperationHandle? Operation;
        internal GameChatTestEndpointState State = GameChatTestEndpointState.Unknown;
        internal string Code = "not_submitted";
        internal string Text = string.Empty;
        internal bool TextTruncated;
        internal GameChatTestMiniGameCapture? MiniGameCapture;
        internal LilithMiniGameDiagnosticRequest? MiniGameDiagnosticRequest;
        internal LilithMiniGameDiagnosticReceipt? MiniGameDiagnostic;
    }

    internal static void InitializeGameChatTestEndpoint()
    {
        if (!_gameChatTestInitialized)
        {
            _gameChatTestInitialized = true;
            try
            {
                _gameChatTestEndpoint = GameChatTestEndpoint.TryStartFromProcessEnvironment();
                if (_gameChatTestEndpoint != null)
                    Plugin.PluginLog.LogInfo("Temporary game-chat test input enabled; normal F7/F8 authority applies.");
            }
            catch (Exception exception)
            {
                Plugin.PluginLog.LogWarning("Game-chat test input unavailable: " + exception.GetType().Name);
            }
        }
    }

    private static bool UpdateGameChatTestEndpoint(DialogueManager manager)
    {
        InitializeGameChatTestEndpoint();
        var endpoint = _gameChatTestEndpoint;
        if (endpoint == null)
            return false;
        if (!endpoint.IsActive)
        {
            endpoint.Dispose();
            _gameChatTestEndpoint = null;
            lock (GameChatTestGate)
                GameChatTests.Clear();
            return false;
        }
        if (!endpoint.TryDequeue(out var command) || command == null)
            return false;
        var mutated = false;
        GameChatTestEndpointReceipt receipt;
        try
        {
            if (command.Action == GameChatTestEndpointAction.Status)
            {
                if (command.TargetId == Guid.Empty)
                    receipt = new GameChatTestEndpointReceipt(
                        IsGameChatTestInputBusy() ? GameChatTestEndpointState.Busy : GameChatTestEndpointState.Ready,
                        "game_input_status");
                else
                    receipt = GameChatTestReceipt(command.TargetId);
            }
            else if (command.Action == GameChatTestEndpointAction.Send)
            {
                // Do not overwrite the player's draft or bypass the F7 busy gate.
                if (IsGameChatTestInputBusy())
                {
                    receipt = new GameChatTestEndpointReceipt(GameChatTestEndpointState.Rejected, "game_input_busy");
                }
                else
                {
                    lock (GameChatTestGate)
                        GameChatTests.Add(command.RequestId, new GameChatTestObservation());
                    _gameChatTestSubmitting = command.RequestId;
                    try
                    {
                        SubmitAiInput(manager, command.Text, PlayerTurnInputSurface.TextEntry);
                        mutated = true;
                    }
                    finally
                    {
                        _gameChatTestSubmitting = Guid.Empty;
                    }
                    receipt = GameChatTestReceipt(command.RequestId);
                }
            }
            else
            {
                PlayerTurnOperationHandle? owned;
                lock (GameChatTestGate)
                    owned = GameChatTests.TryGetValue(command.TargetId, out var observation)
                        ? observation.Operation : null;
                var active = PlayerTurnOperations.GetSnapshot().Active;
                if (owned == null || active == null || !owned.Matches(active.Handle))
                    receipt = new GameChatTestEndpointReceipt(GameChatTestEndpointState.Rejected, "target_not_active");
                else
                {
                    ObserveGameChatTest(owned, GameChatTestEndpointState.StopRequested, "stop_requested");
                    HandleD046Stop(manager);
                    mutated = true;
                    receipt = GameChatTestReceipt(command.TargetId);
                }
            }
        }
        catch (Exception)
        {
            // An unknown dispatch result must never be retried automatically.
            receipt = new GameChatTestEndpointReceipt(GameChatTestEndpointState.Unknown, "dispatch_result_unknown");
        }
        endpoint.Complete(command, receipt);
        return mutated;
    }

    private static bool IsGameChatTestInputBusy()
        => IsGameHermesTextInputBusy()
            || (_inputBubble != null && _inputBubble.activeSelf)
            || D046GameControlCaptureLease.IsActive();

    private static void BindGameChatTest(PlayerTurnOperationHandle operation)
    {
        if (_gameChatTestSubmitting == Guid.Empty)
            return;
        lock (GameChatTestGate)
        {
            if (!GameChatTests.TryGetValue(_gameChatTestSubmitting, out var observation))
                return;
            observation.Operation = operation;
            observation.State = GameChatTestEndpointState.Submitted;
            observation.Code = "game_input_admitted";
        }
    }

    private static bool CanObserveGameChatTestContext(PlayerTurnOperationHandle? operation)
    {
        if (operation == null || _gameChatTestEndpoint?.IsActive != true)
            return false;
        lock (GameChatTestGate)
        {
            foreach (var observation in GameChatTests.Values)
                if (observation.Operation != null && observation.Operation.Matches(operation)
                    && observation.State == GameChatTestEndpointState.Submitted
                    && observation.MiniGameCapture == null)
                    return true;
        }
        return false;
    }

    private static LilithMiniGameDiagnosticRequest? CreateGameChatTestDiagnostic(PlayerTurnOperationHandle operation)
    {
        var endpoint = _gameChatTestEndpoint;
        if (endpoint?.IsActive != true) return null;
        lock (GameChatTestGate)
        {
            foreach (var observation in GameChatTests.Values)
            {
                if (observation.Operation == null || !observation.Operation.Matches(operation)
                    || observation.State != GameChatTestEndpointState.Submitted
                    || observation.MiniGameDiagnosticRequest != null) continue;
                var request = new LilithMiniGameDiagnosticRequest(Guid.NewGuid(), endpoint.ExpiresAtUtc.ToUnixTimeMilliseconds());
                if (!request.IsActive(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())) return null;
                observation.MiniGameDiagnosticRequest = request;
                return request;
            }
        }
        return null;
    }

    private static void ObserveGameChatTestDiagnostic(PlayerTurnOperationHandle operation,
        LilithMiniGameDiagnosticReceipt? receipt)
    {
        if (receipt == null || _gameChatTestEndpoint?.IsActive != true) return;
        lock (GameChatTestGate)
        {
            foreach (var observation in GameChatTests.Values)
            {
                if (observation.Operation == null || !observation.Operation.Matches(operation)
                    || observation.State != GameChatTestEndpointState.Submitted
                    || observation.MiniGameDiagnostic != null) continue;
                var request = observation.MiniGameDiagnosticRequest;
                if (request != null && request.TraceId == receipt.TraceId
                    && request.IsActive(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                    observation.MiniGameDiagnostic = receipt;
                return;
            }
        }
    }

    private static void ObserveGameChatTestContext(PlayerTurnOperationHandle? operation,
        GameChatTestMiniGameCapture capture)
    {
        if (operation == null || _gameChatTestEndpoint?.IsActive != true)
            return;
        lock (GameChatTestGate)
        {
            foreach (var observation in GameChatTests.Values)
            {
                if (observation.Operation == null || !observation.Operation.Matches(operation))
                    continue;
                // First capture from this exact admitted operation only. No global latest,
                // polling recapture, unrelated player turn, or post-expiry observation.
                if (observation.State == GameChatTestEndpointState.Submitted && observation.MiniGameCapture == null)
                    observation.MiniGameCapture = capture;
                return;
            }
        }
    }

    private static void ObserveGameChatTest(
        PlayerTurnOperationHandle operation,
        GameChatTestEndpointState state,
        string code,
        string text = "")
    {
        lock (GameChatTestGate)
        {
            foreach (var observation in GameChatTests.Values)
            {
                if (observation.Operation == null || !observation.Operation.Matches(operation))
                    continue;
                // A late provider return is not proof of publication after Stop.
                if (observation.State == GameChatTestEndpointState.StopRequested
                    || observation.State == GameChatTestEndpointState.Published)
                    return;
                // Keep the exact Runtime failure code when its notice is displayed.
                if (observation.State == GameChatTestEndpointState.Failed
                    && state != GameChatTestEndpointState.StopRequested)
                    return;
                observation.State = state;
                observation.Code = code;
                if (text.Length > 0)
                    observation.Text = GameChatTestEndpointCodec.BoundReply(text, out observation.TextTruncated);
                return;
            }
        }
    }

    private static GameChatTestEndpointReceipt GameChatTestReceipt(Guid id)
    {
        lock (GameChatTestGate)
        {
            if (!GameChatTests.TryGetValue(id, out var observation))
                return new GameChatTestEndpointReceipt(GameChatTestEndpointState.Rejected, "unknown_test_target");
            return new GameChatTestEndpointReceipt(
                observation.State, observation.Code, observation.Text,
                observation.Operation?.OperationId ?? string.Empty, observation.TextTruncated,
                observation.MiniGameCapture, observation.MiniGameDiagnostic);
        }
    }
}
