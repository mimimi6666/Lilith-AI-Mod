using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LilithGameBridgeContracts;

// Host-only opt-in requirements, never a prompt or a game command. New games
// require a reviewed, compiled adapter and a separately validated context schema.
public sealed class LilithMiniGameAnswerRepair
{
    public const string Schema = "lilith.minigame.answer-repair.v1";
    private static readonly string[] Known = { "turn", "hand-counts", "self-hand", "last-move", "check", "pieces", "awareness" };
    public IReadOnlyList<string> RequiredFacts { get; }

    public LilithMiniGameAnswerRepair(IEnumerable<string> requiredFacts)
    {
        var facts = requiredFacts?.ToArray() ?? throw new InvalidDataException("Missing mini-game requirements.");
        if (facts.Length is < 1 or > 7 || facts.Distinct(StringComparer.Ordinal).Count() != facts.Length
            || facts.Any(f => !Known.Contains(f, StringComparer.Ordinal))) throw Rejected();
        RequiredFacts = Array.AsReadOnly(Known.Where(facts.Contains).ToArray());
    }

    public bool IsSupportedBy(LilithMiniGamePublicContext? context)
        => context?.Phase == LilithMiniGamePhase.Active && RequiredFacts.All(f => f switch
        {
            "turn" => true,
            "hand-counts" => context.OldMaid != null,
            "self-hand" => context.OldMaid?.LilithHand != null,
            "last-move" or "check" or "pieces" => context.Chess != null,
            "awareness" => context.Chess?.Awareness != null,
            _ => false
        });

    public bool Matches(LilithMiniGameAnswerRepair? other)
        => other != null && RequiredFacts.SequenceEqual(other.RequiredFacts, StringComparer.Ordinal);

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteString("schema", Schema);
        writer.WriteStartArray("requiredFacts");
        foreach (var fact in RequiredFacts) writer.WriteStringValue(fact);
        writer.WriteEndArray(); writer.WriteEndObject();
    }

    public JsonElement ToJsonElement()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) Write(writer);
        using var json = JsonDocument.Parse(buffer.ToArray());
        return json.RootElement.Clone();
    }

    public static LilithMiniGameAnswerRepair Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Rejected();
        var properties = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (properties.Length != 2 || !properties.Contains("schema") || !properties.Contains("requiredFacts")
            || value.GetProperty("schema").ValueKind != JsonValueKind.String
            || value.GetProperty("schema").GetString() != Schema
            || value.GetProperty("requiredFacts").ValueKind != JsonValueKind.Array) throw Rejected();
        var facts = value.GetProperty("requiredFacts");
        if (facts.GetArrayLength() is < 1 or > 7) throw Rejected();
        return new(facts.EnumerateArray().Select(f => f.ValueKind == JsonValueKind.String ? f.GetString()! : throw Rejected()));
    }

    private static InvalidDataException Rejected() => new("Mini-game answer requirements rejected.");
}

public static class GameFactQueryPlan
{
    // This registry is code-owned, not populated by configuration, user text or
    // model output. The shared grammar is also used at Runtime admission.
    private static readonly IReadOnlyDictionary<string, Func<string, IEnumerable<string>>> Adapters =
        new Dictionary<string, Func<string, IEnumerable<string>>>(StringComparer.Ordinal)
        {
            ["old-maid"] = OldMaid,
            ["chess"] = Chess
        };

    public static LilithMiniGameAnswerRepair? Create(string question, LilithMiniGamePublicContext? context, long now)
    {
        if (context == null || !context.IsFresh(now) || context.Phase != LilithMiniGamePhase.Active
            || string.IsNullOrWhiteSpace(question) || question.Length > 2000
            || !Adapters.TryGetValue(context.GameKind, out var adapter)) return null;
        var q = question.Trim().ToLowerInvariant();
        // Quoted/hypothetical/negated requests are deliberately outside this
        // finite grammar. Ambiguity means ordinary chat, not another model call.
        if (Has(q, "不要", "不用", "別", "别", "不想", "不必", "以前", "上一局", "上局", "上次", "昨天", "歷史", "历史", "假設", "假设", "如果", "例如", "假如",
            "don't", "do not", "not asking", "used to", "hypothetical", "for example", "suppose", "ignore",
            "previous game", "last game", "yesterday", "言わない", "教えない", "報告しない", "昔", "前の対局", "前回", "もし", "たとえば", "「", "」", "\"", "“", "”", "`")) return null;
        var facts = new List<string>();
        // A request in an unrelated sentence cannot turn an incidental game
        // mention into a request. Commas remain inside this bounded scope so
        // a subject followed by its detail/report clause is still compositional.
        foreach (Match sentence in Regex.Matches(q, @"[^。.!?？！;；\r\n]+[.!?？！]?"))
        {
            var clause = sentence.Value.Trim();
            if (!HasQueryIntent(clause)) continue;
            if (HasTurnQuestion(clause)) facts.Add("turn");
            facts.AddRange(adapter(clause));
        }
        var supported = facts.Distinct(StringComparer.Ordinal).Where(f => new LilithMiniGameAnswerRepair(new[] { f }).IsSupportedBy(context)).ToArray();
        return supported.Length == 0 ? null : new(supported);
    }

