using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace LilithTextInjector;

internal enum ControlCenterMetricHealth
{
    Neutral = 0,
    Healthy = 1,
    Attention = 2,
    Blocked = 3
}

internal sealed class ControlCenterRuntimeMetric
{
    internal string Id { get; }
    internal string Group { get; }
    internal string Label { get; }
    internal string Value { get; }
    internal string Unit { get; }
    internal ControlCenterMetricHealth Health { get; }

    internal ControlCenterRuntimeMetric(
        string id,
        string group,
        string label,
        string value,
        string unit = "",
        ControlCenterMetricHealth health = ControlCenterMetricHealth.Neutral)
    {
        Id = Token(id, nameof(id), 80);
        Group = Token(group, nameof(group), 40);
        Label = Text(label, nameof(label), 80);
        Value = Text(value, nameof(value), 128);
        Unit = Text(unit, nameof(unit), 24, allowEmpty: true);
        if (!Enum.IsDefined(typeof(ControlCenterMetricHealth), health))
            throw new ArgumentOutOfRangeException(nameof(health));
        Health = health;
    }

    internal static ControlCenterRuntimeMetric Number(
        string id,
        string group,
        string label,
        double value,
        string unit = "",
        ControlCenterMetricHealth health = ControlCenterMetricHealth.Neutral)
        => new(
            id,
            group,
            label,
            value.ToString("0.###", CultureInfo.InvariantCulture),
            unit,
            health);

    private static string Token(
        string? value,
        string parameterName,
        int maximumLength)
    {
        var result = Text(value, parameterName, maximumLength);
        if (result.Any(character =>
                !char.IsLetterOrDigit(character)
                && character is not '.' and not '-' and not '_'))
        {
            throw new ArgumentException(
                "Telemetry identifiers must be bounded tokens.",
                parameterName);
        }
        return result;
    }

    private static string Text(
        string? value,
        string parameterName,
        int maximumLength,
        bool allowEmpty = false)
    {
        var result = (value ?? string.Empty).Trim();
        if ((!allowEmpty && result.Length == 0)
            || result.Length > maximumLength
            || result.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Telemetry text must be bounded and control-free.",
                parameterName);
        }
        return result;
    }
}

internal sealed class ControlCenterRuntimeTelemetry
{
    internal const int MaximumMetrics = 64;

    internal DateTimeOffset ObservedAt { get; }
    internal string IntegrationState { get; }
    internal string Interruptibility { get; }
    internal string InterruptibilityReason { get; }
    internal IReadOnlyList<ControlCenterRuntimeMetric> Metrics { get; }
    internal string Fingerprint { get; }

    internal ControlCenterRuntimeTelemetry(
        DateTimeOffset observedAt,
        string integrationState,
        string interruptibility,
        string interruptibilityReason,
        IEnumerable<ControlCenterRuntimeMetric> metrics)
    {
        ObservedAt = DecisionCoreContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        IntegrationState = Bounded(integrationState, nameof(integrationState));
        Interruptibility = Bounded(interruptibility, nameof(interruptibility));
        InterruptibilityReason = Bounded(
            interruptibilityReason,
            nameof(interruptibilityReason),
            160);
        var copy = (metrics ?? throw new ArgumentNullException(nameof(metrics)))
            .Take(MaximumMetrics)
            .ToArray();
        if (copy.Any(value => value == null))
            throw new ArgumentException("Telemetry metrics cannot contain null.", nameof(metrics));
        Metrics = new ReadOnlyCollection<ControlCenterRuntimeMetric>(copy);
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D034/FORMAL-TELEMETRY/1",
            IntegrationState,
            Interruptibility,
            InterruptibilityReason,
            string.Join(
                "|",
                copy.Select(value =>
                    value.Id + "=" + value.Value + value.Unit + ":" + (int)value.Health)));
    }

    private static string Bounded(
        string? value,
        string parameterName,
        int maximumLength = 80)
    {
        var result = (value ?? string.Empty).Trim();
        if (result.Length == 0
            || result.Length > maximumLength
            || result.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Telemetry state must be bounded and control-free.",
                parameterName);
        }
        return result;
    }
}

