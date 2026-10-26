using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LilithTextInjector;

internal interface ILivingPlayerReplyMemoryPacketFactory
{
    LongTermMemoryPromptPacket? Create(
        LivingPromptRootOwnership rootOwnership);
}

internal enum LivingPlayerReplyRouteAdmission
{
    Disabled = 0,
    ProviderNotAllowlisted = 1,
    SearchExcluded = 2,
    ToolsExcluded = 3,
    BuiltInShadowExcluded = 4,
    MotionAgentExcluded = 5,
    PromptOwnerExcluded = 6,
    OwnershipRejected = 7,
    CompositionRejected = 8,
    DispatchRejected = 9,
    OwnedNoFallback = 10,
    InvocationClaimed = 11,
    CompletionRejected = 12,
    Completed = 13,
    Cancelled = 14,
    Faulted = 15
}

internal sealed class LivingPlayerReplyRouteFacts
{
    internal bool Enabled { get; }
    internal string ProviderRouteId { get; }
    internal PlayerReplyCurrentFactRequirement CurrentFactRequirement {
        get;
    }
    internal bool LocalWeatherContextAdmitted { get; }
    internal bool SearchRequested => CurrentFactRequirement switch
    {
        PlayerReplyCurrentFactRequirement.None => false,
        PlayerReplyCurrentFactRequirement.CurrentConfiguredWeather =>
            !LocalWeatherContextAdmitted,
        PlayerReplyCurrentFactRequirement.GeneralSearch => true,
        _ => true
    };
    internal bool DesktopToolsEnabled { get; }
    internal bool BuiltInShadowEnabled { get; }
    internal bool MotionAgentEnabled { get; }
    internal bool ExistingPromptOwnerEnabled { get; }
    internal bool GeminiRegistryMigrationAllowed { get; }

    internal LivingPlayerReplyRouteFacts(
        bool enabled,
        string providerRouteId,
        bool searchRequested,
        bool desktopToolsEnabled,
        bool builtInShadowEnabled,
        bool motionAgentEnabled,
        bool existingPromptOwnerEnabled,
        bool geminiRegistryMigrationAllowed = false)
        : this(
            enabled,
            providerRouteId,
            searchRequested
                ? PlayerReplyCurrentFactRequirement.GeneralSearch
                : PlayerReplyCurrentFactRequirement.None,
            localWeatherContextAdmitted: false,
            desktopToolsEnabled,
            builtInShadowEnabled,
            motionAgentEnabled,
            existingPromptOwnerEnabled,
            geminiRegistryMigrationAllowed)
    {
    }

    internal LivingPlayerReplyRouteFacts(
        bool enabled,
        string providerRouteId,
        PlayerReplyCurrentFactRequirement currentFactRequirement,
        bool localWeatherContextAdmitted,
        bool desktopToolsEnabled,
        bool builtInShadowEnabled,
        bool motionAgentEnabled,
        bool existingPromptOwnerEnabled,
        bool geminiRegistryMigrationAllowed = false)
    {
        Enabled = enabled;
        ProviderRouteId = providerRouteId ?? string.Empty;
        if (!Enum.IsDefined(
                typeof(PlayerReplyCurrentFactRequirement),
                currentFactRequirement))
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentFactRequirement));
        }
        if (localWeatherContextAdmitted
            && currentFactRequirement
                != PlayerReplyCurrentFactRequirement
                    .CurrentConfiguredWeather)
        {
            throw new ArgumentException(
                "Only current configured weather may admit local weather context.",
                nameof(localWeatherContextAdmitted));
        }
        CurrentFactRequirement = currentFactRequirement;
        LocalWeatherContextAdmitted = localWeatherContextAdmitted;
        DesktopToolsEnabled = desktopToolsEnabled;
        BuiltInShadowEnabled = builtInShadowEnabled;
        MotionAgentEnabled = motionAgentEnabled;
        ExistingPromptOwnerEnabled = existingPromptOwnerEnabled;
        GeminiRegistryMigrationAllowed = geminiRegistryMigrationAllowed;
    }
}

