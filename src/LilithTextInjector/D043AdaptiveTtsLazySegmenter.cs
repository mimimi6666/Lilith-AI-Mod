using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum D043AdaptiveTtsLanguage
{
    Chinese = 1,
    Japanese = 2,
    English = 3,
    Mixed = 4
}

internal enum D043AdaptiveTtsBoundaryKind
{
    Terminal = 1,
    Paragraph = 2,
    Sentence = 3,
    SoftPunctuation = 4,
    WhiteSpace = 5,
    SoftLimit = 6,
    ForcedHardLimit = 7
}

internal enum D043AdaptiveTtsSegmentationAdmission
{
    Created = 1,
    Empty = 2,
    InvalidUnicode = 3,
    InputTextElementLimitExceeded = 4,
    InputUtf8LimitExceeded = 5,
    NoSpeakableContent = 6,
    AuthorityMismatch = 7,
    NonSpeakableRunLimitExceeded = 8,
    SegmentUtf8WindowLimitExceeded = 9,
    SegmentationCapsOutOfRange = 10,
    InputSegmentCapacityExceeded = 11
}

internal enum D043AdaptiveTtsSegmentTakeAdmission
{
    Created = 1,
    Completed = 2,
    SegmentLimitExceeded = 3,
    SegmentCouldNotAdvance = 4,
    Retired = 5
}

internal static class D043AdaptiveTtsFingerprint
{
    internal static string Text(string value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    internal static string Canonical(params object?[] values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            var encoded = value switch
            {
                null => "<null>",
                Enum enumeration => Convert.ToInt64(
                    enumeration,
                    CultureInfo.InvariantCulture).ToString(
                        CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(
                    null,
                    CultureInfo.InvariantCulture) ?? string.Empty,
                _ => value.ToString() ?? string.Empty
            };
            builder.Append(encoded.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(encoded);
            builder.Append('|');
        }
        return Text(builder.ToString());
    }

    internal static string Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 64)
        {
            throw new ArgumentException(
                "A SHA-256 fingerprint is required.",
                name);
        }
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!((character >= '0' && character <= '9')
                  || (character >= 'A' && character <= 'F')))
            {
                throw new ArgumentException(
                    "The fingerprint must be uppercase hexadecimal SHA-256.",
                    name);
            }
        }
        return value;
    }

    internal static string Metadata(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Bounded metadata is required.", name);
        }
        foreach (var character in value)
        {
            if (char.IsControl(character))
                throw new ArgumentException("Metadata contains control text.", name);
        }
        return value;
    }
}

internal sealed class D043AdaptiveTtsPublicationAuthority
{
    internal string AuthorityId { get; }
    internal long PublicationSequence { get; }
    internal string ProcessEpochId { get; }
    internal long RuntimeGeneration { get; }
    internal string PublishedTextSha256 { get; }
    internal string SpeechProjectionSha256 { get; }
    internal D043AdaptiveTtsLanguage Language { get; }
    internal long IssuedAtMonotonicTicks { get; }
    internal string Fingerprint { get; }
    internal bool PublicationCommitted => true;

    internal D043AdaptiveTtsPublicationAuthority(
        string authorityId,
        long publicationSequence,
        string processEpochId,
        long runtimeGeneration,
        string publishedTextSha256,
        string speechProjectionSha256,
        D043AdaptiveTtsLanguage language,
        long issuedAtMonotonicTicks)
    {
        AuthorityId = D043AdaptiveTtsFingerprint.Metadata(
            authorityId,
            128,
            nameof(authorityId));
        if (publicationSequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(publicationSequence));
        PublicationSequence = publicationSequence;
        ProcessEpochId = D043AdaptiveTtsFingerprint.Metadata(
            processEpochId,
            128,
            nameof(processEpochId));
        if (runtimeGeneration <= 0)
            throw new ArgumentOutOfRangeException(nameof(runtimeGeneration));
        RuntimeGeneration = runtimeGeneration;
        PublishedTextSha256 = D043AdaptiveTtsFingerprint.Require(
            publishedTextSha256,
            nameof(publishedTextSha256));
        SpeechProjectionSha256 = D043AdaptiveTtsFingerprint.Require(
            speechProjectionSha256,
            nameof(speechProjectionSha256));
        if (!Enum.IsDefined(typeof(D043AdaptiveTtsLanguage), language))
            throw new ArgumentOutOfRangeException(nameof(language));
        Language = language;
        if (issuedAtMonotonicTicks < 0)
            throw new ArgumentOutOfRangeException(nameof(issuedAtMonotonicTicks));
        IssuedAtMonotonicTicks = issuedAtMonotonicTicks;
        Fingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.AdaptiveTts.PublicationAuthority.V1",
            AuthorityId,
            PublicationSequence,
            ProcessEpochId,
            RuntimeGeneration,
            PublishedTextSha256,
            SpeechProjectionSha256,
            Language,
            IssuedAtMonotonicTicks);
    }

    public override string ToString()
        => "D043AdaptiveTtsPublicationAuthority(content-free)";
}

internal sealed class D043AdaptiveTtsTurnRequest
{
    internal D043AdaptiveTtsPublicationAuthority Authority { get; }
    internal string SpeechText { get; }
    internal string SpeechTextSha256 { get; }
    internal string TurnId { get; }
    internal bool ContainsEphemeralSpeechText => true;
    internal bool MayPersistSpeechText => false;
    internal bool MayLogSpeechText => false;

