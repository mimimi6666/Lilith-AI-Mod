using System;

namespace LilithTextInjector;

internal enum IdentityContextAssistMode
{
    Off = 0,
    Observe = 1,
    Assist = 2
}

internal static class IdentityContextAssistModeParser
{
    internal static IdentityContextAssistMode Parse(string? value)
    {
        if (string.Equals(value?.Trim(), "Observe", StringComparison.OrdinalIgnoreCase))
            return IdentityContextAssistMode.Observe;
        if (string.Equals(value?.Trim(), "Assist", StringComparison.OrdinalIgnoreCase))
            return IdentityContextAssistMode.Assist;
        return IdentityContextAssistMode.Off;
    }
}

internal sealed class IdentityContextAssistDecision
{
    internal IdentityContextAssistMode Mode { get; }
    internal PlayerAuthoredIdentityProjectionCandidate Projection { get; }
    internal IdentityContextFragment Fragment { get; }
    internal bool CandidatePassesIdentityAndSafetyGate =>
        Projection.EligibleForFutureContext
        && Fragment.CharacterCount > 0
        && Fragment.Safety.IsSafe;
    internal bool ModeAllowsPromptAssist => Mode == IdentityContextAssistMode.Assist;
    internal bool MayApplyToPrompt =>
        ModeAllowsPromptAssist
        && CandidatePassesIdentityAndSafetyGate;
    internal bool RawPlayerTextAccepted => false;
    internal bool PlannerPromptTextAccepted => false;
    internal bool FragmentStored => false;

    internal IdentityContextAssistDecision(
        IdentityContextAssistMode mode,
        PlayerAuthoredIdentityProjectionCandidate projection,
        IdentityContextFragment fragment)
    {
        Mode = mode;
        Projection = projection ?? throw new ArgumentNullException(nameof(projection));
        Fragment = fragment ?? throw new ArgumentNullException(nameof(fragment));
    }

    internal string GateReason
    {
        get
        {
            if (Mode == IdentityContextAssistMode.Off)
                return "mode-off";
            if (Mode == IdentityContextAssistMode.Observe)
                return "observe-only";
            if (!Projection.EligibleForFutureContext)
                return "identity-not-eligible";
            if (Fragment.CharacterCount == 0)
                return "empty-fragment";
            if (!Fragment.Safety.IsSafe)
                return "fragment-safety";
            return "passed";
        }
    }
}

internal static class IdentityContextControlledAssistPlanner
{
    internal static IdentityContextAssistDecision Decide(
        IdentityContextAssistMode mode,
        PlayerAuthoredIdentityState state)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        var projection = PlayerAuthoredIdentityProjection.Project(state);
        var fragment = IdentityContextFragmentBuilder.Build(projection);
        return new IdentityContextAssistDecision(mode, projection, fragment);
    }
}

internal sealed class IdentityContextAssistApplication
{
    internal IdentityContextAssistDecision Decision { get; }
    internal string Prompt { get; }
    internal int BaselinePromptCharacters { get; }
    internal int CandidatePromptCharacters => Prompt.Length;
    internal int AddedCharacters => CandidatePromptCharacters - BaselinePromptCharacters;
    internal bool Applied { get; }
    internal string ResultReason { get; }
    internal int PromptChanges => Applied ? 1 : 0;
    internal int ReplyChanges => 0;
    internal int PsychologyWrites => 0;
    internal int MotionWrites => 0;
    internal int AdditionalProviderRequests => 0;
    internal bool FragmentStored => false;
    internal bool PromptStored => false;

    internal IdentityContextAssistApplication(
        IdentityContextAssistDecision decision,
        string prompt,
        int baselinePromptCharacters,
        bool applied,
        string resultReason)
    {
        if (baselinePromptCharacters < 0)
            throw new ArgumentOutOfRangeException(nameof(baselinePromptCharacters));
        if (string.IsNullOrWhiteSpace(resultReason))
            throw new ArgumentException("A result reason is required.", nameof(resultReason));
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
        Prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        BaselinePromptCharacters = baselinePromptCharacters;
        Applied = applied;
        ResultReason = resultReason;
    }
}

internal static class IdentityContextPromptAppender
{
    internal const int MaxCandidatePromptCharacters = 32000;
    private const string Separator = "\n\n";

    internal static IdentityContextAssistApplication Apply(
        string originalPrompt,
        IdentityContextAssistDecision decision)
    {
        if (originalPrompt == null)
            throw new ArgumentNullException(nameof(originalPrompt));
        if (decision == null)
            throw new ArgumentNullException(nameof(decision));

        if (!decision.MayApplyToPrompt)
        {
            return new IdentityContextAssistApplication(
                decision,
                originalPrompt,
                originalPrompt.Length,
                applied: false,
                decision.GateReason);
        }

        var candidateLength = checked(
            originalPrompt.Length
            + Separator.Length
            + decision.Fragment.CharacterCount);
        if (candidateLength > MaxCandidatePromptCharacters)
        {
            return new IdentityContextAssistApplication(
                decision,
                originalPrompt,
                originalPrompt.Length,
                applied: false,
                "candidate-prompt-budget");
        }

        var candidate = originalPrompt + Separator + decision.Fragment.Text;
        return new IdentityContextAssistApplication(
            decision,
            candidate,
            originalPrompt.Length,
            applied: true,
            "applied");
    }
}
