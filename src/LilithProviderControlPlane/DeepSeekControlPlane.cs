using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LilithProviderControlPlane;

internal enum DeepSeekControlAdmission
{
    Invalid = 0,
    Accepted = 1,
    Missing = 2,
    AuthenticationRejected = 3,
    InvalidCredential = 4,
    InvalidModel = 5,
    ModelNotCatalogued = 6,
    HttpStatusRejected = 7,
    RedirectRejected = 8,
    ResponseRejected = 9,
    ResourceLimitExceeded = 10,
    NetworkFailure = 11,
    Timeout = 12,
    Cancelled = 13,
    UnsupportedPlatform = 14,
    ProtectionFailed = 15,
    StorageFailure = 16,
    Corrupt = 17
}

internal sealed class DeepSeekCatalogResult
{
    internal DeepSeekControlAdmission Admission { get; }
    internal IReadOnlyList<string> Models { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal int NetworkCalls { get; }

    internal bool Accepted => Admission == DeepSeekControlAdmission.Accepted;

    internal DeepSeekCatalogResult(
        DeepSeekControlAdmission admission,
        IEnumerable<string>? models,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        int networkCalls)
    {
        if (!Enum.IsDefined(typeof(DeepSeekControlAdmission), admission)
            || admission == DeepSeekControlAdmission.Invalid)
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if (expiresAt <= observedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        if (networkCalls is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(networkCalls));
        var frozen = (models ?? Array.Empty<string>()).ToArray();
        if ((admission == DeepSeekControlAdmission.Accepted) != (frozen.Length > 0))
            throw new ArgumentException("Accepted catalog results require models.", nameof(models));
        Models = Array.AsReadOnly(frozen);
        Admission = admission;
        ObservedAt = observedAt;
        ExpiresAt = expiresAt;
        NetworkCalls = networkCalls;
    }
}

internal static class DeepSeekControlPlanePolicy
{
    internal const string ProviderId = "deepseek";
    internal const string BaseUri = "https://api.deepseek.com/";
    internal const string CatalogPath = "models";
    internal const string ChatCompletionsPath = "chat/completions";
    internal const string PreferredModel = "deepseek-v4-flash";
    internal const int MaximumCredentialCharacters = 8192;
    internal const int MaximumModels = 128;
    internal const int MaximumResponseBytes = 64 * 1024;
    internal static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(8);
    internal static TimeSpan CatalogFreshness { get; } = TimeSpan.FromHours(6);

    internal static bool IsAdmittedModel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            return false;
        if (!value.StartsWith("deepseek-", StringComparison.Ordinal))
            return false;
        return value.All(character =>
            char.IsLetterOrDigit(character)
            || character is '-' or '_' or '.');
    }
}

internal static class DeepSeekControlPlaneHttp
{
    internal static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            MaxConnectionsPerServer = 1,
            CheckCertificateRevocationList = true
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }
}

internal sealed class DeepSeekCatalogClient
{
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;

