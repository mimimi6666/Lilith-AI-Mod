using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LilithProviderControlPlane;

/// <summary>
/// The single JSON and fingerprint contract for the D-033 control-plane
/// snapshot.  The ordinary Store keeps its legacy/fallback I/O semantics and
/// maps this neutral contract into its existing typed objects.  A hardened
/// reader can require the current canonical schema without taking a dependency
/// on the Store writer, credential lease, or Provider transport graph.
/// </summary>
internal static class UnifiedProviderControlPlaneCanonicalSnapshotCodec
{
    internal const string CodecSchemaIdentity =
        "D033/CONTROL-PLANE-CANONICAL-SNAPSHOT-CODEC/1";
    internal const string CodecIdentity = CodecSchemaIdentity;
    internal const int LegacySchemaVersion = 1;
    internal const int RouteSchemaVersion = 2;
    internal const int CurrentSchemaVersion = 3;
    internal const int MaximumManifestBytes = 32 * 1024;
    internal const int MaximumPayloadBytes = 2 * 1024 * 1024;
    internal const int MaximumInstances = 32;
    internal const int MaximumModels = 512;
    internal const int MaximumModelIdCharacters = 256;
    internal const int MaximumCapabilitiesPerModel = 32;
    internal static readonly string CodecSchemaFingerprint = Hash(
        CodecSchemaIdentity,
        CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
        MaximumManifestBytes.ToString(CultureInfo.InvariantCulture),
        MaximumPayloadBytes.ToString(CultureInfo.InvariantCulture),
        MaximumInstances.ToString(CultureInfo.InvariantCulture),
        MaximumModels.ToString(CultureInfo.InvariantCulture),
        MaximumModelIdCharacters.ToString(CultureInfo.InvariantCulture),
        MaximumCapabilitiesPerModel.ToString(CultureInfo.InvariantCulture));

