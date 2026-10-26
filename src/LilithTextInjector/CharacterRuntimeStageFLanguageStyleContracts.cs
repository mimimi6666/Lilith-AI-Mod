using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum PlayerTriggeredRespondAuthorizationKind
{
    LocalDecisionCoreRespond = 0,
    ExplicitPlayerRequestedLegacyRespond = 1
}

internal enum PlayerTriggeredResponseSurface
{
    TextEntry = 0,
    PushToTalkTranscript = 1
}

internal enum LanguageStylePromptOwner
{
    LegacyOwned = 0,
    CharacterRuntimeOwned = 1
}

internal enum ExistingCharacterPromptInfluenceMode
{
    Off = 0,
    Observe = 1,
    Assist = 2
}

internal enum CharacterLanguageStyleId
{
    Balanced = 0,
    Direct = 1,
    Restrained = 2,
    Warm = 3
}

internal enum CharacterLanguageStyleStageFAdmission
{
    LeaseIssued = 0,
    OwnerConflictLegacyOwned = 1,
    DuplicateAuthorizationRejected = 2,
    DuplicateLeaseRejected = 3,
    AuthorizationExpired = 4,
    RuntimeMismatch = 5,
    SessionMismatch = 6,
    SituationMismatch = 7,
    LeaseNotFound = 8,
    LeaseExpired = 9,
    LeaseAlreadyTerminal = 10,
    AuthorizationMismatch = 11,
    ProviderRouteMismatch = 12,
    BaselineHashMismatch = 13,
    ExpressionMismatch = 14,
    ProfileMismatch = 15,
    LegacyOwnedNoDirective = 16,
    DirectiveCreated = 17,
    PromptDiffValid = 18,
    PromptDiffRejected = 19,
    RestartCleared = 20
}

internal static class CharacterLanguageStyleIds
{
    internal static IReadOnlyList<CharacterLanguageStyleId> All {
        get;
    } = Enum.GetValues<CharacterLanguageStyleId>();

    internal static string ToStableId(CharacterLanguageStyleId style)
        => style switch
        {
            CharacterLanguageStyleId.Balanced => "style-balanced",
            CharacterLanguageStyleId.Direct => "style-direct",
            CharacterLanguageStyleId.Restrained => "style-restrained",
            CharacterLanguageStyleId.Warm => "style-warm",
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
}

internal static class CharacterLanguageStyleStageFContractGuard
{
    internal static string Identifier(string? value, string parameterName)
        => CharacterRuntimeContractGuard.Identifier(value, parameterName);

    internal static Guid NonEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static DateTimeOffset Timestamp(
        DateTimeOffset value,
        string parameterName)
        => CharacterRuntimeContractGuard.Timestamp(value, parameterName);

    internal static long Positive(long value, string parameterName)
        => CharacterRuntimeContractGuard.Positive(value, parameterName);

    internal static int Positive(int value, string parameterName)
        => CharacterRuntimeContractGuard.Positive(value, parameterName);

    internal static string Sha256(string value)
        => Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        value ?? throw new ArgumentNullException(
                            nameof(value)))))
            .ToLowerInvariant();

    internal static string Sha256Identifier(
        string? value,
        string parameterName)
    {
        var result = Identifier(value, parameterName);
        if (result.Length != 64
            || result.Any(
                character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A lowercase or uppercase SHA-256 hex value is required.",
                parameterName);
        }
        return result.ToLowerInvariant();
    }
}