internal static class LivingPlayerReplyEligibility
{
    internal static LivingPlayerReplyRouteAdmission Evaluate(
        LivingPlayerReplyRouteFacts facts)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));
        if (!facts.Enabled)
            return LivingPlayerReplyRouteAdmission.Disabled;
        var existingProvider = string.Equals(
                facts.ProviderRouteId,
                "OpenAI",
                StringComparison.Ordinal)
            || string.Equals(
                facts.ProviderRouteId,
                "DeepSeek",
                StringComparison.Ordinal);
        var boundedGeminiMigration =
            facts.GeminiRegistryMigrationAllowed
            && string.Equals(
                facts.ProviderRouteId,
                "Gemini",
                StringComparison.Ordinal);
        if (!existingProvider && !boundedGeminiMigration)
        {
            return LivingPlayerReplyRouteAdmission.ProviderNotAllowlisted;
        }
        if (facts.SearchRequested)
            return LivingPlayerReplyRouteAdmission.SearchExcluded;
        if (facts.DesktopToolsEnabled)
            return LivingPlayerReplyRouteAdmission.ToolsExcluded;
        if (facts.BuiltInShadowEnabled)
            return LivingPlayerReplyRouteAdmission.BuiltInShadowExcluded;
        if (facts.MotionAgentEnabled)
            return LivingPlayerReplyRouteAdmission.MotionAgentExcluded;
        if (facts.ExistingPromptOwnerEnabled)
            return LivingPlayerReplyRouteAdmission.PromptOwnerExcluded;
        return LivingPlayerReplyRouteAdmission.OwnedNoFallback;
    }
}

internal sealed class LivingPlayerReplyPreparedInput
{
    internal const int MaximumWorkingContextTurns =
        LivingPromptCompositionPolicy.MaximumWorkingContextTurns;

    internal string CompositionId { get; }
    internal LivingPromptSourceAuthorization Authorization { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string ProviderRouteId { get; }
    internal string LivingSnapshotFingerprint { get; }
    internal string SystemPrompt { get; }
    internal LivingPromptCharacterSlot CharacterSlot { get; }
    internal ILivingPlayerReplyMemoryPacketFactory? MemoryPacketFactory {
        get;
    }
    internal IReadOnlyList<LivingPromptTurn> WorkingContext { get; }
    internal LivingMemoryWorkingContextAuthorization?
        WorkingContextAuthorization { get; }
    internal LivingPromptTurn CurrentRequest { get; }
    internal DateTimeOffset PreparedAt { get; }

    internal LivingPlayerReplyPreparedInput(
        string compositionId,
        LivingPromptSourceAuthorization authorization,
        CharacterRuntimeIdentity runtimeIdentity,
        string providerRouteId,
        string livingSnapshotFingerprint,
        string systemPrompt,
        IEnumerable<LivingPromptTurn> workingContext,
        LivingPromptTurn currentRequest,
        DateTimeOffset preparedAt,
        LivingPromptCharacterSlot? characterSlot = null,
        ILivingPlayerReplyMemoryPacketFactory? memoryPacketFactory = null,
        LivingMemoryWorkingContextAuthorization?
            workingContextAuthorization = null)
    {
        CompositionId = LivingContinuityContractGuard.Identifier(
            compositionId,
            nameof(compositionId));
        Authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
        if (Authorization.SourceKind
            != LivingPromptSourceKind.PlayerTriggered)
        {
            throw new ArgumentException(
                "F2 only accepts player-triggered authorization.",
                nameof(authorization));
        }
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ProviderRouteId = LivingContinuityContractGuard.Identifier(
            providerRouteId,
            nameof(providerRouteId));
        LivingSnapshotFingerprint =
            LivingContinuityContractGuard.Sha256(
                livingSnapshotFingerprint,
                nameof(livingSnapshotFingerprint));
        SystemPrompt = systemPrompt
            ?? throw new ArgumentNullException(nameof(systemPrompt));
        var frozenWorking = (workingContext
                ?? throw new ArgumentNullException(nameof(workingContext)))
            .Select(
                item => item
                    ?? throw new ArgumentException(
                        "Working context cannot contain null.",
                        nameof(workingContext)))
            .ToArray();
        if (frozenWorking.Length > MaximumWorkingContextTurns)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workingContext),
                "Working context cannot exceed 32 turns.");
        }
        WorkingContext = Array.AsReadOnly(
            frozenWorking);
        WorkingContextAuthorization = workingContextAuthorization;
        CurrentRequest = currentRequest
            ?? throw new ArgumentNullException(nameof(currentRequest));
        CharacterSlot = characterSlot
            ?? LivingPromptCharacterSlot.Empty;
        MemoryPacketFactory = memoryPacketFactory;
        PreparedAt = LivingContinuityContractGuard.Timestamp(
            preparedAt,
            nameof(preparedAt));
    }
}

