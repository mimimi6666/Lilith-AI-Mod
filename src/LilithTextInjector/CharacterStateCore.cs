using System;

namespace LilithTextInjector;

internal enum CharacterInteractionKind
{
    Conversation = 0,
    Warmth = 1,
    Praise = 2,
    Care = 3,
    Apology = 4,
    BoundaryPressure = 5,
    HarshCommand = 6,
    Insult = 7,
    WakeRequest = 8,
    DraggedAwake = 9
}

internal enum CharacterInteractionTone
{
    Neutral = 0,
    Gentle = 1,
    Affectionate = 2,
    Playful = 3,
    Firm = 4,
    Angry = 5
}

internal enum CharacterWakeDisposition
{
    InvalidState = 0,
    AlreadyAwake = 1,
    StronglyPreferSleep = 2,
    PreferSleep = 3,
    OpenToWake = 4,
    WillWakeToLieDown = 5
}

internal sealed class CharacterPsychologyTraits
{
    internal static CharacterPsychologyTraits LilithDefault { get; } = new(
        warmth: 0.82,
        autonomy: 0.78,
        sensitivity: 0.66,
        stubbornness: 0.52,
        forgiveness: 0.72);

    internal double Warmth { get; }
    internal double Autonomy { get; }
    internal double Sensitivity { get; }
    internal double Stubbornness { get; }
    internal double Forgiveness { get; }

    internal CharacterPsychologyTraits(
        double warmth,
        double autonomy,
        double sensitivity,
        double stubbornness,
        double forgiveness)
    {
        Warmth = Unit(warmth, nameof(warmth));
        Autonomy = Unit(autonomy, nameof(autonomy));
        Sensitivity = Unit(sensitivity, nameof(sensitivity));
        Stubbornness = Unit(stubbornness, nameof(stubbornness));
        Forgiveness = Unit(forgiveness, nameof(forgiveness));
    }

