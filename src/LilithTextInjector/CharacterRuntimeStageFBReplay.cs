using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal sealed class CharacterLanguageStyleStageFBReplayScenario
{
    internal CharacterLanguageStyleStageFBConfiguration Configuration {
        get;
    }
    internal string CorrelationId { get; }
    internal long SourceSequence { get; }
    internal PlayerTriggeredRespondAuthorization Authorization { get; }
    internal CharacterRuntimeIdentity ActiveRuntimeIdentity { get; }
    internal string ActiveSessionId { get; }
    internal Guid ActiveSituationId { get; }
    internal long ActiveSituationRevision { get; }
    internal string ProviderRouteId { get; }
    internal string PromptBaselineSha256 { get; }
    internal int PromptBaselineCharacters { get; }
    internal ExistingCharacterPromptInfluenceMode ExistingMode { get; }
    internal CharacterLanguageStyleStageFBExclusion Exclusions { get; }
    internal DateTimeOffset TurnObservedAt { get; }
    internal DateTimeOffset RequestObservedAt { get; }
    internal bool DuplicateBegin { get; }
    internal bool DuplicateRequest { get; }
    internal bool DisableAtEnd { get; }
    internal bool RestartAtEnd { get; }

    internal CharacterLanguageStyleStageFBReplayScenario(
        CharacterLanguageStyleStageFBConfiguration configuration,
        string correlationId,
        long sourceSequence,
        PlayerTriggeredRespondAuthorization authorization,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string promptBaselineSha256,
        int promptBaselineCharacters,
        ExistingCharacterPromptInfluenceMode existingMode,
        CharacterLanguageStyleStageFBExclusion exclusions,
        DateTimeOffset turnObservedAt,
        DateTimeOffset requestObservedAt,
        bool duplicateBegin,
        bool duplicateRequest,
        bool disableAtEnd,
        bool restartAtEnd)
    {
        Configuration = configuration
            ?? throw new ArgumentNullException(nameof(configuration));
        CorrelationId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                correlationId,
                nameof(correlationId));
        SourceSequence =
            CharacterLanguageStyleStageFContractGuard.Positive(
                sourceSequence,
                nameof(sourceSequence));
        Authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
        ActiveRuntimeIdentity = activeRuntimeIdentity
            ?? throw new ArgumentNullException(
                nameof(activeRuntimeIdentity));
        ActiveSessionId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                activeSessionId,
                nameof(activeSessionId));
        ActiveSituationId =
            CharacterLanguageStyleStageFContractGuard.NonEmpty(
                activeSituationId,
                nameof(activeSituationId));
        ActiveSituationRevision =
            CharacterLanguageStyleStageFContractGuard.Positive(
                activeSituationRevision,
                nameof(activeSituationRevision));
        ProviderRouteId = providerRouteId;
        PromptBaselineSha256 = promptBaselineSha256;
        PromptBaselineCharacters = promptBaselineCharacters;
        ExistingMode = existingMode;
        Exclusions = exclusions;
        TurnObservedAt = turnObservedAt;
        RequestObservedAt = requestObservedAt;
        DuplicateBegin = duplicateBegin;
        DuplicateRequest = duplicateRequest;
        DisableAtEnd = disableAtEnd;
        RestartAtEnd = restartAtEnd;
    }
}

internal sealed class CharacterLanguageStyleStageFBReplayResult
{
    internal CharacterLanguageStyleStageFBSnapshot BeforeLifecycleEnd {
        get;
    }
    internal CharacterLanguageStyleStageFBSnapshot FinalSnapshot {
        get;
    }
    internal string TraceFingerprint { get; }
    internal CharacterLanguageStyleStageFBAuthorityCounters
        AuthorityCounters { get; }

    internal CharacterLanguageStyleStageFBReplayResult(
        CharacterLanguageStyleStageFBSnapshot beforeLifecycleEnd,
        CharacterLanguageStyleStageFBSnapshot finalSnapshot)
    {
        BeforeLifecycleEnd = beforeLifecycleEnd
            ?? throw new ArgumentNullException(nameof(beforeLifecycleEnd));
        FinalSnapshot = finalSnapshot
            ?? throw new ArgumentNullException(nameof(finalSnapshot));
        TraceFingerprint = Fingerprint(
            beforeLifecycleEnd,
            finalSnapshot);
        AuthorityCounters =
            CharacterLanguageStyleStageFBAuthorityCounters.Zero;
    }

