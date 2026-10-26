using System.Collections.Immutable;
using System.Text.Json;

namespace LilithRuntime;

internal enum LilithLetterWritingStage { Classify, Draft, Review }
internal enum LilithLetterWritingStatus { Candidate, NoMaterial, Deferred, Rejected, Revoked, Expired, Cancelled, TimedOut, Unavailable, Busy }
internal sealed record LilithLetterTextRequest(LilithLetterWritingStage Stage, string SystemPrompt, string DataJson, int MaximumReplyCharacters)
{
    public override string ToString() => $"Letter text request: {Stage}";
}
internal interface ILilithLetterTextModel
{
    // The future Hermes adapter must preserve system/data separation and use a
    // bounded tool-free completion, not the tool-capable game conversation.
    Task<string> CompleteAsync(LilithLetterTextRequest request, CancellationToken cancellationToken);
}
internal sealed record LilithLetterMeaning(Guid Key, LilithLetterConversationKind Kind, string PlayerQuote);
internal sealed record LilithLetterDraftCandidate(Guid PacketId, string StateFingerprint, string Text,
    ImmutableArray<Guid> MainKeys, ImmutableArray<Guid> BackdropKeys, ImmutableArray<Guid> ToolIds,
    DateTimeOffset ExpiresAtUtc)
{
    internal bool GrantsDelivery => false;
    public override string ToString() => $"Letter draft candidate: {PacketId:N}; grantsDelivery=false";
}
internal sealed record LilithLetterWritingOutcome(LilithLetterWritingStatus Status, int ModelCalls,
    LilithLetterDraftCandidate? Draft, ImmutableArray<LilithLetterMeaning> Meanings, string? DiagnosticCode = null)
{
    public override string ToString() => $"Letter writing: {Status}; calls={ModelCalls}";
}

// Disconnected source pipeline, at most three calls, no retries, store or effects.
// Parsing proves structural/source binding only, not semantic truth or warmth.
internal sealed class LilithLettersWriter
{
    private readonly ILilithLetterTextModel _model;
    private readonly TimeProvider _time;
    private readonly TimeSpan _callTimeout;
    private int _busy;
    private Task<string>? _pendingCompletion;
    internal LilithLettersWriter(ILilithLetterTextModel model, TimeProvider? time = null, TimeSpan? callTimeout = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _time = time ?? TimeProvider.System; _callTimeout = callTimeout ?? TimeSpan.FromSeconds(20);
        if (_callTimeout <= TimeSpan.Zero || _callTimeout > TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(callTimeout));
    }

