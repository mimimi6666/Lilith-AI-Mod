using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LilithPermissionControl;

namespace LilithTextInjector;

/// <summary>
/// Provider-neutral wire contract for the typed C7 workstation executor.  It
/// compiles its capability enum from one frozen manifest and never accepts a
/// permission revision, task identity or owner identity from the model.
/// </summary>
internal static class D045CodexProviderToolContract
{
    internal const string FunctionName = "run_workstation_tool";

    private static readonly HashSet<string> AllowedProperties = new(
        new[]
        {
            "capability", "operation", "target", "secondary_target",
            "payload", "query", "working_directory", "arguments",
            "expected_sha256", "recursive", "maximum_results",
            "maximum_depth", "maximum_bytes", "timeout_ms"
        },
        StringComparer.Ordinal);

    internal static object BuildJsonSchema(D045AgentToolManifest manifest)
    {
        if (manifest is null)
            throw new ArgumentNullException(nameof(manifest));
        var tokens = manifest.Tools
            .Where(value => value.Ordinal >= 21)
            .Select(value => value.ToolToken)
            .ToArray();
        if (tokens.Length == 0)
            throw new InvalidOperationException(
                "No enabled C7 workstation capability is available.");

        return new
        {
            type = "object",
            properties = new
            {
                capability = new
                {
                    type = "string",
                    @enum = tokens,
                    description = "One exact enabled permission leaf."
                },
                operation = new
                {
                    type = "string",
                    @enum = Enum.GetNames(typeof(D045CodexOperation))
                        .Where(value => value != nameof(D045CodexOperation.Default))
                        .Select(value => ToSnakeCase(value))
                        .Prepend("default")
                        .ToArray(),
                    description = "Use default unless the selected capability recipe explicitly names another operation."
                },
                target = new
                {
                    type = "string",
                    description = "Primary exact target. Depending on the selected capability this is an absolute local path, exact PID, exact executable path, owned job id, or public HTTPS URL."
                },
                secondary_target = new
                {
                    type = "string",
                    description = "Second exact path only when the selected recipe says source-to-destination or URL-to-destination; otherwise omit."
                },
                payload = new
                {
                    type = "string",
                    description = "Literal UTF-8 content, clipboard text, or bounded typed action data only when the selected recipe says so; never put a command line here."
                },
                query = new
                {
                    type = "string",
                    description = "Bounded search text or an exact inspected semantic element name only when the selected recipe says so. Semantic UI inspection itself never accepts a query."
                },
                working_directory = new
                {
                    type = "string",
                    description = "Absolute working directory only for process/application execution capabilities."
                },
                arguments = new
                {
                    type = "array",
                    items = new { type = "string" },
                    maxItems = 128,
                    description = "Exact argument vector for process/application execution. Each argument is one array item; never concatenate a shell command."
                },
                expected_sha256 = new
                {
                    type = "string",
                    pattern = "^[A-Fa-f0-9]{64}$",
                    description = "Expected SHA-256 for download integrity or optimistic file modification when the selected recipe supports it."
                },
                recursive = new { type = "boolean" },
                maximum_results = new
                {
                    type = "integer",
                    minimum = 1,
                    maximum = 256
                },
                maximum_depth = new
                {
                    type = "integer",
                    minimum = 0,
                    maximum = 16
                },
                maximum_bytes = new
                {
                    type = "integer",
                    minimum = 1024,
                    maximum = 268435456
                },
                timeout_ms = new
                {
                    type = "integer",
                    minimum = 1000,
                    maximum = 120000,
                    description = "Per-operation timeout. Omit to use the bounded default."
                }
            },
            required = new[] { "capability" },
            additionalProperties = false
        };
    }

    internal static string BuildUsageGuide(D045AgentToolManifest manifest)
    {
        if (manifest is null)
            throw new ArgumentNullException(nameof(manifest));
        return string.Join(" ", manifest.Tools
            .Where(value => value.Ordinal >= 21)
            .Select(value => value.ToolToken + ": "
                + UsageFor(value.Capability)));
    }

