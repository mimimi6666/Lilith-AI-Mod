using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LilithTextInjector;

// UI labels are selected by the existing display locale, never by voice mode.
// Fixed reviewed speech remains a separate Chinese/Japanese catalog.
internal static class MiniGameUiText
{
    private readonly record struct Line(string Traditional, string Simplified, string Japanese, string English);
    private static readonly IReadOnlyDictionary<string, Line> Lines =
        new ReadOnlyDictionary<string, Line>(new Dictionary<string, Line>(StringComparer.Ordinal)
        {
            ["oldmaid.play"] = new("一起玩抽鬼牌", "一起玩抽鬼牌", "一緒にババ抜きをする", "Play Old Maid together"),
            ["oldmaid.replay"] = new("再玩一局抽鬼牌", "再玩一局抽鬼牌", "もう一度ババ抜きをする", "Play Old Maid again"),
            ["chess.play"] = new("一起玩西洋棋", "一起玩国际象棋", "一緒にチェスをする", "Play chess together"),
            ["chess.continue"] = new("棋局畫面", "棋局画面", "盤面に戻る", "Back to board"),
            ["mode.casual"] = new("休閒（不計時）", "休闲（不计时）", "カジュアル（時間制限なし）", "Casual (untimed)"),
            ["mode.challenge"] = new("挑戰（計時）", "挑战（计时）", "チャレンジ（持ち時間あり）", "Challenge (timed)"),
            ["snack.back"] = new("回到點心時間", "回到点心时间", "おやつの時間に戻る", "Back to snack time"),
            ["start"] = new("開始對局", "开始对局", "対局開始", "Start game"),
            ["restart"] = new("重新開始", "重新开始", "最初からやり直す", "Restart game"),
            ["replay"] = new("再來一局", "再来一局", "もう一局", "Play again"),
            ["leave"] = new("不玩了", "不玩了", "ここまでにする", "Stop playing"),
            ["draw.claim"] = new("申請和棋", "申请和棋", "引き分けを申請", "Claim a draw"),
            ["draw.cancel"] = new("取消和棋申請", "取消和棋申请", "引き分けの申請を取り消す", "Cancel draw claim"),
            ["promotion.queen"] = new("升變為后", "升变为后", "クイーンに昇格", "Promote to queen"),
            ["promotion.rook"] = new("升變為車", "升变为车", "ルークに昇格", "Promote to rook"),
            ["promotion.bishop"] = new("升變為象", "升变为象", "ビショップに昇格", "Promote to bishop"),
            ["promotion.knight"] = new("升變為馬", "升变为马", "ナイトに昇格", "Promote to knight"),
            ["notice.progress"] = new("進度暫時無法儲存，但這一局還能繼續。", "进度暂时无法保存，但这一局还能继续。",
                "進み具合を保存できないけれど、この対局は続けられるよ。", "Progress cannot be saved right now, but this game can continue."),
            ["notice.opponent"] = new("棋局暫時無法繼續，先回到點心時間吧。", "棋局暂时无法继续，先回到点心时间吧。",
                "今は対局を続けられないみたい。おやつの時間に戻ろう。", "This game cannot continue right now. Let's return to snack time."),
            ["notice.claim"] = new("這步還不能申請和棋。雙擊莉莉絲可以取消申請。", "这步还不能申请和棋。双击莉莉丝可以取消申请。",
                "この手ではまだ引き分けを申請できないよ。リリスをダブルクリックすると申請を取り消せるよ。",
                "A draw cannot be claimed with that move. Double-click Lilith to cancel the claim.")
        });

    internal static IEnumerable<string> Keys => Lines.Keys;

    internal static string Text(string key, LilithGameDisplayLocale locale)
    {
        if (key == null || !Lines.TryGetValue(key, out var line))
            throw new ArgumentException("Unknown mini-game UI text key.", nameof(key));
        return locale switch
        {
            LilithGameDisplayLocale.SimplifiedChinese => line.Simplified,
            LilithGameDisplayLocale.Japanese => line.Japanese,
            LilithGameDisplayLocale.English => line.English,
            _ => line.Traditional
        };
    }
}
