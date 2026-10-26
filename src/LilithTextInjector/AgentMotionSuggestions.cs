using System;
using System.Collections.Generic;
using System.Text.Json;

namespace LilithTextInjector;

internal enum ModelMotionSuggestionMode
{
    Off,
    Observe,
    Assist
}

internal static class ModelMotionSuggestionModeResolver
{
    internal static bool TryResolve(string? configured, out ModelMotionSuggestionMode mode)
    {
        var value = (configured ?? string.Empty).Trim();
        if (value.Equals("Off", StringComparison.OrdinalIgnoreCase))
        {
            mode = ModelMotionSuggestionMode.Off;
            return true;
        }
        if (value.Equals("Observe", StringComparison.OrdinalIgnoreCase))
        {
            mode = ModelMotionSuggestionMode.Observe;
            return true;
        }
        if (value.Equals("Assist", StringComparison.OrdinalIgnoreCase))
        {
            mode = ModelMotionSuggestionMode.Assist;
            return true;
        }

        mode = ModelMotionSuggestionMode.Off;
        return false;
    }
}

internal enum AgentAvatarPoseKind
{
    Unknown,
    Standing,
    Sitting,
    LyingDown,
    Sleeping,
    Interacting,
    TransientAction
}

internal readonly struct AgentAvatarMotionContext
{
    internal AgentAvatarPoseKind Pose { get; }
    internal bool IsPajamas { get; }

    internal AgentAvatarMotionContext(
        AgentAvatarPoseKind pose,
        bool isPajamas)
    {
        Pose = pose;
        IsPajamas = isPajamas;
    }
}

internal enum AgentMotionEnvelopeStatus
{
    NotRequested,
    Absent,
    Parsed,
    Invalid,
    UnsupportedVersion
}

internal sealed class AgentMotionEnvelopeResult
{
    internal string ReplyPayload { get; }
    internal AgentMotionEnvelopeStatus Status { get; }
    internal IReadOnlyList<AgentMotionSuggestion> Suggestions { get; }
    internal int DeclaredCount { get; }
    internal int RejectedCount { get; }
    internal int TruncatedCount { get; }

    internal AgentMotionEnvelopeResult(
        string replyPayload,
        AgentMotionEnvelopeStatus status,
        IReadOnlyList<AgentMotionSuggestion>? suggestions = null,
        int declaredCount = 0,
        int rejectedCount = 0,
        int truncatedCount = 0)
    {
        ReplyPayload = replyPayload ?? throw new ArgumentNullException(nameof(replyPayload));
        Status = status;
        Suggestions = suggestions ?? Array.Empty<AgentMotionSuggestion>();
        DeclaredCount = declaredCount;
        RejectedCount = rejectedCount;
        TruncatedCount = truncatedCount;
    }
}

internal static class AgentMotionSuggestionEnvelope
{
    internal const int ProtocolVersion = 1;
    internal const int MaximumSuggestions = AgentMotionSuggestionRouter.MaximumSuggestionsPerTurn;
    internal const string PlainStartTag = "<lilith_motion_v1>";
    internal const string PlainEndTag = "</lilith_motion_v1>";
    internal const string JapanesePropertyName = "motion_intents";

    internal static AgentMotionEnvelopeResult Parse(
        string rawReply,
        bool japaneseVoiceMode,
        bool requested)
    {
        if (rawReply == null)
            throw new ArgumentNullException(nameof(rawReply));
        if (!requested)
        {
            return new AgentMotionEnvelopeResult(
                rawReply,
                AgentMotionEnvelopeStatus.NotRequested);
        }

        return japaneseVoiceMode
            ? ParseJapaneseJson(rawReply)
            : ParsePlainSuffix(rawReply);
    }

    internal static string BuildPromptInstruction(bool japaneseVoiceMode)
    {
        const string allowlist = "arrive_at_window, walk_left, walk_right, play_one_shot, sit, stand, peek_from_window, drop_from_window";
        const string semanticGuide = " Intent mapping: use play_one_shot when the user asks Lilith to perform, show, or choose any gesture, reaction, cute action, or small/random action. Use walk_left or walk_right only when the user explicitly asks her to walk or move in that direction. Use arrive_at_window only when the user explicitly asks her to go, jump, teleport, or snap onto a window; never infer it merely from a request to sit.";
        if (japaneseVoiceMode)
        {
            return "\nOptional avatar motion suggestion protocol: keep the required JSON reply object and add a top-level property \"motion_intents\" containing an array of zero to four strings. Allowed strings: "
                + allowlist
                + "."
                + semanticGuide
                + " Use an empty array unless a physical gesture clearly fits the reply. This is only a suggestion; never mention it, add coordinates, timing, force, window identifiers, or claim that it ran.";
        }

        return "\nOptional avatar motion suggestion protocol: after the complete user-visible reply, append exactly one machine-readable suffix on a new line in this form: "
            + PlainStartTag
            + "{\"version\":1,\"intents\":[]}"
            + PlainEndTag
            + ". The intents array may contain zero to four of these strings only: "
            + allowlist
            + "."
            + semanticGuide
            + " Use an empty array unless a physical gesture clearly fits the reply. Never mention the suffix, add coordinates, timing, force, window identifiers, or claim that a suggestion ran.";
    }