    internal D043AdaptiveTtsTurnRequest(
        D043AdaptiveTtsPublicationAuthority authority,
        string speechText)
    {
        Authority = authority ?? throw new ArgumentNullException(nameof(authority));
        if (string.IsNullOrWhiteSpace(speechText)
            || !string.Equals(speechText, speechText.Trim(), StringComparison.Ordinal)
            || speechText.IndexOf('\0') >= 0)
        {
            throw new ArgumentException(
                "Non-empty normalized speech text is required.",
                nameof(speechText));
        }
        if (speechText.Length > D043AdaptiveTtsSegmentationCaps.V1.MaximumInputUtf8Bytes
            || Encoding.UTF8.GetByteCount(speechText)
                > D043AdaptiveTtsSegmentationCaps.V1.MaximumInputUtf8Bytes)
        {
            throw new ArgumentException(
                "Speech text exceeds the frozen bounded input size.",
                nameof(speechText));
        }
        SpeechText = speechText;
        SpeechTextSha256 = D043AdaptiveTtsFingerprint.Text(SpeechText);
        if (!string.Equals(
                SpeechTextSha256,
                Authority.SpeechProjectionSha256,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Speech text does not match publication authority.",
                nameof(speechText));
        }
        TurnId = D043AdaptiveTtsFingerprint.Canonical(
            "D043.AdaptiveTts.Turn.V1",
            Authority.Fingerprint,
            SpeechTextSha256);
    }

    public override string ToString()
        => "D043AdaptiveTtsTurnRequest(content-redacted)";
}

internal sealed class D043AdaptiveTtsSegmentationCaps
{
    private const int V1MaximumInputTextElements = 4_096;
    private const int V1MaximumInputUtf8Bytes = 16 * 1_024;
    private const int V1MaximumSegments = 64;
    private const int V1MaximumSegmentTextElements = 160;
    private const int V1MaximumSegmentUtf8Bytes = 2 * 1_024;
    private const int V1FirstForcedHardMaximumUnits = 120;
    private const int V1SteadyForcedHardMaximumUnits = 120;
    internal static readonly D043AdaptiveTtsSegmentationCaps V1 = new(
        maximumInputTextElements: V1MaximumInputTextElements,
        maximumInputUtf8Bytes: V1MaximumInputUtf8Bytes,
        maximumSegments: V1MaximumSegments,
        maximumSegmentTextElements: V1MaximumSegmentTextElements,
        maximumSegmentUtf8Bytes: V1MaximumSegmentUtf8Bytes,
        firstForcedHardMaximumUnits: V1FirstForcedHardMaximumUnits,
        steadyForcedHardMaximumUnits: V1SteadyForcedHardMaximumUnits);

    internal int MaximumInputTextElements { get; }
    internal int MaximumInputUtf8Bytes { get; }
    internal int MaximumSegments { get; }
    internal int MaximumSegmentTextElements { get; }
    internal int MaximumSegmentUtf8Bytes { get; }
    internal int FirstForcedHardMaximumUnits { get; }
    internal int SteadyForcedHardMaximumUnits { get; }
    internal bool IsWithinV1Policy => MaximumInputTextElements is >= 1
            and <= V1MaximumInputTextElements
        && MaximumInputUtf8Bytes is >= 1 and <= V1MaximumInputUtf8Bytes
        && MaximumSegments is >= 1 and <= V1MaximumSegments
        && MaximumSegmentTextElements is >= 8
            and <= V1MaximumSegmentTextElements
        && MaximumSegmentUtf8Bytes is >= 8
            and <= V1MaximumSegmentUtf8Bytes
        && FirstForcedHardMaximumUnits is >= 8
            and <= V1FirstForcedHardMaximumUnits
        && SteadyForcedHardMaximumUnits >= FirstForcedHardMaximumUnits
        && SteadyForcedHardMaximumUnits <= V1SteadyForcedHardMaximumUnits;

    internal D043AdaptiveTtsSegmentationCaps(
        int maximumInputTextElements,
        int maximumInputUtf8Bytes,
        int maximumSegments,
        int maximumSegmentTextElements,
        int maximumSegmentUtf8Bytes,
        int firstForcedHardMaximumUnits,
        int steadyForcedHardMaximumUnits)
    {
        if (maximumInputTextElements <= 0
            || maximumInputTextElements > V1MaximumInputTextElements
            || maximumInputUtf8Bytes <= 0
            || maximumInputUtf8Bytes > V1MaximumInputUtf8Bytes
            || maximumSegments <= 0
            || maximumSegments > V1MaximumSegments
            || maximumSegmentTextElements < 8
            || maximumSegmentTextElements > V1MaximumSegmentTextElements
            || maximumSegmentUtf8Bytes < 8
            || maximumSegmentUtf8Bytes > V1MaximumSegmentUtf8Bytes
            || firstForcedHardMaximumUnits < 8
            || firstForcedHardMaximumUnits > V1FirstForcedHardMaximumUnits
            || steadyForcedHardMaximumUnits < firstForcedHardMaximumUnits
            || steadyForcedHardMaximumUnits > V1SteadyForcedHardMaximumUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumInputTextElements));
        }
        MaximumInputTextElements = maximumInputTextElements;
        MaximumInputUtf8Bytes = maximumInputUtf8Bytes;
        MaximumSegments = maximumSegments;
        MaximumSegmentTextElements = maximumSegmentTextElements;
        MaximumSegmentUtf8Bytes = maximumSegmentUtf8Bytes;
        FirstForcedHardMaximumUnits = firstForcedHardMaximumUnits;
        SteadyForcedHardMaximumUnits = steadyForcedHardMaximumUnits;
    }
}

internal sealed class D043AdaptiveTtsSegmentationTargets
{
    internal static readonly D043AdaptiveTtsSegmentationTargets ColdV1 = new(
        minimumSegmentTextElements: 4,
        firstTargetUnits: 10,
        steadyTargetUnits: 25,
        firstSoftMaximumUnits: 18,
        firstForcedHardMaximumUnits: 120,
        steadyForcedHardMaximumUnits: 120);