internal sealed class PlayerTriggeredRespondAuthorization
{
    internal Guid AuthorizationId { get; }
    internal Guid TurnId { get; }
    internal PlayerTriggeredResponseSurface InputSurface { get; }
    internal PlayerTriggeredRespondAuthorizationKind AuthorizationKind {
        get;
    }
    internal Guid? DecisionId { get; }
    internal string? GoalId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal string SessionId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CharacterExpressionCapability Capability =>
        CharacterExpressionCapability.PlayerTriggeredResponse;
    internal CharacterExpressionTarget Target { get; }
    internal string SemanticCommitmentHash { get; }
    internal CharacterExpressionSafetyEnvelope SafetyEnvelope { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal bool ClaimsLocalDecisionCoreDecision =>
        AuthorizationKind
            == PlayerTriggeredRespondAuthorizationKind
                .LocalDecisionCoreRespond;
    internal bool IsExplicitLegacyPlayerRequest =>
        AuthorizationKind
            == PlayerTriggeredRespondAuthorizationKind
                .ExplicitPlayerRequestedLegacyRespond;

    internal PlayerTriggeredRespondAuthorization(
        Guid authorizationId,
        Guid turnId,
        PlayerTriggeredResponseSurface inputSurface,
        PlayerTriggeredRespondAuthorizationKind authorizationKind,
        Guid? decisionId,
        string? goalId,
        Guid situationId,
        long situationRevision,
        string sessionId,
        CharacterRuntimeIdentity runtimeIdentity,
        string semanticCommitmentHash,
        CharacterExpressionSafetyEnvelope safetyEnvelope,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil)
    {
        AuthorizationId =
            CharacterLanguageStyleStageFContractGuard.NonEmpty(
                authorizationId,
                nameof(authorizationId));
        TurnId = CharacterLanguageStyleStageFContractGuard.NonEmpty(
            turnId,
            nameof(turnId));
        if (!Enum.IsDefined(
                typeof(PlayerTriggeredResponseSurface),
                inputSurface))
        {
            throw new ArgumentOutOfRangeException(nameof(inputSurface));
        }
        if (!Enum.IsDefined(
                typeof(PlayerTriggeredRespondAuthorizationKind),
                authorizationKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(authorizationKind));
        }
        if (decisionId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(decisionId));

        var normalizedGoal = goalId == null
            ? null
            : CharacterLanguageStyleStageFContractGuard.Identifier(
                goalId,
                nameof(goalId));
        if (authorizationKind
                == PlayerTriggeredRespondAuthorizationKind
                    .LocalDecisionCoreRespond
            && (!decisionId.HasValue || normalizedGoal == null))
        {
            throw new ArgumentException(
                "Local Decision Core Respond authority requires its "
                + "exact Decision and Goal identities.",
                nameof(decisionId));
        }
        if (authorizationKind
                == PlayerTriggeredRespondAuthorizationKind
                    .ExplicitPlayerRequestedLegacyRespond
            && (decisionId.HasValue || normalizedGoal != null))
        {
            throw new ArgumentException(
                "An explicit legacy player request must not claim or "
                + "synthesize Decision or Goal identity.",
                nameof(decisionId));
        }

        InputSurface = inputSurface;
        AuthorizationKind = authorizationKind;
        DecisionId = decisionId;
        GoalId = normalizedGoal;
        SituationId =
            CharacterLanguageStyleStageFContractGuard.NonEmpty(
                situationId,
                nameof(situationId));
        SituationRevision =
            CharacterLanguageStyleStageFContractGuard.Positive(
                situationRevision,
                nameof(situationRevision));
        SessionId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                sessionId,
                nameof(sessionId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        Target = new CharacterExpressionTarget(
            CharacterExpressionTargetKind.Player,
            "primary-player");
        SemanticCommitmentHash =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                semanticCommitmentHash,
                nameof(semanticCommitmentHash));
        SafetyEnvelope = safetyEnvelope
            ?? throw new ArgumentNullException(nameof(safetyEnvelope));
        IssuedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                issuedAt,
                nameof(issuedAt));
        ValidUntil =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                validUntil,
                nameof(validUntil));
        if (ValidUntil <= IssuedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(validUntil),
                "Authorization expiry must follow issuance.");
        }
    }
}

internal sealed class PlayerTriggeredResponseExpressionBinding
{
    internal string ExpressionIntentId { get; }
    internal Guid AuthorizationId { get; }
    internal PlayerTriggeredRespondAuthorizationKind AuthorizationKind {
        get;
    }
    internal Guid? DecisionId { get; }
    internal string? GoalId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal CharacterExpressionCapability Capability { get; }
    internal CharacterExpressionTarget Target { get; }
    internal string SemanticCommitmentHash { get; }
    internal CharacterExpressionSafetyEnvelope SafetyEnvelope { get; }
    internal EquivalentExpressionSet EquivalentExpressions { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal bool CreatesDecision => false;

    private PlayerTriggeredResponseExpressionBinding(
        string expressionIntentId,
        PlayerTriggeredRespondAuthorization authorization,
        Guid? decisionId,
        string? goalId,
        CharacterExpressionCapability capability,
        CharacterExpressionTarget target,
        string semanticCommitmentHash,
        CharacterExpressionSafetyEnvelope safetyEnvelope,
        EquivalentExpressionSet equivalentExpressions,
        DateTimeOffset validUntil)
    {
        ExpressionIntentId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                expressionIntentId,
                nameof(expressionIntentId));
        AuthorizationId = authorization.AuthorizationId;
        AuthorizationKind = authorization.AuthorizationKind;
        DecisionId = decisionId;
        GoalId = goalId;
        SituationId = authorization.SituationId;
        SituationRevision = authorization.SituationRevision;
        RuntimeIdentity = authorization.RuntimeIdentity;
        Capability = capability;
        Target = target;
        SemanticCommitmentHash = semanticCommitmentHash;
        SafetyEnvelope = safetyEnvelope;
        EquivalentExpressions = equivalentExpressions;
        ValidUntil =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                validUntil,
                nameof(validUntil));