    internal async Task<LilithLetterWritingOutcome> RunAsync(LilithLettersWritingPacket? packet,
        Func<LilithLettersWritingPacket, bool> isCurrent, CancellationToken cancellationToken, bool classifyOnly = false)
    {
        if (packet is null) return new(LilithLetterWritingStatus.NoMaterial, 0, null, []);
        if (Volatile.Read(ref _pendingCompletion) is { IsCompleted: false })
            return new(LilithLetterWritingStatus.Busy, 0, null, []);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return new(LilithLetterWritingStatus.Busy, 0, null, []);
        var calls = 0;
        var currentStage = "start";
        try
        {
            Check();
            var classify = new
            {
                packet_id = packet.Id.ToString("N"), output_locale = packet.Locale,
                turns = packet.Turns.Select(t => new { key = t.Key.ToString("N"), role = t.Role.ToString(), t.PlayerText, t.LilithText, t.WithinRecentTwelveHours })
            };
            var meanings = ParseMeanings(await Call(LilithLetterWritingStage.Classify, LilithLettersWritingPrompts.Classify, classify), packet);
            if (classifyOnly) return new(LilithLetterWritingStatus.Deferred, calls, null, meanings);
            var meaningful = meanings.Where(m => m.Kind == LilithLetterConversationKind.Conversation).Select(m => m.Key).ToHashSet();
            var turns = packet.Turns.Where(t => meaningful.Contains(t.Key)).ToArray();
            if (!turns.Any(t => t.Role == LilithLetterMaterialRole.Main))
                return new(LilithLetterWritingStatus.Deferred, calls, null, meanings);
            var tools = packet.Tools.Where(t => meaningful.Contains(t.TurnKey)).ToArray();
            var data = new
            {
                packet_id = packet.Id.ToString("N"), output_locale = packet.Locale,
                turns = turns.Select(t => new { key = t.Key.ToString("N"), role = t.Role.ToString(), t.PlayerText, t.LilithText, t.WithinRecentTwelveHours,
                    player_quote = meanings.Single(m => m.Key == t.Key).PlayerQuote }),
                tool_facts = tools.Select(t => new { id = t.Id.ToString("N"), turn_key = t.TurnKey.ToString("N"), data = t.Excerpt })
            };
            var draft = ParseDraft(await Call(LilithLetterWritingStage.Draft, LilithLettersWritingPrompts.Draft, data), packet, meaningful);
            if (draft is null) return new(LilithLetterWritingStatus.Deferred, calls, null, meanings);
            var accepted = ParseReview(await Call(LilithLetterWritingStage.Review, LilithLettersWritingPrompts.Review,
                new
                {
                    packet_id = packet.Id.ToString("N"), output_locale = packet.Locale,
                    cited_turns = data.turns.Where(t => draft.MainKeys.Concat(draft.BackdropKeys).Any(k => k.ToString("N") == t.key)),
                    cited_tool_facts = data.tool_facts.Where(t => draft.ToolIds.Any(id => id.ToString("N") == t.id)),
                    candidate = new { draft.Text, main_keys = draft.MainKeys, backdrop_keys = draft.BackdropKeys, tool_ids = draft.ToolIds }
                }), packet);
            Check();
            return accepted ? new(LilithLetterWritingStatus.Candidate, calls, draft, meanings)
                : new(LilithLetterWritingStatus.Rejected, calls, null, meanings);
        }
        catch (WritingStop stop) { return new(stop.Status, calls, null, [], currentStage + "_" + stop.Status); }
        catch (LilithLettersNativeUnavailableException error)
        { return new(LilithLetterWritingStatus.Unavailable, calls, null, [], currentStage + "_" + error.Code); }
        catch (LilithLettersValidationException error) { return new(LilithLetterWritingStatus.Rejected, calls, null, [], currentStage + "_" + error.Code); }
        catch (JsonException) { return new(LilithLetterWritingStatus.Rejected, calls, null, [], currentStage + "_invalid_json"); }
        catch (OperationCanceledException)
        { return new(cancellationToken.IsCancellationRequested ? LilithLetterWritingStatus.Cancelled : LilithLetterWritingStatus.TimedOut, calls, null, [], currentStage + "_cancelled_or_timeout"); }
        catch (TimeoutException) { return new(LilithLetterWritingStatus.TimedOut, calls, null, [], currentStage + "_timeout"); }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or InvalidOperationException or FormatException or OverflowException)
        { return new(LilithLetterWritingStatus.Rejected, calls, null, [], currentStage + "_shape_invalid"); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { return new(LilithLetterWritingStatus.Unavailable, calls, null, [], currentStage + "_unavailable"); }
        finally { Volatile.Write(ref _busy, 0); }

        void Check()
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = _time.GetUtcNow();
            if (now < packet.CreatedAtUtc || now >= packet.ExpiresAtUtc) throw new WritingStop(LilithLetterWritingStatus.Expired);
            if (!isCurrent(packet)) throw new WritingStop(LilithLetterWritingStatus.Revoked);
        }
        async Task<string> Call(LilithLetterWritingStage stage, string system, object data)
        {
            currentStage = stage.ToString().ToLowerInvariant();
            Check();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_callTimeout);
            calls++;
            var task = _model.CompleteAsync(new(stage, system, JsonSerializer.Serialize(data), 20_000), timeout.Token);
            Volatile.Write(ref _pendingCompletion, task);
            // Observe faults from a non-cooperative provider after our bounded wait.
            _ = task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var result = await task.WaitAsync(timeout.Token).ConfigureAwait(false);
            Check();
            if (result is null || result.Length > 20_000) throw new LilithLettersValidationException("reply_bound");
            return result;
        }
    }

    private static ImmutableArray<LilithLetterMeaning> ParseMeanings(string json, LilithLettersWritingPacket packet)
    {
        using var doc = Parse(json); var root = doc.RootElement;
        Exact(root, "packet_id", "decisions"); Bind(root, packet);
        var decisions = root.GetProperty("decisions");
        if (decisions.ValueKind != JsonValueKind.Array || decisions.GetArrayLength() != packet.Turns.Length)
            throw new LilithLettersValidationException("coverage");
        var result = ImmutableArray.CreateBuilder<LilithLetterMeaning>(); var seen = new HashSet<Guid>();
        foreach (var decision in decisions.EnumerateArray())
        {
            Exact(decision, "key", "kind", "player_quote"); var key = Id(decision.GetProperty("key"));
            var source = packet.Turns.SingleOrDefault(t => t.Key == key);
            if (source is null || !seen.Add(key)) throw new LilithLettersValidationException("source_binding");
            var kind = decision.GetProperty("kind").GetString() switch
            {
                "conversation" => LilithLetterConversationKind.Conversation,
                "tool_only" => LilithLetterConversationKind.ToolOnly,
                "uncertain" => LilithLetterConversationKind.Uncertain,
                _ => throw new LilithLettersValidationException("kind")
            };
            var quote = decision.GetProperty("player_quote").GetString();
            if (quote is null || quote.Length > 240) throw new LilithLettersValidationException("quote_not_verbatim");
            if (kind == LilithLetterConversationKind.Conversation
                ? string.IsNullOrWhiteSpace(quote) || !source.PlayerText.Contains(quote, StringComparison.Ordinal)
                : quote.Length != 0)
            {
                // Reject this piece of evidence, not unrelated valid turns.
                // Never repair a paraphrase into a quote or use it in the draft.
                kind = LilithLetterConversationKind.Uncertain;
                quote = "";
            }
            result.Add(new(key, kind, quote));
        }
        return result.ToImmutable();
    }

    private static LilithLetterDraftCandidate? ParseDraft(string json, LilithLettersWritingPacket packet, HashSet<Guid> meaningful)
    {
        using var doc = Parse(json); var root = doc.RootElement;
        Exact(root, "packet_id", "action", "text", "main_keys", "backdrop_keys", "tool_ids"); Bind(root, packet);
        var main = Ids(root.GetProperty("main_keys"), 3); var backdrop = Ids(root.GetProperty("backdrop_keys"), 1);
        var tools = Ids(root.GetProperty("tool_ids"), 2); var text = root.GetProperty("text").GetString();
        if (root.GetProperty("action").GetString() == "defer")
        {
            if (text != string.Empty || main.Length + backdrop.Length + tools.Length != 0)
                throw new LilithLettersValidationException("defer_shape");
            return null;
        }
        if (root.GetProperty("action").GetString() != "draft" || string.IsNullOrWhiteSpace(text) || text.Length > 600
            || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r')) || main.Length == 0
            || main.Any(k => !meaningful.Contains(k) || !packet.Turns.Any(t => t.Key == k && t.Role == LilithLetterMaterialRole.Main))
            || backdrop.Any(k => !meaningful.Contains(k) || !packet.Turns.Any(t => t.Key == k && t.Role == LilithLetterMaterialRole.Backdrop))
            || tools.Any(id => !packet.Tools.Any(t => t.Id == id && (main.Contains(t.TurnKey) || backdrop.Contains(t.TurnKey)))))
            throw new LilithLettersValidationException("draft_binding");
        return new(packet.Id, packet.StateFingerprint, text, main, backdrop, tools, packet.ExpiresAtUtc);
    }

    private static bool ParseReview(string json, LilithLettersWritingPacket packet)
    {
        using var doc = Parse(json); var root = doc.RootElement;
        var flags = new[] { "warm_specific", "grounded", "non_pressuring", "not_summary", "privacy_safe", "locale_ok" };
        Exact(root, ["packet_id", .. flags]); Bind(root, packet);
        var accepted = true;
        foreach (var flag in flags)
        {
            var value = root.GetProperty(flag);
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new LilithLettersValidationException("review_flag");
            accepted &= value.GetBoolean();
        }
        return accepted;
    }
    private static JsonDocument Parse(string json)
    {
        // Some Chat Completions providers wrap an otherwise exact JSON object
        // in one Markdown fence. Unwrap only that complete outer envelope;
        // never extract a JSON substring from prose or repair its contents.
        var text = json.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var lineEnd = text.IndexOf('\n');
            var header = lineEnd < 0 ? "" : text[..lineEnd].TrimEnd('\r');
            if ((header == "```" || header.Equals("```json", StringComparison.OrdinalIgnoreCase))
                && text.EndsWith("\n```", StringComparison.Ordinal) && text.Length > lineEnd + 4)
                json = text[(lineEnd + 1)..^4].TrimEnd('\r');
        }
        var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        try { Duplicates(doc.RootElement); return doc; } catch { doc.Dispose(); throw; }
    }
    private static void Duplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            { if (!names.Add(property.Name)) throw new LilithLettersValidationException("duplicate_field"); Duplicates(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Duplicates(item);
    }
    private static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Count() != names.Length
            || names.Any(n => !value.TryGetProperty(n, out _))) throw new LilithLettersValidationException("shape");
    }
    private static Guid Id(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || !Guid.TryParseExact(value.GetString(), "N", out var id) || id == Guid.Empty)
            throw new LilithLettersValidationException("identity");
        return id;
    }
    private static ImmutableArray<Guid> Ids(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > maximum) throw new LilithLettersValidationException("citations_bound");
        var ids = value.EnumerateArray().Select(Id).ToImmutableArray();
        if (ids.Distinct().Count() != ids.Length) throw new LilithLettersValidationException("citation_repeated");
        return ids;
    }
    private static void Bind(JsonElement root, LilithLettersWritingPacket packet)
    { if (Id(root.GetProperty("packet_id")) != packet.Id) throw new LilithLettersValidationException("packet_binding"); }
    private sealed class WritingStop(LilithLetterWritingStatus status) : Exception
    { internal LilithLetterWritingStatus Status { get; } = status; }
}
