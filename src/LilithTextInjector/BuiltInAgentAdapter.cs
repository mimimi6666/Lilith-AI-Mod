using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal delegate ValueTask<AgentTurnResponse> AgentTurnExecutor(
    AgentTurnRequest request,
    CancellationToken cancellationToken);

internal sealed class BuiltInAgentAdapter : IAgentAdapter
{
    private readonly AgentTurnExecutor _executor;

    public string Id { get; }
    public AgentAdapterCapabilities Capabilities { get; }

    internal BuiltInAgentAdapter(
        string id,
        AgentAdapterCapabilities capabilities,
        AgentTurnExecutor executor)
    {
        if (!MotionProducerIdentity.TryNormalize(id, out var normalizedId))
            throw new ArgumentException("Adapter ID must be a non-empty, control-free identifier.", nameof(id));
        if (!capabilities.HasFlag(AgentAdapterCapabilities.TextReply))
            throw new ArgumentException("A built-in agent adapter must provide text replies.", nameof(capabilities));
        Id = normalizedId;
        Capabilities = capabilities;
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public async ValueTask<AgentTurnResponse> ExecuteTurnAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        var response = await _executor(request, cancellationToken).ConfigureAwait(false);
        return response ?? throw new InvalidOperationException("The built-in agent executor returned no response.");
    }
}

internal static class BuiltInLegacyReplyProjector
{
    internal static AgentTurnResponse Project(
        string rawReply,
        bool japaneseVoiceMode,
        bool includeMotionSuggestions = false)
    {
        if (rawReply == null)
            throw new ArgumentNullException(nameof(rawReply));
        var envelope = AgentMotionSuggestionEnvelope.Parse(
            rawReply,
            japaneseVoiceMode,
            includeMotionSuggestions);
        var displayText = envelope.ReplyPayload;
        if (japaneseVoiceMode
            && TryParseBilingualReply(envelope.ReplyPayload, out var bilingualDisplay))
        {
            displayText = bilingualDisplay;
        }
        return new AgentTurnResponse(
            CleanDisplayText(displayText),
            envelope.Suggestions);
    }

    internal static string CleanDisplayText(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return string.Empty;
        var cleaned = Regex.Replace(reply, "[`*_#>]", string.Empty);
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    private static bool TryParseBilingualReply(
        string raw,
        out string displayText)
    {
        displayText = string.Empty;
        try
        {
            var json = raw.Trim();
            if (json.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline)
                    json = json.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var display = root.TryGetProperty("display_text", out var displayElement)
                ? displayElement.GetString()
                : root.TryGetProperty("display_zh", out var legacyDisplayElement)
                    ? legacyDisplayElement.GetString()
                    : null;
            var speech = root.TryGetProperty("speech_ja", out var speechElement)
                ? speechElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(speech))
                return false;
            displayText = display!;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class BuiltInAgentShadowComparison
{
    internal Guid RequestId { get; }
    internal string AdapterId { get; }
    internal bool ReplyMatches { get; }
    internal bool HasNoMotionSuggestions { get; }
    internal bool Matched => ReplyMatches && HasNoMotionSuggestions;
    internal int ReplyLength { get; }
    internal TimeSpan Elapsed { get; }

    internal BuiltInAgentShadowComparison(
        Guid requestId,
        string adapterId,
        bool replyMatches,
        bool hasNoMotionSuggestions,
        int replyLength,
        TimeSpan elapsed)
    {
        RequestId = requestId;
        AdapterId = adapterId;
        ReplyMatches = replyMatches;
        HasNoMotionSuggestions = hasNoMotionSuggestions;
        ReplyLength = replyLength;
        Elapsed = elapsed;
    }
}

internal static class BuiltInAgentShadowComparer
{
    internal static BuiltInAgentShadowComparison Compare(
        AgentTurnRequest request,
        IAgentAdapter adapter,
        AgentTurnResponse adapterResponse,
        string legacyDisplayText,
        DateTimeOffset completedAt)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (adapter == null)
            throw new ArgumentNullException(nameof(adapter));
        if (adapterResponse == null)
            throw new ArgumentNullException(nameof(adapterResponse));
        if (legacyDisplayText == null)
            throw new ArgumentNullException(nameof(legacyDisplayText));

        var elapsed = completedAt - request.RequestedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        return new BuiltInAgentShadowComparison(
            request.Id,
            adapter.Id,
            string.Equals(adapterResponse.ReplyText, legacyDisplayText, StringComparison.Ordinal),
            adapterResponse.MotionSuggestions.Count == 0,
            legacyDisplayText.Length,
            elapsed);
    }
}