        if (ValidUntil > authorization.ValidUntil)
        {
            throw new ArgumentOutOfRangeException(
                nameof(validUntil),
                "Expression binding cannot outlive authorization.");
        }
        ValidateExactAuthorization(authorization);
        ValidateFixedStyleSet(equivalentExpressions);
    }

    internal static PlayerTriggeredResponseExpressionBinding
        FromExplicitPlayerRequest(
            string expressionIntentId,
            PlayerTriggeredRespondAuthorization authorization,
            EquivalentExpressionSet equivalentExpressions,
            DateTimeOffset validUntil)
    {
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        if (!authorization.IsExplicitLegacyPlayerRequest)
        {
            throw new ArgumentException(
                "Explicit-player binding requires explicit legacy "
                + "player-request authority.",
                nameof(authorization));
        }

        return new PlayerTriggeredResponseExpressionBinding(
            expressionIntentId,
            authorization,
            decisionId: null,
            goalId: null,
            authorization.Capability,
            authorization.Target,
            authorization.SemanticCommitmentHash,
            authorization.SafetyEnvelope,
            equivalentExpressions
                ?? throw new ArgumentNullException(
                    nameof(equivalentExpressions)),
            validUntil);
    }

    internal static PlayerTriggeredResponseExpressionBinding
        FromLocalDecisionCore(
            PlayerTriggeredRespondAuthorization authorization,
            ExpressionIntent expressionIntent)
    {
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        if (expressionIntent == null)
            throw new ArgumentNullException(nameof(expressionIntent));
        if (!authorization.ClaimsLocalDecisionCoreDecision)
        {
            throw new ArgumentException(
                "Local Decision Core binding requires Decision-backed "
                + "authorization.",
                nameof(authorization));
        }
        if (expressionIntent.Disposition != DecisionDisposition.Respond)
        {
            throw new ArgumentException(
                "Stage F only accepts an existing Respond intent.",
                nameof(expressionIntent));
        }
        if (authorization.DecisionId != expressionIntent.DecisionId
            || !string.Equals(
                authorization.GoalId,
                expressionIntent.GoalId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Expression intent must preserve the exact accepted "
                + "Decision and Goal.",
                nameof(expressionIntent));
        }

        return new PlayerTriggeredResponseExpressionBinding(
            expressionIntent.IntentId,
            authorization,
            expressionIntent.DecisionId,
            expressionIntent.GoalId,
            expressionIntent.Capability,
            expressionIntent.Target,
            expressionIntent.SemanticCommitmentHash,
            expressionIntent.SafetyEnvelope,
            expressionIntent.EquivalentExpressions,
            expressionIntent.ValidUntil);
    }

    internal bool MatchesAuthorization(
        PlayerTriggeredRespondAuthorization? authorization)
        => authorization != null
            && AuthorizationId == authorization.AuthorizationId
            && AuthorizationKind == authorization.AuthorizationKind
            && DecisionId == authorization.DecisionId
            && string.Equals(
                GoalId,
                authorization.GoalId,
                StringComparison.Ordinal)
            && SituationId == authorization.SituationId
            && SituationRevision == authorization.SituationRevision
            && RuntimeIdentity.Matches(
                authorization.RuntimeIdentity)
            && Capability == authorization.Capability
            && Target.Matches(authorization.Target)
            && string.Equals(
                SemanticCommitmentHash,
                authorization.SemanticCommitmentHash,
                StringComparison.Ordinal)
            && SafetyEnvelope.Matches(
                authorization.SafetyEnvelope)
            && ValidUntil <= authorization.ValidUntil;

    private void ValidateExactAuthorization(
        PlayerTriggeredRespondAuthorization authorization)
    {
        if (!MatchesAuthorization(authorization))
        {
            throw new ArgumentException(
                "Expression binding must preserve exact authorization "
                + "identity and semantics.",
                nameof(authorization));
        }
        if (Capability
                != CharacterExpressionCapability.PlayerTriggeredResponse
            || Target.Kind != CharacterExpressionTargetKind.Player)
        {
            throw new ArgumentException(
                "Stage F accepts only player-triggered response "
                + "expression bindings.",
                nameof(authorization));
        }
    }

    private static void ValidateFixedStyleSet(
        EquivalentExpressionSet equivalentExpressions)
    {
        var expected = CharacterLanguageStyleIds.All
            .Select(CharacterLanguageStyleIds.ToStableId)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var actual = equivalentExpressions.SemanticVariantIds
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Stage F EquivalentExpressionSet must contain exactly "
                + "the four accepted style IDs.",
                nameof(equivalentExpressions));
        }
    }
}

