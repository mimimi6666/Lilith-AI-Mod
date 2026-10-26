using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum CharacterLanguageStyleStageFReplayOperation
{
    IssueLease = 0,
    DuplicateIssue = 1,
    ConsumeLease = 2,
    VerifyPromptDiff = 3,
    DuplicateConsume = 4,
    Restart = 5
}

internal sealed class CharacterLanguageStyleStageFReplayRecord
{
    internal long Sequence { get; }
    internal CharacterLanguageStyleStageFReplayOperation Operation {
        get;
    }
    internal CharacterLanguageStyleStageFAdmission Admission { get; }
    internal Guid? LeaseId { get; }
    internal LanguageStylePromptOwner? Owner { get; }
    internal string? DirectiveId { get; }
    internal CharacterLanguageStyleId? Style { get; }
    internal string? BaselineSha256 { get; }
    internal string? FinalSha256 { get; }

    internal CharacterLanguageStyleStageFReplayRecord(
        long sequence,
        CharacterLanguageStyleStageFReplayOperation operation,
        CharacterLanguageStyleStageFAdmission admission,
        Guid? leaseId,
        LanguageStylePromptOwner? owner,
        string? directiveId,
        CharacterLanguageStyleId? style,
        string? baselineSha256,
        string? finalSha256)
    {
        Sequence = CharacterLanguageStyleStageFContractGuard.Positive(
            sequence,
            nameof(sequence));
        if (!Enum.IsDefined(
                typeof(CharacterLanguageStyleStageFReplayOperation),
                operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }
        if (!Enum.IsDefined(
                typeof(CharacterLanguageStyleStageFAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }

        Operation = operation;
        Admission = admission;
        LeaseId = leaseId;
        Owner = owner;
        DirectiveId = directiveId;
        Style = style;
        BaselineSha256 = baselineSha256;
        FinalSha256 = finalSha256;
    }
}

internal sealed class CharacterLanguageStyleStageFReplayScenario
{
    internal PlayerTriggeredRespondAuthorization Authorization { get; }
    internal PlayerTriggeredResponseExpressionBinding Expression { get; }
    internal CharacterLanguageStyleProfile Profile { get; }
    internal Guid LeaseId { get; }
    internal string DirectiveId { get; }
    internal LanguageStylePromptOwner RequestedOwner { get; }
    internal ExistingCharacterPromptInfluenceMode ExistingMode { get; }
    internal CharacterLanguageStyleId SelectedStyle { get; }
    internal CharacterRuntimeIdentity ActiveRuntimeIdentity { get; }
    internal string ActiveSessionId { get; }
    internal Guid ActiveSituationId { get; }
    internal long ActiveSituationRevision { get; }
    internal string ProviderRouteId { get; }
    internal string BaselinePrompt { get; }
    internal string FinalPrompt { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset LeaseValidUntil { get; }
    internal DateTimeOffset ConsumedAt { get; }
    internal int MaximumFinalCharacters { get; }
    internal bool AttemptDuplicateIssue { get; }
    internal bool AttemptDuplicateConsume { get; }
    internal bool RestartAtEnd { get; }

    internal CharacterLanguageStyleStageFReplayScenario(
        PlayerTriggeredRespondAuthorization authorization,
        PlayerTriggeredResponseExpressionBinding expression,
        CharacterLanguageStyleProfile profile,
        Guid leaseId,
        string directiveId,
        LanguageStylePromptOwner requestedOwner,
        ExistingCharacterPromptInfluenceMode existingMode,
        CharacterLanguageStyleId selectedStyle,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string baselinePrompt,
        string finalPrompt,
        DateTimeOffset issuedAt,
        DateTimeOffset leaseValidUntil,
        DateTimeOffset consumedAt,
        int maximumFinalCharacters,
        bool attemptDuplicateIssue,
        bool attemptDuplicateConsume,
        bool restartAtEnd)
    {
        Authorization = authorization
            ?? throw new ArgumentNullException(nameof(authorization));
        Expression = expression
            ?? throw new ArgumentNullException(nameof(expression));
        Profile = profile ?? throw new ArgumentNullException(nameof(profile));
        LeaseId = CharacterLanguageStyleStageFContractGuard.NonEmpty(
            leaseId,
            nameof(leaseId));
        DirectiveId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                directiveId,
                nameof(directiveId));
        if (!Enum.IsDefined(
                typeof(LanguageStylePromptOwner),
                requestedOwner))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedOwner));
        }
        if (!Enum.IsDefined(
                typeof(ExistingCharacterPromptInfluenceMode),
                existingMode))
        {
            throw new ArgumentOutOfRangeException(nameof(existingMode));
        }
        if (!Enum.IsDefined(
                typeof(CharacterLanguageStyleId),
                selectedStyle))
        {
            throw new ArgumentOutOfRangeException(nameof(selectedStyle));
        }

        RequestedOwner = requestedOwner;
        ExistingMode = existingMode;
        SelectedStyle = selectedStyle;
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
        ProviderRouteId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                providerRouteId,
                nameof(providerRouteId));
        BaselinePrompt = baselinePrompt
            ?? throw new ArgumentNullException(nameof(baselinePrompt));
        FinalPrompt = finalPrompt
            ?? throw new ArgumentNullException(nameof(finalPrompt));
        IssuedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                issuedAt,
                nameof(issuedAt));
        LeaseValidUntil =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                leaseValidUntil,
                nameof(leaseValidUntil));
        ConsumedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                consumedAt,
                nameof(consumedAt));
        MaximumFinalCharacters =
            CharacterLanguageStyleStageFContractGuard.Positive(
                maximumFinalCharacters,
                nameof(maximumFinalCharacters));
        AttemptDuplicateIssue = attemptDuplicateIssue;
        AttemptDuplicateConsume = attemptDuplicateConsume;
        RestartAtEnd = restartAtEnd;
    }
}