    private static string UsageFor(D045PermissionCapability capability)
        => capability switch
        {
            D045PermissionCapability.ClipboardWrite =>
                "operation=default, payload is the exact clipboard text; omit all target fields.",
            D045PermissionCapability.SystemDiagnosticsRead =>
                "operation=default and omit every optional field.",
            D045PermissionCapability.ApplicationInventoryRead =>
                "operation=default and omit every optional field.",
            D045PermissionCapability.ProcessInventoryRead =>
                "operation=default and omit every optional field.",
            D045PermissionCapability.WindowInventoryRead =>
                "operation=default and omit every optional field.",
            D045PermissionCapability.ExternalAppSemanticUiInspect =>
                "operation=default, target is the exact host-bound visible-window token from the verified application launch; omit query, payload, secondary_target, working_directory, expected_sha256, and arguments; optional maximum_depth<=6 and maximum_results<=256.",
            D045PermissionCapability.LocalPathSearch =>
                "operation=default, target is an existing absolute directory root, query is one leaf-name fragment; recursive/maximum_depth/maximum_results bound the search.",
            D045PermissionCapability.LocalContentSearch =>
                "operation=default, target is an existing absolute directory root, query is literal text to find; recursive/maximum_depth/maximum_results/maximum_bytes bound the read.",
            D045PermissionCapability.FileModify =>
                "operation=file_overwrite or file_append, target is one existing absolute file, payload is exact UTF-8 content; expected_sha256 may bind the old file.",
            D045PermissionCapability.LocalContentCopy =>
                "operation=default, target is the existing absolute source and secondary_target is a new absolute destination.",
            D045PermissionCapability.LocalContentMoveRename =>
                "operation=default, target is the existing absolute source and secondary_target is a new same-volume absolute destination.",
            D045PermissionCapability.FolderTreeCreate =>
                "operation=default, target is one new absolute directory path; omit secondary_target and payload.",
            D045PermissionCapability.ArchiveManage =>
                "operation=archive_create uses target=existing source and secondary_target=new .zip; operation=archive_extract uses target=existing .zip and secondary_target=new destination directory. For extraction, maximum_bytes must cover the total uncompressed bytes (up to 268435456), each regular entry may expand to at most 134217728 bytes, and maximum_depth must cover the archive tree.",
            D045PermissionCapability.WindowControl =>
                "operation=window_focus/window_minimize/window_maximize/window_restore and target is one exact PID.",
            D045PermissionCapability.ExternalAppSemanticUiAction =>
                "operation=ui_invoke/ui_focus/ui_toggle/ui_select/ui_scroll, target is the exact host-bound visible-window token, secondary_target is the exact inspected safe role, and query optionally supplies its exact non-redacted safe name.",
            D045PermissionCapability.ExternalAppPointerInput =>
                "operation=pointer_move/pointer_click/pointer_double_click/pointer_drag, target is exact PID and payload is the bounded normalized pointer action data required by that operation.",
            D045PermissionCapability.ApplicationLaunchWithArguments =>
                "operation=default, target is an exact absolute executable, arguments is the argument vector, working_directory is optional absolute directory.",
            D045PermissionCapability.ProcessExecuteNonInteractive =>
                "operation=default, target is an exact absolute executable, arguments is the argument vector, working_directory is optional absolute directory; stdout/stderr and exit code are captured.",
            D045PermissionCapability.ProcessBackgroundManage =>
                "operation=background_start uses target=exact executable plus arguments/working_directory; background_status/background_stop use target=the owned job_id from the start receipt.",
            D045PermissionCapability.WebPublicSearch =>
                "operation=default, query is the public web search text; omit target fields.",
            D045PermissionCapability.BrowserNavigate =>
                "operation=default, target is one public HTTPS URL.",
            D045PermissionCapability.DownloadPublicHttps =>
                "operation=default, target is the public HTTPS source URL, secondary_target is the absolute destination file, expected_sha256 is strongly recommended, and maximum_bytes must cover the expected download size.",
            _ => "Use only the exact fields described by the capability descriptor."
        };