internal sealed class CharacterLanguageStyleTemplate
{
    internal const int MaximumSuffixCharacters = 512;

    internal CharacterLanguageStyleId Style { get; }
    internal string StyleId { get; }
    internal string TemplateVersion { get; }
    internal string ExactSuffixText { get; }
    internal string SuffixSha256 { get; }

    internal CharacterLanguageStyleTemplate(
        CharacterLanguageStyleId style,
        string templateVersion,
        string exactSuffixText)
    {
        if (!Enum.IsDefined(typeof(CharacterLanguageStyleId), style))
            throw new ArgumentOutOfRangeException(nameof(style));
        if (string.IsNullOrWhiteSpace(exactSuffixText)
            || exactSuffixText.Length > MaximumSuffixCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exactSuffixText));
        }

        Style = style;
        StyleId = CharacterLanguageStyleIds.ToStableId(style);
        TemplateVersion =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                templateVersion,
                nameof(templateVersion));
        ExactSuffixText = exactSuffixText;
        SuffixSha256 =
            CharacterLanguageStyleStageFContractGuard.Sha256(
                exactSuffixText);
    }
}

internal sealed class CharacterLanguageStyleProfile
{
    private readonly IReadOnlyDictionary<
        CharacterLanguageStyleId,
        CharacterLanguageStyleTemplate> _templates;

    internal string ProfileId { get; }
    internal int ProfileVersion { get; }
    internal IReadOnlyList<CharacterLanguageStyleTemplate> Templates {
        get;
    }

    internal CharacterLanguageStyleProfile(
        string profileId,
        int profileVersion,
        IEnumerable<CharacterLanguageStyleTemplate> templates)
    {
        ProfileId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                profileId,
                nameof(profileId));
        ProfileVersion =
            CharacterLanguageStyleStageFContractGuard.Positive(
                profileVersion,
                nameof(profileVersion));
        var materialized = (templates
                ?? throw new ArgumentNullException(nameof(templates)))
            .OrderBy(template => template.Style)
            .ToArray();
        if (materialized.Length != CharacterLanguageStyleIds.All.Count
            || materialized.Select(template => template.Style)
                .Distinct()
                .Count() != CharacterLanguageStyleIds.All.Count
            || !materialized.Select(template => template.Style)
                .SequenceEqual(CharacterLanguageStyleIds.All))
        {
            throw new ArgumentException(
                "A profile must define exactly one template for every "
                + "accepted style.",
                nameof(templates));
        }

        Templates = materialized;
        _templates = materialized.ToDictionary(
            template => template.Style,
            template => template);
    }

    internal CharacterLanguageStyleTemplate Get(
        CharacterLanguageStyleId style)
    {
        if (!Enum.IsDefined(typeof(CharacterLanguageStyleId), style)
            || !_templates.TryGetValue(style, out var template))
        {
            throw new ArgumentOutOfRangeException(nameof(style));
        }
        return template;
    }
}

internal sealed class CharacterPromptInfluenceOwnershipLease
{
    internal Guid LeaseId { get; }
    internal Guid AuthorizationId { get; }
    internal Guid TurnId { get; }
    internal Guid SituationId { get; }
    internal long SituationRevision { get; }
    internal string SessionId { get; }
    internal CharacterRuntimeIdentity RuntimeIdentity { get; }
    internal string ProviderRouteId { get; }
    internal string PromptBaselineSha256 { get; }
    internal LanguageStylePromptOwner Owner { get; }
    internal DateTimeOffset IssuedAt { get; }
    internal DateTimeOffset ValidUntil { get; }

    internal CharacterPromptInfluenceOwnershipLease(
        Guid leaseId,
        PlayerTriggeredRespondAuthorization authorization,
        string providerRouteId,
        string promptBaselineSha256,
        LanguageStylePromptOwner owner,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil)
    {
        LeaseId = CharacterLanguageStyleStageFContractGuard.NonEmpty(
            leaseId,
            nameof(leaseId));
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        if (!Enum.IsDefined(typeof(LanguageStylePromptOwner), owner))
            throw new ArgumentOutOfRangeException(nameof(owner));

        AuthorizationId = authorization.AuthorizationId;
        TurnId = authorization.TurnId;
        SituationId = authorization.SituationId;
        SituationRevision = authorization.SituationRevision;
        SessionId = authorization.SessionId;
        RuntimeIdentity = authorization.RuntimeIdentity;
        ProviderRouteId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                providerRouteId,
                nameof(providerRouteId));
        PromptBaselineSha256 =
            CharacterLanguageStyleStageFContractGuard.Sha256Identifier(
                promptBaselineSha256,
                nameof(promptBaselineSha256));
        Owner = owner;
        IssuedAt =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                issuedAt,
                nameof(issuedAt));
        ValidUntil =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                validUntil,
                nameof(validUntil));
        if (ValidUntil <= IssuedAt
            || ValidUntil > authorization.ValidUntil)
        {
            throw new ArgumentOutOfRangeException(nameof(validUntil));
        }
    }
}