    internal static byte[] SerializeManifest(
        UnifiedProviderControlPlaneCanonicalManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Schema != CurrentSchemaVersion)
            throw new InvalidDataException("Only the current Provider schema may be written.");
        ValidateManifest(manifest, requireCurrentSchema: false);
        return JsonSerializer.SerializeToUtf8Bytes(manifest);
    }

    internal static byte[] SerializePayload(
        UnifiedProviderControlPlaneCanonicalPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Schema != CurrentSchemaVersion
            || payload.StoreGeneration <= 0
            || payload.Instances == null)
        {
            throw new InvalidDataException("Only a current Provider payload may be written.");
        }
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    internal static UnifiedProviderControlPlaneCanonicalManifest DecodeManifest(
        byte[] bytes,
        bool requireCurrentSchema)
    {
        ValidateEncodedBytes(bytes, MaximumManifestBytes, "manifest");
        var manifest = JsonSerializer.Deserialize<
                UnifiedProviderControlPlaneCanonicalManifest>(bytes)
            ?? throw new InvalidDataException(
                "The Provider manifest JSON was empty.");
        ValidateManifest(manifest, requireCurrentSchema);
        if (requireCurrentSchema)
            RequireCanonicalEncoding(bytes, JsonSerializer.SerializeToUtf8Bytes(manifest));
        return manifest;
    }

    internal static UnifiedProviderControlPlaneCanonicalPayload DecodePayload(
        byte[] bytes,
        UnifiedProviderControlPlaneCanonicalManifest manifest,
        bool requireCurrentSchema)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateEncodedBytes(bytes, MaximumPayloadBytes, "payload");
        var actualHash = Sha256(bytes);
        if (!string.Equals(
                actualHash,
                manifest.PayloadSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Provider payload digest did not match its manifest.");
        }

        var payload = JsonSerializer.Deserialize<
                UnifiedProviderControlPlaneCanonicalPayload>(bytes)
            ?? throw new InvalidDataException(
                "The Provider payload JSON was empty.");
        if (payload.Schema != manifest.Schema
            || payload.StoreGeneration != manifest.StoreGeneration)
        {
            throw new InvalidDataException(
                "The Provider payload identity did not match its manifest.");
        }

        if (requireCurrentSchema)
        {
            ValidateCurrentPayload(payload);
            RequireCanonicalEncoding(bytes, JsonSerializer.SerializeToUtf8Bytes(payload));
            var snapshotFingerprint = ComputeCurrentSnapshotFingerprint(payload);
            if (!string.Equals(
                    snapshotFingerprint,
                    manifest.SnapshotFingerprint,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The Provider snapshot fingerprint was invalid.");
            }
        }
        return payload;
    }

    internal static string Sha256(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    internal static string Hash(params string[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var bytes = Encoding.UTF8.GetBytes(string.Join("\u001f", values));
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    internal static string ComputeCurrentRouteFingerprint(
        UnifiedProviderControlPlaneCanonicalInstance value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Hash(
            "D033/CONFIGURED-ENDPOINT-ROUTE-INTENT/1",
            value.RouteKind.ToString(CultureInfo.InvariantCulture),
            value.RouteExposure.ToString(CultureInfo.InvariantCulture),
            value.RouteTrust.ToString(CultureInfo.InvariantCulture),
            value.RouteTransportProtection.ToString(CultureInfo.InvariantCulture));
    }

    internal static string ComputeCurrentCapabilityFingerprint(
        IEnumerable<UnifiedProviderControlPlaneCanonicalCapabilityEvidence>
            evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var frozen = evidence
            .OrderBy(value => value.ModelId, StringComparer.Ordinal)
            .ThenBy(value => value.Kind)
            .ToArray();
        return Hash(
            "D033/CATALOG-CAPABILITY-EVIDENCE/1",
            string.Join(
                "\u001e",
                frozen.Select(value => string.Join(
                    "\u001d",
                    value.ModelId,
                    value.Kind.ToString(CultureInfo.InvariantCulture),
                    value.State.ToString(CultureInfo.InvariantCulture),
                    value.Provenance.ToString(CultureInfo.InvariantCulture)))));
    }

    internal static string ComputeCurrentConfigurationFingerprint(
        UnifiedProviderControlPlaneCanonicalInstance value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var evidence = value.CatalogCapabilityEvidence
            ?? Array.Empty<UnifiedProviderControlPlaneCanonicalCapabilityEvidence>();
        var values = new List<string>
        {
            evidence.Length == 0
                ? "D033/CONFIGURED-INSTANCE/2"
                : "D033/CONFIGURED-INSTANCE/3",
            value.InstanceId,
            value.ProviderKind.ToString(CultureInfo.InvariantCulture),
            value.ProfileId,
            value.BaseUri,
            value.EndpointProfileId,
            value.WorkspaceId,
            ComputeCurrentRouteFingerprint(value),
            value.ConfigurationGeneration.ToString(CultureInfo.InvariantCulture),
            value.SelectedModel,
            value.CatalogFingerprint,
            value.CredentialConfigured ? "1" : "0",
            value.CredentialSha256
        };
        if (evidence.Length != 0)
            values.Add(ComputeCurrentCapabilityFingerprint(evidence));
        return Hash(values.ToArray());
    }

    internal static string ComputeCurrentSnapshotFingerprint(
        UnifiedProviderControlPlaneCanonicalPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var instances = payload.Instances
            ?? Array.Empty<UnifiedProviderControlPlaneCanonicalInstance>();
        var frozen = instances
            .OrderBy(value => value.ProviderKind)
            .ThenBy(value => value.InstanceId, StringComparer.Ordinal)
            .ToArray();
        var assignment = payload.TextAssignment;
        return Hash(
            "D033/CONTROL-PLANE-SNAPSHOT/1",
            payload.StoreGeneration.ToString(CultureInfo.InvariantCulture),
            string.Join(
                ",",
                frozen.Select(ComputeCurrentConfigurationFingerprint)),
            assignment == null
                ? string.Empty
                : string.Join(
                    ":",
                    assignment.InstanceId,
                    assignment.ModelId,
                    assignment.ConfigurationFingerprint));
    }

    internal static string ComputeEndpointAuthorityFingerprint(
        UnifiedProviderControlPlaneCanonicalInstance value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Hash(
            "D033/CURRENT-METADATA/ENDPOINT-AUTHORITY/1",
            value.ProviderKind.ToString(CultureInfo.InvariantCulture),
            value.ProfileId,
            value.BaseUri,
            value.EndpointProfileId,
            value.WorkspaceId,
            ComputeCurrentRouteFingerprint(value));
    }

    private static void ValidateManifest(
        UnifiedProviderControlPlaneCanonicalManifest manifest,
        bool requireCurrentSchema)
    {
        if (requireCurrentSchema
            ? manifest.Schema != CurrentSchemaVersion
            : manifest.Schema is not (
                LegacySchemaVersion or RouteSchemaVersion or CurrentSchemaVersion))
        {
            throw new InvalidDataException(
                "The Provider manifest schema was not admitted.");
        }
        if (manifest.StoreGeneration <= 0
            || !IsSha256(manifest.PayloadSha256)
            || !IsSha256(manifest.SnapshotFingerprint)
            || Path.GetFileName(manifest.PayloadFile) != manifest.PayloadFile)
        {
            throw new InvalidDataException(
                "The Provider manifest identity was invalid.");
        }
        if (!requireCurrentSchema)
            return;

        var expectedPayloadFile = "providers-g"
            + manifest.StoreGeneration.ToString(CultureInfo.InvariantCulture)
            + "-"
            + manifest.PayloadSha256[..16].ToLowerInvariant()
            + ".json";
        if (!string.Equals(
                manifest.PayloadFile,
                expectedPayloadFile,
                StringComparison.Ordinal)
            || manifest.CreatedAtUnixMilliseconds <= 0)
        {
            throw new InvalidDataException(
                "The current Provider manifest was not canonical.");
        }
        try
        {
            _ = DateTimeOffset.FromUnixTimeMilliseconds(
                manifest.CreatedAtUnixMilliseconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                "The Provider manifest time was invalid.",
                exception);
        }
    }

    private static void ValidateCurrentPayload(
        UnifiedProviderControlPlaneCanonicalPayload payload)
    {
        if (payload.Schema != CurrentSchemaVersion
            || payload.StoreGeneration <= 0)
        {
            throw new InvalidDataException(
                "The current Provider payload identity was invalid.");
        }
        var instances = payload.Instances
            ?? throw new InvalidDataException(
                "The current Provider instances were missing.");
        if (instances.Length > MaximumInstances
            || instances.Any(value => value == null))
        {
            throw new InvalidDataException(
                "The current Provider instance collection was invalid.");
        }
        var canonicalOrder = instances
            .OrderBy(value => value.ProviderKind)
            .ThenBy(value => value.InstanceId, StringComparer.Ordinal)
            .ToArray();
        if (!instances.SequenceEqual(canonicalOrder)
            || instances.Select(value => value.InstanceId)
                .Distinct(StringComparer.Ordinal).Count() != instances.Length)
        {
            throw new InvalidDataException(
                "The current Provider instance ordering was not canonical.");
        }
        foreach (var instance in instances)
            ValidateCurrentInstance(instance);

        var assignment = payload.TextAssignment;
        if (assignment == null)
            return;
        ValidateIdentifier(assignment.InstanceId, 96, allowEmpty: false);
        ValidateModelId(assignment.ModelId);
        if (assignment.ConfigurationGeneration <= 0
            || !IsSha256(assignment.ConfigurationFingerprint))
        {
            throw new InvalidDataException(
                "The current Provider assignment identity was invalid.");
        }
        var exact = instances.SingleOrDefault(value =>
            string.Equals(
                value.InstanceId,
                assignment.InstanceId,
                StringComparison.Ordinal)
            && string.Equals(
                value.SelectedModel,
                assignment.ModelId,
                StringComparison.Ordinal)
            && value.ConfigurationGeneration
                == assignment.ConfigurationGeneration
            && string.Equals(
                ComputeCurrentConfigurationFingerprint(value),
                assignment.ConfigurationFingerprint,
                StringComparison.Ordinal));
        if (exact == null)
        {
            throw new InvalidDataException(
                "The current Provider assignment was not exact.");
        }
    }

    private static void ValidateCurrentInstance(
        UnifiedProviderControlPlaneCanonicalInstance value)
    {
        ValidateIdentifier(value.InstanceId, 96, allowEmpty: false);
        if (value.ProviderKind is < 1 or > 13)
        {
            throw new InvalidDataException(
                "The current Provider kind was invalid.");
        }
        ValidateIdentifier(value.ProfileId, 64, allowEmpty: false);
        ValidateIdentifier(value.EndpointProfileId, 96, allowEmpty: true);
        ValidateIdentifier(value.WorkspaceId, 128, allowEmpty: true);
        ValidateBaseUri(value.BaseUri);
        if (!CanonicalRoute(
                value.RouteKind,
                value.RouteExposure,
                value.RouteTrust,
                value.RouteTransportProtection)
            || !EndpointIsAdmitted(value)
            || value.ConfigurationGeneration <= 0)
        {
            throw new InvalidDataException(
                "The current Provider route or generation was invalid.");
        }
        ValidateModelId(value.SelectedModel);

        var models = value.CatalogModels
            ?? throw new InvalidDataException(
                "The current Provider catalog was missing.");
        if (models.Length == 0 || models.Length > MaximumModels)
        {
            throw new InvalidDataException(
                "The current Provider catalog size was invalid.");
        }
        foreach (var model in models)
            ValidateModelId(model);
        var canonicalModels = models
            .Distinct(StringComparer.Ordinal)
            .OrderBy(model => model, StringComparer.Ordinal)
            .ToArray();
        if (!models.SequenceEqual(canonicalModels)
            || !models.Contains(value.SelectedModel, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The current Provider catalog was not canonical.");
        }

        var evidence = value.CatalogCapabilityEvidence
            ?? throw new InvalidDataException(
                "The current Provider capability evidence was missing.");
        if (evidence.Any(item => item == null)
            || evidence.Length > checked(models.Length * MaximumCapabilitiesPerModel))
        {
            throw new InvalidDataException(
                "The current Provider capability evidence was invalid.");
        }
        foreach (var item in evidence)
        {
            ValidateModelId(item.ModelId);
            if (!models.Contains(item.ModelId, StringComparer.Ordinal)
                || item.Kind is < 1 or > 13
                || item.State != 2
                || item.Provenance is not (2 or 3))
            {
                throw new InvalidDataException(
                    "The current Provider capability evidence was invalid.");
            }
        }
        var canonicalEvidence = evidence
            .OrderBy(item => item.ModelId, StringComparer.Ordinal)
            .ThenBy(item => item.Kind)
            .ToArray();
        if (!evidence.SequenceEqual(canonicalEvidence)
            || evidence.GroupBy(item => (item.ModelId, item.Kind))
                .Any(group => group.Count() != 1)
            || evidence.GroupBy(item => item.ModelId)
                .Any(group => group.Count() > MaximumCapabilitiesPerModel))
        {
            throw new InvalidDataException(
                "The current Provider capability evidence was not canonical.");
        }

        if (value.CatalogExpiresAtUnixMilliseconds
                <= value.CatalogObservedAtUnixMilliseconds
            || !IsSha256(value.CatalogFingerprint))
        {
            throw new InvalidDataException(
                "The current Provider catalog identity was invalid.");
        }
        try
        {
            var observed = DateTimeOffset.FromUnixTimeMilliseconds(
                value.CatalogObservedAtUnixMilliseconds);
            _ = DateTimeOffset.FromUnixTimeMilliseconds(
                value.CatalogExpiresAtUnixMilliseconds);
            if (observed == default)
            {
                throw new InvalidDataException(
                    "The current Provider catalog observation was default.");
            }
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidDataException(
                "The current Provider catalog time was invalid.",
                exception);
        }

        if (value.CredentialConfigured)
        {
            if (string.IsNullOrWhiteSpace(value.CredentialFile)
                || Path.GetFileName(value.CredentialFile) != value.CredentialFile
                || !value.CredentialFile.EndsWith(
                    ".bin",
                    StringComparison.Ordinal)
                || value.CredentialFile.Length > 180
                || !IsSha256(value.CredentialSha256))
            {
                throw new InvalidDataException(
                    "The current Provider credential reference was invalid.");
            }
        }
        else if (!string.IsNullOrEmpty(value.CredentialFile)
                 || !string.IsNullOrEmpty(value.CredentialSha256))
        {
            throw new InvalidDataException(
                "The current Provider empty credential reference was invalid.");
        }
    }

    private static void ValidateEncodedBytes(
        byte[] bytes,
        int maximumBytes,
        string role)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length <= 0 || bytes.Length > maximumBytes)
        {
            throw new InvalidDataException(
                "The Provider " + role + " was out of bounds.");
        }
    }

    private static void RequireCanonicalEncoding(byte[] actual, byte[] canonical)
    {
        if (!actual.AsSpan().SequenceEqual(canonical))
        {
            throw new InvalidDataException(
                "The current Provider JSON encoding was not canonical.");
        }
    }

    private static void ValidateBaseUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)
            || parsed.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(parsed.Host)
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || value.Contains('?')
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)
            || parsed.Port <= 0
            || parsed.Port > 65535
            || value.Length > 1024
            || !string.Equals(
                parsed.AbsoluteUri.TrimEnd('/') + "/",
                value,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The current Provider endpoint encoding was invalid.");
        }
    }

    private static void ValidateIdentifier(
        string value,
        int maximumCharacters,
        bool allowEmpty)
    {
        if (value == null
            || (allowEmpty
                ? value.Length != 0 && string.IsNullOrWhiteSpace(value)
                : string.IsNullOrWhiteSpace(value))
            || value.Length > maximumCharacters
            || value.Any(character => !(char.IsLetterOrDigit(character)
                || character is '-' or '_' or '.')))
        {
            throw new InvalidDataException(
                "A current Provider identifier was invalid.");
        }
    }

    private static void ValidateModelId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumModelIdCharacters
            || value.Any(char.IsControl)
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A current Provider model identifier was invalid.");
        }
    }

    private static bool EndpointIsAdmitted(
        UnifiedProviderControlPlaneCanonicalInstance value)
    {
        var endpoint = new Uri(value.BaseUri, UriKind.Absolute);
        var managed = value.ProviderKind is >= 1 and <= 7;
        var selfHosted = value.ProviderKind is >= 8 and <= 13;
        if ((value.RouteKind == 1 && !managed)
            || (value.RouteKind != 1 && !selfHosted))
        {
            return false;
        }

        if (value.RouteKind == 1)
        {
            return value.ProviderKind == 4
                ? QwenEndpointIsExact(
                    endpoint,
                    value.EndpointProfileId,
                    value.WorkspaceId)
                : value.EndpointProfileId.Length == 0
                  && value.WorkspaceId.Length == 0
                  && FixedCloudEndpointIsExact(value.ProviderKind, endpoint);
        }

        if (value.EndpointProfileId.Length != 0
            || value.WorkspaceId.Length != 0)
        {
            return false;
        }
        var classification = Classify(endpoint);
        return value.RouteKind switch
        {
            2 => classification.Scope == EndpointScope.Loopback,
            3 => endpoint.Scheme == Uri.UriSchemeHttps
                 && classification.Scope is EndpointScope.PrivateNetwork
                     or EndpointScope.UnresolvedDns,
            4 => endpoint.Scheme == Uri.UriSchemeHttps
                 && classification.Scope is EndpointScope.Public
                     or EndpointScope.UnresolvedDns,
            _ => false
        };
    }

    private static bool FixedCloudEndpointIsExact(int providerKind, Uri endpoint)
    {
        var expected = providerKind switch
        {
            1 => "https://api.deepseek.com/",
            2 => "https://generativelanguage.googleapis.com/",
            3 => "https://api.openai.com/",
            5 => "https://api.anthropic.com/",
            6 => "https://openrouter.ai/api/v1/",
            7 => "https://ollama.com/api/",
            _ => string.Empty
        };
        return expected.Length != 0
               && EquivalentBaseEndpoint(
                   endpoint,
                   new Uri(expected, UriKind.Absolute));
    }

    private static bool QwenEndpointIsExact(
        Uri endpoint,
        string endpointProfileId,
        string workspaceId)
    {
        var definition = endpointProfileId switch
        {
            "cn-beijing-shared" => new QwenEndpointDefinition(
                "https://dashscope.aliyuncs.com/compatible-mode/v1/",
                false),
            "ap-southeast-1-shared" => new QwenEndpointDefinition(
                "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/",
                false),
            "us-shared" => new QwenEndpointDefinition(
                "https://dashscope-us.aliyuncs.com/compatible-mode/v1/",
                false),
            "cn-beijing-workspace" => new QwenEndpointDefinition(
                "https://{workspaceId}.cn-beijing.maas.aliyuncs.com/compatible-mode/v1/",
                true),
            "ap-southeast-1-workspace" => new QwenEndpointDefinition(
                "https://{workspaceId}.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1/",
                true),
            "ap-northeast-1-workspace" => new QwenEndpointDefinition(
                "https://{workspaceId}.ap-northeast-1.maas.aliyuncs.com/compatible-mode/v1/",
                true),
            "eu-central-1-workspace" => new QwenEndpointDefinition(
                "https://{workspaceId}.eu-central-1.maas.aliyuncs.com/compatible-mode/v1/",
                true),
            _ => default
        };
        if (definition.Template == null
            || definition.RequiresWorkspace != (workspaceId.Length != 0)
            || (definition.RequiresWorkspace && !IsDnsLabel(workspaceId)))
        {
            return false;
        }
        var expected = definition.Template.Replace(
            "{workspaceId}",
            workspaceId,
            StringComparison.Ordinal);
        return EquivalentBaseEndpoint(
            endpoint,
            new Uri(expected, UriKind.Absolute));
    }

    private static EndpointClassification Classify(Uri endpoint)
    {
        var host = endpoint.Host.Trim('[', ']');
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return new EndpointClassification(EndpointFamily.DnsName, EndpointScope.Loopback);
        if (IsKnownMetadataHost(host))
            return new EndpointClassification(EndpointFamily.DnsName, EndpointScope.Metadata);
        if (!IPAddress.TryParse(host, out var address))
        {
            return Uri.CheckHostName(host) == UriHostNameType.Dns
                ? new EndpointClassification(
                    EndpointFamily.DnsName,
                    EndpointScope.UnresolvedDns)
                : new EndpointClassification(EndpointFamily.Invalid, EndpointScope.Invalid);
        }
        if (address.IsIPv4MappedToIPv6)
        {
            return new EndpointClassification(
                EndpointFamily.IPv4MappedToIPv6,
                ClassifyIPv4(address.MapToIPv4()));
        }
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return new EndpointClassification(EndpointFamily.IPv4, ClassifyIPv4(address));
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return new EndpointClassification(EndpointFamily.IPv6, ClassifyIPv6(address));
        return new EndpointClassification(EndpointFamily.Invalid, EndpointScope.Invalid);
    }

    private static EndpointScope ClassifyIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.SequenceEqual(new byte[] { 0, 0, 0, 0 }))
            return EndpointScope.Unspecified;
        if (bytes.SequenceEqual(new byte[] { 255, 255, 255, 255 }))
            return EndpointScope.Broadcast;
        if (IPAddress.IsLoopback(address))
            return EndpointScope.Loopback;
        if (bytes.SequenceEqual(new byte[] { 169, 254, 169, 254 })
            || bytes.SequenceEqual(new byte[] { 169, 254, 170, 2 })
            || bytes.SequenceEqual(new byte[] { 100, 100, 100, 200 }))
        {
            return EndpointScope.Metadata;
        }
        if (bytes[0] == 169 && bytes[1] == 254)
            return EndpointScope.LinkLocal;
        if (bytes[0] is >= 224 and <= 239)
            return EndpointScope.Multicast;
        if (bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127))
        {
            return EndpointScope.PrivateNetwork;
        }
        if (bytes[0] == 0
            || bytes[0] >= 240
            || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
            || (bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
            || (bytes[0] == 198 && bytes[1] is 18 or 19)
            || (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
            || (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113))
        {
            return EndpointScope.Reserved;
        }
        return EndpointScope.Public;
    }

    private static EndpointScope ClassifyIPv6(IPAddress address)
    {
        if (address.Equals(IPAddress.IPv6None))
            return EndpointScope.Unspecified;
        if (address.Equals(IPAddress.IPv6Loopback))
            return EndpointScope.Loopback;
        if (address.Equals(IPAddress.Parse("fd00:ec2::254")))
            return EndpointScope.Metadata;
        if (address.IsIPv6LinkLocal)
            return EndpointScope.LinkLocal;
        if (address.IsIPv6Multicast)
            return EndpointScope.Multicast;
        if (address.IsIPv6SiteLocal
            || (address.GetAddressBytes()[0] & 0xFE) == 0xFC)
        {
            return EndpointScope.PrivateNetwork;
        }
        var bytes = address.GetAddressBytes();
        if (bytes[0] == 0x20 && bytes[1] == 0x01
            && bytes[2] == 0x0D && bytes[3] == 0xB8)
        {
            return EndpointScope.Reserved;
        }
        return EndpointScope.Public;
    }

    private static bool EquivalentBaseEndpoint(Uri left, Uri right)
        => string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase)
           && left.Port == right.Port
           && string.Equals(
               left.AbsolutePath.TrimEnd('/') + "/",
               right.AbsolutePath.TrimEnd('/') + "/",
               StringComparison.Ordinal);

    private static bool IsDnsLabel(string value)
        => value.Length is >= 1 and <= 63
           && char.IsLetterOrDigit(value[0])
           && char.IsLetterOrDigit(value[^1])
           && value.All(character => char.IsLetterOrDigit(character)
               || character == '-');

    private static bool IsKnownMetadataHost(string host)
        => string.Equals(
               host,
               "metadata.google.internal",
               StringComparison.OrdinalIgnoreCase)
           || string.Equals(host, "metadata.goog", StringComparison.OrdinalIgnoreCase)
           || string.Equals(
               host,
               "instance-data.ec2.internal",
               StringComparison.OrdinalIgnoreCase);

    private static bool CanonicalRoute(
        int kind,
        int exposure,
        int trust,
        int transportProtection)
        => (kind, exposure, trust, transportProtection) is
            (1, 1, 1, 1)
            or (2, 2, 2, 2)
            or (3, 3, 3, 1)
            or (4, 4, 4, 1);

    private readonly struct QwenEndpointDefinition
    {
        internal string? Template { get; }
        internal bool RequiresWorkspace { get; }

        internal QwenEndpointDefinition(string template, bool requiresWorkspace)
        {
            Template = template;
            RequiresWorkspace = requiresWorkspace;
        }
    }

    private readonly struct EndpointClassification
    {
        internal EndpointFamily Family { get; }
        internal EndpointScope Scope { get; }

        internal EndpointClassification(EndpointFamily family, EndpointScope scope)
        {
            Family = family;
            Scope = scope;
        }
    }

    private enum EndpointFamily
    {
        Invalid = 0,
        DnsName = 1,
        IPv4 = 2,
        IPv6 = 3,
        IPv4MappedToIPv6 = 4
    }

    private enum EndpointScope
    {
        Invalid = 0,
        UnresolvedDns = 1,
        Loopback = 2,
        PrivateNetwork = 3,
        Public = 4,
        LinkLocal = 5,
        Metadata = 6,
        Unspecified = 7,
        Multicast = 8,
        Broadcast = 9,
        Reserved = 10
    }

    internal static bool IsSha256(string? value)
        => value?.Length == 64
           && value.All(character => character is >= '0' and <= '9'
               or >= 'A' and <= 'F');
}

