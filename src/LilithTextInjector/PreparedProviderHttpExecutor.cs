using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LilithTextInjector;

internal enum PreparedProviderAuthentication
{
    None = 0,
    GoogleApiKeyHeader = 1,
    Bearer = 2
}

internal enum PreparedProviderExecutionFailure
{
    BindingUnavailable = 0,
    BindingMismatch = 1,
    Expired = 2,
    HttpRejected = 3,
    ResponseRejected = 4,
    DuplicateInvocation = 5
}

internal sealed class PreparedProviderExecutionException : Exception
{
    internal PreparedProviderExecutionFailure Failure { get; }
    internal HttpStatusCode? StatusCode { get; }

    internal PreparedProviderExecutionException(
        PreparedProviderExecutionFailure failure,
        string providerRouteId,
        HttpStatusCode? statusCode = null)
        : base(
            "Prepared provider execution failed: "
            + failure
            + " (route="
            + providerRouteId
            + (statusCode.HasValue
                ? ", status="
                    + ((int)statusCode.Value).ToString(
                        System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty)
            + ").")
    {
        if (!Enum.IsDefined(
                typeof(PreparedProviderExecutionFailure),
                failure))
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }
        Failure = failure;
        StatusCode = statusCode;
    }
}

internal sealed class PreparedProviderRuntimeBinding
{
    private readonly string _credential;
    private readonly CredentialLease? _credentialLease;

    internal PreparedProviderRoute Route { get; }
    internal PreparedProviderProtocol Protocol { get; }
    internal Uri RequestUri { get; }
    internal string ModelId { get; }
    internal PreparedProviderAuthentication Authentication { get; }

    private PreparedProviderRuntimeBinding(
        PreparedProviderRoute route,
        PreparedProviderProtocol protocol,
        Uri requestUri,
        string modelId,
        PreparedProviderAuthentication authentication,
        string credential,
        CredentialLease? credentialLease = null)
    {
        if (!Enum.IsDefined(typeof(PreparedProviderRoute), route))
            throw new ArgumentOutOfRangeException(nameof(route));
        if (!Enum.IsDefined(typeof(PreparedProviderProtocol), protocol))
            throw new ArgumentOutOfRangeException(nameof(protocol));
        if (!Enum.IsDefined(
                typeof(PreparedProviderAuthentication),
                authentication))
        {
            throw new ArgumentOutOfRangeException(nameof(authentication));
        }
        RequestUri = requestUri
            ?? throw new ArgumentNullException(nameof(requestUri));
        if (!RequestUri.IsAbsoluteUri
            || (RequestUri.Scheme != Uri.UriSchemeHttps
                && (RequestUri.Scheme != Uri.UriSchemeHttp
                    || !RequestUri.IsLoopback)))
        {
            throw new ArgumentException(
                "Provider endpoint must use HTTPS, except loopback HTTP "
                + "for a local OpenAI-compatible runtime.",
                nameof(requestUri));
        }
        ModelId = LivingContinuityContractGuard.Identifier(
            modelId,
            nameof(modelId));
        _credential = credential
            ?? throw new ArgumentNullException(nameof(credential));
        if (authentication
                != PreparedProviderAuthentication.None
            && string.IsNullOrWhiteSpace(_credential)
            && credentialLease == null)
        {
            throw new ArgumentException(
                "Authenticated provider binding requires a credential.",
                nameof(credential));
        }
        if (authentication == PreparedProviderAuthentication.None
            && !RequestUri.IsLoopback)
        {
            throw new ArgumentException(
                "Unauthenticated provider binding is restricted to "
                + "loopback.",
                nameof(authentication));
        }
        if (authentication == PreparedProviderAuthentication.None
            && credentialLease != null)
        {
            throw new ArgumentException(
                "Unauthenticated binding cannot carry a credential lease.",
                nameof(credentialLease));
        }
        if (!string.IsNullOrWhiteSpace(_credential)
            && credentialLease != null)
        {
            throw new ArgumentException(
                "A binding cannot carry both legacy credential material "
                + "and a final-boundary lease.",
                nameof(credentialLease));
        }
        Route = route;
        Protocol = protocol;
        Authentication = authentication;
        _credentialLease = credentialLease;
    }