internal sealed class LivingSingleInvocationProviderTransport
    : ILivingPreparedProviderTransport
{
    private readonly object _sync = new();
    private bool _dispatched;
    private bool _claimed;

    internal int Dispatches { get; private set; }
    internal int InvocationClaims { get; private set; }
    internal int Retries => 0;
    internal int Fallbacks => 0;
    internal LivingPreparedProviderRequest? Request { get; private set; }

    public LivingPreparedProviderAcceptance Dispatch(
        LivingPreparedProviderRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        lock (_sync)
        {
            if (_dispatched)
            {
                throw new InvalidOperationException(
                    "Provider dispatch is single-use.");
            }
            _dispatched = true;
            Dispatches++;
            Request = request;
            var providerRequestId =
                "d025-f2-provider-"
                + LivingPromptHash.Of(
                    request.RequestId
                    + "|"
                    + request.AttemptId)[..32];
            return new LivingPreparedProviderAcceptance(
                providerRequestId,
                request,
                request.AttemptedAt);
        }
    }

    internal bool TryClaimInvocation(
        string providerRequestId,
        out LivingPreparedProviderRequest? request)
    {
        providerRequestId = LivingContinuityContractGuard.Identifier(
            providerRequestId,
            nameof(providerRequestId));
        lock (_sync)
        {
            request = null;
            if (!_dispatched
                || _claimed
                || Request == null)
            {
                return false;
            }
            var expected =
                "d025-f2-provider-"
                + LivingPromptHash.Of(
                    Request.RequestId
                    + "|"
                    + Request.AttemptId)[..32];
            if (!string.Equals(
                    providerRequestId,
                    expected,
                    StringComparison.Ordinal))
            {
                return false;
            }
            _claimed = true;
            InvocationClaims++;
            request = Request;
            return true;
        }
    }
}

internal sealed class LivingPlayerReplyOwnedTransaction
{
    private readonly object _sync = new();
    private bool _terminal;
    private bool _invocationClaimed;