    private static double Unit(double value, string parameterName)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal sealed class CharacterPsychologyState
{
    internal const int CurrentSchemaVersion = 1;

    internal int SchemaVersion { get; }
    internal double Trust { get; }
    internal double Closeness { get; }
    internal double Familiarity { get; }
    internal double Security { get; }
    internal double Mood { get; }
    internal double Energy { get; }
    internal double Irritation { get; }
    internal double SleepPressure { get; }
    internal double WakeRequestPressure { get; }
    internal DateTimeOffset UpdatedAt { get; }

    internal CharacterPsychologyState(
        double trust,
        double closeness,
        double familiarity,
        double security,
        double mood,
        double energy,
        double irritation,
        double sleepPressure,
        double wakeRequestPressure,
        DateTimeOffset updatedAt,
        int schemaVersion = CurrentSchemaVersion)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (updatedAt == default || updatedAt.Year < 2000 || updatedAt.Year > 9998)
            throw new ArgumentOutOfRangeException(nameof(updatedAt));

        SchemaVersion = schemaVersion;
        Trust = Unit(trust, nameof(trust));
        Closeness = Unit(closeness, nameof(closeness));
        Familiarity = Unit(familiarity, nameof(familiarity));
        Security = Unit(security, nameof(security));
        Mood = SignedUnit(mood, nameof(mood));
        Energy = Unit(energy, nameof(energy));
        Irritation = Unit(irritation, nameof(irritation));
        SleepPressure = Unit(sleepPressure, nameof(sleepPressure));
        WakeRequestPressure = Unit(wakeRequestPressure, nameof(wakeRequestPressure));
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    internal static CharacterPsychologyState CreateDefault(DateTimeOffset now)
        => new(
            trust: 0.55,
            closeness: 0.55,
            familiarity: 0.35,
            security: 0.75,
            mood: 0.15,
            energy: 0.45,
            irritation: 0,
            sleepPressure: 0.50,
            wakeRequestPressure: 0,
            updatedAt: now);

    internal CharacterPsychologyState WithBodyState(
        double energy,
        double sleepPressure,
        DateTimeOffset observedAt)
    {
        if (observedAt < UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(observedAt));
        return new CharacterPsychologyState(
            Trust,
            Closeness,
            Familiarity,
            Security,
            Mood,
            energy,
            Irritation,
            sleepPressure,
            WakeRequestPressure,
            observedAt);
    }

    private static double Unit(double value, string parameterName)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    private static double SignedUnit(double value, string parameterName)
    {
        if (double.IsNaN(value) || value < -1 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal sealed class CharacterPsychologyEvent
{
    internal CharacterInteractionKind Kind { get; }
    internal CharacterInteractionTone Tone { get; }
    internal double Intensity { get; }
    internal DateTimeOffset OccurredAt { get; }

    internal CharacterPsychologyEvent(
        CharacterInteractionKind kind,
        CharacterInteractionTone tone,
        double intensity,
        DateTimeOffset occurredAt)
    {
        if (!Enum.IsDefined(typeof(CharacterInteractionKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(typeof(CharacterInteractionTone), tone))
            throw new ArgumentOutOfRangeException(nameof(tone));
        if (double.IsNaN(intensity) || intensity < 0 || intensity > 1)
            throw new ArgumentOutOfRangeException(nameof(intensity));
        if (occurredAt == default || occurredAt.Year < 2000 || occurredAt.Year > 9998)
            throw new ArgumentOutOfRangeException(nameof(occurredAt));

        Kind = kind;
        Tone = tone;
        Intensity = intensity;
        OccurredAt = occurredAt.ToUniversalTime();
    }
}

internal static class CharacterStateCore
{
    internal static CharacterPsychologyState Apply(
        CharacterPsychologyState state,
        CharacterPsychologyEvent psychologyEvent,
        CharacterPsychologyTraits? traits = null)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (psychologyEvent == null)
            throw new ArgumentNullException(nameof(psychologyEvent));
        if (psychologyEvent.OccurredAt < state.UpdatedAt)
            throw new ArgumentOutOfRangeException(nameof(psychologyEvent), "Psychology events must be applied in timestamp order.");

        var resolvedTraits = traits ?? CharacterPsychologyTraits.LilithDefault;
        var elapsedHours = Math.Min(168d, (psychologyEvent.OccurredAt - state.UpdatedAt).TotalHours);
        var mood = DecayToward(state.Mood, 0.15, elapsedHours, halfLifeHours: 2.5);
        var irritation = DecayToward(state.Irritation, 0, elapsedHours, halfLifeHours: 0.45);
        var wakePressure = DecayToward(state.WakeRequestPressure, 0, elapsedHours, halfLifeHours: 0.20);
        var trust = state.Trust;
        var closeness = state.Closeness;
        var familiarity = state.Familiarity;
        var security = state.Security;
        var intensity = psychologyEvent.Intensity;

        familiarity += 0.0015 * intensity;
        switch (psychologyEvent.Kind)
        {
            case CharacterInteractionKind.Conversation:
                mood += 0.012 * resolvedTraits.Warmth * intensity;
                break;
            case CharacterInteractionKind.Warmth:
                trust += 0.004 * intensity;
                closeness += 0.006 * intensity;
                mood += 0.12 * intensity;
                irritation -= 0.08 * intensity;
                break;
            case CharacterInteractionKind.Praise:
                closeness += 0.004 * intensity;
                mood += 0.15 * intensity;
                break;
            case CharacterInteractionKind.Care:
                trust += 0.008 * intensity;
                closeness += 0.006 * intensity;
                security += 0.006 * intensity;
                mood += 0.10 * intensity;
                irritation -= 0.16 * intensity;
                break;
            case CharacterInteractionKind.Apology:
                trust += 0.004 * resolvedTraits.Forgiveness * intensity;
                security += 0.003 * resolvedTraits.Forgiveness * intensity;
                irritation -= 0.28 * resolvedTraits.Forgiveness * intensity;
                mood += 0.06 * resolvedTraits.Forgiveness * intensity;
                break;
            case CharacterInteractionKind.BoundaryPressure:
                trust -= 0.004 * resolvedTraits.Autonomy * intensity;
                security -= 0.004 * resolvedTraits.Sensitivity * intensity;
                mood -= 0.08 * intensity;
                irritation += 0.18 * resolvedTraits.Autonomy * intensity;
                break;
            case CharacterInteractionKind.HarshCommand:
                trust -= 0.003 * resolvedTraits.Sensitivity * intensity;
                mood -= 0.10 * resolvedTraits.Sensitivity * intensity;
                irritation += 0.22 * resolvedTraits.Autonomy * intensity;
                break;
            case CharacterInteractionKind.Insult:
                trust -= 0.014 * resolvedTraits.Sensitivity * intensity;
                closeness -= 0.008 * resolvedTraits.Sensitivity * intensity;
                security -= 0.018 * resolvedTraits.Sensitivity * intensity;
                mood -= 0.24 * resolvedTraits.Sensitivity * intensity;
                irritation += 0.32 * resolvedTraits.Sensitivity * intensity;
                break;
            case CharacterInteractionKind.WakeRequest:
                ApplyWakeRequestTone(
                    psychologyEvent.Tone,
                    resolvedTraits,
                    intensity,
                    ref mood,
                    ref irritation,
                    ref wakePressure,
                    ref trust,
                    ref security);
                break;
            case CharacterInteractionKind.DraggedAwake:
                mood -= 0.06 * resolvedTraits.Sensitivity * intensity;
                irritation += 0.14 * resolvedTraits.Autonomy * intensity;
                wakePressure += 0.20 * intensity;
                break;
        }

        return new CharacterPsychologyState(
            Clamp01(trust),
            Clamp01(closeness),
            Clamp01(familiarity),
            Clamp01(security),
            ClampSigned(mood),
            state.Energy,
            Clamp01(irritation),
            state.SleepPressure,
            Clamp01(wakePressure),
            psychologyEvent.OccurredAt);
    }

    internal static CharacterWakeDisposition EvaluateWakeDisposition(
        CharacterPsychologyState state,
        CharacterInteractionTone requestTone,
        double requestIntensity,
        bool isSleeping,
        bool isPajamas,
        CharacterPsychologyTraits? traits = null)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));
        if (!Enum.IsDefined(typeof(CharacterInteractionTone), requestTone))
            throw new ArgumentOutOfRangeException(nameof(requestTone));
        if (double.IsNaN(requestIntensity) || requestIntensity < 0 || requestIntensity > 1)
            throw new ArgumentOutOfRangeException(nameof(requestIntensity));
        if (!isSleeping)
            return CharacterWakeDisposition.AlreadyAwake;
        if (!isPajamas)
            return CharacterWakeDisposition.InvalidState;

        var resolvedTraits = traits ?? CharacterPsychologyTraits.LilithDefault;
        var tone = requestTone switch
        {
            CharacterInteractionTone.Gentle => 0.035,
            CharacterInteractionTone.Affectionate => 0.055,
            CharacterInteractionTone.Playful => 0.025,
            CharacterInteractionTone.Firm => 0.015,
            CharacterInteractionTone.Angry => -0.10 * resolvedTraits.Autonomy,
            _ => 0
        };
        var score = 0.28
            + state.Trust * 0.12
            + state.Closeness * 0.08
            + state.Mood * 0.10
            + state.Energy * 0.15
            + requestIntensity * 0.04
            + tone
            - state.SleepPressure * 0.28
            - state.Irritation * (0.22 + resolvedTraits.Autonomy * 0.10)
            - resolvedTraits.Stubbornness * 0.08
            - state.WakeRequestPressure * 0.05;

        if (score >= 0.54)
            return CharacterWakeDisposition.WillWakeToLieDown;
        if (score >= 0.43)
            return CharacterWakeDisposition.OpenToWake;
        if (score >= 0.30)
            return CharacterWakeDisposition.PreferSleep;
        return CharacterWakeDisposition.StronglyPreferSleep;
    }

    private static void ApplyWakeRequestTone(
        CharacterInteractionTone tone,
        CharacterPsychologyTraits traits,
        double intensity,
        ref double mood,
        ref double irritation,
        ref double wakePressure,
        ref double trust,
        ref double security)
    {
        wakePressure += tone switch
        {
            CharacterInteractionTone.Angry => 0.34 * intensity,
            CharacterInteractionTone.Firm => 0.22 * intensity,
            _ => 0.12 * intensity
        };
        switch (tone)
        {
            case CharacterInteractionTone.Gentle:
            case CharacterInteractionTone.Affectionate:
                mood += 0.025 * traits.Warmth * intensity;
                irritation += 0.025 * intensity;
                break;
            case CharacterInteractionTone.Playful:
                mood += 0.012 * traits.Warmth * intensity;
                irritation += 0.04 * intensity;
                break;
            case CharacterInteractionTone.Firm:
                mood -= 0.025 * traits.Sensitivity * intensity;
                irritation += 0.09 * traits.Autonomy * intensity;
                break;
            case CharacterInteractionTone.Angry:
                trust -= 0.003 * traits.Sensitivity * intensity;
                security -= 0.004 * traits.Sensitivity * intensity;
                mood -= 0.11 * traits.Sensitivity * intensity;
                irritation += 0.22 * traits.Autonomy * intensity;
                break;
            default:
                irritation += 0.045 * intensity;
                break;
        }
    }

    private static double DecayToward(double value, double target, double hours, double halfLifeHours)
    {
        if (hours <= 0)
            return value;
        var retained = Math.Pow(0.5, hours / halfLifeHours);
        return target + (value - target) * retained;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);
    private static double ClampSigned(double value) => Math.Clamp(value, -1, 1);
}

internal static class CharacterStatePromptProjector
{
    internal static string BuildPrivateGuidance(
        CharacterPsychologyState state,
        CharacterWakeDisposition wakeDisposition)
    {
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        var relationship = state.Trust >= 0.75 && state.Closeness >= 0.70
            ? "deeply trusting and close"
            : state.Trust >= 0.52 && state.Closeness >= 0.50
                ? "warm and familiar"
                : "careful and still developing";
        var mood = state.Irritation >= 0.58
            ? "noticeably irritated"
            : state.Mood <= -0.35
                ? "hurt or withdrawn"
                : state.Mood >= 0.45
                    ? "bright and affectionate"
                    : "gentle and fairly calm";
        var energy = state.SleepPressure >= 0.72 || state.Energy <= 0.25
            ? "very sleepy"
            : state.SleepPressure >= 0.48 || state.Energy <= 0.45
                ? "drowsy"
                : "reasonably alert";
        var wakeGuidance = wakeDisposition switch
        {
            CharacterWakeDisposition.WillWakeToLieDown =>
                "She is willing to wake, but should remain lying down after waking.",
            CharacterWakeDisposition.OpenToWake =>
                "She may either refuse or wake while remaining lying down; choose the response that best fits her personality and the immediate conversation.",
            CharacterWakeDisposition.PreferSleep =>
                "She prefers to keep sleeping and should normally refuse naturally.",
            CharacterWakeDisposition.StronglyPreferSleep =>
                "She strongly prefers to keep sleeping and should refuse without changing pose.",
            _ => "Do not infer a wake-up decision from this guidance."
        };

        return "\nPrivate local character-state guidance: the relationship feels "
            + relationship
            + "; Lilith is "
            + mood
            + " and "
            + energy
            + ". "
            + wakeGuidance
            + " Express this subtly in character. Never mention scores, hidden state, a psychology system, or guaranteed obedience.";
    }
}
