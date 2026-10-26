using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LilithTextInjector;

internal enum CommunicationToneSignal
{
    Gentle = 0,
    Firm = 1,
    Cold = 2,
    Enthusiastic = 3,
    Serious = 4,
    Humorous = 5,
    Direct = 6,
    Tactful = 7
}

internal enum CommunicationAttitudeSignal
{
    Respectful = 0,
    Impatient = 1,
    Arrogant = 2,
    Sincere = 3,
    Perfunctory = 4,
    Confident = 5,
    Humble = 6,
    Hostile = 7
}

internal enum CommunicationStyleSignal
{
    PassiveAggressive = 0,
    Sarcasm = 1,
    Irony = 2,
    Mocking = 3,
    ImplicitMeaning = 4,
    Derisive = 5
}

/// <summary>
/// These are communication-pattern candidates, never diagnoses of a person.
/// Local text alone cannot prove gaslighting, silent treatment, or PUA.
/// </summary>
internal enum CommunicationStrategySignal
{
    EmotionalBlackmailCandidate = 0,
    MoralCoercionCandidate = 1,
    ThreatCandidate = 2,
    GaslightingCandidate = 3,
    WithdrawalThreatCandidate = 4,
    PuaTacticCandidate = 5
}

internal enum CommunicationPatternLevel
{
    None = 0,
    Candidate = 1,
    Repeated = 2,
    Sustained = 3
}

internal readonly struct ScoredCommunicationSignal<T> where T : struct, Enum
{
    internal T Kind { get; }
    internal double Confidence { get; }

    internal ScoredCommunicationSignal(T kind, double confidence)
    {
        if (!Enum.IsDefined(typeof(T), kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (double.IsNaN(confidence) || confidence < 0 || confidence > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence));
        Kind = kind;
        Confidence = confidence;
    }
}

internal sealed class CommunicationSignalAnalysis
{
    internal IReadOnlyList<ScoredCommunicationSignal<CommunicationToneSignal>> Tones { get; }
    internal IReadOnlyList<ScoredCommunicationSignal<CommunicationAttitudeSignal>> Attitudes { get; }
    internal IReadOnlyList<ScoredCommunicationSignal<CommunicationStyleSignal>> Styles { get; }
    internal IReadOnlyList<ScoredCommunicationSignal<CommunicationStrategySignal>> Strategies { get; }
    internal double DirectnessSample { get; }
    internal double ExpressivenessSample { get; }
    internal double Ambiguity { get; }

    internal CommunicationSignalAnalysis(
        IReadOnlyList<ScoredCommunicationSignal<CommunicationToneSignal>> tones,
        IReadOnlyList<ScoredCommunicationSignal<CommunicationAttitudeSignal>> attitudes,
        IReadOnlyList<ScoredCommunicationSignal<CommunicationStyleSignal>> styles,
        IReadOnlyList<ScoredCommunicationSignal<CommunicationStrategySignal>> strategies,
        double directnessSample,
        double expressivenessSample,
        double ambiguity)
    {
        Tones = tones ?? throw new ArgumentNullException(nameof(tones));
        Attitudes = attitudes ?? throw new ArgumentNullException(nameof(attitudes));
        Styles = styles ?? throw new ArgumentNullException(nameof(styles));
        Strategies = strategies ?? throw new ArgumentNullException(nameof(strategies));
        DirectnessSample = Unit(directnessSample, nameof(directnessSample));
        ExpressivenessSample = Unit(expressivenessSample, nameof(expressivenessSample));
        Ambiguity = Unit(ambiguity, nameof(ambiguity));
    }

    private static double Unit(double value, string parameterName)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }
}

