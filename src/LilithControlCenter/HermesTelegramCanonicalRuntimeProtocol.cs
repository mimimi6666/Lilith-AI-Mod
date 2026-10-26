using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
    : IHermesTelegramMessagingRuntimeProtocol
{
    private const int MaximumTelegramRows = 256;
    private const int MaximumTelegramTokenUtf8Bytes = 512;
    private static readonly Regex TelegramBotTokenPattern = new(
        @"^\d+:[A-Za-z0-9_-]{30,}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly UTF8Encoding TelegramStrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public D046HermesRuntimeRequestBinding BindTelegramStatus()
        => Bind(TelegramStatusMethod, new { });

    public D046HermesRuntimeRequestBinding BindTelegramTokenReplace(
        string botToken,
        string operationId)
    {
        RequireTelegramBotToken(botToken);
        RequireTelegramIdentity(operationId, nameof(operationId));
        return Bind(
            TelegramReplaceMethod,
            new { bot_token = botToken, operation_id = operationId });
    }

    public D046HermesRuntimeRequestBinding BindTelegramTokenRemove(
        string operationId)
    {
        RequireTelegramIdentity(operationId, nameof(operationId));
        return Bind(TelegramRemoveMethod, new { operation_id = operationId });
    }

    public D046HermesRuntimeRequestBinding BindTelegramPairingList()
        => Bind(TelegramPairingListMethod, new { });

    public D046HermesRuntimeRequestBinding BindTelegramPairingApprove(
        string requestId)
    {
        RequireTelegramIdentity(requestId, nameof(requestId));
        return Bind(TelegramPairingApproveMethod, new { request_id = requestId });
    }

    public D046HermesRuntimeRequestBinding BindTelegramPairingRevoke(string userId)
    {
        RequireTelegramIdentity(userId, nameof(userId));
        return Bind(TelegramPairingRevokeMethod, new { user_id = userId });
    }

    public HermesTelegramStatus DecodeTelegramStatus(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var values = TelegramExactObject(
            RequireTelegramSuccess(response, envelope, binding),
            "configured",
            "gateway_running",
            "state");
        var configured = TelegramBoolean(values, "configured");
        var running = TelegramBoolean(values, "gateway_running");
        var stateText = TelegramString(values, "state", 32, allowEmpty: false);
        var state = stateText switch
        {
            "unconfigured" => HermesTelegramConnectionState.Unconfigured,
            "starting" => HermesTelegramConnectionState.Starting,
            "connected" => HermesTelegramConnectionState.Connected,
            "attention" => HermesTelegramConnectionState.Attention,
            _ => throw new InvalidDataException(
                "Hermes returned an unknown Telegram state.")
        };
        var consistent = (configured, running, state) switch
        {
            (false, false, HermesTelegramConnectionState.Unconfigured) => true,
            (true, _, HermesTelegramConnectionState.Starting) => true,
            (true, true, HermesTelegramConnectionState.Connected) => true,
            (true, _, HermesTelegramConnectionState.Attention) => true,
            _ => false
        };
        if (!consistent)
        {
            throw new InvalidDataException(
                "Hermes returned a contradictory Telegram state.");
        }
        return new HermesTelegramStatus(configured, running, state);
    }

    public HermesTelegramTokenMutationReceipt DecodeTelegramTokenMutation(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var values = TelegramExactObject(
            RequireTelegramSuccess(response, envelope, binding),
            "operation_id",
            "configured",
            "changed");
        var operationId = TelegramString(
            values,
            "operation_id",
            128,
            allowEmpty: false);
        var requestValues = TelegramExactObject(
            binding.HermesPayload,
            binding.Method == TelegramReplaceMethod
                ? new[] { "bot_token", "operation_id" }
                : new[] { "operation_id" });
        var requestedOperationId = TelegramString(
            requestValues,
            "operation_id",
            128,
            allowEmpty: false);
        var configured = TelegramBoolean(values, "configured");
        var expectedConfigured = binding.Method == TelegramReplaceMethod;
        if (!string.Equals(operationId, requestedOperationId, StringComparison.Ordinal)
            || configured != expectedConfigured)
        {
            throw new InvalidDataException(
                "Hermes returned a mismatched Telegram mutation receipt.");
        }
        return new HermesTelegramTokenMutationReceipt(
            operationId,
            configured,
            TelegramBoolean(values, "changed"));
    }

    public HermesTelegramPairingCatalog DecodeTelegramPairing(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var values = TelegramExactObject(
            RequireTelegramSuccess(response, envelope, binding),
            "pending",
            "approved");
        var pendingValue = values["pending"];
        var approvedValue = values["approved"];
        if (pendingValue.ValueKind != JsonValueKind.Array
            || approvedValue.ValueKind != JsonValueKind.Array
            || pendingValue.GetArrayLength() > MaximumTelegramRows
            || approvedValue.GetArrayLength() > MaximumTelegramRows)
        {
            throw new InvalidDataException(
                "Hermes returned an invalid Telegram pairing catalog.");
        }

        var pending = new List<HermesTelegramPendingPairing>();
        foreach (var row in pendingValue.EnumerateArray())
        {
            var item = TelegramExactObject(
                row,
                "request_id",
                "user_id",
                "user_name",
                "age_minutes");
            var age = TelegramNumber(item, "age_minutes");
            if (!double.IsFinite(age) || age < 0 || age > 10080)
            {
                throw new InvalidDataException(
                    "Hermes returned an invalid Telegram pairing age.");
            }
            pending.Add(new HermesTelegramPendingPairing(
                TelegramIdentity(item, "request_id"),
                TelegramIdentity(item, "user_id"),
                TelegramString(item, "user_name", 256, allowEmpty: true),
                age));
        }

        var approved = new List<HermesTelegramApprovedUser>();
        foreach (var row in approvedValue.EnumerateArray())
        {
            var item = TelegramExactObject(row, "user_id");
            approved.Add(new HermesTelegramApprovedUser(
                TelegramIdentity(item, "user_id")));
        }
        return new HermesTelegramPairingCatalog(pending, approved);
    }

    public HermesTelegramPairingApprovalReceipt DecodeTelegramPairingApproval(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var values = TelegramExactObject(
            RequireTelegramSuccess(response, envelope, binding),
            "request_id",
            "user_id",
            "approved");
        var requestId = TelegramIdentity(values, "request_id");
        var requested = TelegramIdentity(
            TelegramExactObject(binding.HermesPayload, "request_id"),
            "request_id");
        if (!string.Equals(requestId, requested, StringComparison.Ordinal)
            || !TelegramBoolean(values, "approved"))
        {
            throw new InvalidDataException(
                "Hermes returned a mismatched Telegram approval receipt.");
        }
        return new HermesTelegramPairingApprovalReceipt(
            requestId,
            TelegramIdentity(values, "user_id"),
            Approved: true);
    }

    public HermesTelegramPairingRevokeReceipt DecodeTelegramPairingRevoke(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var values = TelegramExactObject(
            RequireTelegramSuccess(response, envelope, binding),
            "user_id",
            "revoked");
        var userId = TelegramIdentity(values, "user_id");
        var requested = TelegramIdentity(
            TelegramExactObject(binding.HermesPayload, "user_id"),
            "user_id");
        if (!string.Equals(userId, requested, StringComparison.Ordinal)
            || !TelegramBoolean(values, "revoked"))
        {
            throw new InvalidDataException(
                "Hermes returned a mismatched Telegram revoke receipt.");
        }
        return new HermesTelegramPairingRevokeReceipt(userId, Revoked: true);
    }

    private static JsonElement RequireTelegramSuccess(
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
            throw new InvalidDataException(
                "Telegram response did not match its request.");
        }
        if (!response.Succeeded)
        {
            if (binding.Method is TelegramReplaceMethod or TelegramRemoveMethod
                or TelegramVoiceSetMethod)
            {
                throw new HermesTelegramResultUncertainException(
                    "Runtime did not provide a confirmed Telegram mutation result.");
            }
            throw new HermesTelegramOperationException(
                "Hermes did not complete the Telegram operation.");
        }
        return response.HermesPayload;
    }

    private static Dictionary<string, JsonElement> TelegramExactObject(
        JsonElement value,
        params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Telegram payload was not an object.");
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var decoded = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)
                || !decoded.TryAdd(property.Name, property.Value.Clone()))
            {
                throw new InvalidDataException(
                    "Telegram payload contained an unexpected field.");
            }
        }
        if (decoded.Count != allowed.Count
            || allowed.Any(name => !decoded.ContainsKey(name)))
        {
            throw new InvalidDataException("Telegram payload was incomplete.");
        }
        return decoded;
    }

    private static string TelegramString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumCharacters,
        bool allowEmpty)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || text.Length > maximumCharacters
            || text.Any(char.IsControl)
            || !string.Equals(text, text.Trim(), StringComparison.Ordinal)
            || (!allowEmpty && string.IsNullOrWhiteSpace(text)))
        {
            throw new InvalidDataException("Telegram text was rejected.");
        }
        return text;
    }

    private static string TelegramIdentity(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = TelegramString(values, name, 128, allowEmpty: false);
        try
        {
            RequireTelegramIdentity(value, name);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                "Hermes returned an invalid Telegram identity.",
                exception);
        }
        return value;
    }

    private static bool TelegramBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("Telegram boolean was rejected.");
        }
        return value.GetBoolean();
    }

    private static double TelegramNumber(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number))
        {
            throw new InvalidDataException("Telegram number was rejected.");
        }
        return number;
    }

    private static void RequireTelegramBotToken(string value)
    {
        var match = TelegramBotTokenPattern.Match(value ?? string.Empty);
        if (value is null
            || TelegramStrictUtf8.GetByteCount(value) > MaximumTelegramTokenUtf8Bytes
            || !match.Success
            || match.Index != 0
            || match.Length != value.Length)
        {
            throw new ArgumentException(
                "Telegram Bot Token 的格式無效。",
                nameof(value));
        }
    }

    private static void RequireTelegramIdentity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or ':' or '-')))
        {
            throw new ArgumentException(
                "Telegram operation identity was rejected.",
                parameterName);
        }
    }
}