    internal static bool TryParse(
        JsonElement arguments,
        D045AgentToolManifest manifest,
        Guid taskId,
        string ownerId,
        out D045CodexCapabilityRequest? request,
        out string errorCode)
    {
        request = null;
        errorCode = string.Empty;
        if (manifest is null)
            throw new ArgumentNullException(nameof(manifest));
        if (taskId == Guid.Empty)
            throw new ArgumentException("A host task identity is required.", nameof(taskId));
        if (arguments.ValueKind != JsonValueKind.Object)
            return Fail("invalid_object", out errorCode);

        foreach (var property in arguments.EnumerateObject())
        {
            if (!AllowedProperties.Contains(property.Name))
                return Fail("unknown_property", out errorCode);
        }
        if (!TryString(arguments, "capability", out var token)
            || !D045CapabilityDescriptorCatalog.TryResolveExactToolToken(
                token,
                out var capability)
            || (int)capability < 21
            || !manifest.Contains(capability))
        {
            return Fail("capability_unavailable", out errorCode);
        }
        if (!TryOperation(arguments, out var operation))
            return Fail("operation_invalid", out errorCode);

        try
        {
            request = new D045CodexCapabilityRequest(
                taskId,
                capability,
                operation,
                target: OptionalString(arguments, "target"),
                secondaryTarget: OptionalString(arguments, "secondary_target"),
                payload: OptionalString(arguments, "payload"),
                query: OptionalString(arguments, "query"),
                workingDirectory: OptionalString(arguments, "working_directory"),
                expectedSha256: OptionalString(arguments, "expected_sha256"),
                ownerId: ownerId,
                arguments: OptionalStrings(arguments, "arguments"),
                recursive: OptionalBoolean(arguments, "recursive", true),
                maximumResults: OptionalBoundedInt32(
                    arguments,
                    "maximum_results",
                    64,
                    1,
                    256),
                maximumDepth: OptionalBoundedInt32(
                    arguments,
                    "maximum_depth",
                    8,
                    0,
                    16),
                maximumBytes: OptionalBoundedInt64(
                    arguments,
                    "maximum_bytes",
                    64L * 1024 * 1024,
                    1_024,
                    256L * 1024 * 1024),
                timeoutMilliseconds: OptionalBoundedInt32(
                    arguments,
                    "timeout_ms",
                    30_000,
                    1_000,
                    120_000),
                expectedPermissionRevision: manifest.PermissionRevision);
            return true;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            request = null;
            return Fail(
                "request_out_of_range_"
                    + ToSnakeCase(exception.ParamName ?? "field"),
                out errorCode);
        }
        catch (ArgumentException exception)
        {
            request = null;
            return Fail(
                "request_invalid_"
                    + ToSnakeCase(exception.ParamName ?? "field"),
                out errorCode);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or OverflowException)
        {
            request = null;
            return Fail("request_invalid", out errorCode);
        }
    }