    internal DeepSeekCatalogClient(
        HttpClient http,
        Func<DateTimeOffset>? clock = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    internal async Task<DeepSeekCatalogResult> DiscoverOnceAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        var observedAt = _clock();
        var expiresAt = observedAt + DeepSeekControlPlanePolicy.CatalogFreshness;
        if (string.IsNullOrWhiteSpace(apiKey)
            || apiKey.Length > DeepSeekControlPlanePolicy.MaximumCredentialCharacters)
        {
            return Rejected(
                DeepSeekControlAdmission.InvalidCredential,
                observedAt,
                expiresAt,
                networkCalls: 0);
        }

        var catalogUri = new Uri(
            new Uri(DeepSeekControlPlanePolicy.BaseUri, UriKind.Absolute),
            DeepSeekControlPlanePolicy.CatalogPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, catalogUri);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            apiKey.Trim());
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        using var timeout = new CancellationTokenSource(
            DeepSeekControlPlanePolicy.RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        try
        {
            using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    linked.Token)
                .ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri == null
                || !string.Equals(
                    response.RequestMessage.RequestUri.AbsoluteUri,
                    catalogUri.AbsoluteUri,
                    StringComparison.Ordinal)
                || (int)response.StatusCode is >= 300 and < 400)
            {
                return Rejected(
                    DeepSeekControlAdmission.RedirectRejected,
                    observedAt,
                    expiresAt,
                    networkCalls: 1);
            }
            if (response.StatusCode is HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden)
            {
                return Rejected(
                    DeepSeekControlAdmission.AuthenticationRejected,
                    observedAt,
                    expiresAt,
                    networkCalls: 1);
            }
            if (!response.IsSuccessStatusCode)
            {
                return Rejected(
                    DeepSeekControlAdmission.HttpStatusRejected,
                    observedAt,
                    expiresAt,
                    networkCalls: 1);
            }
            if (response.Content.Headers.ContentLength is >
                DeepSeekControlPlanePolicy.MaximumResponseBytes)
            {
                return Rejected(
                    DeepSeekControlAdmission.ResourceLimitExceeded,
                    observedAt,
                    expiresAt,
                    networkCalls: 1);
            }

