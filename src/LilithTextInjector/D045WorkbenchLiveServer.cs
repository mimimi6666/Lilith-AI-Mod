using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LilithWorkbenchBridge;
using LilithProviderControlPlane;
using UI.Common;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private sealed class PendingWorkbenchLiveTurn
    {
        internal D045WorkbenchLiveRequest Request { get; }
        internal TaskCompletionSource<D045WorkbenchLiveResponse> Completion { get; }
        internal D045WorkbenchTurnLiveness Liveness { get; } = new();
        internal PlayerTurnOperationHandle? Operation { get; set; }
        internal string TerminalText { get; set; } = string.Empty;
        internal string RelatedMemoryText { get; set; } = string.Empty;
        internal int RelatedMemoryCount { get; set; }
        internal string ResponseModelId { get; set; } = string.Empty;
        internal D045WorkbenchLiveAttachment[] GeneratedAttachments { get; set; } =
            Array.Empty<D045WorkbenchLiveAttachment>();

        internal PendingWorkbenchLiveTurn(D045WorkbenchLiveRequest request)
        {
            Request = request;
            Completion = new TaskCompletionSource<D045WorkbenchLiveResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private static readonly object WorkbenchLiveGate = new();
    private static PendingWorkbenchLiveTurn? _dispatchingWorkbenchLiveTurn;
    private static PendingWorkbenchLiveTurn? _activeWorkbenchLiveTurn;

    private static void CancelWorkbenchTurn(
        PendingWorkbenchLiveTurn turn,
        D045WorkbenchTurnCancellationKind kind,
        string statusCode)
    {
        if (!turn.Liveness.TryCancel(kind))
            return;
        turn.Completion.TrySetResult(
            RejectWorkbenchRequest(turn.Request, statusCode));
    }

    private static void CompleteRejectedWorkbenchTurn(
        PendingWorkbenchLiveTurn turn,
        string statusCode)
    {
        if (!turn.Liveness.TryComplete())
        {
            EnsureWorkbenchCancellationResponse(turn);
            return;
        }
        turn.Completion.TrySetResult(
            RejectWorkbenchRequest(turn.Request, statusCode));
    }

    private static void EnsureWorkbenchCancellationResponse(
        PendingWorkbenchLiveTurn turn)
    {
        if (turn.Completion.Task.IsCompleted)
            return;
        var statusCode = WorkbenchCancellationStatusCode(
            turn.Liveness.CancellationKind);
        turn.Completion.TrySetResult(
            RejectWorkbenchRequest(turn.Request, statusCode));
    }

    private static string WorkbenchCancellationStatusCode(
        D045WorkbenchTurnCancellationKind kind)
        => kind switch
        {
            D045WorkbenchTurnCancellationKind.ClientDisconnected =>
                "D045B2.LilithLive.ClientDisconnected",
            D045WorkbenchTurnCancellationKind.TurnTimedOut =>
                "D045B2.LilithLive.TimedOut",
            D045WorkbenchTurnCancellationKind.ServerShutdown =>
                "D045B2.LilithLive.ServerShutdown",
            _ => "D045B2.LilithLive.Cancelled"
        };

    private static void BindWorkbenchDispatch(PlayerTurnOperationHandle operation)
    {
        lock (WorkbenchLiveGate)
        {
            var turn = _dispatchingWorkbenchLiveTurn;
            if (turn == null)
                return;
            if (_activeWorkbenchLiveTurn != null)
                throw new InvalidOperationException("A Workbench turn is already active.");
            turn.Operation = operation;
            _activeWorkbenchLiveTurn = turn;
            _dispatchingWorkbenchLiveTurn = null;
        }
    }

    private static bool IsWorkbenchOperation(PlayerTurnOperationHandle operation)
    {
        lock (WorkbenchLiveGate)
            return IsExactActiveWorkbenchOperation(operation);
    }

    private static CancellationToken GetWorkbenchOperationCancellationToken(
        PlayerTurnOperationHandle operation)
    {
        lock (WorkbenchLiveGate)
        {
            return IsExactActiveWorkbenchOperation(operation)
                ? _activeWorkbenchLiveTurn!.Liveness.Token
                : CancellationToken.None;
        }
    }

    private static bool IsWorkbenchOperationLive(
        PlayerTurnOperationHandle operation)
    {
        lock (WorkbenchLiveGate)
        {
            return IsExactActiveWorkbenchOperation(operation)
                && _activeWorkbenchLiveTurn!.Liveness.IsLive;
        }
    }

    private static bool TryAdmitWorkbenchEffectDispatch(
        PlayerTurnOperationHandle operation,
        Func<bool> hostAdmission)
    {
        if (hostAdmission is null)
            throw new ArgumentNullException(nameof(hostAdmission));
        lock (WorkbenchLiveGate)
        {
            return IsExactActiveWorkbenchOperation(operation)
                && _activeWorkbenchLiveTurn!.Liveness
                    .TryAdmitEffectDispatch(hostAdmission);
        }
    }

    private static bool TryConsumeWorkbenchOperationCancellation(
        PlayerTurnOperationHandle operation,
        out D045WorkbenchTurnCancellationKind kind)
    {
        lock (WorkbenchLiveGate)
        {
            if (!IsExactActiveWorkbenchOperation(operation))
            {
                kind = D045WorkbenchTurnCancellationKind.None;
                return false;
            }
            return _activeWorkbenchLiveTurn!.Liveness
                .TryConsumeCancellation(out kind);
        }
    }

    private static bool CompleteWorkbenchCancellation(
        PlayerTurnOperationHandle operation)
    {
        PendingWorkbenchLiveTurn? turn;
        lock (WorkbenchLiveGate)
        {
            if (!IsExactActiveWorkbenchOperation(operation))
                return false;
            turn = _activeWorkbenchLiveTurn;
            _activeWorkbenchLiveTurn = null;
        }
        EnsureWorkbenchCancellationResponse(turn!);
        return true;
    }

    private static string BuildWorkbenchProviderInput(
        PlayerTurnOperationHandle operation,
        string userText)
    {
        PendingWorkbenchLiveTurn? turn;
        lock (WorkbenchLiveGate)
            turn = IsExactActiveWorkbenchOperation(operation)
                ? _activeWorkbenchLiveTurn
                : null;
        if (turn == null)
            return userText;
        var explicitLocalAction =
            IsExplicitLocalComputerTask(turn.Request.UserText);
        return D045WorkbenchProviderInputComposer.Compose(
            turn.Request,
            turn.Request.UserText,
            turn.RelatedMemoryText,
            explicitLocalAction);
    }


    private static D045WorkbenchLiveRequest? GetActiveWorkbenchRequest(
        PlayerTurnOperationHandle operation)
    {
        lock (WorkbenchLiveGate)
            return IsExactActiveWorkbenchOperation(operation)
                ? _activeWorkbenchLiveTurn!.Request
                : null;
    }

    private static D045WorkbenchExecutionBudgetProfile
        ResolveHostOwnedAgentLoopBudgetProfile(
            PlayerTurnOperationHandle operation)
        => GetActiveWorkbenchRequest(operation)?.ExecutionBudgetProfile
            ?? D045WorkbenchExecutionBudgetProfile.Quick;

    private sealed class WorkbenchGeminiBinding
    {
        internal string ApiKey { get; }
        internal string ModelId { get; }
        internal string Url { get; }
        internal bool HighReasoning { get; }

        internal WorkbenchGeminiBinding(
            string apiKey,
            string modelId,
            string url,
            bool highReasoning)
        {
            ApiKey = apiKey;
            ModelId = modelId;
            Url = url;
            HighReasoning = highReasoning;
        }
    }

    private sealed class WorkbenchProviderBinding
    {
        internal ProviderControlKind ProviderKind { get; }
        internal ProviderControlProtocol Protocol { get; }
        internal ProviderControlAuth Authentication { get; }
        internal string ApiKey { get; }
        internal string ModelId { get; }
        internal string Url { get; }
        internal string RouteToken { get; }
        internal string DisplayName { get; }
        internal bool HighReasoning { get; }

        internal WorkbenchProviderBinding(
            ProviderControlKind providerKind,
            ProviderControlProtocol protocol,
            ProviderControlAuth authentication,
            string apiKey,
            string modelId,
            string url,
            string routeToken,
            string displayName,
            bool highReasoning)
        {
            ProviderKind = providerKind;
            Protocol = protocol;
            Authentication = authentication;
            ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
            Url = url ?? throw new ArgumentNullException(nameof(url));
            RouteToken = routeToken ?? throw new ArgumentNullException(nameof(routeToken));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            HighReasoning = highReasoning;
        }
    }

    private static string ResolvePlayerTurnProvider(
        PlayerTurnOperationHandle operation,
        string fallback)
    {
        if (GetActiveWorkbenchRequest(operation) is null)
            return fallback;
        return TryResolveWorkbenchProvider(
                operation,
                out _,
                out var runtime,
                out _)
            ? runtime!.RuntimeProvider == ProviderKind.QwenDashScope
                ? "Qwen"
                : runtime.RouteToken
            : "Workbench";
    }

    private static bool IsWorkbenchProviderConfigured(
        PlayerTurnOperationHandle operation)
    {
        return TryResolveWorkbenchProvider(
            operation,
            out _,
            out _,
            out _);
    }

    private static ProviderKind ResolveWorkbenchRuntimeProvider(
        PlayerTurnOperationHandle operation)
        => TryResolveWorkbenchProvider(
                operation,
                out _,
                out var runtime,
                out _)
            ? runtime!.RuntimeProvider
            : ProviderKind.Invalid;

    private static string ResolveHigherReasoningRouteToken()
        => TryResolveHigherReasoningProvider(
                out _,
                out var runtime,
                out _)
            ? runtime!.RuntimeProvider == ProviderKind.QwenDashScope
                ? "Qwen"
                : runtime.RouteToken
            : "HigherReasoning";

    private static bool IsHigherReasoningProviderConfigured()
        => TryResolveHigherReasoningProvider(
            out _,
            out _,
            out _);

    private static ProviderKind ResolveHigherReasoningRuntimeProvider()
        => TryResolveHigherReasoningProvider(
                out _,
                out var runtime,
                out _)
            ? runtime!.RuntimeProvider
            : ProviderKind.Invalid;

    private static bool TryResolveWorkbenchProvider(
        PlayerTurnOperationHandle operation,
        out UnifiedProviderConfiguredInstance? instance,
        out ProviderControlRuntimeProfile? runtime,
        out D045WorkbenchLiveRequest? request)
    {
        request = GetActiveWorkbenchRequest(operation);
        instance = null;
        runtime = null;
        if (request == null)
            return false;
        var activeRequest = request;
        var load = UnifiedProviderControlStore.Load();
        instance = load.Snapshot?.Instances.SingleOrDefault(value =>
            string.Equals(
                value.InstanceId,
                activeRequest.ProviderInstanceId,
                StringComparison.Ordinal));
        return load.Accepted
            && instance != null
            && D045WorkbenchModelCatalogRules.IsSelectableLiveTextModel(
                instance,
                activeRequest.ModelId)
            && ProviderControlRuntimeProfileResolver.TryResolve(
                instance.ProviderKind,
                instance.CredentialConfigured,
                out runtime) == ProviderControlRuntimeProfileAdmission.Accepted
            && runtime != null;
    }

    private static bool TryResolveHigherReasoningProvider(
        out UnifiedProviderConfiguredInstance? instance,
        out ProviderControlRuntimeProfile? runtime,
        out D045AiFunctionRoute? route)
    {
        instance = null;
        runtime = null;
        route = null;
        var providers = UnifiedProviderControlStore.Load();
        if (!providers.Accepted
            || providers.Snapshot == null
            || !D045AiFunctionRoutingStore.ForCurrentUser().TryResolve(
                D045AiFunctionKind.HigherReasoning,
                providers.Snapshot,
                out route,
                out instance)
            || route == null
            || instance == null)
        {
            return false;
        }
        return D045WorkbenchModelCatalogRules.IsSelectableLiveTextModel(
                instance,
                route.ModelId)
            && ProviderControlRuntimeProfileResolver.TryResolve(
                instance.ProviderKind,
                instance.CredentialConfigured,
                out runtime) == ProviderControlRuntimeProfileAdmission.Accepted
            && runtime != null;
    }

    private static WorkbenchProviderBinding OpenWorkbenchProviderBinding(
        PlayerTurnOperationHandle operation)
    {
        if (!TryResolveWorkbenchProvider(
                operation,
                out var instance,
                out var runtime,
                out var request)
            || instance == null
            || runtime == null
            || request == null)
        {
            throw new InvalidOperationException(
                "The selected Workbench model is no longer configured.");
        }
        var opened = UnifiedProviderControlStore.OpenCredential(instance);
        if (opened.Admission != UnifiedProviderStoreAdmission.Accepted
            || opened.Lease == null)
        {
            throw new InvalidOperationException(
                "The selected Workbench credential is unavailable.");
        }
        char[]? secret = null;
        try
        {
            secret = opened.Lease.Take();
            var profile = UnifiedProviderProfileRegistry.Get(
                instance.ProviderKind);
            var endpoint = WorkbenchProviderEndpoint(
                instance,
                profile,
                request.ModelId);
            return new WorkbenchProviderBinding(
                instance.ProviderKind,
                profile.Protocol,
                profile.Auth,
                new string(secret),
                request.ModelId,
                endpoint.AbsoluteUri,
                runtime.RouteToken,
                profile.DisplayName,
                string.Equals(
                    request.ReasoningLevel,
                    "high",
                    StringComparison.Ordinal));
        }
        finally
        {
            if (secret != null)
                Array.Clear(secret, 0, secret.Length);
            opened.Lease.Dispose();
        }
    }

    private static WorkbenchProviderBinding OpenHigherReasoningProviderBinding()
    {
        if (!TryResolveHigherReasoningProvider(
                out var instance,
                out var runtime,
                out var route)
            || instance == null
            || runtime == null
            || route == null)
        {
            throw new InvalidOperationException(
                "The assigned Higher Reasoning model is no longer configured.");
        }
        var opened = UnifiedProviderControlStore.OpenCredential(instance);
        if (opened.Admission != UnifiedProviderStoreAdmission.Accepted
            || opened.Lease == null)
        {
            throw new InvalidOperationException(
                "The assigned Higher Reasoning credential is unavailable.");
        }
        char[]? secret = null;
        try
        {
            secret = opened.Lease.Take();
            var profile = UnifiedProviderProfileRegistry.Get(
                instance.ProviderKind);
            var endpoint = WorkbenchProviderEndpoint(
                instance,
                profile,
                route.ModelId);
            return new WorkbenchProviderBinding(
                instance.ProviderKind,
                profile.Protocol,
                profile.Auth,
                new string(secret),
                route.ModelId,
                endpoint.AbsoluteUri,
                runtime.RouteToken,
                profile.DisplayName,
                highReasoning: true);
        }
        finally
        {
            if (secret != null)
                Array.Clear(secret, 0, secret.Length);
            opened.Lease.Dispose();
        }
    }

    private static Uri WorkbenchProviderEndpoint(
        UnifiedProviderConfiguredInstance instance,
        ProviderControlProfile profile,
        string modelId)
    {
        if (profile.Protocol == ProviderControlProtocol.GeminiGenerateContent)
        {
            var model = modelId.StartsWith("models/", StringComparison.Ordinal)
                ? modelId[7..]
                : modelId;
            return new Uri(
                instance.BaseUri,
                "v1beta/models/"
                + Uri.EscapeDataString(model)
                + ":generateContent");
        }
        return new Uri(instance.BaseUri, profile.GenerationPath);
    }

    private static WorkbenchGeminiBinding OpenWorkbenchImageGeminiBinding(
        PlayerTurnOperationHandle operation)
    {
        if (GetActiveWorkbenchRequest(operation)?.RequestImageGeneration != true)
            throw new InvalidOperationException("Image generation was not requested.");
        var load = UnifiedProviderControlStore.Load();
        if (!load.Accepted || load.Snapshot == null
            || !D045AiFunctionRoutingStore.ForCurrentUser().TryResolve(
                D045AiFunctionKind.HigherReasoningImageGeneration,
                load.Snapshot,
                out var route,
                out var instance)
            || route == null
            || instance == null
            || instance.ProviderKind != ProviderControlKind.GoogleGemini
            || !instance.CredentialConfigured
            || !UnifiedProviderCatalogAdapters.IsSupportedGeminiNativeImageModel(
                route.ModelId))
        {
            throw new InvalidOperationException(
                "The assigned image-generation model is unavailable.");
        }
        var opened = UnifiedProviderControlStore.OpenCredential(instance);
        if (opened.Admission != UnifiedProviderStoreAdmission.Accepted
            || opened.Lease == null)
        {
            throw new InvalidOperationException(
                "The image-generation credential is unavailable.");
        }
        char[]? secret = null;
        try
        {
            secret = opened.Lease.Take();
            var model = route.ModelId.StartsWith("models/", StringComparison.Ordinal)
                ? route.ModelId[7..]
                : route.ModelId;
            return new WorkbenchGeminiBinding(
                new string(secret),
                route.ModelId,
                new Uri(
                    instance.BaseUri,
                    "v1beta/models/"
                    + Uri.EscapeDataString(model)
                    + ":generateContent").AbsoluteUri,
                highReasoning: false);
        }
        finally
        {
            if (secret != null)
                Array.Clear(secret, 0, secret.Length);
            opened.Lease.Dispose();
        }
    }

    private static List<object> BuildWorkbenchGeminiContents(
        PlayerTurnOperationHandle operation,
        string providerInput)
    {
        var request = GetActiveWorkbenchRequest(operation);
        if (request == null)
            return BuildGeminiContents().Cast<object>().ToList();
        var parts = new List<object> { new { text = providerInput } };
        foreach (var attachment in request.Attachments)
        {
            parts.Add(new
            {
                inlineData = new
                {
                    mimeType = attachment.MediaType,
                    data = attachment.DataBase64
                }
            });
        }
        return new List<object>
        {
            new { role = "user", parts = parts.ToArray() }
        };
    }

    private static void CaptureWorkbenchTerminalReply(
        PlayerTurnOperationHandle operation,
        string text,
        PlayerTurnTerminalDisposition disposition)
    {
        if (disposition == PlayerTurnTerminalDisposition.None)
            return;
        lock (WorkbenchLiveGate)
        {
            if (IsExactActiveWorkbenchOperation(operation)
                && _activeWorkbenchLiveTurn!.Liveness.IsLive)
                _activeWorkbenchLiveTurn!.TerminalText = text;
        }
    }

    private static void CaptureWorkbenchGeneratedAttachments(
        PlayerTurnOperationHandle operation,
        string modelId,
        IReadOnlyList<D045WorkbenchLiveAttachment> attachments)
    {
        lock (WorkbenchLiveGate)
        {
            if (!IsExactActiveWorkbenchOperation(operation)
                || !_activeWorkbenchLiveTurn!.Liveness.IsLive)
                throw new InvalidOperationException("Workbench turn is no longer active.");
            _activeWorkbenchLiveTurn!.ResponseModelId = modelId;
            _activeWorkbenchLiveTurn.GeneratedAttachments = attachments.ToArray();
        }
    }

    private static void CompleteWorkbenchPublishedTurn(
        PlayerTurnOperationHandle operation,
        PlayerTurnTerminalDisposition disposition)
    {
        PendingWorkbenchLiveTurn? turn;
        lock (WorkbenchLiveGate)
        {
            if (!IsExactActiveWorkbenchOperation(operation))
                return;
            turn = _activeWorkbenchLiveTurn;
            _activeWorkbenchLiveTurn = null;
        }
        if (!turn!.Liveness.TryComplete())
        {
            EnsureWorkbenchCancellationResponse(turn);
            return;
        }
        turn.Completion.TrySetResult(new D045WorkbenchLiveResponse
        {
            ProtocolVersion = D045WorkbenchLiveProtocol.Version,
            RequestId = turn.Request.RequestId,
            Accepted = disposition == PlayerTurnTerminalDisposition.Completed
                && turn.TerminalText.Length > 0,
            AssistantText = turn.TerminalText,
            StatusCode = disposition == PlayerTurnTerminalDisposition.Completed
                ? "D045B2.LilithLive.Completed"
                : "D045B2.LilithLive.PublishedFailure",
            ProviderRouteId = turn.Request.ProviderRouteId,
            ModelId = turn.ResponseModelId.Length == 0
                ? turn.Request.ModelId
                : turn.ResponseModelId,
            RelatedMemoryCount = turn.RelatedMemoryCount,
            AttachmentCount = turn.GeneratedAttachments.Length,
            Attachments = turn.GeneratedAttachments
        });
    }

    private static void CompleteWorkbenchImmediateFailure(
        PlayerTurnOperationHandle operation,
        string text)
    {
        CaptureWorkbenchTerminalReply(
            operation,
            text,
            PlayerTurnTerminalDisposition.Failed);
        CompleteWorkbenchPublishedTurn(
            operation,
            PlayerTurnTerminalDisposition.Failed);
    }

    private static void FailWorkbenchTurn(
        PlayerTurnOperationHandle operation,
        string statusCode)
    {
        PendingWorkbenchLiveTurn? turn;
        lock (WorkbenchLiveGate)
        {
            if (!IsExactActiveWorkbenchOperation(operation))
                return;
            turn = _activeWorkbenchLiveTurn;
            _activeWorkbenchLiveTurn = null;
        }
        CompleteRejectedWorkbenchTurn(turn!, statusCode);
    }

    private static bool IsExactActiveWorkbenchOperation(
        PlayerTurnOperationHandle operation)
        => _activeWorkbenchLiveTurn?.Operation?.Matches(operation) == true;

    private static D045WorkbenchLiveResponse RejectWorkbenchRequest(
        D045WorkbenchLiveRequest request,
        string statusCode)
        => new()
        {
            ProtocolVersion = D045WorkbenchLiveProtocol.Version,
            RequestId = request.RequestId,
            Accepted = false,
            AssistantText = string.Empty,
            StatusCode = statusCode,
            ProviderRouteId = string.Empty,
            ModelId = string.Empty,
            RelatedMemoryCount = 0,
            AttachmentCount = 0,
            Attachments = Array.Empty<D045WorkbenchLiveAttachment>()
        };
}
