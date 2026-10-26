using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed partial class D046CanonicalHermesWorkbenchRuntimeProtocol
    : ID057MemoryManagementRuntimeProtocol
{
    public const string MemoryCatalogMethod = "memory.catalog";
    public const string MemoryEntryAddMethod = "memory.entry.add";
    public const string MemoryEntryPinSetMethod = "memory.entry.pin.set";
    public const string MemoryEntryPromoteMethod = "memory.entry.promote";
    public const string MemoryEntryCorrectMethod = "memory.entry.correct";
    public const string MemoryEntryVisibilitySetMethod =
        "memory.entry.visibility.set";
    public const string MemoryEntryRejectMethod = "memory.entry.reject";
    public const string MemoryEntryDeleteMethod = "memory.entry.delete";
    public const string MemoryPreferencesGetMethod = "memory.preferences.get";
    public const string MemoryPreferencesSetMethod = "memory.preferences.set";
    public const string MemoryExportMethod = "memory.export";

    private const int MemorySchemaVersion = 1;
    private const int MaximumExportPathCharacters = 32 * 1024;
    private static readonly UTF8Encoding MemoryStrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public D046HermesRuntimeRequestBinding BindMemoryCatalog(int offset)
    {
        if (offset < 0
            || offset > D057MemoryContractLimits.MaximumManagedEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }
        return Bind(
            MemoryCatalogMethod,
            new
            {
                include_hidden = true,
                limit = D057MemoryContractLimits.MaximumCatalogEntries,
                offset
            });
    }

    public D046HermesRuntimeRequestBinding BindMemoryPreferences()
        => Bind(MemoryPreferencesGetMethod, new { });

    public D046HermesRuntimeRequestBinding BindMemoryAdd(
        D057MemoryAddRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMemoryOperationId(request.OperationId);
        RequireCatalogRevision(request.ExpectedCatalogRevision);
        RequireAddCategory(request.Category);
        RequireMemoryContent(request.Content, nameof(request));
        if (request.Pinned
            && request.Category is not D057MemoryCategory.RelationshipCore)
        {
            throw new ArgumentException(
                "Only relationship-core memories can be pinned when added.",
                nameof(request));
        }

        return Bind(
            MemoryEntryAddMethod,
            new
            {
                operation_id = request.OperationId,
                expected_catalog_revision = request.ExpectedCatalogRevision,
                category = EncodeCategory(request.Category),
                content = request.Content,
                pinned = request.Pinned
            });
    }

    public D046HermesRuntimeRequestBinding BindMemoryPin(
        D057MemoryPinRequest request)
    {
        RequireEntryMutation(
            request?.OperationId,
            request?.ExpectedCatalogRevision ?? -1,
            request?.EntryId,
            request?.ExpectedEntryVersion ?? -1,
            nameof(request));
        return Bind(
            MemoryEntryPinSetMethod,
            new
            {
                operation_id = request!.OperationId,
                expected_catalog_revision = request.ExpectedCatalogRevision,
                entry_id = request.EntryId,
                expected_entry_version = request.ExpectedEntryVersion,
                pinned = request.Pinned
            });
    }

    public D046HermesRuntimeRequestBinding BindMemoryPromote(
        D057MemoryPromoteRequest request)
    {
        RequireEntryMutation(
            request?.OperationId,
            request?.ExpectedCatalogRevision ?? -1,
            request?.EntryId,
            request?.ExpectedEntryVersion ?? -1,
            nameof(request));
        if (request!.TargetCategory is not (
                D057MemoryCategory.RelationshipCore
                or D057MemoryCategory.LongTerm))
        {
            throw new ArgumentException(
                "A memory can only be promoted to relationship core or long term.",
                nameof(request));
        }
        return Bind(
            MemoryEntryPromoteMethod,
            new
            {
                operation_id = request.OperationId,
                expected_catalog_revision = request.ExpectedCatalogRevision,
                entry_id = request.EntryId,
                expected_entry_version = request.ExpectedEntryVersion,
                target_category = EncodeCategory(request.TargetCategory)
            });
    }

    public D046HermesRuntimeRequestBinding BindMemoryCorrect(
        D057MemoryCorrectRequest request)
    {
        RequireEntryMutation(
            request?.OperationId,
            request?.ExpectedCatalogRevision ?? -1,
            request?.EntryId,
            request?.ExpectedEntryVersion ?? -1,
            nameof(request));
        RequireMemoryContent(request!.ReplacementContent, nameof(request));
        return Bind(
            MemoryEntryCorrectMethod,
            new
            {
                operation_id = request.OperationId,
                expected_catalog_revision = request.ExpectedCatalogRevision,
                entry_id = request.EntryId,
                expected_entry_version = request.ExpectedEntryVersion,
                replacement_content = request.ReplacementContent
            });
    }

    public D046HermesRuntimeRequestBinding BindMemoryVisibility(
        D057MemoryVisibilityRequest request)
    {
        RequireEntryMutation(
            request?.OperationId,
            request?.ExpectedCatalogRevision ?? -1,
            request?.EntryId,
            request?.ExpectedEntryVersion ?? -1,
            nameof(request));
        return Bind(
            MemoryEntryVisibilitySetMethod,
            new
            {
                operation_id = request!.OperationId,
                expected_catalog_revision = request.ExpectedCatalogRevision,
                entry_id = request.EntryId,
                expected_entry_version = request.ExpectedEntryVersion,
                hidden = request.Hidden
            });
    }

    public D046HermesRuntimeRequestBinding BindMemoryReject(
        D057MemoryEntryMutationRequest request)
    {
        RequireEntryMutation(
            request?.OperationId,
            request?.ExpectedCatalogRevision ?? -1,
            request?.EntryId,
            request?.ExpectedEntryVersion ?? -1,
            nameof(request));
        return Bind(
            MemoryEntryRejectMethod,
            new
            {
                operation_id = request!.OperationId,
                expected_catalog_revision = request.ExpectedCatalogRevision,
                entry_id = request.EntryId,
                expected_entry_version = request.ExpectedEntryVersion
            });
    }

    public D046HermesRuntimeRequestBinding BindMemoryPermanentDelete(
        D057MemoryPermanentDeleteRequest request)
    {
        RequireEntryMutation(
            request?.OperationId,
            request?.ExpectedCatalogRevision ?? -1,
            request?.EntryId,
            request?.ExpectedEntryVersion ?? -1,
            nameof(request));
        return Bind(
            MemoryEntryDeleteMethod,
            new
            {
                operation_id = request!.OperationId,
                expected_catalog_revision = request.ExpectedCatalogRevision,
                entry_id = request.EntryId,
                expected_entry_version = request.ExpectedEntryVersion,
                permanent = true
            });
    }

    public D046HermesRuntimeRequestBinding BindMemoryPreferencesSet(
        D057MemoryPreferencesSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMemoryOperationId(request.OperationId);
        if (request.ExpectedPreferencesRevision < 0
            || request.ExpectedPreferencesRevision == long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(request));
        return Bind(
            MemoryPreferencesSetMethod,
            new
            {
                operation_id = request.OperationId,
                expected_preferences_revision =
                    request.ExpectedPreferencesRevision,
                auto_memory_enabled = request.AutoMemoryEnabled
            });
    }

    public D046HermesRuntimeRequestBinding BindMemoryExport(
        D057MemoryExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireMemoryOperationId(request.OperationId);
        RequireCatalogRevision(request.ExpectedCatalogRevision);
        return Bind(
            MemoryExportMethod,
            new
            {
                operation_id = request.OperationId,
                expected_catalog_revision = request.ExpectedCatalogRevision,
                format = "json"
            });
    }

    public D057MemoryCatalog DecodeMemoryCatalog(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var payload = RequireMemorySuccess(response, envelope, binding);
        var values = ExactMemoryObject(
            payload,
            "schema_version",
            "catalog_revision",
            "export_supported",
            "entries",
            "next_offset");
        RequireSchemaVersion(values);
        var entriesValue = RequireProperty(values, "entries");
        if (entriesValue.ValueKind != JsonValueKind.Array
            || entriesValue.GetArrayLength()
                > D057MemoryContractLimits.MaximumCatalogEntries)
        {
            throw new InvalidDataException(
                "Hermes 回傳的記憶清單格式無效。");
        }

        var entries = new List<D057MemoryEntry>(entriesValue.GetArrayLength());
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in entriesValue.EnumerateArray())
        {
            var entry = DecodeMemoryEntry(row);
            if (!ids.Add(entry.EntryId))
                throw new InvalidDataException("Hermes 回傳了重複的記憶識別。");
            entries.Add(entry);
        }

        var request = ExactMemoryObject(
            binding.HermesPayload,
            "include_hidden",
            "limit",
            "offset");
        var requestOffset = RequireNonNegativeInt32(request, "offset");
        var nextOffset = RequireNullableNonNegativeInt32(values, "next_offset");
        if (nextOffset is { } continuation
            && (entries.Count == 0
                || continuation != requestOffset + entries.Count
                || continuation > D057MemoryContractLimits.MaximumManagedEntries))
        {
            throw new InvalidDataException(
                "Hermes 回傳的記憶清單分頁位置無效。");
        }

        return new D057MemoryCatalog(
            RequireNonNegativeInt64(values, "catalog_revision"),
            RequireBoolean(values, "export_supported"),
            entries,
            nextOffset);
    }

    public D057MemoryPreferences DecodeMemoryPreferences(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var payload = RequireMemorySuccess(response, envelope, binding);
        var values = ExactMemoryObject(
            payload,
            "schema_version",
            "preferences_revision",
            "auto_memory_enabled");
        RequireSchemaVersion(values);
        var decoded = new D057MemoryPreferences(
            RequireNonNegativeInt64(values, "preferences_revision"),
            RequireBoolean(values, "auto_memory_enabled"));

        if (string.Equals(
                binding.Method,
                MemoryPreferencesSetMethod,
                StringComparison.Ordinal))
        {
            var request = ExactMemoryObject(
                binding.HermesPayload,
                "operation_id",
                "expected_preferences_revision",
                "auto_memory_enabled");
            if (decoded.AutoMemoryEnabled
                != RequireBoolean(request, "auto_memory_enabled"))
            {
                throw new InvalidDataException(
                    "Hermes 回傳的自動記憶設定與請求不相符。");
            }
        }

        return decoded;
    }

    public D057MemoryMutationReceipt DecodeMemoryMutation(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var payload = RequireMemorySuccess(response, envelope, binding);
        var values = ExactMemoryObject(
            payload,
            "schema_version",
            "operation_id",
            "catalog_revision",
            "entry");
        RequireSchemaVersion(values);
        var operationId = RequireText(
            values,
            "operation_id",
            D057MemoryContractLimits.MaximumOperationIdCharacters,
            allowEmpty: false);
        RequireMemoryOperationId(operationId);
        if (!string.Equals(
                operationId,
                RequestOperationId(binding),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes 回傳的記憶操作識別與請求不相符。");
        }

        var catalogRevision = RequireNonNegativeInt64(
            values,
            "catalog_revision");
        var entry = DecodeMemoryEntry(RequireProperty(values, "entry"));
        ValidateMemoryMutationOutcome(
            binding,
            catalogRevision,
            entry);
        return new D057MemoryMutationReceipt(
            operationId,
            catalogRevision,
            entry);
    }

    public D057MemoryPermanentDeleteReceipt DecodeMemoryPermanentDelete(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var payload = RequireMemorySuccess(response, envelope, binding);
        var values = ExactMemoryObject(
            payload,
            "schema_version",
            "operation_id",
            "catalog_revision",
            "entry_id",
            "deleted");
        RequireSchemaVersion(values);
        var operationId = RequireText(
            values,
            "operation_id",
            D057MemoryContractLimits.MaximumOperationIdCharacters,
            allowEmpty: false);
        var entryId = RequireEntryId(values, "entry_id");
        var request = ExactMemoryObject(
            binding.HermesPayload,
            "operation_id",
            "expected_catalog_revision",
            "entry_id",
            "expected_entry_version",
            "permanent");
        if (!string.Equals(
                operationId,
                RequireText(
                    request,
                    "operation_id",
                    D057MemoryContractLimits.MaximumOperationIdCharacters,
                    allowEmpty: false),
                StringComparison.Ordinal)
            || !string.Equals(
                entryId,
                RequireEntryId(request, "entry_id"),
                StringComparison.Ordinal)
            || !RequireBoolean(request, "permanent")
            || !RequireBoolean(values, "deleted"))
        {
            throw new InvalidDataException(
                "Hermes 回傳的永久刪除結果與請求不相符。");
        }

        return new D057MemoryPermanentDeleteReceipt(
            operationId,
            RequireNonNegativeInt64(values, "catalog_revision"),
            entryId,
            Deleted: true);
    }

    public D057MemoryExportReceipt DecodeMemoryExport(
        LilithRuntimeResponse response,
        D046HermesRuntimeEnvelopeContext envelope,
        D046HermesRuntimeRequestBinding binding)
    {
        var payload = RequireMemorySuccess(response, envelope, binding);
        var values = ExactMemoryObject(
            payload,
            "schema_version",
            "operation_id",
            "path",
            "sha256",
            "entry_count",
            "created_at_utc");
        RequireSchemaVersion(values);
        var operationId = RequireText(
            values,
            "operation_id",
            D057MemoryContractLimits.MaximumOperationIdCharacters,
            allowEmpty: false);
        if (!string.Equals(
                operationId,
                RequestOperationId(binding),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Hermes 回傳的記憶匯出識別與請求不相符。");
        }
        var path = RequireText(
            values,
            "path",
            MaximumExportPathCharacters,
            allowEmpty: false);
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Hermes 回傳的記憶匯出路徑無效。");
        var sha256 = RequireText(values, "sha256", 64, allowEmpty: false);
        if (sha256.Length != 64
            || sha256.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Hermes 回傳的記憶匯出雜湊無效。");
        }
        var entryCount = RequireNonNegativeInt32(values, "entry_count");
        if (entryCount > D057MemoryContractLimits.MaximumExportEntries)
            throw new InvalidDataException("Hermes 回傳的記憶匯出筆數無效。");

        return new D057MemoryExportReceipt(
            operationId,
            path,
            sha256.ToUpperInvariant(),
            entryCount,
            RequireTimestamp(values, "created_at_utc"));
    }

    private static D057MemoryEntry DecodeMemoryEntry(JsonElement payload)
    {
        var values = ExactMemoryObject(
            payload,
            "entry_id",
            "entry_version",
            "category",
            "state",
            "content",
            "source",
            "observed_at_utc",
            "updated_at_utc",
            "expires_at_utc",
            "pinned",
            "evidence_excerpt");
        var entryVersion = RequirePositiveInt64(values, "entry_version");
        var observed = RequireTimestamp(values, "observed_at_utc");
        var updated = RequireTimestamp(values, "updated_at_utc");
        if (updated < observed)
            throw new InvalidDataException("Hermes 回傳的記憶時間順序無效。");
        var category = DecodeCategory(RequireToken(values, "category"));
        var state = DecodeState(RequireToken(values, "state"));
        var pinned = RequireBoolean(values, "pinned");

        return new D057MemoryEntry(
            RequireEntryId(values, "entry_id"),
            entryVersion,
            category,
            state,
            RequireUtf8Text(
                values,
                "content",
                D057MemoryContractLimits.MaximumContentUtf8Bytes,
                allowEmpty: false),
            DecodeSource(RequireToken(values, "source")),
            observed,
            updated,
            RequireNullableTimestamp(values, "expires_at_utc"),
            pinned,
            RequireNullableUtf8Text(
                values,
                "evidence_excerpt",
                D057MemoryContractLimits.MaximumEvidenceUtf8Bytes));
    }

    private static void ValidateMemoryMutationOutcome(
        D046HermesRuntimeRequestBinding binding,
        long catalogRevision,
        D057MemoryEntry entry)
    {
        var request = binding.Method switch
        {
            MemoryEntryAddMethod => ExactMemoryObject(
                binding.HermesPayload,
                "operation_id", "expected_catalog_revision", "category",
                "content", "pinned"),
            MemoryEntryPinSetMethod => ExactMemoryObject(
                binding.HermesPayload,
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version", "pinned"),
            MemoryEntryPromoteMethod => ExactMemoryObject(
                binding.HermesPayload,
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version", "target_category"),
            MemoryEntryCorrectMethod => ExactMemoryObject(
                binding.HermesPayload,
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version", "replacement_content"),
            MemoryEntryVisibilitySetMethod => ExactMemoryObject(
                binding.HermesPayload,
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version", "hidden"),
            MemoryEntryRejectMethod => ExactMemoryObject(
                binding.HermesPayload,
                "operation_id", "expected_catalog_revision", "entry_id",
                "expected_entry_version"),
            _ => throw new InvalidDataException(
                "Hermes 回傳了不屬於記憶修改的結果。")
        };
        if (catalogRevision != RevisionSuccessor(
                RequireNonNegativeInt64(request, "expected_catalog_revision")))
        {
            throw new InvalidDataException(
                "Hermes 回傳的記憶目錄版本與請求不相符。");
        }

        if (binding.Method == MemoryEntryAddMethod)
        {
            if (entry.EntryVersion != 1
                || entry.State != D057MemoryState.Active
                || entry.Source != D057MemorySource.Operator
                || entry.Category != DecodeCategory(
                    RequireToken(request, "category"))
                || !string.Equals(
                    entry.Content,
                    RequireUtf8Text(
                        request,
                        "content",
                        D057MemoryContractLimits.MaximumContentUtf8Bytes,
                        allowEmpty: false),
                    StringComparison.Ordinal)
                || entry.Pinned != RequireBoolean(request, "pinned"))
            {
                throw new InvalidDataException(
                    "Hermes 回傳的新增記憶結果與請求不相符。");
            }
            return;
        }

        if (!string.Equals(
                entry.EntryId,
                RequireEntryId(request, "entry_id"),
                StringComparison.Ordinal)
            || entry.EntryVersion != RevisionSuccessor(
                RequirePositiveInt64(request, "expected_entry_version")))
        {
            throw new InvalidDataException(
                "Hermes 回傳的記憶修改結果不屬於所選項目。");
        }

        var outcomeMatches = binding.Method switch
        {
            MemoryEntryPinSetMethod =>
                entry.Pinned == RequireBoolean(request, "pinned"),
            MemoryEntryPromoteMethod =>
                entry.Category == DecodeCategory(
                    RequireToken(request, "target_category"))
                && entry.ExpiresAtUtc is null
                && !entry.Pinned,
            MemoryEntryCorrectMethod => string.Equals(
                entry.Content,
                RequireUtf8Text(
                    request,
                    "replacement_content",
                    D057MemoryContractLimits.MaximumContentUtf8Bytes,
                    allowEmpty: false),
                StringComparison.Ordinal),
            MemoryEntryVisibilitySetMethod =>
                entry.State == (RequireBoolean(request, "hidden")
                    ? D057MemoryState.Hidden
                    : D057MemoryState.Active),
            MemoryEntryRejectMethod =>
                entry.State == D057MemoryState.Rejected && !entry.Pinned,
            _ => false
        };
        if (!outcomeMatches)
        {
            throw new InvalidDataException(
                "Hermes 回傳的記憶修改狀態與請求不相符。");
        }
    }

    private static long RevisionSuccessor(long value)
        => value < long.MaxValue
            ? value + 1
            : throw new InvalidDataException("記憶版本已無法安全遞增。");

    private static JsonElement RequireMemorySuccess(
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
            throw new InvalidDataException("記憶管理回應與請求不相符。");
        }
        if (response.Succeeded)
            return response.HermesPayload;

        var code = ReadFailureCode(response.HermesPayload);
        if (code.Contains("not_allowed", StringComparison.OrdinalIgnoreCase)
            || code.Contains("unsupported", StringComparison.OrdinalIgnoreCase)
            || code.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || code.Contains("not_implemented", StringComparison.OrdinalIgnoreCase)
            || code.Contains("unknown_method", StringComparison.OrdinalIgnoreCase))
        {
            throw new D057MemoryManagementUnavailableException(
                code,
                "目前的 Lilith Runtime 尚未提供記憶管理介面。");
        }

        throw new D057MemoryManagementOperationException(
            code,
            "Runtime 沒有回傳可確認的記憶管理結果；畫面資料未假設成功。");
    }

    private static string ReadFailureCode(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("code", out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } code
            && code.Length <= 128
            && !code.Any(char.IsControl))
        {
            return code;
        }
        return "memory.operation_failed";
    }

    private static Dictionary<string, JsonElement> ExactMemoryObject(
        JsonElement payload,
        params string[] names)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Hermes 回傳的記憶資料格式無效。");
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in payload.EnumerateObject())
        {
            if (!allowed.Contains(property.Name)
                || !result.TryAdd(property.Name, property.Value.Clone()))
            {
                throw new InvalidDataException("Hermes 回傳了未允許的記憶欄位。");
            }
        }
        if (result.Count != allowed.Count
            || allowed.Any(name => !result.ContainsKey(name)))
        {
            throw new InvalidDataException("Hermes 回傳的記憶資料不完整。");
        }
        return result;
    }

    private static JsonElement RequireProperty(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
        => values.TryGetValue(name, out var value)
            ? value
            : throw new InvalidDataException("Hermes 回傳的記憶資料不完整。");

    private static void RequireSchemaVersion(
        IReadOnlyDictionary<string, JsonElement> values)
    {
        if (!values.TryGetValue("schema_version", out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var schemaVersion)
            || schemaVersion != MemorySchemaVersion)
        {
            throw new InvalidDataException("Hermes 記憶管理 schema 不相容。");
        }
    }

    private static string RequestOperationId(
        D046HermesRuntimeRequestBinding binding)
    {
        if (binding.HermesPayload.ValueKind != JsonValueKind.Object
            || !binding.HermesPayload.TryGetProperty(
                "operation_id",
                out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } operationId)
        {
            throw new InvalidDataException("記憶請求缺少操作識別。");
        }
        RequireMemoryOperationId(operationId);
        return operationId;
    }

    private static void RequireEntryMutation(
        string? operationId,
        long expectedCatalogRevision,
        string? entryId,
        long expectedEntryVersion,
        string parameterName)
    {
        if (operationId is null || entryId is null)
            throw new ArgumentNullException(parameterName);
        RequireMemoryOperationId(operationId);
        RequireCatalogRevision(expectedCatalogRevision);
        RequireEntryId(entryId, parameterName);
        if (expectedEntryVersion <= 0
            || expectedEntryVersion == long.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void RequireCatalogRevision(long value)
    {
        if (value < 0 || value == long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static void RequireMemoryOperationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > D057MemoryContractLimits.MaximumOperationIdCharacters
            || !char.IsAsciiLetterOrDigit(value[0])
            || value.Skip(1).Any(static character =>
                !char.IsAsciiLetterOrDigit(character)
                && character is not ('.' or '_' or ':' or '-')))
        {
            throw new ArgumentException("記憶操作識別無效。", nameof(value));
        }
    }

    private static void RequireEntryId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Length > D057MemoryContractLimits.MaximumEntryIdCharacters
            || value.Any(char.IsControl))
        {
            throw new ArgumentException("記憶識別無效。", parameterName);
        }
    }

    private static string RequireEntryId(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = RequireText(
            values,
            name,
            D057MemoryContractLimits.MaximumEntryIdCharacters,
            allowEmpty: false);
        try
        {
            RequireEntryId(value, name);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Hermes 回傳的記憶識別無效。", exception);
        }
        return value;
    }

    private static void RequireMemoryContent(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(static character =>
                char.IsControl(character)
                && character is not ('\r' or '\n' or '\t'))
            || MemoryStrictUtf8.GetByteCount(value)
                > D057MemoryContractLimits.MaximumContentUtf8Bytes)
        {
            throw new ArgumentException("記憶內容格式無效。", parameterName);
        }
    }

    private static void RequireAddCategory(D057MemoryCategory category)
    {
        if (category is not (D057MemoryCategory.RelationshipCore
                or D057MemoryCategory.LongTerm
                or D057MemoryCategory.Temporary))
        {
            throw new ArgumentException(
                "Only core, long-term, or temporary memories can be added manually.",
                nameof(category));
        }
    }

    private static string EncodeCategory(D057MemoryCategory value)
        => value switch
        {
            D057MemoryCategory.RelationshipCore => "relationship_core",
            D057MemoryCategory.LongTerm => "long_term",
            D057MemoryCategory.Candidate => "candidate",
            D057MemoryCategory.Temporary => "temporary",
            D057MemoryCategory.RelationshipEpisode => "relationship_episode",
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };

    private static D057MemoryCategory DecodeCategory(string value)
        => value switch
        {
            "relationship_core" => D057MemoryCategory.RelationshipCore,
            "long_term" => D057MemoryCategory.LongTerm,
            "candidate" => D057MemoryCategory.Candidate,
            "temporary" => D057MemoryCategory.Temporary,
            "relationship_episode" => D057MemoryCategory.RelationshipEpisode,
            _ => throw new InvalidDataException("Hermes 回傳了未知的記憶分類。")
        };

    private static D057MemoryState DecodeState(string value)
        => value switch
        {
            "active" => D057MemoryState.Active,
            "hidden" => D057MemoryState.Hidden,
            "rejected" => D057MemoryState.Rejected,
            "expired" => D057MemoryState.Expired,
            "superseded" => D057MemoryState.Superseded,
            _ => throw new InvalidDataException("Hermes 回傳了未知的記憶狀態。")
        };

    private static D057MemorySource DecodeSource(string value)
        => value switch
        {
            "explicit_user" => D057MemorySource.ExplicitUser,
            "inferred" => D057MemorySource.Inferred,
            "operator" => D057MemorySource.Operator,
            "relationship_review" => D057MemorySource.RelationshipReview,
            _ => throw new InvalidDataException("Hermes 回傳了未知的記憶來源。")
        };

    private static string RequireToken(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
        => RequireText(values, name, 64, allowEmpty: false);

    private static string RequireText(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumCharacters,
        bool allowEmpty)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || (!allowEmpty && string.IsNullOrWhiteSpace(text))
            || !string.Equals(text, text.Trim(), StringComparison.Ordinal)
            || text.Length > maximumCharacters
            || text.Any(char.IsControl))
        {
            throw new InvalidDataException("Hermes 回傳的記憶文字格式無效。");
        }
        return text;
    }

    private static string RequireUtf8Text(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumBytes,
        bool allowEmpty)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text
            || (!allowEmpty && string.IsNullOrWhiteSpace(text))
            || !string.Equals(text, text.Trim(), StringComparison.Ordinal)
            || text.Any(static character =>
                char.IsControl(character)
                && character is not ('\r' or '\n' or '\t'))
            || MemoryStrictUtf8.GetByteCount(text) > maximumBytes)
        {
            throw new InvalidDataException("Hermes 回傳的記憶正文格式無效。");
        }
        return text;
    }

    private static string? RequireNullableUtf8Text(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        int maximumBytes)
    {
        if (!values.TryGetValue(name, out var value))
            throw new InvalidDataException("Hermes 回傳的記憶資料不完整。");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        return RequireUtf8Text(values, name, maximumBytes, allowEmpty: false);
    }

    private static bool RequireBoolean(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException("Hermes 回傳的記憶布林值格式無效。");
        }
        return value.GetBoolean();
    }

    private static long RequireNonNegativeInt64(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var number)
            || number < 0)
        {
            throw new InvalidDataException("Hermes 回傳的記憶版本格式無效。");
        }
        return number;
    }

    private static long RequirePositiveInt64(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var number = RequireNonNegativeInt64(values, name);
        if (number == 0)
            throw new InvalidDataException("Hermes 回傳的記憶版本格式無效。");
        return number;
    }

    private static int RequireNonNegativeInt32(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var number)
            || number < 0)
        {
            throw new InvalidDataException("Hermes 回傳的記憶數量格式無效。");
        }
        return number;
    }

    private static int? RequireNullableNonNegativeInt32(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value))
            throw new InvalidDataException("Hermes 回傳的記憶資料不完整。");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        return RequireNonNegativeInt32(values, name);
    }

    private static DateTimeOffset RequireTimestamp(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var text = RequireText(values, name, 64, allowEmpty: false);
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal
                    | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new InvalidDataException("Hermes 回傳的記憶時間格式無效。");
        }
        return parsed.ToUniversalTime();
    }

    private static DateTimeOffset? RequireNullableTimestamp(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        if (!values.TryGetValue(name, out var value))
            throw new InvalidDataException("Hermes 回傳的記憶資料不完整。");
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        return RequireTimestamp(values, name);
    }
}