            byte[]? payload = null;
            try
            {
                await using var stream = await response.Content
                    .ReadAsStreamAsync(linked.Token)
                    .ConfigureAwait(false);
                payload = await ReadBoundedAsync(stream, linked.Token)
                    .ConfigureAwait(false);
                using var document = JsonDocument.Parse(
                    payload,
                    new JsonDocumentOptions { MaxDepth = 12 });
                if (!document.RootElement.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                {
                    return Rejected(
                        DeepSeekControlAdmission.ResponseRejected,
                        observedAt,
                        expiresAt,
                        networkCalls: 1);
                }
                var models = data.EnumerateArray()
                    .Select(item =>
                        item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("id", out var id)
                        && id.ValueKind == JsonValueKind.String
                            ? id.GetString()
                            : null)
                    .Where(DeepSeekControlPlanePolicy.IsAdmittedModel)
                    .Cast<string>()
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal)
                    .Take(DeepSeekControlPlanePolicy.MaximumModels + 1)
                    .ToArray();
                if (models.Length == 0)
                {
                    return Rejected(
                        DeepSeekControlAdmission.ResponseRejected,
                        observedAt,
                        expiresAt,
                        networkCalls: 1);
                }
                if (models.Length > DeepSeekControlPlanePolicy.MaximumModels)
                {
                    return Rejected(
                        DeepSeekControlAdmission.ResourceLimitExceeded,
                        observedAt,
                        expiresAt,
                        networkCalls: 1);
                }
                return new DeepSeekCatalogResult(
                    DeepSeekControlAdmission.Accepted,
                    models,
                    observedAt,
                    expiresAt,
                    networkCalls: 1);
            }
            catch (JsonException)
            {
                return Rejected(
                    DeepSeekControlAdmission.ResponseRejected,
                    observedAt,
                    expiresAt,
                    networkCalls: 1);
            }
            catch (DeepSeekResourceLimitException)
            {
                return Rejected(
                    DeepSeekControlAdmission.ResourceLimitExceeded,
                    observedAt,
                    expiresAt,
                    networkCalls: 1);
            }
            finally
            {
                if (payload != null)
                    CryptographicOperations.ZeroMemory(payload);
            }
        }
        catch (OperationCanceledException)
        {
            return Rejected(
                cancellationToken.IsCancellationRequested
                    ? DeepSeekControlAdmission.Cancelled
                    : DeepSeekControlAdmission.Timeout,
                observedAt,
                expiresAt,
                networkCalls: 1);
        }
        catch (HttpRequestException)
        {
            return Rejected(
                DeepSeekControlAdmission.NetworkFailure,
                observedAt,
                expiresAt,
                networkCalls: 1);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(
                        buffer.AsMemory(0, buffer.Length),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                    return output.ToArray();
                if (output.Length + count
                    > DeepSeekControlPlanePolicy.MaximumResponseBytes)
                {
                    throw new DeepSeekResourceLimitException();
                }
                output.Write(buffer, 0, count);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static DeepSeekCatalogResult Rejected(
        DeepSeekControlAdmission admission,
        DateTimeOffset observedAt,
        DateTimeOffset expiresAt,
        int networkCalls)
        => new(admission, null, observedAt, expiresAt, networkCalls);
}

internal sealed class DeepSeekControlPlaneSnapshot
{
    internal long ConfigurationGeneration { get; }
    internal string SelectedModel { get; }
    internal IReadOnlyList<string> CatalogModels { get; }
    internal DateTimeOffset CatalogObservedAt { get; }
    internal DateTimeOffset CatalogExpiresAt { get; }
    internal bool CredentialConfigured { get; }
    internal string CredentialReference { get; }
    internal string CredentialSha256 { get; }

    internal DeepSeekControlPlaneSnapshot(
        long configurationGeneration,
        string selectedModel,
        IEnumerable<string> catalogModels,
        DateTimeOffset catalogObservedAt,
        DateTimeOffset catalogExpiresAt,
        bool credentialConfigured,
        string credentialReference,
        string credentialSha256)
    {
        if (configurationGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(configurationGeneration));
        var frozen = (catalogModels ?? throw new ArgumentNullException(nameof(catalogModels)))
            .ToArray();
        if (frozen.Length == 0
            || frozen.Length > DeepSeekControlPlanePolicy.MaximumModels
            || frozen.Any(value => !DeepSeekControlPlanePolicy.IsAdmittedModel(value)))
        {
            throw new ArgumentException("Catalog models are invalid.", nameof(catalogModels));
        }
        if (!DeepSeekControlPlanePolicy.IsAdmittedModel(selectedModel)
            || !frozen.Contains(selectedModel, StringComparer.Ordinal))
        {
            throw new ArgumentException("Selected model is not catalogued.", nameof(selectedModel));
        }
        if (catalogExpiresAt <= catalogObservedAt)
            throw new ArgumentOutOfRangeException(nameof(catalogExpiresAt));
        if (credentialConfigured && string.IsNullOrWhiteSpace(credentialReference))
            throw new ArgumentException("Credential reference is required.", nameof(credentialReference));
        if (credentialConfigured && !DeepSeekControlPlaneStore.IsCredentialSha256(credentialSha256))
            throw new ArgumentException("Credential hash is required.", nameof(credentialSha256));
        ConfigurationGeneration = configurationGeneration;
        SelectedModel = selectedModel;
        CatalogModels = Array.AsReadOnly(frozen);
        CatalogObservedAt = catalogObservedAt;
        CatalogExpiresAt = catalogExpiresAt;
        CredentialConfigured = credentialConfigured;
        CredentialReference = credentialReference ?? string.Empty;
        CredentialSha256 = credentialSha256 ?? string.Empty;
    }
}

internal sealed class DeepSeekControlPlaneStoreResult
{
    internal DeepSeekControlAdmission Admission { get; }
    internal DeepSeekControlPlaneSnapshot? Snapshot { get; }
    internal bool Accepted => Admission == DeepSeekControlAdmission.Accepted;

    internal DeepSeekControlPlaneStoreResult(
        DeepSeekControlAdmission admission,
        DeepSeekControlPlaneSnapshot? snapshot = null)
    {
        Admission = admission;
        Snapshot = snapshot;
    }
}

internal interface IDeepSeekCurrentUserProtection
{
    bool Supported { get; }
    byte[] Protect(byte[] plaintext, byte[] entropy);
    byte[] Unprotect(byte[] ciphertext, byte[] entropy);
}

internal sealed class DeepSeekControlPlaneStore
{
    private const int SchemaVersion = 1;
    private const int MaximumManifestBytes = 16 * 1024;
    private const int MaximumPayloadBytes = 64 * 1024;
    private const int MaximumCipherBytes = 32 * 1024;
    private readonly string _root;
    private readonly string _generations;
    private readonly string _secrets;
    private readonly string _current;
    private readonly string _previous;
    private readonly string _credentialAuthorityRetirementMarker;
    private readonly IDeepSeekCurrentUserProtection _protection;

    internal DeepSeekControlPlaneStore(
        string localApplicationDataDirectory,
        IDeepSeekCurrentUserProtection? protection = null)
    {
        if (string.IsNullOrWhiteSpace(localApplicationDataDirectory))
            throw new ArgumentException("Local application data is required.", nameof(localApplicationDataDirectory));
        var playerRoot = Path.Combine(
            Path.GetFullPath(localApplicationDataDirectory),
            "LilithAiMod");
        _root = Below(playerRoot, "Config", "provider-control-v1");
        _generations = Below(playerRoot, "Config", "provider-control-v1", "generations");
        _secrets = Below(playerRoot, "Secrets", "provider-control-v1");
        _current = Below(playerRoot, "Config", "provider-control-v1", "current-v1.json");
        _previous = _current + ".previous";
        _credentialAuthorityRetirementMarker =
            LegacyProviderCredentialAuthorityRetirement.MarkerPath(
                localApplicationDataDirectory);
        _protection = protection ?? new WindowsCurrentUserProtection();
    }

    internal static DeepSeekControlPlaneStore ForCurrentUser()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        LegacyProviderCredentialAuthorityRetirement
            .EnforceForCurrentUserWithoutBlockingStartup();
        return new DeepSeekControlPlaneStore(localApplicationData);
    }

    internal DeepSeekControlPlaneStoreResult Load()
    {
        using var authorityLease =
            LegacyProviderCredentialAuthorityRetirement.TryAcquireLegacyAccess(
                _credentialAuthorityRetirementMarker);
        if (authorityLease == null
            || LegacyProviderCredentialAuthorityRetirement.IsEnforced(
                _credentialAuthorityRetirementMarker))
        {
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.Missing);
        }
        return LoadBeforeCredentialAuthorityRetirement();
    }

    internal DeepSeekControlPlaneStoreResult
        LoadBeforeCredentialAuthorityRetirement()
    {
        var current = Read(_current);
        if (current.Admission is DeepSeekControlAdmission.Accepted
            or DeepSeekControlAdmission.Missing)
        {
            return current;
        }
        var previous = Read(_previous);
        return previous.Accepted ? previous : current;
    }

    internal DeepSeekControlPlaneStoreResult ApplyValidated(
        string apiKey,
        DeepSeekCatalogResult catalog,
        string selectedModel,
        DateTimeOffset now)
    {
        using var authorityLease =
            LegacyProviderCredentialAuthorityRetirement.TryAcquireLegacyAccess(
                _credentialAuthorityRetirementMarker);
        if (authorityLease == null
            || LegacyProviderCredentialAuthorityRetirement.IsEnforced(
                _credentialAuthorityRetirementMarker))
        {
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.InvalidCredential);
        }
        if (!catalog.Accepted)
            return new DeepSeekControlPlaneStoreResult(catalog.Admission);
        if (string.IsNullOrWhiteSpace(apiKey)
            || apiKey.Length > DeepSeekControlPlanePolicy.MaximumCredentialCharacters)
        {
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.InvalidCredential);
        }
        if (!DeepSeekControlPlanePolicy.IsAdmittedModel(selectedModel)
            || !catalog.Models.Contains(selectedModel, StringComparer.Ordinal))
        {
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.ModelNotCatalogued);
        }
        if (!_protection.Supported)
        {
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.UnsupportedPlatform);
        }
        var loaded = Load();
        var generation = (loaded.Snapshot?.ConfigurationGeneration ?? 0) + 1;
        byte[]? plaintext = null;
        byte[]? entropy = null;
        byte[]? cipher = null;
        try
        {
            plaintext = Encoding.UTF8.GetBytes(apiKey.Trim());
            entropy = Entropy();
            cipher = _protection.Protect(plaintext, entropy);
            if (cipher.Length == 0 || cipher.Length > MaximumCipherBytes)
            {
                return new DeepSeekControlPlaneStoreResult(
                    DeepSeekControlAdmission.ProtectionFailed);
            }
            Directory.CreateDirectory(_secrets);
            Directory.CreateDirectory(_generations);
            var cipherHash = Hash(cipher);
            var credentialFile = "deepseek-credential-"
                + cipherHash[..24].ToLowerInvariant()
                + ".bin";
            WriteImmutable(Path.Combine(_secrets, credentialFile), cipher);
            var payload = new PayloadDto
            {
                Schema = SchemaVersion,
                ConfigurationGeneration = generation,
                SelectedModel = selectedModel,
                CatalogModels = catalog.Models.ToArray(),
                CatalogObservedAtUnixMilliseconds = catalog.ObservedAt.ToUnixTimeMilliseconds(),
                CatalogExpiresAtUnixMilliseconds = catalog.ExpiresAt.ToUnixTimeMilliseconds(),
                CredentialFile = credentialFile,
                CredentialSha256 = cipherHash
            };
            return Persist(payload, now);
        }
        catch (CryptographicException)
        {
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.ProtectionFailed);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.StorageFailure);
        }
        finally
        {
            if (plaintext != null)
                CryptographicOperations.ZeroMemory(plaintext);
            if (entropy != null)
                CryptographicOperations.ZeroMemory(entropy);
            if (cipher != null)
                CryptographicOperations.ZeroMemory(cipher);
        }
    }

    internal DeepSeekControlPlaneStoreResult SelectModel(
        string selectedModel,
        DateTimeOffset now)
    {
        using var authorityLease =
            LegacyProviderCredentialAuthorityRetirement.TryAcquireLegacyAccess(
                _credentialAuthorityRetirementMarker);
        if (authorityLease == null
            || LegacyProviderCredentialAuthorityRetirement.IsEnforced(
                _credentialAuthorityRetirementMarker))
        {
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.Missing);
        }
        var loaded = Load();
        var current = loaded.Snapshot;
        if (!loaded.Accepted || current == null)
            return loaded;
        if (!current.CatalogModels.Contains(selectedModel, StringComparer.Ordinal))
        {
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.ModelNotCatalogued);
        }
        var manifest = ReadManifest(_current);
        if (manifest == null)
            return new DeepSeekControlPlaneStoreResult(DeepSeekControlAdmission.Corrupt);
        var payload = new PayloadDto
        {
            Schema = SchemaVersion,
            ConfigurationGeneration = current.ConfigurationGeneration + 1,
            SelectedModel = selectedModel,
            CatalogModels = current.CatalogModels.ToArray(),
            CatalogObservedAtUnixMilliseconds = current.CatalogObservedAt.ToUnixTimeMilliseconds(),
            CatalogExpiresAtUnixMilliseconds = current.CatalogExpiresAt.ToUnixTimeMilliseconds(),
            CredentialFile = manifest.CredentialFile,
            CredentialSha256 = manifest.CredentialSha256
        };
        return Persist(payload, now);
    }

    internal (DeepSeekControlAdmission Admission, DeepSeekCredentialLease? Lease)
        OpenCredential(DeepSeekControlPlaneSnapshot expected)
    {
        if (expected == null)
            throw new ArgumentNullException(nameof(expected));
        using var authorityLease =
            LegacyProviderCredentialAuthorityRetirement.TryAcquireLegacyAccess(
                _credentialAuthorityRetirementMarker);
        if (authorityLease == null
            || LegacyProviderCredentialAuthorityRetirement.IsEnforced(
                _credentialAuthorityRetirementMarker))
        {
            return (DeepSeekControlAdmission.Missing, null);
        }
        if (!_protection.Supported)
            return (DeepSeekControlAdmission.UnsupportedPlatform, null);
        var active = Load();
        if (!active.Accepted
            || active.Snapshot == null
            || active.Snapshot.ConfigurationGeneration
                != expected.ConfigurationGeneration
            || !string.Equals(
                active.Snapshot.CredentialReference,
                expected.CredentialReference,
                StringComparison.Ordinal)
            || !string.Equals(
                active.Snapshot.CredentialSha256,
                expected.CredentialSha256,
                StringComparison.Ordinal))
        {
            return (DeepSeekControlAdmission.Corrupt, null);
        }
        var manifest = FindExpectedManifest(expected);
        if (manifest == null)
            return (DeepSeekControlAdmission.Corrupt, null);
        byte[]? cipher = null;
        byte[]? entropy = null;
        byte[]? plaintext = null;
        try
        {
            cipher = ReadBounded(
                Path.Combine(_secrets, manifest.CredentialFile),
                MaximumCipherBytes);
            if (!string.Equals(Hash(cipher), manifest.CredentialSha256, StringComparison.Ordinal))
                return (DeepSeekControlAdmission.Corrupt, null);
            entropy = Entropy();
            plaintext = _protection.Unprotect(cipher, entropy);
            var secret = Encoding.UTF8.GetChars(plaintext);
            return (
                DeepSeekControlAdmission.Accepted,
                new DeepSeekCredentialLease(secret));
        }
        catch (CryptographicException)
        {
            return (DeepSeekControlAdmission.ProtectionFailed, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return (DeepSeekControlAdmission.StorageFailure, null);
        }
        finally
        {
            if (cipher != null)
                CryptographicOperations.ZeroMemory(cipher);
            if (entropy != null)
                CryptographicOperations.ZeroMemory(entropy);
            if (plaintext != null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private DeepSeekControlPlaneStoreResult Persist(PayloadDto payload, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(_generations);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            if (bytes.Length > MaximumPayloadBytes)
                return new DeepSeekControlPlaneStoreResult(DeepSeekControlAdmission.ResourceLimitExceeded);
            var payloadHash = Hash(bytes);
            var payloadFile = "deepseek-g"
                + payload.ConfigurationGeneration
                + "-"
                + payloadHash[..16].ToLowerInvariant()
                + ".json";
            WriteImmutable(Path.Combine(_generations, payloadFile), bytes);
            var manifest = new ManifestDto
            {
                Schema = SchemaVersion,
                ConfigurationGeneration = payload.ConfigurationGeneration,
                PayloadFile = payloadFile,
                PayloadSha256 = payloadHash,
                CredentialFile = payload.CredentialFile,
                CredentialSha256 = payload.CredentialSha256,
                CreatedAtUnixMilliseconds = now.ToUnixTimeMilliseconds()
            };
            AtomicWrite(_current, JsonSerializer.SerializeToUtf8Bytes(manifest), _previous);
            return Read(_current);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new DeepSeekControlPlaneStoreResult(DeepSeekControlAdmission.StorageFailure);
        }
    }

    private DeepSeekControlPlaneStoreResult Read(string manifestPath)
    {
        if (!File.Exists(manifestPath))
            return new DeepSeekControlPlaneStoreResult(DeepSeekControlAdmission.Missing);
        try
        {
            var manifest = ReadManifest(manifestPath);
            if (manifest == null)
                return new DeepSeekControlPlaneStoreResult(DeepSeekControlAdmission.Corrupt);
            var payloadBytes = ReadBounded(
                Path.Combine(_generations, manifest.PayloadFile),
                MaximumPayloadBytes);
            if (!string.Equals(Hash(payloadBytes), manifest.PayloadSha256, StringComparison.Ordinal))
                return new DeepSeekControlPlaneStoreResult(DeepSeekControlAdmission.Corrupt);
            var payload = JsonSerializer.Deserialize<PayloadDto>(payloadBytes);
            if (payload == null
                || payload.Schema != SchemaVersion
                || payload.ConfigurationGeneration != manifest.ConfigurationGeneration
                || !string.Equals(payload.CredentialFile, manifest.CredentialFile, StringComparison.Ordinal)
                || !string.Equals(payload.CredentialSha256, manifest.CredentialSha256, StringComparison.Ordinal))
            {
                return new DeepSeekControlPlaneStoreResult(DeepSeekControlAdmission.Corrupt);
            }
            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                payload.CatalogObservedAtUnixMilliseconds);
            var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(
                payload.CatalogExpiresAtUnixMilliseconds);
            return new DeepSeekControlPlaneStoreResult(
                DeepSeekControlAdmission.Accepted,
                new DeepSeekControlPlaneSnapshot(
                    payload.ConfigurationGeneration,
                    payload.SelectedModel,
                    payload.CatalogModels ?? Array.Empty<string>(),
                    observedAt,
                    expiresAt,
                    credentialConfigured: true,
                    manifest.CredentialFile,
                    manifest.CredentialSha256));
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or ArgumentOutOfRangeException)
        {
            return new DeepSeekControlPlaneStoreResult(DeepSeekControlAdmission.Corrupt);
        }
    }

    private ManifestDto? ReadManifest(string path)
    {
        if (!File.Exists(path))
            return null;
        var bytes = ReadBounded(path, MaximumManifestBytes);
        var manifest = JsonSerializer.Deserialize<ManifestDto>(bytes);
        if (manifest == null
            || manifest.Schema != SchemaVersion
            || manifest.ConfigurationGeneration <= 0
            || !SafeFileName(manifest.PayloadFile, ".json")
            || !SafeFileName(manifest.CredentialFile, ".bin")
            || !IsSha256(manifest.PayloadSha256)
            || !IsSha256(manifest.CredentialSha256))
        {
            return null;
        }
        return manifest;
    }

    private ManifestDto? FindExpectedManifest(
        DeepSeekControlPlaneSnapshot expected)
    {
        foreach (var path in new[] { _current, _previous })
        {
            var manifest = ReadManifest(path);
            if (manifest != null
                && manifest.ConfigurationGeneration
                    == expected.ConfigurationGeneration
                && string.Equals(
                    manifest.CredentialFile,
                    expected.CredentialReference,
                    StringComparison.Ordinal)
                && string.Equals(
                    manifest.CredentialSha256,
                    expected.CredentialSha256,
                    StringComparison.Ordinal))
            {
                return manifest;
            }
        }
        return null;
    }

    private static byte[] Entropy()
        => SHA256.HashData(Encoding.UTF8.GetBytes(
            "LilithAiMod/ProviderControl/DeepSeek/CurrentUser/v1"));

    private static string Below(string root, params string[] segments)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(segments.Aggregate(fullRoot, Path.Combine));
        var prefix = fullRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Provider control-plane path escaped its root.");
        return candidate;
    }

    private static void WriteImmutable(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            var existing = ReadBounded(path, Math.Max(bytes.Length, 1) + 1);
            if (!CryptographicOperations.FixedTimeEquals(existing, bytes))
                throw new IOException("Immutable provider artifact collision.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static void AtomicWrite(string path, byte[] bytes, string backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path))
                File.Replace(temporary, path, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length <= 0 || stream.Length > maximumBytes)
            throw new InvalidDataException("Provider control-plane file is out of bounds.");
        var bytes = new byte[stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var count = stream.Read(bytes, offset, bytes.Length - offset);
            if (count == 0)
                throw new EndOfStreamException();
            offset += count;
        }
        return bytes;
    }

    private static bool SafeFileName(string? value, string suffix)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= 160
            && Path.GetFileName(value) == value
            && value.EndsWith(suffix, StringComparison.Ordinal);

    internal static bool IsCredentialSha256(string? value)
        => IsSha256(value);

    private static bool IsSha256(string? value)
        => value?.Length == 64
            && value.All(character =>
                character is >= '0' and <= '9'
                or >= 'A' and <= 'F');

    private static string Hash(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class ManifestDto
    {
        public int Schema { get; set; }
        public long ConfigurationGeneration { get; set; }
        public string PayloadFile { get; set; } = string.Empty;
        public string PayloadSha256 { get; set; } = string.Empty;
        public string CredentialFile { get; set; } = string.Empty;
        public string CredentialSha256 { get; set; } = string.Empty;
        public long CreatedAtUnixMilliseconds { get; set; }
    }

    private sealed class PayloadDto
    {
        public int Schema { get; set; }
        public long ConfigurationGeneration { get; set; }
        public string SelectedModel { get; set; } = string.Empty;
        public string[]? CatalogModels { get; set; }
        public long CatalogObservedAtUnixMilliseconds { get; set; }
        public long CatalogExpiresAtUnixMilliseconds { get; set; }
        public string CredentialFile { get; set; } = string.Empty;
        public string CredentialSha256 { get; set; } = string.Empty;
    }
}

internal sealed class DeepSeekCredentialLease : IDisposable
{
    private char[]? _secret;

    internal DeepSeekCredentialLease(char[] secret)
        => _secret = secret ?? throw new ArgumentNullException(nameof(secret));

    internal char[] Take()
    {
        var secret = Interlocked.Exchange(ref _secret, null)
            ?? throw new InvalidOperationException("Credential lease was already consumed.");
        return secret;
    }

    public void Dispose()
    {
        var secret = Interlocked.Exchange(ref _secret, null);
        if (secret != null)
            Array.Clear(secret, 0, secret.Length);
    }
}

internal sealed class WindowsCurrentUserProtection : IDeepSeekCurrentUserProtection
{
    internal const int CryptProtectUiForbidden = 0x1;

    public bool Supported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public byte[] Protect(byte[] plaintext, byte[] entropy)
        => Transform(plaintext, entropy, protect: true);

    public byte[] Unprotect(byte[] ciphertext, byte[] entropy)
        => Transform(ciphertext, entropy, protect: false);

    private static byte[] Transform(byte[] input, byte[] entropy, bool protect)
    {
        if (input == null || input.Length == 0)
            throw new ArgumentException("Protected input is required.", nameof(input));
        if (entropy == null || entropy.Length == 0)
            throw new ArgumentException("Protection entropy is required.", nameof(entropy));
        var inputBlob = ToBlob(input);
        var entropyBlob = ToBlob(entropy);
        DataBlob outputBlob = default;
        try
        {
            var success = protect
                ? CryptProtectData(
                    ref inputBlob,
                    "Lilith AI MOD Provider credential",
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);
            if (!success || outputBlob.Data == IntPtr.Zero || outputBlob.Length <= 0)
                throw new CryptographicException(Marshal.GetLastWin32Error());
            var output = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally
        {
            ClearAndFree(ref inputBlob, localFree: false);
            ClearAndFree(ref entropyBlob, localFree: false);
            ClearAndFree(ref outputBlob, localFree: true);
        }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = pointer };
    }

    private static void ClearAndFree(ref DataBlob blob, bool localFree)
    {
        if (blob.Data == IntPtr.Zero)
            return;
        Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length);
        if (localFree)
            LocalFree(blob.Data);
        else
            Marshal.FreeHGlobal(blob.Data);
        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        internal int Length;
        internal IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}

internal sealed class DeepSeekResourceLimitException : Exception
{
}