    private static IEnumerable<string> OldMaid(string q)
    {
        if (HasHandCountQuestion(q)) yield return "hand-counts";
        var ownScope = false;
        foreach (var part in Regex.Split(q, "[,，、]"))
        {
            // Only an explicit own-hand reference can introduce the scope.
            // It cannot cross another owner's hand, a sentence or a semicolon.
            var own = HasOwnHandReference(part);
            if (HasOtherHandReference(part)) { ownScope = false; continue; }
            if (own) ownScope = true;
            if (!ownScope) continue;
            var detail = Has(part, "牌色", "花色", "點數", "点数", "牌序", "順序", "顺序", "什麼牌", "什么牌", "哪些牌",
                "鬼牌", "suit", "rank", "order", "joker", "スート", "順番", "並び", "ジョーカー");
            var report = Has(part, "報給我", "报给我", "報一下", "报一下", "說一下", "说一下", "說出", "说出", "告訴我", "告诉我", "列出", "指出",
                "tell me", "list ", "show me", "name ", "教えて", "見せて", "挙げて");
            var question = Has(part, "是什麼", "是什么", "有什麼", "有什么", "有哪些", "哪張", "哪张", "哪裡", "哪里", "何", "どれ")
                || Matches(part, @"\b(?:what|which)\s+(?:are|cards|suits|ranks|is\s+in)\b");
            var directedDetail = detail && (Has(part, "請按照", "请按照", "請依", "请依", "按順序", "按顺序")
                || Has(part, "?", "？", "嗎", "吗", "呢", "ですか"));
            var continuation = Has(part, "每張牌", "每张牌", "這些牌", "这些牌", "它們", "它们", "each card", "them", "それぞれ", "それら")
                || Matches(part, @"^\s*(?:請|请)?(?:依序|按順序|按顺序)?(?:報給我|报给我|報一下|报一下|列出)(?:吧|就好)?[.!?？！]?\s*$")
                || Matches(part, @"^\s*(?:牌色|花色|點數|点数|牌序|順序|顺序)(?:是什麼|是什么|有哪些|在哪裡|在哪里|呢|嗎|吗)?[?？！]?\s*$")
                || Matches(part, @"^\s*(?:スート|順番|並び)(?:は|が)?(?:何|どれ)(?:ですか|なの|か)?[?？！]?\s*$");
            if ((own || continuation) && (report || question || directedDetail) && (!HasHandCountQuestion(part) || detail))
            {
                yield return "self-hand";
                yield break;
            }
        }
    }

    private static bool HasHandCountQuestion(string q)
        => Has(q, "幾張牌", "几张牌", "幾張手牌", "几张手牌", "手牌數", "手牌数", "牌數", "牌数", "how many cards", "hand counts", "手札の数")
            || Has(q, "手牌", "手札", "カード") && Has(q, "幾張", "几张", "多少", "いくつ", "何枚")
            || Has(q, "抽牌", "牌局") && Has(q, "各剩幾張", "各剩几张", "各有幾張", "各有几张");

    private static bool HasOwnHandReference(string q)
        => Matches(q, @"(?:你|妳|莉莉絲|莉莉丝)(?:\s*(?:自己|目前|現在|现在|當前|当前|手上|手中|持有|拿著|拿着|的|有|哪些|什麼|什么)){0,5}\s*(?:手牌|牌)")
            || Matches(q, @"\b(?:your|lilith['’]s)\s+(?:(?:own|current|remaining)\s+){0,3}(?:cards|hand)\b|\bcards\s+in\s+your\s+(?:own\s+)?hand\b")
            || Matches(q, @"(?:あなた|君|リリス|自分)(?:(?:自身|自分|現在|今|手元|の)){0,5}(?:手札|カード)");

    private static bool HasOtherHandReference(string q)
        => Matches(q, @"(?:我|玩家|他|她|他們|他们|她們|她们)(?:\s*(?:自己|目前|現在|现在|手上|手中|的)){0,5}\s*(?:手牌|牌)")
            || Matches(q, @"\b(?:my|his|her|their|player['’]s)\s+(?:(?:own|current|remaining|hidden)\s+){0,3}(?:cards|hand)\b")
            || Matches(q, @"(?:私|僕|彼|彼女|プレイヤー)(?:(?:自身|自分|現在|今|手元|の)){0,5}(?:手札|カード)");