    internal static string BuildStatePromptInstruction(AgentAvatarMotionContext context)
    {
        return context.Pose switch
        {
            AgentAvatarPoseKind.Sleeping =>
                "\nCurrent avatar motion state: Lilith is sleeping. Preserve that state, return an empty motion-intent array, and answer naturally as remaining asleep or resting. She may express personality by sleepily refusing a request to get up; vary the wording with the conversation instead of repeating a fixed refusal. Never suggest waking, sitting, walking, standing, or repositioning her, and never claim that she moved.",
            AgentAvatarPoseKind.LyingDown =>
                "\nCurrent avatar motion state: Lilith is lying down"
                + (context.IsPajamas ? " in pajamas" : string.Empty)
                + ". Preserve the pose and normally return an empty motion-intent array. If the user asks her to sit or casually keep them company, answer naturally that she can remain lying down with them. Suggest stand only when the user explicitly asks her to get up or wake.",
            AgentAvatarPoseKind.Sitting =>
                "\nCurrent avatar motion state: Lilith is already sitting. Never suggest sit again. If the user explicitly asks her to show, perform, or choose a gesture or small/random action, suggest play_one_shot and let the dispatcher perform the safe transition. Otherwise preserve the pose and normally return an empty motion-intent array; suggest stand only when the user explicitly asks her to get up.",
            AgentAvatarPoseKind.TransientAction =>
                "\nCurrent avatar motion state: a native action or transition is already playing. Preserve it and return an empty motion-intent array.",
            AgentAvatarPoseKind.Interacting =>
                "\nCurrent avatar motion state: Lilith is already interacting with the user. Prefer preserving that interaction and return an empty motion-intent array unless the user explicitly asks for a compatible transition.",
            AgentAvatarPoseKind.Standing when context.IsPajamas =>
                "\nCurrent avatar motion state: Lilith is standing awake in pajamas. The current typed interface does not yet expose lie-down, sleep, or stretch. Return an empty motion-intent array; never substitute sit, walk, window movement, or generic play_one_shot.",
            AgentAvatarPoseKind.Standing =>
                "\nCurrent avatar motion state: Lilith is standing awake in normal clothes. The general motion allowlist may be used, but an empty array is preferred unless a motion clearly improves continuity with the reply.",
            _ =>
                "\nCurrent avatar motion state is uncertain. Return an empty motion-intent array and preserve the visible pose."
        };
    }