    internal int MinimumSegmentTextElements { get; }
    internal int FirstTargetUnits { get; }
    internal int SteadyTargetUnits { get; }
    internal int FirstSoftMaximumUnits { get; }
    internal int FirstForcedHardMaximumUnits { get; }
    internal int SteadyForcedHardMaximumUnits { get; }

    internal D043AdaptiveTtsSegmentationTargets(
        int minimumSegmentTextElements,
        int firstTargetUnits,
        int steadyTargetUnits,
        int firstSoftMaximumUnits,
        int firstForcedHardMaximumUnits,
        int steadyForcedHardMaximumUnits)
    {
        if (minimumSegmentTextElements < 1 || minimumSegmentTextElements > 8)
            throw new ArgumentOutOfRangeException(nameof(minimumSegmentTextElements));
        if (firstTargetUnits < 8 || firstTargetUnits > 14)
            throw new ArgumentOutOfRangeException(nameof(firstTargetUnits));
        if (steadyTargetUnits < 16 || steadyTargetUnits > 80)
            throw new ArgumentOutOfRangeException(nameof(steadyTargetUnits));
        if (firstSoftMaximumUnits != 18
            || firstSoftMaximumUnits < firstTargetUnits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstSoftMaximumUnits));
        }
        if (firstForcedHardMaximumUnits < firstSoftMaximumUnits
            || firstForcedHardMaximumUnits > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstForcedHardMaximumUnits));
        }
        if (steadyForcedHardMaximumUnits != 120
            || steadyForcedHardMaximumUnits < steadyTargetUnits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(steadyForcedHardMaximumUnits));
        }
        MinimumSegmentTextElements = minimumSegmentTextElements;
        FirstTargetUnits = firstTargetUnits;
        SteadyTargetUnits = steadyTargetUnits;
        FirstSoftMaximumUnits = firstSoftMaximumUnits;
        FirstForcedHardMaximumUnits = firstForcedHardMaximumUnits;
        SteadyForcedHardMaximumUnits = steadyForcedHardMaximumUnits;
    }

    internal int TargetFor(int segmentIndex)
        => segmentIndex == 0 ? FirstTargetUnits : SteadyTargetUnits;

    internal int HardMaximumFor(int segmentIndex)
        => segmentIndex == 0
            ? FirstForcedHardMaximumUnits
            : SteadyForcedHardMaximumUnits;
}

internal sealed class D043AdaptiveTtsSegment
{
    internal string TurnId { get; }
    internal int Index { get; }
    internal string Text { get; }
    internal string TextSha256 { get; }
    internal int SpeechUnits { get; }
    internal int TextElementCount { get; }
    internal int Utf8ByteCount { get; }
    internal D043AdaptiveTtsBoundaryKind BoundaryAfter { get; }
    internal bool IsTerminal { get; }
    internal string Fingerprint { get; }
    internal bool ContainsEphemeralSpeechText => true;
    internal bool MayPersistSpeechText => false;
    internal bool MayLogSpeechText => false;

    internal D043AdaptiveTtsSegment(
        string turnId,
        int index,
        string text,
        int speechUnits,
        int textElementCount,
        D043AdaptiveTtsBoundaryKind boundaryAfter,
        bool isTerminal)
    {
        TurnId = D043AdaptiveTtsFingerprint.Require(turnId, nameof(turnId));
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));
        Index = index;
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("Segment text is required.", nameof(text));
        Text = text;
        TextSha256 = D043AdaptiveTtsFingerprint.Text(Text);
        if (speechUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(speechUnits));
        SpeechUnits = speechUnits;
        if (textElementCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(textElementCount));
        TextElementCount = textElementCount;
        Utf8ByteCount = Encoding.UTF8.GetByteCount(Text);
        if (!Enum.IsDefined(
                typeof(D043AdaptiveTtsBoundaryKind),
                boundaryAfter))
        {
            throw new ArgumentOutOfRangeException(nameof(boundaryAfter));
        }
        BoundaryAfter = boundaryAfter;
        IsTerminal = isTerminal;
        if (IsTerminal != (BoundaryAfter == D043AdaptiveTtsBoundaryKind.Terminal))
        {
            throw new ArgumentException(
                "Terminal state and boundary kind must agree.",
                nameof(isTerminal));
        }
        Fingerprint = D043AdaptiveTtsFingerprint.Canonical(
            "D043.AdaptiveTts.Segment.V1",
            TurnId,
            Index,
            TextSha256,
            SpeechUnits,
            TextElementCount,
            Utf8ByteCount,
            BoundaryAfter,
            IsTerminal);
    }

    public override string ToString()
        => "D043AdaptiveTtsSegment(index="
            + Index.ToString(CultureInfo.InvariantCulture)
            + ", content-redacted)";
}

internal sealed class D043AdaptiveTtsSegmentationCreation
{
    internal D043AdaptiveTtsSegmentationAdmission Admission { get; }
    internal D043AdaptiveTtsLazySegmenter? Segmenter { get; }
    internal bool Created =>
        Admission == D043AdaptiveTtsSegmentationAdmission.Created;

    internal D043AdaptiveTtsSegmentationCreation(
        D043AdaptiveTtsSegmentationAdmission admission,
        D043AdaptiveTtsLazySegmenter? segmenter)
    {
        if (!Enum.IsDefined(
                typeof(D043AdaptiveTtsSegmentationAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == D043AdaptiveTtsSegmentationAdmission.Created)
            != (segmenter != null))
        {
            throw new ArgumentException(
                "Creation admission and segmenter presence must agree.",
                nameof(segmenter));
        }
        Admission = admission;
        Segmenter = segmenter;
    }
}

internal sealed class D043AdaptiveTtsSegmentTakeResult
{
    internal D043AdaptiveTtsSegmentTakeAdmission Admission { get; }
    internal D043AdaptiveTtsSegment? Segment { get; }
    internal bool Created =>
        Admission == D043AdaptiveTtsSegmentTakeAdmission.Created;

