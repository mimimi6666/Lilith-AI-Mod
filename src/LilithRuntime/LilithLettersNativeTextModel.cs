using System.Text.Json;

namespace LilithRuntime;

// Source-only adapter. The future host factory must hold the game turn gate and
// model-authority lease for the entire writer run. Never pass the public UI router.
internal sealed class LilithLettersNativeTextModel : ILilithLetterTextModel
{
    internal const string Method = "prompt.letters_text";
    private readonly LilithLettersWritingPacket _packet;
    private readonly string _sessionId, _storedSessionId, _lineageRoot;
    private readonly Func<string, object, CancellationToken, Task<JsonElement>> _request;
    private readonly Func<bool> _isCurrent;
    private readonly TimeProvider _time;
    private int _nextStage, _busy;
    private bool _failed;

    internal LilithLettersNativeTextModel(LilithLettersWritingPacket packet,
        string sessionId, string storedSessionId, string lineageRoot,
        Func<string, object, CancellationToken, Task<JsonElement>> scopedRequest,
        Func<bool> isCurrent, TimeProvider? time = null)
    {
        _packet = packet ?? throw new ArgumentNullException(nameof(packet));
        _sessionId = Identity(sessionId); _storedSessionId = Identity(storedSessionId); _lineageRoot = Identity(lineageRoot);
        _request = scopedRequest ?? throw new ArgumentNullException(nameof(scopedRequest));
        _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        _time = time ?? TimeProvider.System;
    }

    public async Task<string> CompleteAsync(LilithLetterTextRequest request, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            throw new InvalidOperationException("Letter text adapter busy.");
        try
        {
            Check();
            var stage = request.Stage switch
            {
                LilithLetterWritingStage.Classify => (0, "classify", LilithLettersWritingPrompts.Classify),
                LilithLetterWritingStage.Draft => (1, "draft", LilithLettersWritingPrompts.Draft),
                LilithLetterWritingStage.Review => (2, "review", LilithLettersWritingPrompts.Review),
                _ => throw new InvalidDataException("Letter text stage rejected.")
            };
            if (stage.Item1 != _nextStage || request.SystemPrompt != stage.Item3
                || request.MaximumReplyCharacters is < 1 or > 20_000
                || request.DataJson is null || request.DataJson.Length > 200_000)
                throw new InvalidDataException("Letter text request rejected.");
            using (var data = JsonDocument.Parse(request.DataJson, new JsonDocumentOptions { MaxDepth = 12 }))
            {
                ExactUnique(data.RootElement);
                if (data.RootElement.GetProperty("packet_id").GetString() != _packet.Id.ToString("N"))
                    throw new InvalidDataException("Letter text packet mismatch.");
            }
            var operation = Guid.NewGuid().ToString("N");
            // A failed/cancelled attempt burns this adapter; no retry with new IDs.
            _failed = true;
            JsonElement response;
            try
            {
                response = await _request(Method, new
                {
                    session_id = _sessionId, expected_stored_session_id = _storedSessionId,
                    expected_lineage_root = _lineageRoot, operation_id = operation,
                    packet_id = _packet.Id.ToString("N"), stage = stage.Item2,
                    system_prompt = request.SystemPrompt, data_json = request.DataJson,
                    maximum_reply_characters = request.MaximumReplyCharacters
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) when (error is not OutOfMemoryException)
            { throw new LilithLettersNativeUnavailableException("native_transport_unavailable"); }
            Check(allowBurned: true);
            ExactUnique(response);
            var fields = new[] { "operation_id", "packet_id", "stage", "status", "text" };
            if (response.ValueKind != JsonValueKind.Object || response.EnumerateObject().Count() != fields.Length
                || fields.Any(f => !response.TryGetProperty(f, out _))
                || response.GetProperty("operation_id").GetString() != operation
                || response.GetProperty("packet_id").GetString() != _packet.Id.ToString("N")
                || response.GetProperty("stage").GetString() != stage.Item2)
                throw new InvalidDataException("Letter native response rejected.");
            var text = response.GetProperty("text").GetString();
            var status = response.GetProperty("status").GetString();
            if (status != "ready")
            {
                // Closed metadata, not provider prose or model-authored reasons.
                // A failed native operation is not evidence of no usable material.
                if (text != "") throw new InvalidDataException("Letter native response rejected.");
                var code = status switch
                {
                    "failed" => "native_unavailable", "busy" => "native_busy",
                    "unavailable_authority" => "native_authority_unavailable",
                    "unavailable_material" => "native_material_unavailable",
                    "unavailable_route" => "native_route_unavailable",
                    "unavailable_identity" => "native_identity_unavailable",
                    "unavailable_origin" => "native_origin_unavailable",
                    "unavailable_provider" => "native_provider_unavailable",
                    "unavailable_worker" => "native_worker_unavailable",
                    _ => null
                };
                if (code is null) throw new InvalidDataException("Letter native response rejected.");
                throw new LilithLettersNativeUnavailableException(code);
            }
            if (string.IsNullOrWhiteSpace(text) || text.Length > request.MaximumReplyCharacters)
                throw new InvalidDataException("Letter native response bound exceeded.");
            _nextStage++; _failed = false;
            return text;
        }
        catch (OperationCanceledException) { _failed = true; throw; }
        catch (LilithLettersNativeUnavailableException) { _failed = true; throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        { _failed = true; throw new InvalidDataException("Letter native text unavailable."); }
        finally { Volatile.Write(ref _busy, 0); }

        void Check(bool allowBurned = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = _time.GetUtcNow();
            if ((_failed && !allowBurned) || now < _packet.CreatedAtUtc || now >= _packet.ExpiresAtUtc || !_isCurrent())
                throw new InvalidDataException("Letter text authority expired.");
        }
    }

    private static string Identity(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256
        && !value.Any(char.IsControl) ? value : throw new ArgumentException("Letter session identity rejected.");
    private static void ExactUnique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in value.EnumerateObject())
            { if (!names.Add(p.Name)) throw new InvalidDataException("Duplicate letter field."); ExactUnique(p.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) ExactUnique(child);
    }
}

/// <summary>Finite local native-service diagnostic; never carries provider text.</summary>
internal sealed class LilithLettersNativeUnavailableException : Exception
{
    public string Code { get; }
    internal LilithLettersNativeUnavailableException(string code)
        : base("Letter native text unavailable.") => Code = code;
}