internal static class ControlCenterFormalTelemetryProjection
{
    internal static IReadOnlyList<ControlCenterRuntimeMetric> PlayerTurn(
        PlayerTurnOperationRuntimeSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var active = snapshot.Active;
        var latest = snapshot.RecentTerminals.LastOrDefault();
        var terminalValue = latest == null
            ? "None"
            : latest.TerminalDisposition switch
            {
                PlayerTurnTerminalDisposition.Failed =>
                    "Failed / " + latest.FailureReason,
                PlayerTurnTerminalDisposition.Cancelled =>
                    "Cancelled / " + latest.CancellationReason,
                _ => latest.TerminalDisposition.ToString()
            };
        return new ReadOnlyCollection<ControlCenterRuntimeMetric>(new[]
        {
            new ControlCenterRuntimeMetric(
                "player-turn.active",
                "player-turn",
                "Player turn operation",
                active == null
                    ? "Idle"
                    : active.Phase + " / " + active.Handle.InputSource,
                health: active?.Phase == PlayerTurnOperationPhase.ProviderStarted
                    ? ControlCenterMetricHealth.Attention
                    : ControlCenterMetricHealth.Healthy),
            ControlCenterRuntimeMetric.Number(
                "player-turn.began",
                "player-turn",
                "Accepted player turns",
                snapshot.Began),
            ControlCenterRuntimeMetric.Number(
                "player-turn.completed",
                "player-turn",
                "Published replies",
                snapshot.Completed,
                health: ControlCenterMetricHealth.Healthy),
            ControlCenterRuntimeMetric.Number(
                "player-turn.failed",
                "player-turn",
                "Failed player turns",
                snapshot.Failed,
                health: snapshot.Failed > 0
                    ? ControlCenterMetricHealth.Attention
                    : ControlCenterMetricHealth.Healthy),
            ControlCenterRuntimeMetric.Number(
                "player-turn.cancelled",
                "player-turn",
                "Cancelled player turns",
                snapshot.Cancelled,
                health: snapshot.Cancelled > 0
                    ? ControlCenterMetricHealth.Attention
                    : ControlCenterMetricHealth.Healthy),
            ControlCenterRuntimeMetric.Number(
                "player-turn.rejected-transitions",
                "player-turn",
                "Rejected lifecycle transitions",
                snapshot.RejectedTransitions,
                health: snapshot.RejectedTransitions > 0
                    ? ControlCenterMetricHealth.Attention
                    : ControlCenterMetricHealth.Healthy),
            new ControlCenterRuntimeMetric(
                "player-turn.last-terminal",
                "player-turn",
                "Last player turn terminal",
                terminalValue,
                health: latest?.TerminalDisposition switch
                {
                    PlayerTurnTerminalDisposition.Failed =>
                        ControlCenterMetricHealth.Attention,
                    PlayerTurnTerminalDisposition.Cancelled =>
                        ControlCenterMetricHealth.Attention,
                    _ => ControlCenterMetricHealth.Healthy
                })
        });
    }

    internal static IReadOnlyList<ControlCenterRuntimeMetric> AgentRuntime(
        AgentRuntimeHostSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var epoch = snapshot.ProcessEpochId.Substring(
            Math.Max(0, snapshot.ProcessEpochId.Length - 8));
        var active = snapshot.ActiveSourceKind?.ToString() ?? "Idle";
        var lastLoser = snapshot.LastLoserSourceKind.HasValue
            ? snapshot.LastLoserSourceKind.Value
                + " / "
                + snapshot.LastLoserReason
            : "None";
        var lastLoserHealth = snapshot.LastLoserReason switch
        {
            AgentLanguageArbitrationReason.MonotonicClockRejected
                or AgentLanguageArbitrationReason.RuntimeEpochChanged
                or AgentLanguageArbitrationReason.RuntimeGenerationChanged
                or AgentLanguageArbitrationReason.DuplicateProposal
                or AgentLanguageArbitrationReason.ForeignProposal =>
                    ControlCenterMetricHealth.Attention,
            _ => ControlCenterMetricHealth.Healthy
        };
        return new ReadOnlyCollection<ControlCenterRuntimeMetric>(new[]
        {
            new ControlCenterRuntimeMetric(
                "agent-runtime.epoch",
                "agent-runtime",
                "Agent runtime epoch",
                epoch),
            ControlCenterRuntimeMetric.Number(
                "agent-runtime.cycle",
                "agent-runtime",
                "Language arbitration cycle",
                snapshot.CycleSequence),
            new ControlCenterRuntimeMetric(
                "agent-runtime.active-source",
                "agent-runtime",
                "Active language source",
                active,
                health: ControlCenterMetricHealth.Healthy),
            ControlCenterRuntimeMetric.Number(
                "agent-runtime.arbitrations",
                "agent-runtime",
                "Language arbitrations",
                snapshot.Arbitrations),
            ControlCenterRuntimeMetric.Number(
                "agent-runtime.publications",
                "agent-runtime",
                "Language publications",
                snapshot.Publications,
                health: ControlCenterMetricHealth.Healthy),
            ControlCenterRuntimeMetric.Number(
                "agent-runtime.failed-publications",
                "agent-runtime",
                "Failed language publications",
                snapshot.FailedPublications,
                health: snapshot.FailedPublications > 0
                    ? ControlCenterMetricHealth.Attention
                    : ControlCenterMetricHealth.Healthy),
            ControlCenterRuntimeMetric.Number(
                "agent-runtime.rejected-claims",
                "agent-runtime",
                "Rejected language claims",
                snapshot.RejectedClaims,
                health: snapshot.RejectedClaims > 0
                    ? ControlCenterMetricHealth.Attention
                    : ControlCenterMetricHealth.Healthy),
            new ControlCenterRuntimeMetric(
                "agent-runtime.last-loser",
                "agent-runtime",
                "Last language loser",
                lastLoser,
                health: lastLoserHealth)
        });
    }