    internal D043AdaptiveTtsSegmentTakeResult(
        D043AdaptiveTtsSegmentTakeAdmission admission,
        D043AdaptiveTtsSegment? segment)
    {
        if (!Enum.IsDefined(
                typeof(D043AdaptiveTtsSegmentTakeAdmission),
                admission))
        {
            throw new ArgumentOutOfRangeException(nameof(admission));
        }
        if ((admission == D043AdaptiveTtsSegmentTakeAdmission.Created)
            != (segment != null))
        {
            throw new ArgumentException(
                "Take admission and segment presence must agree.",
                nameof(segment));
        }
        Admission = admission;
        Segment = segment;
    }
}

internal sealed class D043AdaptiveTtsLazySegmenter
{
    private const int MaximumConsecutiveNonSpeakableTextElementsV1 = 8;
    private const int MaximumDynamicSegmentTextElementsV1 = 120;
    private static readonly HashSet<string> NonTerminalAbbreviations = new(
        new[]
        {
            "dr", "mr", "mrs", "ms", "prof", "sr", "jr", "st",
            "vs", "e.g", "i.e", "no", "fig", "u.s", "u.k"
        },
        StringComparer.OrdinalIgnoreCase);

    private readonly D043AdaptiveTtsSegmentationCaps _caps;
    private readonly int[] _elementStarts;
    private readonly string _turnId;
    private readonly string _sourceTextSha256;
    private string? _speechText;
    private int _elementOffset;
    private int _nextSegmentIndex;
    private bool _completed;
    private bool _retired;
    private int _activeMinimumTextElements;
    private int _activeHardMaximumTextElements;
    private readonly bool _preserveJapaneseCueContinuation;

    internal bool IsCompleted => _completed;
    internal bool IsRetired => _retired;
    internal int NextSegmentIndex => _nextSegmentIndex;
    internal int RemainingTextElements => _speechText == null
        ? 0
        : Math.Max(0, _elementStarts.Length - _elementOffset);
    internal string SourceTextSha256 => _sourceTextSha256;

    private D043AdaptiveTtsLazySegmenter(
        D043AdaptiveTtsTurnRequest request,
        D043AdaptiveTtsSegmentationCaps caps,
        int[] elementStarts,
        bool preserveJapaneseCueContinuation)
    {
        _caps = caps;
        _elementStarts = elementStarts;
        _turnId = request.TurnId;
        _sourceTextSha256 = request.SpeechTextSha256;
        _speechText = request.SpeechText;
        _preserveJapaneseCueContinuation = preserveJapaneseCueContinuation
            && request.Authority.Language == D043AdaptiveTtsLanguage.Japanese;
    }

    internal static D043AdaptiveTtsSegmentationCreation TryCreate(
        D043AdaptiveTtsTurnRequest request,
        D043AdaptiveTtsSegmentationCaps? caps = null,
        bool preserveJapaneseCueContinuation = false)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        caps ??= D043AdaptiveTtsSegmentationCaps.V1;
        if (!caps.IsWithinV1Policy)
        {
            return Reject(
                D043AdaptiveTtsSegmentationAdmission
                    .SegmentationCapsOutOfRange);
        }
        if (request.SpeechText.Length == 0)
        {
            return Reject(D043AdaptiveTtsSegmentationAdmission.Empty);
        }
        if (!HasValidUtf16(request.SpeechText))
        {
            return Reject(
                D043AdaptiveTtsSegmentationAdmission.InvalidUnicode);
        }
        if (!string.Equals(
                D043AdaptiveTtsFingerprint.Text(request.SpeechText),
                request.Authority.SpeechProjectionSha256,
                StringComparison.Ordinal))
        {
            return Reject(
                D043AdaptiveTtsSegmentationAdmission.AuthorityMismatch);
        }
        var starts = StringInfo.ParseCombiningCharacters(request.SpeechText);
        if (starts.Length > caps.MaximumInputTextElements)
        {
            return Reject(D043AdaptiveTtsSegmentationAdmission
                .InputTextElementLimitExceeded);
        }
        var maximumSegmentCapacity = checked(
            (long)Math.Min(
                caps.FirstForcedHardMaximumUnits,
                caps.MaximumSegmentTextElements)
            + ((long)caps.MaximumSegments - 1)
            * Math.Min(
                caps.SteadyForcedHardMaximumUnits,
                caps.MaximumSegmentTextElements));
        if (starts.Length > maximumSegmentCapacity)
        {
            return Reject(
                D043AdaptiveTtsSegmentationAdmission
                    .InputSegmentCapacityExceeded);
        }
        var inputUtf8Bytes = Encoding.UTF8.GetByteCount(request.SpeechText);
        if (inputUtf8Bytes > caps.MaximumInputUtf8Bytes)
        {
            return Reject(D043AdaptiveTtsSegmentationAdmission
                .InputUtf8LimitExceeded);
        }
        if (inputUtf8Bytes
            > (long)caps.MaximumSegments * caps.MaximumSegmentUtf8Bytes)
        {
            return Reject(
                D043AdaptiveTtsSegmentationAdmission
                    .InputSegmentCapacityExceeded);
        }
        if (HasOversizedMinimumUtf8Window(
                request.SpeechText,
                starts,
                MaximumDynamicSegmentTextElementsV1,
                caps.MaximumSegmentUtf8Bytes))
        {
            return Reject(
                D043AdaptiveTtsSegmentationAdmission
                    .SegmentUtf8WindowLimitExceeded);
        }
        if (CountSpeechUnits(request.SpeechText, starts, 0, starts.Length) == 0)
        {
            return Reject(
                D043AdaptiveTtsSegmentationAdmission.NoSpeakableContent);
        }
        if (HasExcessiveNonSpeakableRun(
                request.SpeechText,
                starts,
                MaximumConsecutiveNonSpeakableTextElementsV1))
        {
            return Reject(
                D043AdaptiveTtsSegmentationAdmission
                    .NonSpeakableRunLimitExceeded);
        }
        return new D043AdaptiveTtsSegmentationCreation(
            D043AdaptiveTtsSegmentationAdmission.Created,
            new D043AdaptiveTtsLazySegmenter(request, caps, starts, preserveJapaneseCueContinuation));
    }