internal sealed class CharacterLanguageStyleStageFReplayResult
{
    internal IReadOnlyList<CharacterLanguageStyleStageFReplayRecord>
        Records { get; }
    internal CharacterPromptOwnershipSnapshot BeforeRestart { get; }
    internal CharacterPromptOwnershipSnapshot AfterReplay { get; }
    internal string TraceFingerprint { get; }
    internal CharacterRuntimeStageFAuthorityCounters AuthorityCounters {
        get;
    }

    internal CharacterLanguageStyleStageFReplayResult(
        IEnumerable<CharacterLanguageStyleStageFReplayRecord> records,
        CharacterPromptOwnershipSnapshot beforeRestart,
        CharacterPromptOwnershipSnapshot afterReplay)
    {
        Records = (records
                ?? throw new ArgumentNullException(nameof(records)))
            .ToArray();
        BeforeRestart = beforeRestart
            ?? throw new ArgumentNullException(nameof(beforeRestart));
        AfterReplay = afterReplay
            ?? throw new ArgumentNullException(nameof(afterReplay));
        TraceFingerprint = Fingerprint(Records, beforeRestart, afterReplay);
        AuthorityCounters = CharacterRuntimeStageFAuthorityCounters.Zero;
    }

    private static string Fingerprint(
        IEnumerable<CharacterLanguageStyleStageFReplayRecord> records,
        CharacterPromptOwnershipSnapshot beforeRestart,
        CharacterPromptOwnershipSnapshot afterReplay)
    {
        var builder = new StringBuilder();
        foreach (var record in records)
        {
            Append(builder, record.Sequence);
            Append(builder, (int)record.Operation);
            Append(builder, (int)record.Admission);
            Append(builder, record.LeaseId?.ToString("N") ?? string.Empty);
            Append(builder, record.Owner.HasValue
                ? (int)record.Owner.Value
                : -1);
            Append(builder, record.DirectiveId ?? string.Empty);
            Append(builder, record.Style.HasValue
                ? (int)record.Style.Value
                : -1);
            Append(builder, record.BaselineSha256 ?? string.Empty);
            Append(builder, record.FinalSha256 ?? string.Empty);
        }
        Append(builder, beforeRestart.LeaseCount);
        Append(builder, beforeRestart.TerminalCount);
        Append(builder, afterReplay.LeaseCount);
        Append(builder, afterReplay.TerminalCount);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
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

internal static class CharacterLanguageStyleStageFReplay
{
    internal static CharacterLanguageStyleStageFReplayResult Run(
        CharacterLanguageStyleStageFReplayScenario scenario)
    {
        if (scenario == null)
            throw new ArgumentNullException(nameof(scenario));

        var records =
            new List<CharacterLanguageStyleStageFReplayRecord>();
        var ledger = new CharacterPromptInfluenceOwnershipLedger();
        long sequence = 0;
        var baselineHash =
            CharacterLanguageStyleStageFContractGuard.Sha256(
                scenario.BaselinePrompt);
        var issue = ledger.TryIssue(
            scenario.LeaseId,
            scenario.Authorization,
            scenario.RequestedOwner,
            scenario.ExistingMode,
            scenario.ActiveRuntimeIdentity,
            scenario.ActiveSessionId,
            scenario.ActiveSituationId,
            scenario.ActiveSituationRevision,
            scenario.ProviderRouteId,
            baselineHash,
            scenario.IssuedAt,
            scenario.LeaseValidUntil);
        records.Add(
            RecordIssue(
                ++sequence,
                CharacterLanguageStyleStageFReplayOperation.IssueLease,
                issue));

        if (scenario.AttemptDuplicateIssue)
        {
            var duplicate = ledger.TryIssue(
                scenario.LeaseId,
                scenario.Authorization,
                scenario.RequestedOwner,
                scenario.ExistingMode,
                scenario.ActiveRuntimeIdentity,
                scenario.ActiveSessionId,
                scenario.ActiveSituationId,
                scenario.ActiveSituationRevision,
                scenario.ProviderRouteId,
                baselineHash,
                scenario.IssuedAt,
                scenario.LeaseValidUntil);
            records.Add(
                RecordIssue(
                    ++sequence,
                    CharacterLanguageStyleStageFReplayOperation
                        .DuplicateIssue,
                    duplicate));
        }

        CharacterLanguageStyleDirectiveResult? consumed = null;
        if (issue.Lease != null)
        {
            consumed = ledger.TryConsume(
                issue.Lease.LeaseId,
                scenario.DirectiveId,
                scenario.Authorization,
                scenario.Expression,
                scenario.Profile,
                scenario.SelectedStyle,
                scenario.ActiveRuntimeIdentity,
                scenario.ActiveSessionId,
                scenario.ActiveSituationId,
                scenario.ActiveSituationRevision,
                scenario.ProviderRouteId,
                baselineHash,
                scenario.ConsumedAt);
            records.Add(
                RecordDirective(
                    ++sequence,
                    CharacterLanguageStyleStageFReplayOperation
                        .ConsumeLease,
                    issue.Lease,
                    consumed));
        }

        if (issue.Lease != null && consumed?.Directive != null)
        {
            var diff = CharacterPromptDiffVerifier.Verify(
                scenario.BaselinePrompt,
                scenario.FinalPrompt,
                issue.Lease,
                consumed.Directive,
                scenario.MaximumFinalCharacters);
            records.Add(
                new CharacterLanguageStyleStageFReplayRecord(
                    ++sequence,
                    CharacterLanguageStyleStageFReplayOperation
                        .VerifyPromptDiff,
                    diff.Admission,
                    issue.Lease.LeaseId,
                    issue.Lease.Owner,
                    consumed.Directive.DirectiveId,
                    consumed.Directive.Style,
                    diff.BaselineSha256,
                    diff.FinalSha256));
        }

        if (scenario.AttemptDuplicateConsume && issue.Lease != null)
        {
            var duplicate = ledger.TryConsume(
                issue.Lease.LeaseId,
                scenario.DirectiveId,
                scenario.Authorization,
                scenario.Expression,
                scenario.Profile,
                scenario.SelectedStyle,
                scenario.ActiveRuntimeIdentity,
                scenario.ActiveSessionId,
                scenario.ActiveSituationId,
                scenario.ActiveSituationRevision,
                scenario.ProviderRouteId,
                baselineHash,
                scenario.ConsumedAt);
            records.Add(
                RecordDirective(
                    ++sequence,
                    CharacterLanguageStyleStageFReplayOperation
                        .DuplicateConsume,
                    issue.Lease,
                    duplicate));
        }

        var beforeRestart = ledger.Snapshot();
        if (scenario.RestartAtEnd)
        {
            ledger.Reset();
            records.Add(
                new CharacterLanguageStyleStageFReplayRecord(
                    ++sequence,
                    CharacterLanguageStyleStageFReplayOperation.Restart,
                    CharacterLanguageStyleStageFAdmission.RestartCleared,
                    leaseId: null,
                    owner: null,
                    directiveId: null,
                    style: null,
                    baselineSha256: null,
                    finalSha256: null));
        }

        return new CharacterLanguageStyleStageFReplayResult(
            records,
            beforeRestart,
            ledger.Snapshot());
    }

    private static CharacterLanguageStyleStageFReplayRecord RecordIssue(
        long sequence,
        CharacterLanguageStyleStageFReplayOperation operation,
        CharacterPromptOwnershipIssue issue)
        => new(
            sequence,
            operation,
            issue.Admission,
            issue.Lease?.LeaseId,
            issue.Lease?.Owner,
            directiveId: null,
            style: null,
            baselineSha256: issue.Lease?.PromptBaselineSha256,
            finalSha256: null);

    private static CharacterLanguageStyleStageFReplayRecord
        RecordDirective(
            long sequence,
            CharacterLanguageStyleStageFReplayOperation operation,
            CharacterPromptInfluenceOwnershipLease lease,
            CharacterLanguageStyleDirectiveResult result)
        => new(
            sequence,
            operation,
            result.Admission,
            lease.LeaseId,
            lease.Owner,
            result.Directive?.DirectiveId,
            result.Directive?.Style,
            baselineSha256: lease.PromptBaselineSha256,
            finalSha256: null);
}