    internal static IReadOnlyList<ControlCenterRuntimeMetric>
        UnderstandingEnvelopeShadow(
            UnderstandingEnvelopeShadowSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var authorityEffects = snapshot.ReplyMutations != 0
            || snapshot.StateMutations != 0
            || snapshot.MemoryMutations != 0
            || snapshot.DecisionInvocations != 0
            || snapshot.ToolInvocations != 0
            || snapshot.ProactiveInvocations != 0
                ? 1L
                : 0L;
        var safetyEffects = snapshot.AdditionalPromptCompositions != 0
            || snapshot.AdditionalProviderCalls != 0
            || authorityEffects != 0;
        return new ReadOnlyCollection<ControlCenterRuntimeMetric>(new[]
        {
            new ControlCenterRuntimeMetric(
                "understanding.status",
                "understanding",
                "Understanding Envelope",
                "Shadow / " + snapshot.LastAdmission,
                health: ControlCenterMetricHealth.Healthy),
            new ControlCenterRuntimeMetric(
                "understanding.counts",
                "understanding",
                "Shadow observations",
                "observed=" + snapshot.Observations
                    + "; accepted=" + snapshot.AcceptedEnvelopes
                    + "; rejected=" + snapshot.RejectedEnvelopes
                    + "; proposals=" + snapshot.AcceptedProposalCount,
                health: snapshot.RejectedEnvelopes > 0
                    ? ControlCenterMetricHealth.Attention
                    : ControlCenterMetricHealth.Healthy),
            new ControlCenterRuntimeMetric(
                "understanding.safety-effects",
                "understanding",
                "Additional Shadow effects",
                "prompt=" + snapshot.AdditionalPromptCompositions
                    + "; provider=" + snapshot.AdditionalProviderCalls
                    + "; authority=" + authorityEffects,
                health: safetyEffects
                    ? ControlCenterMetricHealth.Blocked
                    : ControlCenterMetricHealth.Healthy)
        });
    }

    internal static IReadOnlyList<ControlCenterRuntimeMetric>
        PrioritizeForBoundedTransport(
            IEnumerable<ControlCenterRuntimeMetric> metrics)
    {
        if (metrics == null)
            throw new ArgumentNullException(nameof(metrics));
        var copy = metrics.ToArray();
        if (copy.Any(metric => metric == null))
        {
            throw new ArgumentException(
                "Telemetry metrics cannot contain null.",
                nameof(metrics));
        }
        return new ReadOnlyCollection<ControlCenterRuntimeMetric>(
            copy.OrderBy(metric => IsLowerPriorityDetail(metric.Id) ? 1 : 0)
                .ToArray());
    }

    private static bool IsLowerPriorityDetail(string metricId)
        => metricId is "memory.rolling-file-kib"
            or "memory.imported"
            or "memory.mirrored"
            or "initiative.active-candidates"
            or "initiative.retired-candidates"
            or "initiative.evaluations"
            or "initiative.latest-attention";

    internal static ControlCenterRuntimeMetric DesktopTools(
        FormalIntegrationStopBleedSnapshot snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var value = snapshot.DesktopToolAdmission switch
        {
            FormalDesktopToolAdmission.LegacyEnabled => "Legacy / Enabled",
            FormalDesktopToolAdmission.SuppressedByFormalIntegration
                when snapshot.AdvancedDesktopToolsConfigured =>
                    "Formal / SuppressedConfiguredValue",
            FormalDesktopToolAdmission.SuppressedByFormalIntegration =>
                "Formal / Unavailable",
            _ => "Legacy / Disabled"
        };
        var health = snapshot.DesktopToolAdmission switch
        {
            FormalDesktopToolAdmission.LegacyEnabled =>
                ControlCenterMetricHealth.Attention,
            FormalDesktopToolAdmission.SuppressedByFormalIntegration
                when snapshot.AdvancedDesktopToolsConfigured =>
                    ControlCenterMetricHealth.Attention,
            FormalDesktopToolAdmission.SuppressedByFormalIntegration =>
                ControlCenterMetricHealth.Healthy,
            _ => ControlCenterMetricHealth.Neutral
        };
        return new ControlCenterRuntimeMetric(
            "integration.desktop-tools",
            "integration",
            "Desktop tools",
            value,
            health: health);
    }

