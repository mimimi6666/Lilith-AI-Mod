using System.IO;
using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
    : ID054HermesCredentialRuntimeProtocol
{
    private const int MaximumCredentialProviders = 256;
    private const int MaximumCredentialUtf8Bytes = 16 * 1024;
    private static readonly UTF8Encoding CredentialStrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public D046HermesRuntimeRequestBinding BindCredentialStatus()
        => Bind(CredentialStatusMethod, new { });

    public D046HermesRuntimeRequestBinding BindCredentialReplace(
        string providerId,
        string apiKey,
        string operationId)
    {
        RequireCredentialProviderId(providerId);
        RequireCredentialOperationId(operationId);
        if (string.IsNullOrWhiteSpace(apiKey)
            || !string.Equals(apiKey, apiKey.Trim(), StringComparison.Ordinal)
            || apiKey.Any(char.IsControl)
            || CredentialStrictUtf8.GetByteCount(apiKey) > MaximumCredentialUtf8Bytes)
        {
            throw new ArgumentException(
                "API Key 的格式無效。",
                nameof(apiKey));
        }
        return Bind(
            CredentialReplaceMethod,
            new
            {
                slug = providerId,
                api_key = apiKey,
                operation_id = operationId
            });
    }

    public D046HermesRuntimeRequestBinding BindCredentialRemove(
        string providerId,
        string operationId)
    {
        RequireCredentialProviderId(providerId);
        RequireCredentialOperationId(operationId);
        return Bind(
            CredentialRemoveMethod,
            new
            {
                slug = providerId,
                operation_id = operationId
            });
    }

    public D054HermesCredentialCatalog DecodeCredentialStatus(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var payload = RequireCredentialSuccess(response, envelope, binding);
        var root = CredentialExactObject(payload, "providers");
        var providers = root["providers"];
        if (providers.ValueKind != JsonValueKind.Array
            || providers.GetArrayLength() > MaximumCredentialProviders)
        {
            throw new InvalidDataException(
                "Hermes 回傳的 API Key 清單格式無效。");
        }

        var decoded = new List<D054HermesCredentialProvider>(
            providers.GetArrayLength());
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in providers.EnumerateArray())
        {
            var region = HermesCredentialRegionMetadata.Parse(row);
            var values = CredentialExactObject(row, HermesCredentialRegionMetadata.RowKeys(row));
            var slug = CredentialString(values, "slug", 256);
            RequireCredentialResponseProviderId(slug);
            if (!slugs.Add(slug))
                throw new InvalidDataException("Hermes 回傳了重複的 API Key 平台。");
            decoded.Add(new D054HermesCredentialProvider(
                slug,
                CredentialString(values, "name", 256),
                CredentialBoolean(values, "configured"),
                CredentialBoolean(values, "legacy_sources_present"), region));
        }
        return new D054HermesCredentialCatalog(decoded);
    }

    public D054HermesCredentialMutationReceipt DecodeCredentialMutation(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var payload = RequireCredentialSuccess(response, envelope, binding);
        var values = CredentialExactObject(
            payload,
            "slug",
            "name",
            "configured",
            "changed",
            "legacy_sources_cleared");
        var slug = CredentialString(values, "slug", 256);
        RequireCredentialResponseProviderId(slug);
        var requestedSlug = CredentialMutationRequestSlug(binding);
        if (!string.Equals(slug, requestedSlug, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes 回傳的 API Key 平台與原始請求不相符。");
        }
        var configured = CredentialBoolean(values, "configured");
        var expectedConfigured = string.Equals(
            binding.Method,
            CredentialReplaceMethod,
            StringComparison.Ordinal);
        if (configured != expectedConfigured)
        {
            throw new InvalidDataException(
                "Hermes 回傳了互相矛盾的 API Key 結果。");
        }
        return new D054HermesCredentialMutationReceipt(
            slug,
            CredentialString(values, "name", 256),
            configured,
            CredentialBoolean(values, "changed"),
            CredentialBoolean(values, "legacy_sources_cleared"));
    }

    private static string CredentialMutationRequestSlug(
        D046HermesRuntimeRequestBinding binding)
    {
        string[] expectedProperties = binding.Method switch
        {
            CredentialReplaceMethod => new[]
            {
                "slug", "api_key", "operation_id"
            },
            CredentialRemoveMethod => new[] { "slug", "operation_id" },
            _ => throw new InvalidDataException(
                "API Key 回應不是由變更請求所產生。")
        };
        var request = CredentialExactObject(
            binding.HermesPayload,
            expectedProperties);
        var slug = CredentialString(request, "slug", 256);
        RequireCredentialProviderId(slug);
        return slug;
    }

    private static JsonElement RequireCredentialSuccess(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(binding);
        if (envelope.GenerationId == Guid.Empty
            || !string.Equals(response.Method, binding.Method, StringComparison.Ordinal))
        {
            throw new InvalidDataException("API Key 回應與請求不相符。");
        }
        if (!response.Succeeded)
        {
            var code = "credential.operation_failed";
            if (response.HermesPayload.ValueKind == JsonValueKind.Object)
            {
                if (response.HermesPayload.TryGetProperty("code", out var codeValue)
                    && codeValue.ValueKind == JsonValueKind.String
                    && codeValue.GetString() is { Length: > 0 } decodedCode
                    && decodedCode.Length <= 128
                    && !decodedCode.Any(char.IsControl))
                {
                    code = decodedCode;
                }
            }
            if (string.Equals(
                    code,
                    "hermes.credential_rejected",
                    StringComparison.Ordinal))
            {
                throw new D054HermesCredentialOperationException(
                    code,
                    "Hermes 明確拒絕 API Key 操作；金鑰內容沒有保留在錯誤訊息中。");
            }
            throw new D054HermesCredentialResultUncertainException(
                code,
                "Runtime 未提供可確認的 API Key 操作結果；金鑰內容沒有保留在錯誤訊息中。");
        }
        return response.HermesPayload;
    }

    private static Dictionary<string, JsonElement> CredentialExactObject(
        JsonElement payload,
        params string[] names)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes 回傳的 API Key 資料格式無效。");
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in payload.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)
                || !values.TryAdd(property.Name, property.Value.Clone()))
            {
                throw new InvalidDataException("Hermes 回傳了未允許的 API Key 欄位。");
            }
        }
        if (values.Count != allowed.Count || allowed.Any(name => !values.ContainsKey(name)))
            throw new InvalidDataException("Hermes 回傳的 API Key 資料不完整。");
        return values;
    }

    private static string CredentialString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumCharacters)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || string.IsNullOrWhiteSpace(text)
            || !string.Equals(text, text.Trim(), StringComparison.Ordinal)
            || text.Length > maximumCharacters
            || text.Any(char.IsControl))
        {
            throw new InvalidDataException("Hermes 回傳的 API Key 文字格式無效。");
        }
        return text;
    }

    private static bool CredentialBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("Hermes 回傳的 API Key 狀態格式無效。");
        }
        return value.GetBoolean();
    }

    private static void RequireCredentialProviderId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || !CredentialLowerAscii(value[0])
            || value.Skip(1).Any(static character =>
                !CredentialLowerAscii(character)
                && character is not ('-' or '_')))
        {
            throw new ArgumentException("Hermes 平台識別無效。", nameof(value));
        }
    }

    private static void RequireCredentialResponseProviderId(string value)
    {
        try
        {
            RequireCredentialProviderId(value);
        }
        catch (ArgumentException exception)
        {
            // A provider identity decoded from a Runtime response is remote
            // data, not a caller binding error. Keep those failure classes
            // distinct so a dispatched mutation always retires its Runtime
            // generation even when the receipt identity is malformed.
            throw new InvalidDataException(
                "Hermes 回傳的 API Key 平台識別無效。",
                exception);
        }
    }

    private static void RequireCredentialOperationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || !char.IsAsciiLetterOrDigit(value[0])
            || value.Skip(1).Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or ':' or '-')))
        {
            throw new ArgumentException("API Key 操作識別無效。", nameof(value));
        }
    }

    private static bool CredentialLowerAscii(char value)
        => value is >= 'a' and <= 'z' or >= '0' and <= '9';
}