    internal D043AdaptiveTtsSegmentTakeResult TryTakeNext(
        D043AdaptiveTtsSegmentationTargets targets)
    {
        if (targets == null)
            throw new ArgumentNullException(nameof(targets));
        if (_retired)
        {
            return new D043AdaptiveTtsSegmentTakeResult(
                D043AdaptiveTtsSegmentTakeAdmission.Retired,
                null);
        }
        if (IsCompleted)
        {
            return new D043AdaptiveTtsSegmentTakeResult(
                D043AdaptiveTtsSegmentTakeAdmission.Completed,
                null);
        }
        if (_nextSegmentIndex >= _caps.MaximumSegments)
        {
            return new D043AdaptiveTtsSegmentTakeResult(
                D043AdaptiveTtsSegmentTakeAdmission.SegmentLimitExceeded,
                null);
        }

        var startElement = _elementOffset;
        var startCharacter = _elementStarts[startElement];
        var targetTextElements = targets.TargetFor(_nextSegmentIndex);
        var hardMaximumTextElements = Math.Min(
            targets.HardMaximumFor(_nextSegmentIndex),
            _nextSegmentIndex == 0
                ? _caps.FirstForcedHardMaximumUnits
                : _caps.SteadyForcedHardMaximumUnits);
        var remainingSlots = _caps.MaximumSegments - _nextSegmentIndex;
        var futureSteadyCapacity = Math.Min(
            Math.Min(
                targets.SteadyForcedHardMaximumUnits,
                _caps.SteadyForcedHardMaximumUnits),
            _caps.MaximumSegmentTextElements);
        var requiredForRemainingCapacity = checked(
            (long)_elementStarts.Length - startElement
            - ((long)remainingSlots - 1) * futureSteadyCapacity);
        var minimumTextElements = (int)Math.Max(
            targets.MinimumSegmentTextElements,
            requiredForRemainingCapacity);
        if (minimumTextElements > hardMaximumTextElements)
        {
            return new D043AdaptiveTtsSegmentTakeResult(
                D043AdaptiveTtsSegmentTakeAdmission.SegmentLimitExceeded,
                null);
        }
        targetTextElements = Math.Max(targetTextElements, minimumTextElements);
        _activeMinimumTextElements = minimumTextElements;
        _activeHardMaximumTextElements = hardMaximumTextElements;
        var units = 0;
        var utf8Bytes = 0;
        var lastSoftEndElement = -1;
        var lastWhiteSpaceEndElement = -1;
        var quoteDepth = 0;
        var bracketDepth = 0;
        var pendingQuotedSentence = false;

        for (var index = startElement; index < _elementStarts.Length; index++)
        {
            var element = ElementAt(index);
            var elementBytes = Encoding.UTF8.GetByteCount(element);
            var elementCount = index - startElement + 1;
            if (elementCount > _caps.MaximumSegmentTextElements
                || utf8Bytes + elementBytes > _caps.MaximumSegmentUtf8Bytes)
            {
                var capEnd = PreferredForcedEnd(
                    startElement,
                    index,
                    lastSoftEndElement,
                    lastWhiteSpaceEndElement);
                return CommitSegment(
                    startElement,
                    capEnd,
                    D043AdaptiveTtsBoundaryKind.ForcedHardLimit);
            }
            utf8Bytes += elementBytes;
            units += SpeechUnitsAt(index);

            var wasClosingQuote = IsClosingQuote(element);
            if (IsOpeningQuote(element))
                quoteDepth++;
            else if (wasClosingQuote && quoteDepth > 0)
                quoteDepth--;
            else if (IsOpeningBracket(element))
                bracketDepth++;
            else if (IsClosingBracket(element) && bracketDepth > 0)
                bracketDepth--;

            var boundary = ClassifyBoundaryAfter(index);
            var outsideNesting = quoteDepth == 0 && bracketDepth == 0;
            if (boundary == D043AdaptiveTtsBoundaryKind.Sentence
                && quoteDepth > 0)
            {
                pendingQuotedSentence = true;
            }
            if (wasClosingQuote
                && pendingQuotedSentence
                && outsideNesting)
            {
                boundary = D043AdaptiveTtsBoundaryKind.Sentence;
                pendingQuotedSentence = false;
            }

            if (outsideNesting
                && boundary is D043AdaptiveTtsBoundaryKind.Sentence
                    or D043AdaptiveTtsBoundaryKind.Paragraph)
            {
                var end = ExtendThroughClosersAndWhiteSpace(index + 1);
                if ((end - startElement >= minimumTextElements
                    || end == _elementStarts.Length)
                    && !(boundary == D043AdaptiveTtsBoundaryKind.Sentence
                        && KeepJapaneseCueWithBody(startElement, end)))
                {
                    return CommitSegment(startElement, end, boundary.Value);
                }
            }
            if (outsideNesting
                && boundary == D043AdaptiveTtsBoundaryKind.SoftPunctuation)
            {
                var softEnd = ExtendThroughClosersAndWhiteSpace(index + 1);
                if (!KeepJapaneseCueWithBody(startElement, softEnd))
                    lastSoftEndElement = softEnd;
            }
            if (outsideNesting && IsWhiteSpace(element)
                && !KeepJapaneseCueWithBody(startElement, index + 1))
                lastWhiteSpaceEndElement = index + 1;

            if (elementCount >= targetTextElements)
            {
                if (lastSoftEndElement - startElement
                    >= minimumTextElements)
                {
                    return CommitSegment(
                        startElement,
                        lastSoftEndElement,
                        D043AdaptiveTtsBoundaryKind.SoftPunctuation);
                }
            }
            if (elementCount >= hardMaximumTextElements)
            {
                var end = PreferredForcedEnd(
                    startElement,
                    index + 1,
                    lastSoftEndElement,
                    lastWhiteSpaceEndElement);
                return CommitSegment(
                    startElement,
                    end,
                    D043AdaptiveTtsBoundaryKind.ForcedHardLimit);
            }
        }

        return CommitSegment(
            startElement,
            _elementStarts.Length,
            D043AdaptiveTtsBoundaryKind.Terminal);
    }