internal sealed class CharacterLanguageStyleDirective
{
    internal string DirectiveId { get; }
    internal Guid OwnerLeaseId { get; }
    internal Guid AuthorizationId { get; }
    internal string ExpressionIntentId { get; }
    internal string EquivalentExpressionSetId { get; }
    internal CharacterLanguageStyleId Style { get; }
    internal string StyleId { get; }
    internal string TemplateVersion { get; }
    internal string ExactSuffixText { get; }
    internal string SuffixSha256 { get; }
    internal DateTimeOffset ValidUntil { get; }
    internal bool MetadataOnlyContract => true;
    internal bool AllowsPromptWrite => false;
    internal bool AllowsProviderCall => false;
    internal bool AllowsReplyOutput => false;

    internal CharacterLanguageStyleDirective(
        string directiveId,
        CharacterPromptInfluenceOwnershipLease lease,
        PlayerTriggeredResponseExpressionBinding expression,
        CharacterLanguageStyleTemplate template,
        DateTimeOffset validUntil)
    {
        DirectiveId =
            CharacterLanguageStyleStageFContractGuard.Identifier(
                directiveId,
                nameof(directiveId));
        if (lease == null)
            throw new ArgumentNullException(nameof(lease));
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));
        if (template == null)
            throw new ArgumentNullException(nameof(template));
        if (lease.Owner != LanguageStylePromptOwner.CharacterRuntimeOwned
            || lease.AuthorizationId != expression.AuthorizationId)
        {
            throw new ArgumentException(
                "Only the exact CharacterRuntime-owned lease may "
                + "create a style directive.",
                nameof(lease));
        }
        if (!expression.EquivalentExpressions.SemanticVariantIds.Contains(
                template.StyleId,
                StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "Style must belong to the exact equivalent-expression "
                + "set.",
                nameof(template));
        }

        OwnerLeaseId = lease.LeaseId;
        AuthorizationId = lease.AuthorizationId;
        ExpressionIntentId = expression.ExpressionIntentId;
        EquivalentExpressionSetId =
            expression.EquivalentExpressions.SetId;
        Style = template.Style;
        StyleId = template.StyleId;
        TemplateVersion = template.TemplateVersion;
        ExactSuffixText = template.ExactSuffixText;
        SuffixSha256 = template.SuffixSha256;
        ValidUntil =
            CharacterLanguageStyleStageFContractGuard.Timestamp(
                validUntil,
                nameof(validUntil));
        if (ValidUntil > lease.ValidUntil
            || ValidUntil > expression.ValidUntil)
        {
            throw new ArgumentOutOfRangeException(nameof(validUntil));
        }
    }
}

internal sealed class CharacterPromptOwnershipIssue
{
    internal CharacterLanguageStyleStageFAdmission Admission { get; }
    internal CharacterPromptInfluenceOwnershipLease? Lease { get; }
    internal bool Accepted => Lease != null;

    internal CharacterPromptOwnershipIssue(
        CharacterLanguageStyleStageFAdmission admission,
        CharacterPromptInfluenceOwnershipLease? lease)
    {
        Admission = admission;
        Lease = lease;
    }
}

internal sealed class CharacterLanguageStyleDirectiveResult
{
    internal CharacterLanguageStyleStageFAdmission Admission { get; }
    internal CharacterLanguageStyleDirective? Directive { get; }
    internal bool Accepted =>
        Admission
            is CharacterLanguageStyleStageFAdmission
                .LegacyOwnedNoDirective
            or CharacterLanguageStyleStageFAdmission.DirectiveCreated;

    internal CharacterLanguageStyleDirectiveResult(
        CharacterLanguageStyleStageFAdmission admission,
        CharacterLanguageStyleDirective? directive)
    {
        Admission = admission;
        Directive = directive;
    }
}

internal sealed class CharacterPromptOwnershipSnapshot
{
    internal int LeaseCount { get; }
    internal int TerminalCount { get; }
    internal bool IsEmpty => LeaseCount == 0 && TerminalCount == 0;

