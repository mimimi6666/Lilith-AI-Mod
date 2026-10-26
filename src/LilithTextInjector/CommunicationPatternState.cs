using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

internal sealed class CommunicationPatternState
{
    internal const int CurrentSchemaVersion = 1;

    internal int SchemaVersion { get; }
    internal long ObservedTurns { get; }
    internal double BaselineDirectness { get; }
    internal double BaselineExpressiveness { get; }
    internal double EmotionalBlackmailScore { get; }
    internal double MoralCoercionScore { get; }
    internal double ThreatScore { get; }
    internal double GaslightingScore { get; }
    internal double WithdrawalThreatScore { get; }
    internal double PuaTacticScore { get; }
    internal DateTimeOffset UpdatedAt { get; }

    internal CommunicationPatternState(
        long observedTurns,
        double baselineDirectness,
        double baselineExpressiveness,
        double emotionalBlackmailScore,
        double moralCoercionScore,
        double threatScore,
        double gaslightingScore,
        double withdrawalThreatScore,
        double puaTacticScore,
        DateTimeOffset updatedAt,
        int schemaVersion = CurrentSchemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (observedTurns < 0)
            throw new ArgumentOutOfRangeException(nameof(observedTurns));
        if (updatedAt == default || updatedAt.Year < 2000 || updatedAt.Year > 9998)
            throw new ArgumentOutOfRangeException(nameof(updatedAt));
        SchemaVersion = schemaVersion;
        ObservedTurns = observedTurns;
        BaselineDirectness = Unit(baselineDirectness, nameof(baselineDirectness));
        BaselineExpressiveness = Unit(baselineExpressiveness, nameof(baselineExpressiveness));
        EmotionalBlackmailScore = Unit(emotionalBlackmailScore, nameof(emotionalBlackmailScore));
        MoralCoercionScore = Unit(moralCoercionScore, nameof(moralCoercionScore));
        ThreatScore = Unit(threatScore, nameof(threatScore));
        GaslightingScore = Unit(gaslightingScore, nameof(gaslightingScore));
        WithdrawalThreatScore = Unit(withdrawalThreatScore, nameof(withdrawalThreatScore));
        PuaTacticScore = Unit(puaTacticScore, nameof(puaTacticScore));
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    internal static CommunicationPatternState CreateDefault(DateTimeOffset now)
        => new(
            observedTurns: 0,
            baselineDirectness: 0.35,
            baselineExpressiveness: 0.30,
            emotionalBlackmailScore: 0,
            moralCoercionScore: 0,
            threatScore: 0,
            gaslightingScore: 0,
            withdrawalThreatScore: 0,
            puaTacticScore: 0,
            updatedAt: now);

    internal double GetStrategyScore(CommunicationStrategySignal kind) => kind switch
    {
        CommunicationStrategySignal.EmotionalBlackmailCandidate => EmotionalBlackmailScore,
        CommunicationStrategySignal.MoralCoercionCandidate => MoralCoercionScore,
        CommunicationStrategySignal.ThreatCandidate => ThreatScore,
        CommunicationStrategySignal.GaslightingCandidate => GaslightingScore,
        CommunicationStrategySignal.WithdrawalThreatCandidate => WithdrawalThreatScore,
        CommunicationStrategySignal.PuaTacticCandidate => PuaTacticScore,
        _ => 0
    };

    internal CommunicationPatternLevel GetPatternLevel(CommunicationStrategySignal kind)
    {
        var score = GetStrategyScore(kind);
        if (score >= 0.60)
            return CommunicationPatternLevel.Sustained;
        if (score >= 0.32)
            return CommunicationPatternLevel.Repeated;
        if (score >= 0.08)
            return CommunicationPatternLevel.Candidate;
        return CommunicationPatternLevel.None;
    }

    private static double Unit(double value, string parameterName)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal static class CommunicationPatternCore
{
    internal static CommunicationPatternState Apply(
        CommunicationPatternState state,
        CommunicationSignalAnalysis analysis,
        DateTimeOffset occurredAt)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (analysis == null)
            throw new ArgumentNullException(nameof(analysis));
        if (occurredAt < state.UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(occurredAt));

        var hours = Math.Min(24d * 30d, (occurredAt - state.UpdatedAt).TotalHours);
        var emotional = Decay(state.EmotionalBlackmailScore, hours, 72);
        var moral = Decay(state.MoralCoercionScore, hours, 72);
        var threat = Decay(state.ThreatScore, hours, 96);
        var gaslighting = Decay(state.GaslightingScore, hours, 120);
        var withdrawal = Decay(state.WithdrawalThreatScore, hours, 72);
        var pua = Decay(state.PuaTacticScore, hours, 120);

        foreach (var signal in analysis.Strategies)
        {
            var increment = signal.Kind switch
            {
                CommunicationStrategySignal.EmotionalBlackmailCandidate => 0.16,
                CommunicationStrategySignal.MoralCoercionCandidate => 0.15,
                CommunicationStrategySignal.ThreatCandidate => 0.24,
                CommunicationStrategySignal.GaslightingCandidate => 0.11,
                CommunicationStrategySignal.WithdrawalThreatCandidate => 0.12,
                CommunicationStrategySignal.PuaTacticCandidate => 0.13,
                _ => 0
            } * signal.Confidence;
            switch (signal.Kind)
            {
                case CommunicationStrategySignal.EmotionalBlackmailCandidate:
                    emotional += increment;
                    break;
                case CommunicationStrategySignal.MoralCoercionCandidate:
                    moral += increment;
                    break;
                case CommunicationStrategySignal.ThreatCandidate:
                    threat += increment;
                    break;
                case CommunicationStrategySignal.GaslightingCandidate:
                    gaslighting += increment;
                    break;
                case CommunicationStrategySignal.WithdrawalThreatCandidate:
                    withdrawal += increment;
                    break;
                case CommunicationStrategySignal.PuaTacticCandidate:
                    pua += increment;
                    break;
            }
        }

        var alpha = state.ObservedTurns < 12 ? 0.12 : 0.04;
        return new CommunicationPatternState(
            observedTurns: checked(state.ObservedTurns + 1),
            baselineDirectness: Lerp(state.BaselineDirectness, analysis.DirectnessSample, alpha),
            baselineExpressiveness: Lerp(state.BaselineExpressiveness, analysis.ExpressivenessSample, alpha),
            emotionalBlackmailScore: Clamp(emotional),
            moralCoercionScore: Clamp(moral),
            threatScore: Clamp(threat),
            gaslightingScore: Clamp(gaslighting),
            withdrawalThreatScore: Clamp(withdrawal),
            puaTacticScore: Clamp(pua),
            updatedAt: occurredAt);
    }

    internal static IReadOnlyList<(CommunicationStrategySignal Kind, CommunicationPatternLevel Level, double Score)>
        GetActivePatterns(CommunicationPatternState state)
        => Enum.GetValues<CommunicationStrategySignal>()
            .Select(kind => (Kind: kind, Level: state.GetPatternLevel(kind), Score: state.GetStrategyScore(kind)))
            .Where(item => item.Level != CommunicationPatternLevel.None)
            .OrderByDescending(item => item.Score)
            .ToArray();

    private static double Decay(double value, double hours, double halfLifeHours)
        => hours <= 0 ? value : value * Math.Pow(0.5, hours / halfLifeHours);

    private static double Lerp(double from, double to, double amount)
        => Clamp(from + (to - from) * amount);

    private static double Clamp(double value) => Math.Clamp(value, 0, 1);
}