    public override string ToString()
        => "D043AdaptiveTtsLazySegmenter(next="
            + _nextSegmentIndex.ToString(CultureInfo.InvariantCulture)
            + ", state="
            + (_completed ? "completed" : _retired ? "retired" : "active")
            + ", content-redacted)";

    internal void Retire()
    {
        if (_completed || _retired)
            return;
        _speechText = null;
        _retired = true;
    }

    private bool KeepJapaneseCueWithBody(int start, int end)
    {
        if (!_preserveJapaneseCueContinuation || _speechText == null || end >= _elementStarts.Length)
            return false;
        var prefix = _speechText.Substring(_elementStarts[start], _elementStarts[end] - _elementStarts[start]);
        if (prefix.Contains('\n') || !JapaneseSpeechDeliveryPolicy.IsCueOnly(prefix.Trim())) return false;
        var remaining = _speechText.Substring(_elementStarts[end]).TrimStart();
        if (JapaneseSpeechDeliveryPolicy.TryLeadingCue(_speechText.Substring(_elementStarts[start]),
                out var fullCue, out _) && fullCue.ReferenceCue == "breath_cycle") return true;
        return remaining.Length != 0 && !JapaneseSpeechDeliveryPolicy.TryLeadingCue(remaining, out _, out _);
    }

    private D043AdaptiveTtsSegmentTakeResult CommitSegment(
        int startElement,
        int endElement,
        D043AdaptiveTtsBoundaryKind boundary)
    {
        if (endElement <= startElement || endElement > _elementStarts.Length)
        {
            return new D043AdaptiveTtsSegmentTakeResult(
                D043AdaptiveTtsSegmentTakeAdmission.SegmentCouldNotAdvance,
                null);
        }
        var startCharacter = _elementStarts[startElement];
        var speechText = SpeechText;
        if (endElement - startElement > _activeHardMaximumTextElements)
        {
            endElement = checked(startElement + _activeHardMaximumTextElements);
            boundary = D043AdaptiveTtsBoundaryKind.ForcedHardLimit;
        }
        if (endElement - startElement < _activeMinimumTextElements
            && endElement != _elementStarts.Length)
        {
            return new D043AdaptiveTtsSegmentTakeResult(
                D043AdaptiveTtsSegmentTakeAdmission.SegmentCouldNotAdvance,
                null);
        }
        if (endElement < _elementStarts.Length
            && CountSpeechUnits(
                speechText,
                _elementStarts,
                endElement,
                _elementStarts.Length) == 0)
        {
            if (_elementStarts.Length - startElement
                <= _activeHardMaximumTextElements)
            {
                endElement = _elementStarts.Length;
            }
            else
            {
                var shifted = endElement;
                while (shifted - startElement
                       > _activeMinimumTextElements)
                {
                    shifted--;
                    if (_elementStarts.Length - shifted
                            <= _caps.SteadyForcedHardMaximumUnits
                        && CountSpeechUnits(
                            speechText,
                            _elementStarts,
                            shifted,
                            _elementStarts.Length) > 0)
                    {
                        endElement = shifted;
                        boundary = D043AdaptiveTtsBoundaryKind.SoftLimit;
                        break;
                    }
                }
                if (CountSpeechUnits(
                        speechText,
                        _elementStarts,
                        endElement,
                        _elementStarts.Length) == 0)
                {
                    return new D043AdaptiveTtsSegmentTakeResult(
                        D043AdaptiveTtsSegmentTakeAdmission
                            .SegmentCouldNotAdvance,
                        null);
                }
            }
        }
        var endCharacter = endElement == _elementStarts.Length
            ? speechText.Length
            : _elementStarts[endElement];
        var text = speechText.Substring(
            startCharacter,
            endCharacter - startCharacter);
        var units = CountSpeechUnits(
            speechText,
            _elementStarts,
            startElement,
            endElement);
        if (units <= 0)
        {
            return new D043AdaptiveTtsSegmentTakeResult(
                D043AdaptiveTtsSegmentTakeAdmission.SegmentCouldNotAdvance,
                null);
        }
        var terminal = endElement == _elementStarts.Length;
        if (_preserveJapaneseCueContinuation && text.TrimEnd(' ', '\t').EndsWith("\n", StringComparison.Ordinal))
            boundary = D043AdaptiveTtsBoundaryKind.Paragraph;
        if (terminal)
            boundary = D043AdaptiveTtsBoundaryKind.Terminal;
        var segment = new D043AdaptiveTtsSegment(
            _turnId,
            _nextSegmentIndex,
            text,
            units,
            endElement - startElement,
            boundary,
            terminal);
        if (segment.TextElementCount > _caps.MaximumSegmentTextElements
            || segment.Utf8ByteCount > _caps.MaximumSegmentUtf8Bytes)
        {
            return new D043AdaptiveTtsSegmentTakeResult(
                D043AdaptiveTtsSegmentTakeAdmission.SegmentCouldNotAdvance,
                null);
        }
        _elementOffset = endElement;
        _nextSegmentIndex++;
        if (terminal)
        {
            _completed = true;
            _speechText = null;
        }
        return new D043AdaptiveTtsSegmentTakeResult(
            D043AdaptiveTtsSegmentTakeAdmission.Created,
            segment);
    }