    internal CharacterPromptOwnershipSnapshot(
        int leaseCount,
        int terminalCount)
    {
        LeaseCount = leaseCount;
        TerminalCount = terminalCount;
    }
}

internal sealed class CharacterPromptInfluenceOwnershipLedger
{
    private sealed class LeaseState
    {
        internal CharacterPromptInfluenceOwnershipLease Lease { get; }
        internal bool Terminal { get; set; }

        internal LeaseState(
            CharacterPromptInfluenceOwnershipLease lease)
        {
            Lease = lease;
        }
    }

    private readonly Dictionary<Guid, LeaseState> _leases = new();
    private readonly Dictionary<Guid, Guid> _authorizationLeases = new();

    internal CharacterPromptOwnershipIssue TryIssue(
        Guid leaseId,
        PlayerTriggeredRespondAuthorization authorization,
        LanguageStylePromptOwner requestedOwner,
        ExistingCharacterPromptInfluenceMode existingMode,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string promptBaselineSha256,
        DateTimeOffset issuedAt,
        DateTimeOffset validUntil)
    {
        if (authorization == null)
            throw new ArgumentNullException(nameof(authorization));
        if (activeRuntimeIdentity == null)
        {
            throw new ArgumentNullException(
                nameof(activeRuntimeIdentity));
        }
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

        var now = CharacterLanguageStyleStageFContractGuard.Timestamp(
            issuedAt,
            nameof(issuedAt));
        if (authorization.ValidUntil <= now)
            return RejectIssue(
                CharacterLanguageStyleStageFAdmission
                    .AuthorizationExpired);
        if (!authorization.RuntimeIdentity.Matches(
                activeRuntimeIdentity))
        {
            return RejectIssue(
                CharacterLanguageStyleStageFAdmission.RuntimeMismatch);
        }
        if (!string.Equals(
                authorization.SessionId,
                activeSessionId,
                StringComparison.Ordinal))
        {
            return RejectIssue(
                CharacterLanguageStyleStageFAdmission.SessionMismatch);
        }
        if (authorization.SituationId != activeSituationId
            || authorization.SituationRevision
                != activeSituationRevision)
        {
            return RejectIssue(
                CharacterLanguageStyleStageFAdmission.SituationMismatch);
        }
        if (_authorizationLeases.ContainsKey(
                authorization.AuthorizationId))
        {
            return RejectIssue(
                CharacterLanguageStyleStageFAdmission
                    .DuplicateAuthorizationRejected);
        }
        if (_leases.ContainsKey(leaseId))
        {
            return RejectIssue(
                CharacterLanguageStyleStageFAdmission
                    .DuplicateLeaseRejected);
        }

        var owner = requestedOwner
            == LanguageStylePromptOwner.CharacterRuntimeOwned
            && existingMode
                == ExistingCharacterPromptInfluenceMode.Assist
            ? LanguageStylePromptOwner.LegacyOwned
            : requestedOwner;
        var boundedValidUntil = validUntil < authorization.ValidUntil
            ? validUntil
            : authorization.ValidUntil;
        var lease = new CharacterPromptInfluenceOwnershipLease(
            leaseId,
            authorization,
            providerRouteId,
            promptBaselineSha256,
            owner,
            now,
            boundedValidUntil);
        _leases.Add(lease.LeaseId, new LeaseState(lease));
        _authorizationLeases.Add(
            authorization.AuthorizationId,
            lease.LeaseId);
        var admission = owner != requestedOwner
            ? CharacterLanguageStyleStageFAdmission
                .OwnerConflictLegacyOwned
            : CharacterLanguageStyleStageFAdmission.LeaseIssued;
        return new CharacterPromptOwnershipIssue(admission, lease);
    }

