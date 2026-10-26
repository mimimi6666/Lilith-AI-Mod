using System;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

internal sealed record GameHermesFailureTextSet(
    string TraditionalChinese,
    string SimplifiedChinese,
    string Japanese,
    string English);

// Presentation only: the authenticated bridge terminal is the authority.
// A failed turn proves neither a transport outage nor a particular model cause.
// Do not classify arbitrary error codes, exception messages or model prose here.
internal static class GameHermesFailurePresentation
{
    private static readonly GameHermesFailureTextSet Failed = new(
        "這次回覆沒有完成。請到控制中心查看狀態。",
        "这次回复没有完成。请到控制中心查看状态。",
        "今回の返答は完了しなかったよ。コントロールセンターで状態を確認してね。",
        "This reply did not complete. Please check the status in Control Center.");

    private static readonly GameHermesFailureTextSet Interrupted = new(
        "這一回合已經停下來了，可以繼續和我說話。",
        "这一回合已经停下来了，可以继续和我说话。",
        "この会話はいったん止めたよ。続けて話しかけて大丈夫。",
        "That turn has stopped. You can keep talking to me.");

    internal static GameHermesFailureTextSet ForTerminal(
        LilithGameBridgeTerminal terminal)
        => terminal switch
        {
            LilithGameBridgeTerminal.Failed => Failed,
            LilithGameBridgeTerminal.Interrupted => Interrupted,
            _ => throw new ArgumentOutOfRangeException(nameof(terminal))
        };
}