    private int PreferredForcedEnd(
        int startElement,
        int fallbackEndElement,
        int softEndElement,
        int whiteSpaceEndElement)
    {
        if (softEndElement - startElement >= _activeMinimumTextElements)
            return softEndElement;
        if (whiteSpaceEndElement - startElement >= _activeMinimumTextElements)
            return whiteSpaceEndElement;
        return Math.Min(
            startElement + _activeHardMaximumTextElements,
            Math.Max(
                startElement + _activeMinimumTextElements,
                fallbackEndElement));
    }

    private int ExtendThroughClosersAndWhiteSpace(int endElement)
    {
        var result = endElement;
        while (result < _elementStarts.Length)
        {
            var element = ElementAt(result);
            if (!IsClosingQuote(element)
                && !IsClosingBracket(element)
                && !IsWhiteSpace(element))
            {
                break;
            }
            var candidateCount = result - _elementOffset + 1;
            var start = _elementStarts[_elementOffset];
            var speechText = SpeechText;
            var end = result + 1 == _elementStarts.Length
                ? speechText.Length
                : _elementStarts[result + 1];
            if (candidateCount > _activeHardMaximumTextElements
                || candidateCount > _caps.MaximumSegmentTextElements
                || Encoding.UTF8.GetByteCount(
                    speechText.Substring(start, end - start))
                    > _caps.MaximumSegmentUtf8Bytes)
            {
                break;
            }
            result++;
        }
        return result;
    }

    private D043AdaptiveTtsBoundaryKind? ClassifyBoundaryAfter(int index)
    {
        var element = ElementAt(index);
        if (IsParagraphBreakAt(index))
            return D043AdaptiveTtsBoundaryKind.Paragraph;
        if (element == ".")
        {
            return IsTerminalPeriod(index)
                ? D043AdaptiveTtsBoundaryKind.Sentence
                : null;
        }
        if (element is "…")
        {
            return index + 1 < _elementStarts.Length
                   && ElementAt(index + 1) == element
                ? null
                : D043AdaptiveTtsBoundaryKind.Sentence;
        }
        if (element is "。" or "！" or "!" or "？" or "?")
        {
            return index + 1 < _elementStarts.Length
                   && ElementAt(index + 1) is "。" or "！" or "!" or "？" or "?"
                ? null
                : D043AdaptiveTtsBoundaryKind.Sentence;
        }
        if (element is "，" or "、" or "；" or ";" or "：" or ":" or ",")
        {
            if (IsNumericSeparator(index))
                return null;
            return D043AdaptiveTtsBoundaryKind.SoftPunctuation;
        }
        if (element == "—")
        {
            return index + 1 < _elementStarts.Length
                   && ElementAt(index + 1) == element
                ? null
                : D043AdaptiveTtsBoundaryKind.SoftPunctuation;
        }
        return null;
    }

    private bool IsTerminalPeriod(int index)
    {
        if (IsNumericSeparator(index))
            return false;
        var previousPeriod = index > 0 && ElementAt(index - 1) == ".";
        var nextPeriod = index + 1 < _elementStarts.Length
                         && ElementAt(index + 1) == ".";
        if (nextPeriod)
            return false;
        if (previousPeriod)
            return true;
        if (index + 1 < _elementStarts.Length
            && IsAsciiLetterOrDigit(ElementAt(index + 1)))
        {
            return false;
        }
        var token = TokenBeforePeriod(index);
        if (NonTerminalAbbreviations.Contains(token)
            || IsInitialism(token))
        {
            return false;
        }
        return true;
    }

    private string TokenBeforePeriod(int periodIndex)
    {
        var builder = new StringBuilder();
        for (var index = periodIndex - 1; index >= 0; index--)
        {
            var element = ElementAt(index);
            if (!(IsAsciiLetterOrDigit(element) || element == "."))
                break;
            builder.Insert(0, element);
        }
        return builder.ToString().Trim('.');
    }