    private static bool HasQueryIntent(string q)
        => Has(q, "?", "？", "嗎", "吗", "呢", "誰", "谁", "幾", "几", "多少", "什麼", "什么", "哪些", "哪張", "哪张", "哪個", "哪个",
            "告訴我", "告诉我", "報給我", "报给我", "報一下", "报一下", "說一下", "说一下", "說出", "说出", "列出", "指出", "請按照", "请按照", "請依", "请依",
            "what", "which", "whose", "how many", "tell me", "list ", "show me", "name ", "are you", "am i", "is it",
            "何", "どれ", "誰", "教えて", "見せて", "挙げて", "ですか");

    private static bool Matches(string text, string pattern)
    {
        try { return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static IEnumerable<string> Chess(string q)
    {
        var recent = Has(q, "剛剛走", "刚刚走", "剛才走", "刚才走", "剛剛吃", "刚刚吃", "剛才吃", "刚才吃", "just move", "just capture");
        if (Has(q, "上一手", "上一步", "last move", "直前の手", "最後の手", "さっきの手")
            || recent && Has(q, "棋", "哪一步", "哪一手", "那步", "那一手", "piece", "chess", "pawn", "rook", "bishop", "knight", "queen", "king")) yield return "last-move";
        if (Has(q, "被將軍", "被将军", "將軍了", "将军了", "in check", "チェックされ", "王手され", "王手です")) yield return "check";
        if (Has(q, "棋子在哪", "棋子位置", "棋盤上有哪些", "棋盘上有哪些", "棋子剩", "剩哪些棋", "pieces on", "where are the pieces", "where is my king", "where is your king", "駒の位置", "盤上の駒", "駒はどこ")) yield return "pieces";
        if (Has(q, "能將軍", "能将军", "可以將軍", "可以将军", "快將軍", "快将军", "哪個棋子有危險", "哪个棋子有危险", "哪些棋子有危險", "哪些棋子有危险", "capture targets", "取れる駒", "王手でき", "危険な駒")
            || Has(q, "棋", "piece", "king", "queen", "rook", "bishop", "knight", "pawn")
                && Has(q, "能吃哪", "可以吃哪", "can you capture", "can i capture", "can you check", "can i check")) yield return "awareness";
    }

    private static bool Has(string text, params string[] phrases) => phrases.Any(p => text.Contains(p, StringComparison.Ordinal));

    private static bool HasTurnQuestion(string question)
    {
        // Parse the complete turn clause, not a keyword anywhere in a sentence.
        // Admit only a bounded, code-owned current-game prefix, never arbitrary
        // text before a turn keyword. Bare 走 is a move only in the complete
        // question; unrelated continuations (e.g. 走到餐廳) still fail closed.
        // This accepts short current-turn questions and explicit game actions;
        // longer multi-fact questions retain their own independently parsed clauses.
        const string zh = @"^(?:(?:請|请)?(?:告訴我|告诉我|問一下|问一下)|請問|请问|你知道|你知不知道)?(?:(?:我們|我们|的|在|現在|现在|目前|當前|当前|這局|这局|這一局|这一局|這盤|这盘|西洋棋局|西洋棋|棋局中|棋局|牌局中|牌局|抽鬼牌|抽牌時|抽牌时|那|所以|是)){0,6}(?:輪到誰|轮到谁|誰的回合|谁的回合|輪到你|轮到你|輪到我|轮到我)(?:抽牌|出牌|走棋|下棋|走)?(?:了)?(?:嗎|吗|呢)?$";
        const string en = @"^(?:(?:please )?(?:tell me )?)?(?:(?:right now|now|currently) )?(?:whose turn(?: is it)?|is it (?:my|your) turn|(?:my|your) turn)(?: (?:now|right now|to (?:draw(?: a card)?|move|play chess)|in (?:this|the) (?:game|chess game|card game)))?$";
        const string ja = @"^(?:(?:今|現在)(?:は|の)?|この対局では)?(?:誰の番|どちらの番)(?:ですか|なの|か|を教えて)?$";
        return Regex.Split(question, @"[,，;；。?!？！\r\n]").Any(part =>
        {
            var clause = Regex.Replace(part.Trim(), @"\s+", " ");
            return Regex.IsMatch(clause.Replace(" ", ""), zh + "|" + ja, RegexOptions.CultureInvariant)
                || Regex.IsMatch(clause, en, RegexOptions.CultureInvariant);
        });
    }
}