    private static string Fingerprint(
        CharacterLanguageStyleStageFBSnapshot beforeLifecycleEnd,
        CharacterLanguageStyleStageFBSnapshot finalSnapshot)
    {
        var builder = new StringBuilder();
        Append(builder, (int)beforeLifecycleEnd.Status);
        Append(builder, beforeLifecycleEnd.PendingCount);
        Append(builder, beforeLifecycleEnd.TerminalCount);
        foreach (var record in beforeLifecycleEnd.Records)
        {
            Append(builder, record.Sequence);
            Append(builder, (int)record.Kind);
            Append(builder, (int)record.Admission);
            Append(builder, record.CorrelationId ?? string.Empty);
            Append(
                builder,
                record.AuthorizationId?.ToString("N")
                    ?? string.Empty);
            Append(builder, record.ProviderRouteId ?? string.Empty);
            Append(builder, record.PromptBaselineSha256 ?? string.Empty);
            Append(builder, record.PromptBaselineCharacters);
            Append(
                builder,
                record.ExistingMode.HasValue
                    ? (int)record.ExistingMode.Value
                    : -1);
            Append(builder, (int)record.Exclusions);
            Append(
                builder,
                record.FutureOwnerObservation.HasValue
                    ? (int)record.FutureOwnerObservation.Value
                    : -1);
            Append(builder, record.ObservedAt.ToUnixTimeMilliseconds());
        }
        Append(builder, (int)finalSnapshot.Status);
        Append(builder, finalSnapshot.PendingCount);
        Append(builder, finalSnapshot.TerminalCount);
        Append(builder, finalSnapshot.Records.Count);
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, object value)
    {
        var text = value switch
        {
            IFormattable formattable => formattable.ToString(
                null,
                CultureInfo.InvariantCulture),
            _ => value.ToString()
        } ?? string.Empty;
        builder.Append(text.Length)
            .Append(':')
            .Append(text)
            .Append('|');
    }
}

internal static class CharacterLanguageStyleStageFBReplay
{
    internal static CharacterLanguageStyleStageFBReplayResult Run(
        CharacterLanguageStyleStageFBReplayScenario scenario)
    {
        if (scenario == null)
            throw new ArgumentNullException(nameof(scenario));
        var host = new CharacterLanguageStyleStageFBObserveHost();
        host.Reconcile(scenario.Configuration);
        var correlation = host.TryBegin(
            scenario.CorrelationId,
            scenario.SourceSequence,
            scenario.Authorization,
            scenario.TurnObservedAt);
        if (scenario.DuplicateBegin)
        {
            host.TryBegin(
                scenario.CorrelationId,
                scenario.SourceSequence,
                scenario.Authorization,
                scenario.TurnObservedAt);
        }
        if (correlation != null)
        {
            host.TryObserveRequest(
                correlation,
                scenario.ActiveRuntimeIdentity,
                scenario.ActiveSessionId,
                scenario.ActiveSituationId,
                scenario.ActiveSituationRevision,
                scenario.ProviderRouteId,
                scenario.PromptBaselineSha256,
                scenario.PromptBaselineCharacters,
                scenario.ExistingMode,
                scenario.Exclusions,
                scenario.RequestObservedAt);
            if (scenario.DuplicateRequest)
            {
                host.TryObserveRequest(
                    correlation,
                    scenario.ActiveRuntimeIdentity,
                    scenario.ActiveSessionId,
                    scenario.ActiveSituationId,
                    scenario.ActiveSituationRevision,
                    scenario.ProviderRouteId,
                    scenario.PromptBaselineSha256,
                    scenario.PromptBaselineCharacters,
                    scenario.ExistingMode,
                    scenario.Exclusions,
                    scenario.RequestObservedAt);
            }
        }

        var beforeLifecycleEnd = host.GetSnapshot();
        if (scenario.DisableAtEnd)
        {
            host.Reconcile(
                CharacterLanguageStyleStageFBConfiguration.Off);
        }
        if (scenario.RestartAtEnd)
            host.Restart();
        return new CharacterLanguageStyleStageFBReplayResult(
            beforeLifecycleEnd,
            host.GetSnapshot());
    }
}