    internal LivingPromptCompositionHost CompositionHost { get; }
    internal LivingPreparedProviderAdapter ProviderAdapter { get; }
    internal LivingSingleInvocationProviderTransport ProviderTransport {
        get;
    }
    internal LivingPromptEnvelope Envelope { get; }
    internal LivingPromptProviderReceipt Receipt { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string SessionId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal bool LegacyMayFallback => false;

    internal LivingPlayerReplyOwnedTransaction(
        LivingPromptCompositionHost compositionHost,
        LivingPreparedProviderAdapter providerAdapter,
        LivingSingleInvocationProviderTransport providerTransport,
        LivingPromptEnvelope envelope,
        LivingPromptProviderReceipt receipt)
    {
        CompositionHost = compositionHost
            ?? throw new ArgumentNullException(nameof(compositionHost));
        ProviderAdapter = providerAdapter
            ?? throw new ArgumentNullException(nameof(providerAdapter));
        ProviderTransport = providerTransport
            ?? throw new ArgumentNullException(nameof(providerTransport));
        Envelope = envelope
            ?? throw new ArgumentNullException(nameof(envelope));
        Receipt = receipt
            ?? throw new ArgumentNullException(nameof(receipt));
        RuntimeIdentity = new CharacterRuntimeIdentity(
            envelope.RuntimeIdentity.RuntimeProfileId,
            envelope.RuntimeIdentity.RuntimeInstanceId,
            envelope.RuntimeIdentity.Generation,
            envelope.RuntimeIdentity.OrchestratorSessionId,
            envelope.RuntimeIdentity.OwnerIdentity);
        SessionId = envelope.SessionId;
        SituationId = envelope.SituationId;
        SituationRevision = envelope.SituationRevision;
    }

    internal LivingPlayerReplyRouteAdmission TryClaimInvocation(
        out LivingPreparedProviderRequest? request)
    {
        lock (_sync)
        {
            request = null;
            if (_terminal || _invocationClaimed)
                return LivingPlayerReplyRouteAdmission.DispatchRejected;
            if (!ProviderTransport.TryClaimInvocation(
                    Receipt.ProviderRequestId,
                    out request))
            {
                return LivingPlayerReplyRouteAdmission.DispatchRejected;
            }
            _invocationClaimed = true;
            return LivingPlayerReplyRouteAdmission.InvocationClaimed;
        }
    }

    internal LivingPlayerReplyRouteAdmission TryComplete(
        string responseText,
        DateTimeOffset completedAt,
        CharacterRuntimeIdentity activeRuntime,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        out LivingPlayerReplyCompletionProjection? projection)
    {
        if (responseText == null)
            throw new ArgumentNullException(nameof(responseText));
        projection = null;
        lock (_sync)
        {
            if (_terminal)
                return LivingPlayerReplyRouteAdmission.CompletionRejected;
            if (!_invocationClaimed)
            {
                _terminal = true;
                return LivingPlayerReplyRouteAdmission.CompletionRejected;
            }
            if (completedAt >= Envelope.ExpiresAt)
            {
                _terminal = true;
                CompositionHost.Cancel(
                    Envelope.CompositionId,
                    ContinuityRetirementReason.Expired,
                    completedAt);
                return LivingPlayerReplyRouteAdmission.CompletionRejected;
            }
            var frozen = ProviderAdapter.FrozenRequest;
            if (frozen == null)
            {
                _terminal = true;
                CompositionHost.Cancel(
                    Envelope.CompositionId,
                    ContinuityRetirementReason.Faulted,
                    completedAt);
                return LivingPlayerReplyRouteAdmission.CompletionRejected;
            }
            var response = new LivingPreparedProviderResponse(
                Receipt.ProviderRequestId,
                frozen,
                responseText,
                completedAt);
            if (ProviderAdapter.TryAcceptResponse(
                    response,
                    completedAt,
                    out var exchange)
                != LivingPreparedProviderAdmission.ResponseAccepted
                || exchange == null)
            {
                _terminal = true;
                CompositionHost.Cancel(
                    Envelope.CompositionId,
                    ContinuityRetirementReason.Faulted,
                    completedAt);
                return LivingPlayerReplyRouteAdmission.CompletionRejected;
            }
            if (CompositionHost.RecordCompletion(
                    Envelope,
                    Receipt,
                    "d025-f2-completion-"
                    + LivingPromptHash.Of(
                        Receipt.ProviderRequestId
                        + "|"
                        + completedAt.UtcDateTime.Ticks.ToString(
                            CultureInfo.InvariantCulture))[..32],
                    OutcomeStatus.Completed,
                    activeRuntime,
                    activeSessionId,
                    activeSituationId,
                    activeSituationRevision,
                    completedAt,
                    out var completion)
                != LivingPromptCompositionAdmission.CompletionRecorded
                || completion == null)
            {
                _terminal = true;
                CompositionHost.Cancel(
                    Envelope.CompositionId,
                    ContinuityRetirementReason.Faulted,
                    completedAt);
                return LivingPlayerReplyRouteAdmission.CompletionRejected;
            }
            if (LivingPreparedCompletionProjector.TryProject(
                    exchange,
                    completion,
                    out var projected)
                != LivingPreparedProviderAdmission.CompletionProjected
                || projected
                    is not LivingPlayerReplyCompletionProjection player)
            {
                _terminal = true;
                CompositionHost.Cancel(
                    Envelope.CompositionId,
                    ContinuityRetirementReason.Faulted,
                    completedAt);
                return LivingPlayerReplyRouteAdmission.CompletionRejected;
            }
            _terminal = true;
            projection = player;
            return LivingPlayerReplyRouteAdmission.Completed;
        }
    }

    internal LivingPlayerReplyRouteAdmission Cancel(
        ContinuityRetirementReason reason,
        DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_terminal)
                return LivingPlayerReplyRouteAdmission.Cancelled;
            _terminal = true;
            CompositionHost.Cancel(Envelope.CompositionId, reason, now);
            return LivingPlayerReplyRouteAdmission.Cancelled;
        }
    }
}