    private static bool IsInitialism(string token)
    {
        if (token.Length < 3 || token.IndexOf('.') < 0)
            return false;
        var pieces = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length < 2)
            return false;
        foreach (var piece in pieces)
        {
            if (piece.Length != 1 || !char.IsLetter(piece[0]))
                return false;
        }
        return true;
    }

    private bool IsNumericSeparator(int index)
        => index > 0
           && index + 1 < _elementStarts.Length
           && IsAsciiDigit(ElementAt(index - 1))
           && IsAsciiDigit(ElementAt(index + 1));

    private bool IsParagraphBreakAt(int index)
    {
        var element = ElementAt(index);
        if (!ContainsNewLine(element))
            return false;
        if (index + 1 < _elementStarts.Length
            && ContainsNewLine(ElementAt(index + 1)))
        {
            return false;
        }
        return index > 0 && ContainsNewLine(ElementAt(index - 1));
    }

    private int SpeechUnitsAt(int index)
    {
        var element = ElementAt(index);
        if (IsWhiteSpace(element) || IsPunctuation(element))
            return 0;
        if (IsAsciiLetterOrDigit(element))
        {
            return IsAsciiTokenContinuation(index) ? 0 : 2;
        }
        var rune = Rune.GetRuneAt(element, 0);
        if (IsCjkOrKana(rune.Value))
            return 1;
        var category = CharUnicodeInfo.GetUnicodeCategory(element, 0);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format
            ? 0
            : 1;
    }

    private bool IsAsciiTokenContinuation(int index)
    {
        if (index <= 0)
            return false;
        var previous = ElementAt(index - 1);
        if (IsAsciiLetterOrDigit(previous))
            return true;
        if (!IsAsciiTokenConnector(previous) || index <= 1)
            return false;
        return IsAsciiLetterOrDigit(ElementAt(index - 2));
    }

    private string ElementAt(int index)
    {
        var start = _elementStarts[index];
        var speechText = SpeechText;
        var end = index + 1 == _elementStarts.Length
            ? speechText.Length
            : _elementStarts[index + 1];
        return speechText.Substring(start, end - start);
    }

    private string SpeechText => _speechText
        ?? throw new InvalidOperationException(
            "A retired or completed segmenter has no retained speech text.");

    private static int CountSpeechUnits(
        string text,
        int[] starts,
        int startElement,
        int endElement)
    {
        var units = 0;
        for (var index = startElement; index < endElement; index++)
        {
            var start = starts[index];
            var end = index + 1 == starts.Length
                ? text.Length
                : starts[index + 1];
            var element = text.Substring(start, end - start);
            if (IsWhiteSpace(element) || IsPunctuation(element))
                continue;
            if (IsAsciiLetterOrDigit(element))
            {
                var continuation = index > startElement
                    && (IsAsciiLetterOrDigit(Element(text, starts, index - 1))
                        || (IsAsciiTokenConnector(Element(text, starts, index - 1))
                            && index > startElement + 1
                            && IsAsciiLetterOrDigit(
                                Element(text, starts, index - 2))));
                if (!continuation)
                    units += 2;
                continue;
            }
            var rune = Rune.GetRuneAt(element, 0);
            if (IsCjkOrKana(rune.Value))
            {
                units++;
                continue;
            }
            var category = CharUnicodeInfo.GetUnicodeCategory(element, 0);
            if (category is not UnicodeCategory.NonSpacingMark
                and not UnicodeCategory.EnclosingMark
                and not UnicodeCategory.Format)
            {
                units++;
            }
        }
        return units;
    }

    private static string Element(string text, int[] starts, int index)
    {
        var start = starts[index];
        var end = index + 1 == starts.Length ? text.Length : starts[index + 1];
        return text.Substring(start, end - start);
    }

    private static bool HasValidUtf16(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (index + 1 >= text.Length
                    || !char.IsLowSurrogate(text[index + 1]))
                {
                    return false;
                }
                index++;
            }
            else if (char.IsLowSurrogate(text[index]))
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasExcessiveNonSpeakableRun(
        string text,
        int[] starts,
        int maximumRun)
    {
        var run = 0;
        for (var index = 0; index < starts.Length; index++)
        {
            if (CountSpeechUnits(text, starts, index, index + 1) == 0)
            {
                run++;
                if (run > maximumRun)
                    return true;
            }
            else
            {
                run = 0;
            }
        }
        return false;
    }

    private static bool HasOversizedMinimumUtf8Window(
        string text,
        int[] elementStarts,
        int maximumWindowTextElements,
        int maximumUtf8Bytes)
    {
        var elementBytes = new int[elementStarts.Length];
        var windowBytes = 0;
        for (var end = 0; end < elementStarts.Length; end++)
        {
            var startCharacter = elementStarts[end];
            var endCharacter = end + 1 == elementStarts.Length
                ? text.Length
                : elementStarts[end + 1];
            elementBytes[end] = Encoding.UTF8.GetByteCount(
                text.AsSpan(startCharacter, endCharacter - startCharacter));
            windowBytes = checked(windowBytes + elementBytes[end]);
            if (end >= maximumWindowTextElements)
                windowBytes -= elementBytes[end - maximumWindowTextElements];
            if (windowBytes > maximumUtf8Bytes)
                return true;
        }
        return false;
    }

    private static bool IsCjkOrKana(int value)
        => value is >= 0x2E80 and <= 0x9FFF
            or >= 0xF900 and <= 0xFAFF
            or >= 0x3040 and <= 0x30FF
            or >= 0x31F0 and <= 0x31FF
            or >= 0x3400 and <= 0x4DBF
            or >= 0x20000 and <= 0x323AF;

    private static bool IsAsciiLetterOrDigit(string value)
        => value.Length == 1
           && ((value[0] >= 'a' && value[0] <= 'z')
               || (value[0] >= 'A' && value[0] <= 'Z')
               || (value[0] >= '0' && value[0] <= '9'));

    private static bool IsAsciiDigit(string value)
        => value.Length == 1 && value[0] >= '0' && value[0] <= '9';

    private static bool IsAsciiTokenConnector(string value)
        => value is "." or "-" or "_" or "@" or "/" or ":" or "#" or "+";

    private static bool IsPunctuation(string value)
    {
        var category = CharUnicodeInfo.GetUnicodeCategory(value, 0);
        return category is UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;
    }

    private static bool IsWhiteSpace(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
                return false;
        }
        return value.Length > 0;
    }

    private static bool ContainsNewLine(string value)
        => value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;

    private static bool IsOpeningQuote(string value)
        => value is "「" or "『" or "“" or "‘";

    private static bool IsClosingQuote(string value)
        => value is "」" or "』" or "”" or "’";

    private static bool IsOpeningBracket(string value)
        => value is "(" or "（" or "[" or "［" or "【" or "《" or "〈";

    private static bool IsClosingBracket(string value)
        => value is ")" or "）" or "]" or "］" or "】" or "》" or "〉";

    private static D043AdaptiveTtsSegmentationCreation Reject(
        D043AdaptiveTtsSegmentationAdmission admission)
        => new(admission, null);
}