    internal static IReadOnlyList<ControlCenterRuntimeMetric> ProviderBinding(
        string effectiveProvider,
        string effectiveModel,
        string desiredProvider,
        string desiredModel,
        bool desiredAvailable,
        string effectiveSource)
    {
        var canonicalEffectiveProvider = CanonicalProvider(effectiveProvider);
        var canonicalDesiredProvider = CanonicalProvider(desiredProvider);
        var effective = BoundedProvider(canonicalEffectiveProvider, "Unknown")
            + " / "
            + BoundedProvider(effectiveModel, "Unknown");
        var desired = desiredAvailable
            ? BoundedProvider(canonicalDesiredProvider, "Unknown")
                + " / "
                + BoundedProvider(desiredModel, "Unknown")
            : "Unassigned";
        var source = BoundedProvider(effectiveSource, "UnknownSource");
        var matches = desiredAvailable
            && string.Equals(
                canonicalEffectiveProvider,
                canonicalDesiredProvider,
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                effectiveModel,
                desiredModel,
                StringComparison.Ordinal);
        return new ReadOnlyCollection<ControlCenterRuntimeMetric>(new[]
        {
            new ControlCenterRuntimeMetric(
                "provider.desired",
                "provider",
                "Provider Desired",
                desired,
                health: desiredAvailable
                    ? matches
                        ? ControlCenterMetricHealth.Healthy
                        : ControlCenterMetricHealth.Attention
                    : ControlCenterMetricHealth.Neutral),
            new ControlCenterRuntimeMetric(
                "provider.effective",
                "provider",
                "Provider Effective",
                effective + " / " + source,
                health: ControlCenterMetricHealth.Healthy),
            new ControlCenterRuntimeMetric(
                "provider.binding",
                "provider",
                "Desired / Effective",
                !desiredAvailable
                    ? "Unassigned / " + source
                    : matches
                        ? "Match / " + source
                        : "Mismatch / " + source,
                health: !desiredAvailable
                    ? ControlCenterMetricHealth.Neutral
                    : matches
                        ? ControlCenterMetricHealth.Healthy
                        : ControlCenterMetricHealth.Attention)
        });
    }

    internal static IReadOnlyList<ControlCenterRuntimeMetric> EffectiveRoles(
        bool formalIntegrationActive,
        string motionAuthority)
    {
        var motion = BoundedProvider(motionAuthority, "Unknown");
        return new ReadOnlyCollection<ControlCenterRuntimeMetric>(new[]
        {
            new ControlCenterRuntimeMetric(
                "integration.memory-role",
                "integration",
                "Memory authority",
                formalIntegrationActive
                    ? "MemoryCore / Behavior"
                    : "Legacy / Behavior"),
            new ControlCenterRuntimeMetric(
                "integration.character-role",
                "integration",
                "Character authority",
                "Retired"),
            new ControlCenterRuntimeMetric(
                "integration.prompt-role",
                "integration",
                "Prompt authority",
                formalIntegrationActive
                    ? "LivingContinuity / Behavior"
                    : "Legacy / Behavior"),
            new ControlCenterRuntimeMetric(
                "integration.motion-role",
                "integration",
                "Motion authority",
                motion + " / Behavior"),
            new ControlCenterRuntimeMetric(
                "integration.orchestrator-role",
                "integration",
                "Orchestrator authority",
                "Retired"),
            new ControlCenterRuntimeMetric(
                "integration.initiative-role",
                "integration",
                "Initiative authority",
                "OriginalGame / Behavior; Legacy Initiative / Retired")
        });
    }

    internal static ControlCenterRuntimeMetric CharacterPromptOwner(
        bool runtimeEnabled,
        bool armed,
        bool ownershipStarted,
        bool activeApplication,
        bool completedApplication)
    {
        // Historical callers may retain their positional inputs, not authority.
        return new ControlCenterRuntimeMetric(
            "character.prompt-owner",
            "character",
            "人格 Prompt 所有權",
            "Retired",
            health: ControlCenterMetricHealth.Neutral);
    }

    private static string BoundedProvider(string? value, string fallback)
    {
        var bounded = (value ?? string.Empty).Trim();
        if (bounded.Length == 0)
            return fallback;
        if (bounded.Length > 56)
            bounded = bounded.Substring(0, 56);
        return new string(bounded.Where(character => !char.IsControl(character)).ToArray());
    }

    private static string CanonicalProvider(string? value)
    {
        var token = new string((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .ToArray());
        if (token.Equals("GoogleGemini", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            return "Gemini";
        if (token.Equals("QwenDashScope", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Qwen", StringComparison.OrdinalIgnoreCase)
            || token.Equals("DashScope", StringComparison.OrdinalIgnoreCase))
            return "Qwen";
        if (token.Equals("AnthropicClaude", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Anthropic", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Claude", StringComparison.OrdinalIgnoreCase))
            return "Anthropic";
        return BoundedProvider(value, "Unknown");
    }
}