internal static class LivingContinuityPlayerReplyBridge
{
    internal static LivingPlayerReplyRouteAdmission TryOwnAndPrepare(
        LivingPlayerReplyRouteFacts facts,
        LivingPlayerReplyPreparedInput input,
        ContinuityOwnershipRegistry sharedOwnership,
        out LivingPlayerReplyOwnedTransaction? transaction)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));
        if (input == null)
            throw new ArgumentNullException(nameof(input));
        if (sharedOwnership == null)
            throw new ArgumentNullException(nameof(sharedOwnership));
        transaction = null;

        var eligibility = LivingPlayerReplyEligibility.Evaluate(facts);
        if (eligibility
            != LivingPlayerReplyRouteAdmission.OwnedNoFallback)
        {
            return eligibility;
        }
        if (!string.Equals(
                facts.ProviderRouteId,
                input.ProviderRouteId,
                StringComparison.Ordinal))
        {
            return LivingPlayerReplyRouteAdmission.ProviderNotAllowlisted;
        }

        var host = new LivingPromptCompositionHost(
            new LivingPromptCompositionPolicy(
                LivingPromptCompositionMode.Prepared),
            input.RuntimeIdentity,
            sharedOwnership);
        var begin = host.TryBegin(
            input.CompositionId,
            input.Authorization,
            input.RuntimeIdentity,
            input.Authorization.SessionId,
            input.Authorization.SituationId,
            input.Authorization.SituationRevision,
            input.PreparedAt);
        if (begin.Admission
            != LivingPromptCompositionAdmission.RootOwnershipAcquired
            || begin.RootOwnership == null)
        {
            return LivingPlayerReplyRouteAdmission.OwnershipRejected;
        }

        LongTermMemoryPromptPacket? memoryPacket;
        try
        {
            memoryPacket = input.MemoryPacketFactory?.Create(
                begin.RootOwnership);
        }
        catch
        {
            host.Cancel(
                input.CompositionId,
                ContinuityRetirementReason.Faulted,
                input.PreparedAt);
            return LivingPlayerReplyRouteAdmission.CompositionRejected;
        }
        var compose = host.TryCompose(
            new LivingPromptCompositionRequest(
                input.CompositionId,
                input.Authorization,
                begin.RootOwnership,
                input.ProviderRouteId,
                input.LivingSnapshotFingerprint,
                input.SystemPrompt,
                input.CharacterSlot,
                memoryPacket,
                input.WorkingContext,
                input.CurrentRequest,
                input.PreparedAt,
                input.WorkingContextAuthorization),
            input.RuntimeIdentity,
            input.Authorization.SessionId,
            input.Authorization.SituationId,
            input.Authorization.SituationRevision,
            input.ProviderRouteId,
            input.PreparedAt);
        if (compose.Admission != LivingPromptCompositionAdmission.Composed
            || compose.Envelope == null)
        {
            return LivingPlayerReplyRouteAdmission.CompositionRejected;
        }

        var transport = new LivingSingleInvocationProviderTransport();
        var adapter = new LivingPreparedProviderAdapter(transport);
        var dispatch = host.TryDispatch(
            compose.Envelope,
            input.RuntimeIdentity,
            input.Authorization.SessionId,
            input.Authorization.SituationId,
            input.Authorization.SituationRevision,
            input.ProviderRouteId,
            input.PreparedAt,
            adapter,
            out var receipt);
        if (dispatch
                != LivingPromptCompositionAdmission.DispatchAccepted
            || receipt == null)
        {
            return LivingPlayerReplyRouteAdmission.DispatchRejected;
        }

        transaction = new LivingPlayerReplyOwnedTransaction(
            host,
            adapter,
            transport,
            compose.Envelope,
            receipt);
        return LivingPlayerReplyRouteAdmission.OwnedNoFallback;
    }
}