    internal static PreparedProviderRuntimeBinding Gemini(
        Uri serviceBase,
        string modelId,
        string credential)
    {
        if (serviceBase == null)
            throw new ArgumentNullException(nameof(serviceBase));
        modelId = LivingContinuityContractGuard.Identifier(
            modelId,
            nameof(modelId));
        var normalizedBase = serviceBase.AbsoluteUri.EndsWith(
                "/",
                StringComparison.Ordinal)
            ? serviceBase
            : new Uri(serviceBase.AbsoluteUri + "/", UriKind.Absolute);
        var requestUri = new Uri(
            normalizedBase,
            "v1beta/models/"
            + Uri.EscapeDataString(modelId)
            + ":generateContent");
        return new PreparedProviderRuntimeBinding(
            PreparedProviderRoute.Gemini,
            PreparedProviderProtocol.GeminiGenerateContent,
            requestUri,
            modelId,
            PreparedProviderAuthentication.GoogleApiKeyHeader,
            credential);
    }

    internal static PreparedProviderRuntimeBinding
        GeminiWithCredentialLease(
            Uri serviceBase,
            string modelId,
            CredentialLease credentialLease)
    {
        if (serviceBase == null)
            throw new ArgumentNullException(nameof(serviceBase));
        if (credentialLease == null)
            throw new ArgumentNullException(nameof(credentialLease));
        modelId = LivingContinuityContractGuard.Identifier(
            modelId,
            nameof(modelId));
        var normalizedBase = serviceBase.AbsoluteUri.EndsWith(
                "/",
                StringComparison.Ordinal)
            ? serviceBase
            : new Uri(serviceBase.AbsoluteUri + "/", UriKind.Absolute);
        var requestUri = new Uri(
            normalizedBase,
            "v1beta/models/"
            + Uri.EscapeDataString(modelId)
            + ":generateContent");
        return new PreparedProviderRuntimeBinding(
            PreparedProviderRoute.Gemini,
            PreparedProviderProtocol.GeminiGenerateContent,
            requestUri,
            modelId,
            PreparedProviderAuthentication.GoogleApiKeyHeader,
            string.Empty,
            credentialLease);
    }

    internal static PreparedProviderRuntimeBinding Responses(
        PreparedProviderRoute route,
        Uri requestUri,
        string modelId,
        PreparedProviderAuthentication authentication,
        string credential)
    {
        if (route != PreparedProviderRoute.Qwen)
        {
            throw new ArgumentOutOfRangeException(
                nameof(route),
                "The first Responses binding is Qwen-only.");
        }
        return new PreparedProviderRuntimeBinding(
            route,
            PreparedProviderProtocol.OpenAiResponses,
            requestUri,
            modelId,
            authentication,
            credential);
    }

    internal static PreparedProviderRuntimeBinding Chat(
        PreparedProviderRoute route,
        Uri requestUri,
        string modelId,
        PreparedProviderAuthentication authentication,
        string credential)
    {
        if (route != PreparedProviderRoute.OpenAI
            && route != PreparedProviderRoute.DeepSeek)
        {
            throw new ArgumentOutOfRangeException(
                nameof(route),
                "Chat-compatible binding requires OpenAI or DeepSeek.");
        }
        return new PreparedProviderRuntimeBinding(
            route,
            PreparedProviderProtocol.OpenAiChatCompletions,
            requestUri,
            modelId,
            authentication,
            credential);
    }

    internal bool Matches(PreparedProviderExecutionRequest request)
        => request != null
            && request.Route == Route
            && request.Protocol == Protocol;

    internal void ApplyAuthentication(
        HttpRequestMessage request,
        DateTimeOffset? now = null)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        switch (Authentication)
        {
            case PreparedProviderAuthentication.None:
                return;
            case PreparedProviderAuthentication.GoogleApiKeyHeader:
                request.Headers.Add(
                    "x-goog-api-key",
                    ResolveCredentialForSend(now));
                return;
            case PreparedProviderAuthentication.Bearer:
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        ResolveCredentialForSend(now));
                return;
            default:
                throw new InvalidOperationException(
                    "Unsupported provider authentication.");
        }
    }

    private string ResolveCredentialForSend(DateTimeOffset? now)
    {
        if (_credentialLease == null)
            return _credential;
        string? copy = null;
        var consumed = _credentialLease.TryConsumeOnce(
            now ?? DateTimeOffset.UtcNow,
            value =>
            {
                copy = new string(value);
                return true;
            });
        if (!consumed || string.IsNullOrWhiteSpace(copy))
        {
            throw new PreparedProviderExecutionException(
                PreparedProviderExecutionFailure.BindingUnavailable,
                Route.ToString());
        }
        return copy;
    }

    internal string BindModel(
        PreparedProviderWirePayload payload)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));
        if (payload.Protocol
            == PreparedProviderProtocol.GeminiGenerateContent)
        {
            return payload.Json;
        }
        using var document = JsonDocument.Parse(payload.Json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", ModelId);
            foreach (var property
                     in document.RootElement.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        "model",
                        StringComparison.Ordinal))
                {
                    continue;
                }
                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public override string ToString()
        => Route
            + "/"
            + Protocol
            + "@"
            + RequestUri.GetLeftPart(UriPartial.Authority)
            + " (credential redacted)";
}

