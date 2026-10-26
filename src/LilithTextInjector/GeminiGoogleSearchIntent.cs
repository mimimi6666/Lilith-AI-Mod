using System;
using System.Text.RegularExpressions;

namespace LilithTextInjector;

internal static class GeminiGoogleSearchIntent
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex ExplicitLookup = new(
        "(?:幫我|帮我)?\\s*(?:查查|查一下|查詢|查询|搜尋|搜索|上網查|上网查|聯網查|联网查)"
        + "|(?:調べて|検索して|ネットで調べて)"
        + "|(?:look\\s*(?:it|this)?\\s*up|search(?:\\s+the)?\\s+web"
        + "|google\\s+(?:it|this)|find\\s+(?:it\\s+)?online)",
        Options);

    private static readonly Regex TemporalMarker = new(
        "(?:最新|新聞|新闻|即時|即时|現任|现任|今天|今日|今年|目前|最近|新版|更新後|更新后)"
        + "|(?:最新|ニュース|今日|現在|最近|新版|更新後)"
        + "|(?:latest|current|today|recent|new\\s+version|after\\s+(?:the\\s+)?update)",
        Options);

    private static readonly Regex VolatileTopic = new(
        "(?:新聞|新闻|消息|資訊|信息|情報|進度|进度|價格|价格|費用|费用"
        + "|比賽|比赛|賽程|赛程|天氣|天气|氣溫|气温|版本|模型|功能"
        + "|政策|規定|规定|支援|支持|行程|時刻表|时刻表|總統|总统"
        + "|首相|市長|市长|執行長|执行长|負責人|负责人|是誰|是谁)"
        + "|(?:ニュース|価格|費用|天気|予報|バージョン|モデル|機能"
        + "|政策|規則|対応|予定|試合|大統領|首相|市長|責任者)"
        + "|(?:news|price|cost|weather|forecast|version|model|feature"
        + "|policy|regulation|support|schedule|score|president"
        + "|prime\\s+minister|mayor|ceo|who\\s+is)",
        Options);

    internal static bool ShouldForceSearch(string userText)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return false;

        return ExplicitLookup.IsMatch(userText)
            || (TemporalMarker.IsMatch(userText)
                && VolatileTopic.IsMatch(userText));
    }
}
