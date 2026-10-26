using System;

namespace LilithTextInjector;

internal enum AgentMotionAssistPlanReason
{
    ModeOff = 0,
    ObserveOnly = 1,
    CapabilityMissing = 2,
    NoSuggestions = 3,
    CooldownActive = 4,
    NoLowRiskCandidate = 5,
    PolicyRejected = 6,
    Selected = 7
}

internal enum AgentMotionTransitionRequirement
{
    None = 0,
    NativeSnapAction36Entry = 1,
    NativeSnapAction36EntryAndExit = 2
}

internal static class AgentMotionTransitionContract
{
    internal static AgentMotionTransitionRequirement RequirementFor(MotionIntentKind kind)
        => kind switch
        {
            MotionIntentKind.ArriveAtWindow => AgentMotionTransitionRequirement.NativeSnapAction36Entry,
            MotionIntentKind.PeekFromWindow => AgentMotionTransitionRequirement.NativeSnapAction36EntryAndExit,
            _ => AgentMotionTransitionRequirement.None
        };

    internal static bool IsSatisfied(
        MotionIntentKind kind,
        bool nativeSnapAction36Completed)
        => RequirementFor(kind) switch
        {
            AgentMotionTransitionRequirement.None => true,
            AgentMotionTransitionRequirement.NativeSnapAction36Entry => nativeSnapAction36Completed,
            AgentMotionTransitionRequirement.NativeSnapAction36EntryAndExit => nativeSnapAction36Completed,
            _ => false
        };

    internal static bool RequiresSnapOnEntry(MotionIntentKind kind)
        => RequirementFor(kind) is AgentMotionTransitionRequirement.NativeSnapAction36Entry
            or AgentMotionTransitionRequirement.NativeSnapAction36EntryAndExit;

    internal static bool RequiresSnapOnExit(MotionIntentKind kind)
        => RequirementFor(kind) == AgentMotionTransitionRequirement.NativeSnapAction36EntryAndExit;
}

internal sealed class AgentMotionAssistPlan
{
    internal ModelMotionSuggestionMode Mode { get; }
    internal AgentMotionSuggestionReview Review { get; }
    internal AgentMotionAssistPlanReason Reason { get; }
    internal MotionIntentKind SelectedKind { get; }
    internal int LowRiskCandidates { get; }
    internal int PolicyRejectedCandidates { get; }
    internal bool HasSelection => Reason == AgentMotionAssistPlanReason.Selected
        && SelectedKind != MotionIntentKind.None;
    internal int PlannerQueueWrites => 0;
    internal int PlannerExecutions => 0;
    internal int AdditionalProviderRequests => 0;

    internal AgentMotionAssistPlan(
        ModelMotionSuggestionMode mode,
        AgentMotionSuggestionReview review,
        AgentMotionAssistPlanReason reason,
        MotionIntentKind selectedKind,
        int lowRiskCandidates,
        int policyRejectedCandidates)
    {
        if (lowRiskCandidates < 0)
            throw new ArgumentOutOfRangeException(nameof(lowRiskCandidates));
        if (policyRejectedCandidates < 0)
            throw new ArgumentOutOfRangeException(nameof(policyRejectedCandidates));
        Mode = mode;
        Review = review ?? throw new ArgumentNullException(nameof(review));
        Reason = reason;
        SelectedKind = selectedKind;
        LowRiskCandidates = lowRiskCandidates;
        PolicyRejectedCandidates = policyRejectedCandidates;
    }
}

internal static class AgentMotionSuggestionControlledAssistPlanner
{
    internal const int MaximumSelectionsPerTurn = 1;

    internal static ModelMotionSuggestionMode ResolveEffectiveMode(
        ModelMotionSuggestionMode modeAtRequest,
        ModelMotionSuggestionMode currentMode)
        => modeAtRequest == ModelMotionSuggestionMode.Assist
            && currentMode == ModelMotionSuggestionMode.Assist
                ? ModelMotionSuggestionMode.Assist
                : currentMode == ModelMotionSuggestionMode.Off
                    ? ModelMotionSuggestionMode.Off
                    : ModelMotionSuggestionMode.Observe;

    internal static bool ShouldCancelQueuedProposals(
        ModelMotionSuggestionMode previousMode,
        ModelMotionSuggestionMode currentMode)
        => previousMode == ModelMotionSuggestionMode.Assist
            && currentMode != ModelMotionSuggestionMode.Assist;

    internal static float ClampCooldownSeconds(float configuredSeconds)
        => Math.Clamp(configuredSeconds, 2f, 30f);

    internal static AgentMotionAssistPlan Plan(
        ModelMotionSuggestionMode mode,
        AgentMotionSuggestionReview review,
        bool cooldownReady)
    {
        if (review == null)
            throw new ArgumentNullException(nameof(review));
        if (mode == ModelMotionSuggestionMode.Off)
            return Empty(mode, review, AgentMotionAssistPlanReason.ModeOff);
        if (mode == ModelMotionSuggestionMode.Observe)
            return Empty(mode, review, AgentMotionAssistPlanReason.ObserveOnly);
        if (!review.CapabilityAdvertised)
            return Empty(mode, review, AgentMotionAssistPlanReason.CapabilityMissing);
        if (review.ReviewedCount == 0)
            return Empty(mode, review, AgentMotionAssistPlanReason.NoSuggestions);
        if (!cooldownReady)
            return Empty(mode, review, AgentMotionAssistPlanReason.CooldownActive);

        var lowRiskCandidates = 0;
        var policyRejectedCandidates = 0;
        foreach (var item in review.Items)
        {
            if (!IsLowRiskAssistKind(item.Kind))
                continue;
            lowRiskCandidates++;
            if (!item.Allowed)
            {
                policyRejectedCandidates++;
                continue;
            }

            return new AgentMotionAssistPlan(
                mode,
                review,
                AgentMotionAssistPlanReason.Selected,
                item.Kind,
                lowRiskCandidates,
                policyRejectedCandidates);
        }

        return new AgentMotionAssistPlan(
            mode,
            review,
            lowRiskCandidates == 0
                ? AgentMotionAssistPlanReason.NoLowRiskCandidate
                : AgentMotionAssistPlanReason.PolicyRejected,
            MotionIntentKind.None,
            lowRiskCandidates,
            policyRejectedCandidates);
    }

    internal static bool IsLowRiskAssistKind(MotionIntentKind kind)
        => kind is MotionIntentKind.ArriveAtWindow
            or MotionIntentKind.WalkLeft
            or MotionIntentKind.WalkRight
            or MotionIntentKind.PlayOneShot
            or MotionIntentKind.Sit
            or MotionIntentKind.Stand;

    private static AgentMotionAssistPlan Empty(
        ModelMotionSuggestionMode mode,
        AgentMotionSuggestionReview review,
        AgentMotionAssistPlanReason reason)
        => new(
            mode,
            review,
            reason,
            MotionIntentKind.None,
            lowRiskCandidates: 0,
            policyRejectedCandidates: 0);
}