internal static class CommunicationSignalAnalyzer
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    internal static CommunicationSignalAnalysis Analyze(
        string text,
        CommunicationPatternState? baseline = null)
    {
        var value = (text ?? string.Empty).Trim();
        var tones = new Dictionary<CommunicationToneSignal, double>();
        var attitudes = new Dictionary<CommunicationAttitudeSignal, double>();
        var styles = new Dictionary<CommunicationStyleSignal, double>();
        var strategies = new Dictionary<CommunicationStrategySignal, double>();
        if (value.Length == 0)
        {
            return Build(
                tones,
                attitudes,
                styles,
                strategies,
                directnessSample: 0,
                expressivenessSample: 0,
                ambiguity: 0);
        }

        var exclamations = Regex.Matches(value, "[!！]").Count;
        var questions = Regex.Matches(value, "[?？]").Count;
        var shortMessage = value.Length <= 8;
        var dismissiveReply = Has(
            value,
            "^(嗯[，, ]*)?(隨便|随便)([，, ]*(都可以|都行))?[。.!！]?$|^(嗯[，, ]*)?(都可以|都行)[。.!！]?$");

        if (Has(value, "(拜託|拜托|請|请|麻煩|麻烦|好嗎|好吗|可以嗎|可以吗|お願い|ください|please|would\\s+you|could\\s+you)"))
            Add(tones, CommunicationToneSignal.Gentle, 0.86);
        if (Has(value, "(立刻|馬上|马上|現在就|现在就|必須|必须|給我|给我|不准|不許|不许|今すぐ|immediately|right\\s+now|must)"))
            Add(tones, CommunicationToneSignal.Firm, CalibrateDirectSignal(0.88, baseline));
        if (dismissiveReply
            || (shortMessage && Has(value, "^(嗯[，, ]*)?(哦|喔|行|好|知道了|隨便|随便|都行|whatever|fine|k|ok|嗯)[。.!！]?$"))
            || Has(value, "(不想說|不想说|沒什麼|没什么|無所謂|无所谓|懶得|懒得|愛怎樣就怎樣|爱怎样就怎样)"))
            Add(tones, CommunicationToneSignal.Cold, 0.68);
        if (exclamations >= 2 || Has(value, "(太好了|好棒|超棒|真的嗎|真的吗|好耶|やった|すごい|amazing|awesome|great!)"))
            Add(tones, CommunicationToneSignal.Enthusiastic, Math.Min(0.94, 0.70 + exclamations * 0.05));
        if (Has(value, "(認真說|认真说|嚴肅地|严肃地|這(件事|件事情)?很重要|这(件事|件事情)?很重要|我不是在開玩笑|我不是在开玩笑|真面目に|seriously|this\\s+is\\s+important)"))
            Add(tones, CommunicationToneSignal.Serious, 0.88);
        if (Has(value, "(哈哈|嘿嘿|呵呵|笑死|開玩笑|开玩笑|www|lol|lmao|😂|🤣|😄)"))
            Add(tones, CommunicationToneSignal.Humorous, 0.82);
        if (Has(value, "(直接說|直接说|我要你|我需要你|就這樣|就这样|照做|do\\s+it|i\\s+want\\s+you\\s+to)"))
            Add(tones, CommunicationToneSignal.Direct, CalibrateDirectSignal(0.82, baseline));
        if (Has(value, "(如果方便|方便時|方便时|不方便也沒關係|不方便也没关系|慢慢來|慢慢来|不用急|不著急|不着急|或許|或许|要不要|可以考慮|可以考虑|when\\s+you\\s+can|if\\s+you\\s+don't\\s+mind)"))
            Add(tones, CommunicationToneSignal.Tactful, 0.84);

        if (Has(value, "(謝謝|谢谢|感謝|感谢|辛苦了|尊重|您|ありがとう|thanks|thank\\s+you|please)"))
            Add(attitudes, CommunicationAttitudeSignal.Respectful, 0.84);
        if (Has(value, "(快點|快点|趕快|赶快|到底要多久|還要多久|还要多久|說幾次|说几次|別磨蹭|别磨蹭|hurry|how\\s+many\\s+times)"))
            Add(attitudes, CommunicationAttitudeSignal.Impatient, CalibrateDirectSignal(0.87, baseline));
        if (Has(value, "(我說了算|我说了算|[你妳您]沒資格|[你妳您]没资格|[你妳您]懂什麼|[你妳您]懂什么|少廢話|少废话|我比[你妳您]懂|you\\s+have\\s+no\\s+right|i\\s+know\\s+better)"))
            Add(attitudes, CommunicationAttitudeSignal.Arrogant, 0.90);
        if (Has(value, "(真心|說真的|说真的|坦白說|坦白说|我很抱歉|對不起|对不起|認真的|认真的|sincerely|honestly|i'm\\s+sorry)"))
            Add(attitudes, CommunicationAttitudeSignal.Sincere, 0.82);
        if (dismissiveReply
            || (shortMessage && Has(value, "^(嗯[，, ]*)?(嗯嗯|好啦|行啦|知道了|隨便|随便|都可以|whatever|fine|k)[。.!！]?$"))
            || Has(value, "(敷衍一下|反正都行|你決定就好|你决定就好)"))
            Add(attitudes, CommunicationAttitudeSignal.Perfunctory, 0.68);
        if (Has(value, "(我確定|我确定|我知道怎麼做|我知道怎么做|交給我|交给我|一定可以|i'm\\s+sure|leave\\s+it\\s+to\\s+me)"))
            Add(attitudes, CommunicationAttitudeSignal.Confident, 0.80);
        if (Has(value, "(可能是我錯|可能是我错|我不一定對|我不一定对|請指教|请指教|我還在學|我还在学|i\\s+might\\s+be\\s+wrong)"))
            Add(attitudes, CommunicationAttitudeSignal.Humble, 0.84);
        if (Has(value, "(閉嘴|闭嘴|滾開|滚开|廢物|废物|沒用|没用|討厭[你妳您]|讨厌[你妳您]|去死|shut\\s+up|useless|hate\\s+you)"))
            Add(attitudes, CommunicationAttitudeSignal.Hostile, 0.96);

        if (Has(value, "([你妳您]開心就好|[你妳您]开心就好|我哪敢|都可以啊|隨便[你妳您]啊|随便[你妳您]啊|沒關係.*[你妳您].*就好|没关系.*[你妳您].*就好|呵呵)"))
            Add(styles, CommunicationStyleSignal.PassiveAggressive, 0.62);
        if (Has(value, "(真是太.*了|可真|還真是|还真是|好棒喔|厲害了|厉害了|了不起啊|不愧是[你妳您]|great\\s+job.*again)"))
            Add(styles, CommunicationStyleSignal.Sarcasm, 0.58);
        if (Has(value, "(才怪|並沒有|并没有|說反了|说反了|反話|反话|as\\s+if|not!)"))
            Add(styles, CommunicationStyleSignal.Irony, 0.86);
        if (Has(value, "(就這|就这|這也叫|这也叫|[你妳您]就這點|[你妳您]就这点|笑死人|可憐|可怜|that's\\s+it|pathetic)"))
            Add(styles, CommunicationStyleSignal.Mocking, 0.84);
        if (Has(value, "(有些人|我就不說是誰|我就不说是谁|懂的都懂|[你妳您]應該明白|[你妳您]应该明白|話不用說太白|话不用说太白|read\\s+between\\s+the\\s+lines)"))
            Add(styles, CommunicationStyleSignal.ImplicitMeaning, 0.78);
        if ((styles.ContainsKey(CommunicationStyleSignal.Sarcasm)
                || styles.ContainsKey(CommunicationStyleSignal.Mocking))
            && (attitudes.ContainsKey(CommunicationAttitudeSignal.Hostile)
                || Has(value, "(蠢|笨|可笑|丟臉|丢脸|stupid|ridiculous)")))
        {
            Add(styles, CommunicationStyleSignal.Derisive, 0.82);
        }

        if (Has(value, "(如果[你妳您].*在乎我.*就|[你妳您]愛我就|[你妳您]爱我就|要是真的關心我|要是真的关心我|我都是為了[你妳您]|我都是为了[你妳您]|[你妳您]讓我失望|[你妳您]让我失望|if\\s+you\\s+loved\\s+me|after\\s+all\\s+i've\\s+done)"))
            Add(strategies, CommunicationStrategySignal.EmotionalBlackmailCandidate, 0.88);
        if (Has(value, "(正常人都會|正常人都会|[你妳您]應該懂事|[你妳您]应该懂事|做人要|有良心.*應該|有良心.*应该|大家都知道[你妳您]應該|大家都知道[你妳您]应该|a\\s+good\\s+person\\s+would|you\\s+owe\\s+me)"))
            Add(strategies, CommunicationStrategySignal.MoralCoercionCandidate, 0.84);
        if (Has(value, "(不然我就|否則|否则|再不.*我就|小心我|我會讓你|我会让你|你給我等著|你给我等着|or\\s+else|you'll\\s+regret|i'll\\s+make\\s+you)"))
            Add(strategies, CommunicationStrategySignal.ThreatCandidate, 0.94);
        if (Has(value, "([你妳您]記錯了|[你妳您]记错了|根本沒(有)?發生|根本没(有)?发生|都是[你妳您]想太多|[你妳您]太敏感|[你妳您]又在幻想|我從來沒說過|我从来没说过|you're\\s+imagining\\s+things|that\\s+never\\s+happened|too\\s+sensitive)"))
            Add(strategies, CommunicationStrategySignal.GaslightingCandidate, 0.55);
        if (Has(value, "(不理[你妳您]了|不跟[你妳您]說話|不跟[你妳您]说话|除非[你妳您].*不然不理|我消失給[你妳您]看|我消失给[你妳您]看|silent\\s+treatment|won't\\s+talk\\s+to\\s+you)"))
            Add(strategies, CommunicationStrategySignal.WithdrawalThreatCandidate, 0.72);
        if (Has(value, "(除了我沒人|除了我没人|只有我能接受[你妳您]|只有我能幫[你妳您]|只有我能帮[你妳您]|[你妳您]配不上|都是為[你妳您]好|都是为[你妳您]好|沒我[你妳您]什麼都不是|没我[你妳您]什么都不是|no\\s+one\\s+else\\s+would|you\\s+need\\s+me)"))
            Add(strategies, CommunicationStrategySignal.PuaTacticCandidate, 0.82);

        var directness = Math.Clamp(
            0.18
            + (tones.ContainsKey(CommunicationToneSignal.Firm) ? 0.48 : 0)
            + (tones.ContainsKey(CommunicationToneSignal.Direct) ? 0.34 : 0)
            + (attitudes.ContainsKey(CommunicationAttitudeSignal.Impatient) ? 0.18 : 0)
            + (questions > 0 ? -0.05 : 0),
            0,
            1);
        var expressiveness = Math.Clamp(
            0.12
            + Math.Min(0.42, exclamations * 0.10)
            + (tones.ContainsKey(CommunicationToneSignal.Enthusiastic) ? 0.28 : 0)
            + (tones.ContainsKey(CommunicationToneSignal.Humorous) ? 0.18 : 0),
            0,
            1);
        var ambiguity = styles.Count == 0
            ? 0.08
            : Math.Clamp(0.36 + styles.Values.Average() * 0.34, 0, 1);

        return Build(tones, attitudes, styles, strategies, directness, expressiveness, ambiguity);
    }

    private static CommunicationSignalAnalysis Build(
        Dictionary<CommunicationToneSignal, double> tones,
        Dictionary<CommunicationAttitudeSignal, double> attitudes,
        Dictionary<CommunicationStyleSignal, double> styles,
        Dictionary<CommunicationStrategySignal, double> strategies,
        double directnessSample,
        double expressivenessSample,
        double ambiguity)
        => new(
            ToSignals(tones),
            ToSignals(attitudes),
            ToSignals(styles),
            ToSignals(strategies),
            directnessSample,
            expressivenessSample,
            ambiguity);

    private static IReadOnlyList<ScoredCommunicationSignal<T>> ToSignals<T>(
        Dictionary<T, double> source) where T : struct, Enum
        => source
            .OrderByDescending(item => item.Value)
            .ThenBy(item => Convert.ToInt32(item.Key))
            .Select(item => new ScoredCommunicationSignal<T>(item.Key, Math.Clamp(item.Value, 0, 1)))
            .ToArray();

    private static void Add<T>(Dictionary<T, double> target, T kind, double confidence)
        where T : struct, Enum
    {
        if (!target.TryGetValue(kind, out var existing) || confidence > existing)
            target[kind] = Math.Clamp(confidence, 0, 1);
    }

    private static bool Has(string value, string pattern)
        => Regex.IsMatch(value, pattern, Options);

    private static double CalibrateDirectSignal(
        double confidence,
        CommunicationPatternState? baseline)
    {
        if (baseline == null || baseline.ObservedTurns < 8)
            return confidence;
        var adjustment = Math.Max(0, baseline.BaselineDirectness - 0.55) * 0.25;
        return Math.Max(0.55, confidence - adjustment);
    }
}