    /// <summary>
    /// Semantic inspection is a host-owned read of the exact visible window
    /// established by the verified launch receipt. Providers may choose only
    /// bounded depth/result ceilings; irrelevant generated fields never become
    /// authority and never reach the strict Harness contract.
    /// </summary>
    internal static D045CodexCapabilityRequest
        CanonicalizeHostBoundSemanticUiInspect(
            D045CodexCapabilityRequest parsed,
            string exactHostWindowTarget,
            int timeoutMilliseconds)
    {
        if (parsed is null)
            throw new ArgumentNullException(nameof(parsed));
        if (parsed.Capability
            != D045PermissionCapability.ExternalAppSemanticUiInspect)
        {
            throw new ArgumentException(
                "Only semantic UI inspection has this host canonical shape.",
                nameof(parsed));
        }
        if (string.IsNullOrWhiteSpace(exactHostWindowTarget)
            || !string.Equals(
                parsed.Target,
                exactHostWindowTarget,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The semantic inspection target is not the verified host window.",
                nameof(exactHostWindowTarget));
        }

        return new D045CodexCapabilityRequest(
            parsed.TaskId,
            parsed.Capability,
            D045CodexOperation.Default,
            target: exactHostWindowTarget,
            ownerId: parsed.OwnerId,
            recursive: false,
            maximumResults: parsed.MaximumResults,
            maximumDepth: Math.Min(parsed.MaximumDepth, 6),
            maximumBytes: 1_024,
            timeoutMilliseconds: timeoutMilliseconds,
            expectedPermissionRevision: parsed.ExpectedPermissionRevision);
    }

    private static bool TryOperation(
        JsonElement arguments,
        out D045CodexOperation operation)
    {
        operation = D045CodexOperation.Default;
        if (!arguments.TryGetProperty("operation", out var property))
            return true;
        if (property.ValueKind != JsonValueKind.String)
            return false;
        var value = property.GetString() ?? string.Empty;
        foreach (var candidate in Enum.GetValues<D045CodexOperation>())
        {
            var token = candidate == D045CodexOperation.Default
                ? "default"
                : ToSnakeCase(candidate.ToString());
            if (string.Equals(value, token, StringComparison.Ordinal))
            {
                operation = candidate;
                return true;
            }
        }
        return false;
    }

    private static string ToSnakeCase(string value)
    {
        var characters = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            if (index > 0 && char.IsUpper(value[index]))
                characters.Add('_');
            characters.Add(char.ToLowerInvariant(value[index]));
        }
        return new string(characters.ToArray());
    }

    private static bool TryString(
        JsonElement value,
        string name,
        out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        result = property.GetString() ?? string.Empty;
        return true;
    }

    private static string OptionalString(JsonElement value, string name)
        => !value.TryGetProperty(name, out var property)
            ? string.Empty
            : property.ValueKind == JsonValueKind.String
                ? property.GetString() ?? string.Empty
                : throw new ArgumentException("A string field was invalid.", name);

    private static IReadOnlyList<string> OptionalStrings(
        JsonElement value,
        string name)
    {
        if (!value.TryGetProperty(name, out var property))
            return Array.Empty<string>();
        if (property.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("An array field was invalid.", name);
        var result = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ArgumentException("An array item was invalid.", name);
            result.Add(item.GetString() ?? string.Empty);
        }
        return result.AsReadOnly();
    }

    private static bool OptionalBoolean(
        JsonElement value,
        string name,
        bool fallback)
        => !value.TryGetProperty(name, out var property)
            ? fallback
            : property.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? property.GetBoolean()
                : throw new ArgumentException("A boolean field was invalid.", name);

    private static int OptionalBoundedInt32(
        JsonElement value,
        string name,
        int fallback,
        int minimum,
        int maximum)
        => !value.TryGetProperty(name, out var property)
            ? fallback
            : property.ValueKind == JsonValueKind.Number
                && property.TryGetInt32(out var result)
                    ? Math.Clamp(result, minimum, maximum)
                    : throw new ArgumentException("An integer field was invalid.", name);

    private static long OptionalBoundedInt64(
        JsonElement value,
        string name,
        long fallback,
        long minimum,
        long maximum)
        => !value.TryGetProperty(name, out var property)
            ? fallback
            : property.ValueKind == JsonValueKind.Number
                && property.TryGetInt64(out var result)
                    ? Math.Clamp(result, minimum, maximum)
                    : throw new ArgumentException("An integer field was invalid.", name);

    private static bool Fail(string code, out string errorCode)
    {
        errorCode = code;
        return false;
    }
}