    private static AgentMotionEnvelopeResult ParsePlainSuffix(string rawReply)
    {
        var trimmedEnd = rawReply.TrimEnd();
        var start = trimmedEnd.LastIndexOf(PlainStartTag, StringComparison.Ordinal);
        var hasEndTag = trimmedEnd.EndsWith(PlainEndTag, StringComparison.Ordinal);
        if (start < 0 && !hasEndTag)
            return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.Absent);
        if (start < 0 || !hasEndTag)
            return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.Invalid);

        var payloadStart = start + PlainStartTag.Length;
        var payloadLength = trimmedEnd.Length - PlainEndTag.Length - payloadStart;
        if (payloadLength < 0)
            return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.Invalid);
        var json = trimmedEnd.Substring(payloadStart, payloadLength);
        var visibleReply = trimmedEnd.Substring(0, start).TrimEnd();

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("version", out var versionElement)
                || versionElement.ValueKind != JsonValueKind.Number
                || !versionElement.TryGetInt32(out var version))
            {
                return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.Invalid);
            }
            if (version != ProtocolVersion)
                return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.UnsupportedVersion);
            if (!root.TryGetProperty("intents", out var intents))
                return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.Invalid);
            return ParseIntentArray(visibleReply, intents);
        }
        catch (JsonException)
        {
            return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.Invalid);
        }
    }

    private static AgentMotionEnvelopeResult ParseJapaneseJson(string rawReply)
    {
        try
        {
            var json = RemoveOptionalCodeFence(rawReply);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.Invalid);
            if (!root.TryGetProperty(JapanesePropertyName, out var intents))
                return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.Absent);
            return ParseIntentArray(rawReply, intents);
        }
        catch (JsonException)
        {
            return new AgentMotionEnvelopeResult(rawReply, AgentMotionEnvelopeStatus.Invalid);
        }
    }

    private static AgentMotionEnvelopeResult ParseIntentArray(
        string replyPayload,
        JsonElement intents)
    {
        if (intents.ValueKind != JsonValueKind.Array)
            return new AgentMotionEnvelopeResult(replyPayload, AgentMotionEnvelopeStatus.Invalid);

        var suggestions = new List<AgentMotionSuggestion>(MaximumSuggestions);
        var seen = new HashSet<MotionIntentKind>();
        var declared = 0;
        var rejected = 0;
        var truncated = 0;
        foreach (var element in intents.EnumerateArray())
        {
            declared++;
            if (element.ValueKind != JsonValueKind.String
                || !TryMapIntent(element.GetString(), out var kind)
                || !seen.Add(kind))
            {
                rejected++;
                continue;
            }
            if (suggestions.Count >= MaximumSuggestions)
            {
                truncated++;
                continue;
            }
            suggestions.Add(new AgentMotionSuggestion(kind));
        }

        return new AgentMotionEnvelopeResult(
            replyPayload,
            AgentMotionEnvelopeStatus.Parsed,
            suggestions.AsReadOnly(),
            declared,
            rejected,
            truncated);
    }

    private static bool TryMapIntent(string? value, out MotionIntentKind kind)
    {
        kind = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "arrive_at_window" => MotionIntentKind.ArriveAtWindow,
            "walk_left" => MotionIntentKind.WalkLeft,
            "walk_right" => MotionIntentKind.WalkRight,
            "play_one_shot" => MotionIntentKind.PlayOneShot,
            "sit" => MotionIntentKind.Sit,
            "stand" => MotionIntentKind.Stand,
            "peek_from_window" => MotionIntentKind.PeekFromWindow,
            "drop_from_window" => MotionIntentKind.DropFromWindow,
            _ => MotionIntentKind.None
        };
        return kind != MotionIntentKind.None;
    }

    private static string RemoveOptionalCodeFence(string rawReply)
    {
        var json = rawReply.Trim();
        if (!json.StartsWith("```", StringComparison.Ordinal))
            return json;
        var firstNewline = json.IndexOf('\n');
        var lastFence = json.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? json.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim()
            : json;
    }
}

internal readonly struct AgentMotionSuggestionReviewItem
{
    internal MotionIntentKind Kind { get; }
    internal bool Allowed { get; }
    internal MotionIntentReason Reason { get; }

    internal AgentMotionSuggestionReviewItem(
        MotionIntentKind kind,
        bool allowed,
        MotionIntentReason reason)
    {
        Kind = kind;
        Allowed = allowed;
        Reason = reason;
    }
}

internal sealed class AgentMotionSuggestionReview
{
    internal int SuggestedCount { get; }
    internal int ReviewedCount { get; }
    internal int DroppedByTurnLimit { get; }
    internal bool CapabilityAdvertised { get; }
    internal IReadOnlyList<AgentMotionSuggestionReviewItem> Items { get; }

    internal AgentMotionSuggestionReview(
        int suggestedCount,
        int reviewedCount,
        int droppedByTurnLimit,
        bool capabilityAdvertised,
        IReadOnlyList<AgentMotionSuggestionReviewItem> items)
    {
        SuggestedCount = suggestedCount;
        ReviewedCount = reviewedCount;
        DroppedByTurnLimit = droppedByTurnLimit;
        CapabilityAdvertised = capabilityAdvertised;
        Items = items;
    }
}

internal static class AgentMotionSuggestionObserver
{
    internal static AgentMotionSuggestionReview Review(
        IAgentAdapter adapter,
        AgentTurnResponse response,
        MotionPolicySnapshot snapshot)
    {
        if (adapter == null)
            throw new ArgumentNullException(nameof(adapter));
        if (response == null)
            throw new ArgumentNullException(nameof(response));

        var capable = adapter.Capabilities.HasFlag(AgentAdapterCapabilities.MotionSuggestions);
        if (!capable)
        {
            return new AgentMotionSuggestionReview(
                response.MotionSuggestions.Count,
                0,
                0,
                false,
                Array.Empty<AgentMotionSuggestionReviewItem>());
        }

        var reviewed = Math.Min(response.MotionSuggestions.Count, AgentMotionSuggestionRouter.MaximumSuggestionsPerTurn);
        var items = new AgentMotionSuggestionReviewItem[reviewed];
        for (var index = 0; index < reviewed; index++)
        {
            var kind = response.MotionSuggestions[index].Kind;
            var request = MotionIntentRequest.Create(kind, MotionIntentSource.Model);
            var decision = MotionIntentPolicy.Evaluate(request, snapshot);
            items[index] = new AgentMotionSuggestionReviewItem(
                kind,
                decision.Allowed,
                decision.Reason);
        }

        return new AgentMotionSuggestionReview(
            response.MotionSuggestions.Count,
            reviewed,
            response.MotionSuggestions.Count - reviewed,
            true,
            Array.AsReadOnly(items));
    }
}