internal interface IPreparedProviderRuntimeBindingResolver
{
    bool TryResolve(
        PreparedProviderRoute route,
        PreparedProviderProtocol protocol,
        out PreparedProviderRuntimeBinding? binding);
}

internal sealed class PreparedProviderSingleBindingResolver
    : IPreparedProviderRuntimeBindingResolver
{
    private readonly PreparedProviderRuntimeBinding _binding;

    internal PreparedProviderSingleBindingResolver(
        PreparedProviderRuntimeBinding binding)
    {
        _binding = binding
            ?? throw new ArgumentNullException(nameof(binding));
    }

    public bool TryResolve(
        PreparedProviderRoute route,
        PreparedProviderProtocol protocol,
        out PreparedProviderRuntimeBinding? binding)
    {
        binding = _binding;
        return _binding.Route == route
            && _binding.Protocol == protocol;
    }
}

internal interface IPreparedProviderExecutionLifecycle
{
    void OnProviderSendStarting(
        PreparedProviderExecutionRequest request);
}

internal sealed class PreparedProviderHttpExecutor
    : IPreparedProviderExecutor
{
    private readonly HttpClient _http;
    private readonly IPreparedProviderRuntimeBindingResolver _bindings;
    private readonly IPreparedProviderExecutionLifecycle? _lifecycle;
    private readonly Func<DateTimeOffset> _clock;
    private int _attempted;

    internal PreparedProviderHttpExecutor(
        HttpClient http,
        IPreparedProviderRuntimeBindingResolver bindings,
        IPreparedProviderExecutionLifecycle? lifecycle = null,
        Func<DateTimeOffset>? clock = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _bindings = bindings
            ?? throw new ArgumentNullException(nameof(bindings));
        _lifecycle = lifecycle;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<PreparedProviderNormalizedCompletion>
        ExecuteAsync(
            PreparedProviderExecutionRequest request,
            CancellationToken cancellationToken)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (Interlocked.Exchange(ref _attempted, 1) != 0)
        {
            throw new PreparedProviderExecutionException(
                PreparedProviderExecutionFailure.DuplicateInvocation,
                request.ProviderRouteId);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (_clock() >= request.Deadline)
        {
            throw new PreparedProviderExecutionException(
                PreparedProviderExecutionFailure.Expired,
                request.ProviderRouteId);
        }
        if (!_bindings.TryResolve(
                request.Route,
                request.Protocol,
                out var binding)
            || binding == null)
        {
            throw new PreparedProviderExecutionException(
                PreparedProviderExecutionFailure.BindingUnavailable,
                request.ProviderRouteId);
        }
        if (!binding.Matches(request))
        {
            throw new PreparedProviderExecutionException(
                PreparedProviderExecutionFailure.BindingMismatch,
                request.ProviderRouteId);
        }

        var portable = PreparedProviderTextCodec.Encode(request);
        var boundJson = binding.BindModel(portable);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            binding.RequestUri);
        binding.ApplyAuthentication(httpRequest, _clock());
        httpRequest.Content = new StringContent(
            boundJson,
            Encoding.UTF8,
            "application/json");

        cancellationToken.ThrowIfCancellationRequested();
        _lifecycle?.OnProviderSendStarting(request);
        using var response = await _http.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken)
            .ConfigureAwait(false);
        var responseJson = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PreparedProviderExecutionException(
                PreparedProviderExecutionFailure.HttpRejected,
                request.ProviderRouteId,
                response.StatusCode);
        }
        var admission = PreparedProviderTextCodec.TryParse(
            request,
            responseJson,
            out var completion);
        if (admission != PreparedProviderCodecAdmission.Parsed
            || completion == null)
        {
            throw new PreparedProviderExecutionException(
                PreparedProviderExecutionFailure.ResponseRejected,
                request.ProviderRouteId);
        }
        return completion;
    }
}