    internal CharacterLanguageStyleDirectiveResult TryConsume(
        Guid leaseId,
        string directiveId,
        PlayerTriggeredRespondAuthorization authorization,
        PlayerTriggeredResponseExpressionBinding expression,
        CharacterLanguageStyleProfile profile,
        CharacterLanguageStyleId selectedStyle,
        CharacterRuntimeIdentity activeRuntimeIdentity,
        string activeSessionId,
        Guid activeSituationId,
        long activeSituationRevision,
        string providerRouteId,
        string promptBaselineSha256,
        DateTimeOffset consumedAt)
    {
        if (!_leases.TryGetValue(leaseId, out var state))
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission.LeaseNotFound);
        }
        if (state.Terminal)
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission
                    .LeaseAlreadyTerminal);
        }

        state.Terminal = true;
        var lease = state.Lease;
        var now = CharacterLanguageStyleStageFContractGuard.Timestamp(
            consumedAt,
            nameof(consumedAt));
        if (lease.ValidUntil <= now
            || authorization.ValidUntil <= now
            || expression.ValidUntil <= now)
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission.LeaseExpired);
        }
        if (lease.AuthorizationId != authorization.AuthorizationId
            || lease.TurnId != authorization.TurnId)
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission
                    .AuthorizationMismatch);
        }
        if (!lease.RuntimeIdentity.Matches(activeRuntimeIdentity)
            || !authorization.RuntimeIdentity.Matches(
                activeRuntimeIdentity))
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission.RuntimeMismatch);
        }
        if (!string.Equals(
                lease.SessionId,
                activeSessionId,
                StringComparison.Ordinal)
            || !string.Equals(
                authorization.SessionId,
                activeSessionId,
                StringComparison.Ordinal))
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission.SessionMismatch);
        }
        if (lease.SituationId != activeSituationId
            || authorization.SituationId != activeSituationId
            || lease.SituationRevision != activeSituationRevision
            || authorization.SituationRevision != activeSituationRevision)
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission.SituationMismatch);
        }
        if (!string.Equals(
                lease.ProviderRouteId,
                providerRouteId,
                StringComparison.Ordinal))
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission
                    .ProviderRouteMismatch);
        }
        var baselineHash =
            CharacterLanguageStyleStageFContractGuard.Sha256Identifier(
                promptBaselineSha256,
                nameof(promptBaselineSha256));
        if (!string.Equals(
                lease.PromptBaselineSha256,
                baselineHash,
                StringComparison.Ordinal))
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission
                    .BaselineHashMismatch);
        }
        if (!expression.MatchesAuthorization(authorization))
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission.ExpressionMismatch);
        }
        if (!string.Equals(
                profile.ProfileId,
                activeRuntimeIdentity.RuntimeProfileId,
                StringComparison.Ordinal))
        {
            return RejectDirective(
                CharacterLanguageStyleStageFAdmission.ProfileMismatch);
        }
        if (lease.Owner == LanguageStylePromptOwner.LegacyOwned)
        {
            return new CharacterLanguageStyleDirectiveResult(
                CharacterLanguageStyleStageFAdmission
                    .LegacyOwnedNoDirective,
                directive: null);
        }

        var template = profile.Get(selectedStyle);
        var validUntil = lease.ValidUntil < expression.ValidUntil
            ? lease.ValidUntil
            : expression.ValidUntil;
        var directive = new CharacterLanguageStyleDirective(
            directiveId,
            lease,
            expression,
            template,
            validUntil);
        return new CharacterLanguageStyleDirectiveResult(
            CharacterLanguageStyleStageFAdmission.DirectiveCreated,
            directive);
    }

    internal CharacterPromptOwnershipSnapshot Snapshot()
        => new(
            _leases.Count,
            _leases.Values.Count(state => state.Terminal));

    internal void Reset()
    {
        _leases.Clear();
        _authorizationLeases.Clear();
    }

    private static CharacterPromptOwnershipIssue RejectIssue(
        CharacterLanguageStyleStageFAdmission admission)
        => new(admission, lease: null);

    private static CharacterLanguageStyleDirectiveResult RejectDirective(
        CharacterLanguageStyleStageFAdmission admission)
        => new(admission, directive: null);
}

internal sealed class CharacterPromptDiffVerification
{
    internal CharacterLanguageStyleStageFAdmission Admission { get; }
    internal string BaselineSha256 { get; }
    internal string FinalSha256 { get; }
    internal string SuffixSha256 { get; }
    internal int BaselineCharacterCount { get; }
    internal int FinalCharacterCount { get; }
    internal int SuffixCharacterCount { get; }
    internal int FirstChangedOffset { get; }
    internal int LastChangedOffset { get; }
    internal LanguageStylePromptOwner Owner { get; }
    internal string TemplateVersion { get; }
    internal bool Valid =>
        Admission
            == CharacterLanguageStyleStageFAdmission.PromptDiffValid;

    internal CharacterPromptDiffVerification(
        CharacterLanguageStyleStageFAdmission admission,
        string baselineSha256,
        string finalSha256,
        string suffixSha256,
        int baselineCharacterCount,
        int finalCharacterCount,
        int suffixCharacterCount,
        int firstChangedOffset,
        int lastChangedOffset,
        LanguageStylePromptOwner owner,
        string templateVersion)
    {
        Admission = admission;
        BaselineSha256 = baselineSha256;
        FinalSha256 = finalSha256;
        SuffixSha256 = suffixSha256;
        BaselineCharacterCount = baselineCharacterCount;
        FinalCharacterCount = finalCharacterCount;
        SuffixCharacterCount = suffixCharacterCount;
        FirstChangedOffset = firstChangedOffset;
        LastChangedOffset = lastChangedOffset;
        Owner = owner;
        TemplateVersion = templateVersion;
    }
}