internal sealed class UnifiedProviderControlPlaneCanonicalManifest
{
    public int Schema { get; init; }
    public long StoreGeneration { get; init; }
    public string PayloadFile { get; init; } = string.Empty;
    public string PayloadSha256 { get; init; } = string.Empty;
    public string SnapshotFingerprint { get; init; } = string.Empty;
    public long CreatedAtUnixMilliseconds { get; init; }
}

internal sealed class UnifiedProviderControlPlaneCanonicalPayload
{
    public int Schema { get; init; }
    public long StoreGeneration { get; init; }
    public UnifiedProviderControlPlaneCanonicalInstance[]? Instances { get; init; }
    public UnifiedProviderControlPlaneCanonicalAssignment? TextAssignment { get; init; }
}

internal sealed class UnifiedProviderControlPlaneCanonicalInstance
{
    public string InstanceId { get; init; } = string.Empty;
    public int ProviderKind { get; init; }
    public string ProfileId { get; init; } = string.Empty;
    public string BaseUri { get; init; } = string.Empty;
    public string EndpointProfileId { get; init; } = string.Empty;
    public string WorkspaceId { get; init; } = string.Empty;
    public int RouteKind { get; init; }
    public int RouteExposure { get; init; }
    public int RouteTrust { get; init; }
    public int RouteTransportProtection { get; init; }
    public long ConfigurationGeneration { get; init; }
    public string SelectedModel { get; init; } = string.Empty;
    public string[]? CatalogModels { get; init; }
    public UnifiedProviderControlPlaneCanonicalCapabilityEvidence[]?
        CatalogCapabilityEvidence { get; init; }
    public long CatalogObservedAtUnixMilliseconds { get; init; }
    public long CatalogExpiresAtUnixMilliseconds { get; init; }
    public string CatalogFingerprint { get; init; } = string.Empty;
    public bool CredentialConfigured { get; init; }
    public string CredentialFile { get; init; } = string.Empty;
    public string CredentialSha256 { get; init; } = string.Empty;
}

internal sealed class UnifiedProviderControlPlaneCanonicalCapabilityEvidence
{
    public string ModelId { get; init; } = string.Empty;
    public int Kind { get; init; }
    public int State { get; init; }
    public int Provenance { get; init; }
}

internal sealed class UnifiedProviderControlPlaneCanonicalAssignment
{
    public string InstanceId { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public long ConfigurationGeneration { get; init; }
    public string ConfigurationFingerprint { get; init; } = string.Empty;
}