internal static class CharacterPromptDiffVerifier
{
    private const string Separator = "\n\n";

    internal static CharacterPromptDiffVerification Verify(
        string baselinePrompt,
        string finalPrompt,
        CharacterPromptInfluenceOwnershipLease lease,
        CharacterLanguageStyleDirective directive,
        int maximumFinalCharacters)
    {
        if (baselinePrompt == null)
            throw new ArgumentNullException(nameof(baselinePrompt));
        if (finalPrompt == null)
            throw new ArgumentNullException(nameof(finalPrompt));
        if (lease == null)
            throw new ArgumentNullException(nameof(lease));
        if (directive == null)
            throw new ArgumentNullException(nameof(directive));
        CharacterLanguageStyleStageFContractGuard.Positive(
            maximumFinalCharacters,
            nameof(maximumFinalCharacters));

        var baselineHash =
            CharacterLanguageStyleStageFContractGuard.Sha256(
                baselinePrompt);
        var finalHash =
            CharacterLanguageStyleStageFContractGuard.Sha256(finalPrompt);
        var suffixHash =
            CharacterLanguageStyleStageFContractGuard.Sha256(
                directive.ExactSuffixText);
        var expected = baselinePrompt
            + Separator
            + directive.ExactSuffixText;
        var valid = lease.Owner
                == LanguageStylePromptOwner.CharacterRuntimeOwned
            && lease.LeaseId == directive.OwnerLeaseId
            && lease.AuthorizationId == directive.AuthorizationId
            && string.Equals(
                lease.PromptBaselineSha256,
                baselineHash,
                StringComparison.Ordinal)
            && string.Equals(
                directive.SuffixSha256,
                suffixHash,
                StringComparison.Ordinal)
            && finalPrompt.Length <= maximumFinalCharacters
            && string.Equals(
                finalPrompt,
                expected,
                StringComparison.Ordinal);
        FindChangedOffsets(
            baselinePrompt,
            finalPrompt,
            out var firstChanged,
            out var lastChanged);
        return new CharacterPromptDiffVerification(
            valid
                ? CharacterLanguageStyleStageFAdmission.PromptDiffValid
                : CharacterLanguageStyleStageFAdmission
                    .PromptDiffRejected,
            baselineHash,
            finalHash,
            suffixHash,
            baselinePrompt.Length,
            finalPrompt.Length,
            directive.ExactSuffixText.Length,
            firstChanged,
            lastChanged,
            lease.Owner,
            directive.TemplateVersion);
    }

    private static void FindChangedOffsets(
        string baseline,
        string final,
        out int first,
        out int last)
    {
        var commonPrefix = 0;
        var shortest = Math.Min(baseline.Length, final.Length);
        while (commonPrefix < shortest
            && baseline[commonPrefix] == final[commonPrefix])
        {
            commonPrefix++;
        }
        if (commonPrefix == baseline.Length
            && commonPrefix == final.Length)
        {
            first = -1;
            last = -1;
            return;
        }

        first = commonPrefix;
        last = final.Length == 0 ? -1 : final.Length - 1;
    }
}

internal sealed class CharacterRuntimeStageFAuthorityCounters
{
    internal static CharacterRuntimeStageFAuthorityCounters Zero {
        get;
    } = new();

    internal int RuntimeCallers => 0;
    internal int PromptWrites => 0;
    internal int ReplyOutputs => 0;
    internal int ProviderCalls => 0;
    internal int ProviderRetries => 0;
    internal int MemoryCalls => 0;
    internal int EvidenceCalls => 0;
    internal int ReasoningCalls => 0;
    internal int DecisionOutputs => 0;
    internal int StateWrites => 0;
    internal int PersistenceWrites => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int VoiceOutputs => 0;
    internal int ExecutionOutputs => 0;
    internal bool IsZero =>
        RuntimeCallers == 0
        && PromptWrites == 0
        && ReplyOutputs == 0
        && ProviderCalls == 0
        && ProviderRetries == 0
        && MemoryCalls == 0
        && EvidenceCalls == 0
        && ReasoningCalls == 0
        && DecisionOutputs == 0
        && StateWrites == 0
        && PersistenceWrites == 0
        && QueueWrites == 0
        && MotionOutputs == 0
        && VoiceOutputs == 0
        && ExecutionOutputs == 0;

    private CharacterRuntimeStageFAuthorityCounters()
    {
    }
}
