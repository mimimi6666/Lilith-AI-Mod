using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LilithRuntimeContracts;

namespace LilithGameBridgeContracts;

// Speech-only advice, never avatar state or an arbitrary reference path.
public static class LilithJapaneseSpeechEmotion
{
    public static bool IsValid(string? value) => value is "" or "calm" or "gentle"
        or "playful" or "affectionate" or "excited" or "relieved" or "surprised";
}

public static class LilithGameBridgeEndpoint
{
    public const string PipeName = "lilith-runtime-game-v1";
    public const int MaximumServerInstances =
        NamedPipeServerStream.MaxAllowedServerInstances;
    public const PipeOptions RequiredServerOptions =
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;

    public static NamedPipeServerStream CreateCurrentUserServer()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Lilith game bridge current-user pipes require Windows.");
        }

        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            MaximumServerInstances,
            PipeTransmissionMode.Byte,
            RequiredServerOptions,
            LilithGameBridgeProtocol.MaximumFrameBytes,
            LilithGameBridgeProtocol.MaximumFrameBytes);
    }

    public static NamedPipeClientStream CreateCurrentUserClient()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Lilith game bridge current-user pipes require Windows.");
        }

        return new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
    }
}

public enum LilithGameBridgeInputSurface
{
    Text = 1,
    Voice = 2
}

public enum LilithGameBridgeTerminal
{
    Completed = 1,
    Failed = 2,
    Interrupted = 3
}

public enum LilithGameBridgeForegroundProactiveState
{
    Streaming = 1,
    Completed = 2,
    Silent = 3,
    Abandoned = 4
}

public enum LilithGameBridgeDisplayLocale
{
    TraditionalChinese = 1,
    SimplifiedChinese = 2,
    Japanese = 3,
    English = 4
}

public enum LilithGameBridgeNoteDraftState
{
    Ready = 1,
    Silent = 2,
    Busy = 3,
    Failed = 4
}

// A bounded text draft only. Neither the request nor its receipt authorizes
// native NoteInbox delivery, filesystem access, conversation or speech output.
public sealed class LilithGameBridgeNoteDraftRequest : LilithGameBridgeClientRequest
{
    public const string NoteDraftMethod = "game.note.draft";
    public override string Method => NoteDraftMethod;
    public Guid EventId { get; }
    public string Category { get; }
    public string Topic { get; }
    public string Context { get; }
    public LilithGameBridgeDisplayLocale DisplayLocale { get; }

    public LilithGameBridgeNoteDraftRequest(
        Guid generationId, Guid requestId, long sequence, Guid eventId,
        string category, string topic, string context,
        LilithGameBridgeDisplayLocale displayLocale)
        : base(generationId, requestId, sequence)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A note event identity is required.", nameof(eventId));
        if (category is not ("upcoming" or "achievement" or "comfort" or "promise" or "bond"))
            throw new ArgumentException("The note category was rejected.", nameof(category));
        EventId = eventId;
        Category = category;
        Topic = LilithGameBridgeProtocol.ValidateNoteText(
            topic, nameof(topic), LilithGameBridgeProtocol.MaximumNoteContextCharacters, false);
        Context = LilithGameBridgeProtocol.ValidateNoteText(
            context, nameof(context), LilithGameBridgeProtocol.MaximumNoteContextCharacters, true);
        _ = LilithGameBridgeProtocol.NoteDraftDisplayLocaleToken(displayLocale);
        DisplayLocale = displayLocale;
    }
}

public sealed class LilithGameBridgeNoteDraftResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method => LilithGameBridgeNoteDraftRequest.NoteDraftMethod;
    public Guid EventId { get; }
    public LilithGameBridgeNoteDraftState State { get; }
    public string Text { get; }

    public LilithGameBridgeNoteDraftResponse(
        Guid generationId, Guid requestId, long sequence, Guid eventId,
        LilithGameBridgeNoteDraftState state, string text)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty || eventId == Guid.Empty)
            throw new ArgumentException("Exact note operation and event identities are required.");
        _ = LilithGameBridgeProtocol.NoteDraftStateToken(state);
        Text = LilithGameBridgeProtocol.ValidateNoteText(
            text, nameof(text), LilithGameBridgeProtocol.MaximumNoteDraftCharacters,
            state != LilithGameBridgeNoteDraftState.Ready);
        if (state != LilithGameBridgeNoteDraftState.Ready && Text.Length != 0)
            throw new ArgumentException("A non-ready note draft must not contain text.", nameof(text));
        RequestId = requestId;
        EventId = eventId;
        State = state;
    }
}

public enum LilithGameBridgeSpeechLocale
{
    Chinese = 1,
    Japanese = 2
}

public enum LilithGameBridgeVoiceLanguage
{
    Japanese = 1,
    TraditionalChinese = 2,
    Automatic = 3,
    English = 4
}

public enum LilithGameBridgeVoiceStopDisposition
{
    Transcribe = 1,
    Cancel = 2
}

public abstract class LilithGameBridgeFrame
{
    public int Version => LilithGameBridgeProtocol.Version;
    public Guid GenerationId { get; }
    public long Sequence { get; }

    internal abstract string Kind { get; }

    protected LilithGameBridgeFrame(Guid generationId, long sequence)
    {
        if (generationId == Guid.Empty)
            throw new ArgumentException("Generation identity is required.", nameof(generationId));
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        GenerationId = generationId;
        Sequence = sequence;
    }
}

public sealed class LilithGameBridgeHello : LilithGameBridgeFrame
{
    internal override string Kind => "runtime.hello";
    public bool Ready { get; }

    public LilithGameBridgeHello(Guid generationId, long sequence, bool ready)
        : base(generationId, sequence)
    {
        Ready = ready;
    }
}

public abstract class LilithGameBridgeClientRequest : LilithGameBridgeFrame
{
    internal override string Kind => "game.request";
    public Guid RequestId { get; }
    public abstract string Method { get; }

    protected LilithGameBridgeClientRequest(
        Guid generationId,
        Guid requestId,
        long sequence)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        RequestId = requestId;
    }
}

public sealed class LilithGameBridgeRequest : LilithGameBridgeClientRequest
{
    public const string SubmitMethod = "game.chat.submit";

    public override string Method => SubmitMethod;
    public Guid TurnId { get; }
    public LilithGameBridgeInputSurface Surface { get; }
    public string Text { get; }
    public LilithGameBridgeDisplayLocale DisplayLocale { get; }
    public LilithGameBridgeSpeechLocale SpeechLocale { get; }
    public LilithMiniGamePublicContext? MiniGameContext { get; }
    public LilithMiniGameDiagnosticRequest? MiniGameDiagnostic { get; }
    public LilithMiniGameAnswerRepair? MiniGameAnswerRepair { get; }

    public LilithGameBridgeRequest(
        Guid generationId, Guid requestId, long sequence, Guid turnId,
        LilithGameBridgeInputSurface surface, string text,
        LilithGameBridgeDisplayLocale displayLocale, LilithGameBridgeSpeechLocale speechLocale,
        LilithMiniGamePublicContext? miniGameContext = null)
        : this(generationId, requestId, sequence, turnId, surface, text, displayLocale,
            speechLocale, miniGameContext, null)
    {
    }

    public LilithGameBridgeRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid turnId,
        LilithGameBridgeInputSurface surface,
        string text,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        LilithMiniGamePublicContext? miniGameContext,
        LilithMiniGameDiagnosticRequest? miniGameDiagnostic,
        LilithMiniGameAnswerRepair? miniGameAnswerRepair = null)
        : base(generationId, requestId, sequence)
    {
        if (turnId == Guid.Empty)
            throw new ArgumentException("Turn identity is required.", nameof(turnId));
        if (!Enum.IsDefined(typeof(LilithGameBridgeInputSurface), surface))
            throw new ArgumentOutOfRangeException(nameof(surface));
        if (!Enum.IsDefined(typeof(LilithGameBridgeDisplayLocale), displayLocale))
            throw new ArgumentOutOfRangeException(nameof(displayLocale));
        if (!Enum.IsDefined(typeof(LilithGameBridgeSpeechLocale), speechLocale))
            throw new ArgumentOutOfRangeException(nameof(speechLocale));
        TurnId = turnId;
        Surface = surface;
        Text = LilithGameBridgeProtocol.ValidateConversationText(
            text,
            nameof(text),
            allowEmpty: false);
        DisplayLocale = displayLocale;
        SpeechLocale = speechLocale;
        MiniGameContext = miniGameContext;
        MiniGameDiagnostic = miniGameDiagnostic;
        if (miniGameAnswerRepair != null && !miniGameAnswerRepair.IsSupportedBy(miniGameContext))
            throw new InvalidDataException("Mini-game answer repair requires an active supported context.");
        MiniGameAnswerRepair = miniGameAnswerRepair;
    }

    public LilithGameBridgeRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid turnId,
        LilithGameBridgeInputSurface surface,
        string text)
        : this(
            generationId,
            requestId,
            sequence,
            turnId,
            surface,
            text,
            LilithGameBridgeDisplayLocale.TraditionalChinese,
            LilithGameBridgeSpeechLocale.Chinese)
    {
    }
}

// Official game display-name data only: never an instruction, account identity,
// player utterance, or a grant to mutate the original game archive.
public static class LilithPlayerDisplayName
{
    public const int MaximumUtf8Bytes = 256;
    public const string ProfileId = "lilith-core-3c5fd918";
    public const string Source = "official_game_archive";
    public const string StateFileName = "player-display-name.v1.json";

    public static string Validate(string value)
    {
        if (value is null || (value.Length > 0 && string.IsNullOrWhiteSpace(value)))
            throw new ArgumentException("Player display name data is invalid.");
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(value) > MaximumUtf8Bytes)
                throw new ArgumentException("Player display name exceeds its fixed bound.");
        }
        catch (EncoderFallbackException)
        { throw new ArgumentException("Player display name Unicode is invalid."); }
        foreach (var rune in value.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is System.Globalization.UnicodeCategory.Control
                or System.Globalization.UnicodeCategory.LineSeparator
                or System.Globalization.UnicodeCategory.ParagraphSeparator
                || category == System.Globalization.UnicodeCategory.Format
                    && rune.Value is not (0x200C or 0x200D))
                throw new ArgumentException("Player display name contains unsupported control data.");
        }
        return value;
    }
}

public sealed class LilithGameBridgePlayerNameRequest : LilithGameBridgeClientRequest
{
    public const string SyncMethod = "game.player_name.sync";
    public override string Method => SyncMethod;
    public Guid GameEpoch { get; }
    public long ObservationSequence { get; }
    public string DisplayName { get; }
    public LilithGameBridgePlayerNameRequest(Guid generationId, Guid requestId, long sequence,
        Guid gameEpoch, long observationSequence, string displayName)
        : base(generationId, requestId, sequence)
    {
        if (gameEpoch == Guid.Empty || observationSequence <= 0)
            throw new ArgumentException("Player-name observation identity is invalid.");
        GameEpoch = gameEpoch;
        ObservationSequence = observationSequence;
        DisplayName = LilithPlayerDisplayName.Validate(displayName);
    }
}

public sealed class LilithGameBridgePlayerNameResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public string Method => LilithGameBridgePlayerNameRequest.SyncMethod;
    public Guid RequestId { get; }
    public Guid GameEpoch { get; }
    public long ObservationSequence { get; }
    public bool Accepted { get; }
    public long Revision { get; }
    public LilithGameBridgePlayerNameResponse(Guid generationId, Guid requestId, long sequence,
        Guid gameEpoch, long observationSequence, bool accepted, long revision)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty || gameEpoch == Guid.Empty || observationSequence <= 0
            || accepted && revision <= 0 || !accepted && revision != 0)
            throw new ArgumentException("Player-name receipt is invalid.");
        RequestId = requestId;
        GameEpoch = gameEpoch;
        ObservationSequence = observationSequence;
        Accepted = accepted;
        Revision = revision;
    }
}

public sealed class LilithGameBridgeHistoryRequest : LilithGameBridgeClientRequest
{
    public const string HistoryMethod = "game.chat.history";

    public override string Method => HistoryMethod;
    public string Cursor { get; }
    public int PageSize { get; }

    public LilithGameBridgeHistoryRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        string? cursor,
        int pageSize)
        : base(generationId, requestId, sequence)
    {
        if (pageSize is < 1 or > LilithGameBridgeProtocol.MaximumHistoryPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        Cursor = LilithGameBridgeProtocol.ValidateCursor(cursor, nameof(cursor));
        PageSize = pageSize;
    }
}

public sealed class LilithGameBridgeNotificationRequest : LilithGameBridgeClientRequest
{
    public const string NotificationMethod = "game.chat.notifications";

    public override string Method => NotificationMethod;
    public long AfterSequence { get; }
    public int WaitMilliseconds { get; }
    public LilithGameBridgeDisplayLocale DisplayLocale { get; }
    public LilithGameBridgeSpeechLocale SpeechLocale { get; }
    public bool TailOnly { get; }

    public LilithGameBridgeNotificationRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        long afterSequence,
        int waitMilliseconds)
        : this(
            generationId,
            requestId,
            sequence,
            afterSequence,
            waitMilliseconds,
            LilithGameBridgeDisplayLocale.TraditionalChinese,
            LilithGameBridgeSpeechLocale.Chinese,
            tailOnly: false)
    {
    }

    public LilithGameBridgeNotificationRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        long afterSequence,
        int waitMilliseconds,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale)
        : this(
            generationId,
            requestId,
            sequence,
            afterSequence,
            waitMilliseconds,
            displayLocale,
            speechLocale,
            tailOnly: false)
    {
    }

    public LilithGameBridgeNotificationRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        long afterSequence,
        int waitMilliseconds,
        LilithGameBridgeDisplayLocale displayLocale,
        LilithGameBridgeSpeechLocale speechLocale,
        bool tailOnly)
        : base(generationId, requestId, sequence)
    {
        if (afterSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (waitMilliseconds is < 0
            or > LilithGameBridgeProtocol.MaximumNotificationWaitMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        }
        if (!Enum.IsDefined(typeof(LilithGameBridgeDisplayLocale), displayLocale))
            throw new ArgumentOutOfRangeException(nameof(displayLocale));
        if (!Enum.IsDefined(typeof(LilithGameBridgeSpeechLocale), speechLocale))
            throw new ArgumentOutOfRangeException(nameof(speechLocale));
        if (tailOnly && (afterSequence != 0 || waitMilliseconds != 0))
            throw new ArgumentException(
                "A notification tail query cannot wait or carry a replay cursor.");
        AfterSequence = afterSequence;
        WaitMilliseconds = waitMilliseconds;
        DisplayLocale = displayLocale;
        SpeechLocale = speechLocale;
        TailOnly = tailOnly;
    }
}

public sealed class LilithGameBridgeInterruptRequest : LilithGameBridgeClientRequest
{
    public const string InterruptMethod = "game.chat.interrupt";

    public override string Method => InterruptMethod;
    public Guid TurnId { get; }

    public LilithGameBridgeInterruptRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid turnId)
        : base(generationId, requestId, sequence)
    {
        if (turnId == Guid.Empty)
            throw new ArgumentException("Turn identity is required.", nameof(turnId));
        TurnId = turnId;
    }
}

public sealed class LilithGameBridgeNativePerceptionSnapshot
{
    public const int CurrentSchemaVersion = 1;
    public const string OfficialNativeDialogueSource =
        "official-native-dialogue";

    public int SchemaVersion { get; }
    public string Source { get; }
    public string Text { get; }
    public long OccurredAtUnixMilliseconds { get; }
    public string NodeId { get; }
    public string LineId { get; }
    public string Trigger { get; }
    public string Locale { get; }

    public LilithGameBridgeNativeApplicationObservation? Application { get; }

    public LilithGameBridgeNativePerceptionSnapshot(
        string text,
        long occurredAtUnixMilliseconds,
        string nodeId,
        string lineId,
        string trigger,
        string locale)
        : this(
            CurrentSchemaVersion,
            OfficialNativeDialogueSource,
            text,
            occurredAtUnixMilliseconds,
            nodeId,
            lineId,
            trigger,
            locale)
    {
    }

    public LilithGameBridgeNativePerceptionSnapshot(
        int schemaVersion,
        string source,
        string text,
        long occurredAtUnixMilliseconds,
        string nodeId,
        string lineId,
        string trigger,
        string locale)
        : this(schemaVersion, source, text, occurredAtUnixMilliseconds, nodeId, lineId, trigger, locale, null)
    {
    }

    public LilithGameBridgeNativePerceptionSnapshot(
        int schemaVersion, string source, string text, long occurredAtUnixMilliseconds,
        string nodeId, string lineId, string trigger, string locale,
        LilithGameBridgeNativeApplicationObservation? application)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (!string.Equals(
                source,
                OfficialNativeDialogueSource,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Native perception source was rejected.",
                nameof(source));
        }
        if (occurredAtUnixMilliseconds is <= 0 or > 253402300799999)
            throw new ArgumentOutOfRangeException(nameof(occurredAtUnixMilliseconds));

        SchemaVersion = schemaVersion;
        Source = source;
        Text = LilithGameBridgeProtocol.ValidateConversationText(
            text,
            nameof(text),
            allowEmpty: false);
        if (Text.Length > LilithGameBridgeProtocol.MaximumNativeAssistantEventCharacters)
            throw new ArgumentOutOfRangeException(nameof(text));
        OccurredAtUnixMilliseconds = occurredAtUnixMilliseconds;
        NodeId = LilithGameBridgeProtocol.ValidateMetadata(
            nodeId,
            nameof(nodeId),
            LilithGameBridgeProtocol.MaximumNativeAssistantNodeIdCharacters,
            allowEmpty: true);
        LineId = LilithGameBridgeProtocol.ValidateMetadata(
            lineId,
            nameof(lineId),
            LilithGameBridgeProtocol.MaximumNativeAssistantLineIdCharacters,
            allowEmpty: true);
        Trigger = LilithGameBridgeProtocol.ValidateMetadata(
            trigger,
            nameof(trigger),
            LilithGameBridgeProtocol.MaximumNativeAssistantTriggerCharacters,
            allowEmpty: true);
        Locale = LilithGameBridgeProtocol.ValidateMetadata(
            locale,
            nameof(locale),
            LilithGameBridgeProtocol.MaximumNativeAssistantLocaleCharacters,
            allowEmpty: false);
        if (application is not null && (!LilithGameBridgeNativeApplicationObservation.IsApplicationTrigger(trigger)
            || application.ObservedAtUnixMilliseconds > occurredAtUnixMilliseconds))
            throw new ArgumentException("Application observation is not bound to an application event.");
        Application = application;
    }
}

public sealed class LilithGameBridgeNativeAssistantCommitRequest :
    LilithGameBridgeClientRequest
{
    public const string CommitMethod = "game.chat.native_assistant.commit";

    public override string Method => CommitMethod;
    public Guid EventId { get; }
    public bool ProactiveDialogueEnabled { get; }
    public LilithGameBridgeNativePerceptionSnapshot Perception { get; }
    public LilithMiniGamePublicContext? MiniGameContext { get; }
    public string Text => Perception.Text;
    public long OccurredAtUnixMilliseconds =>
        Perception.OccurredAtUnixMilliseconds;
    public string NodeId => Perception.NodeId;
    public string LineId => Perception.LineId;
    public string Trigger => Perception.Trigger;
    public string Locale => Perception.Locale;

    public LilithGameBridgeNativeAssistantCommitRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid eventId,
        string text,
        long occurredAtUnixMilliseconds,
        string nodeId,
        string lineId,
        string trigger,
        string locale)
        : this(
            generationId,
            requestId,
            sequence,
            eventId,
            proactiveDialogueEnabled: false,
            new LilithGameBridgeNativePerceptionSnapshot(
                text,
                occurredAtUnixMilliseconds,
                nodeId,
                lineId,
                trigger,
                locale))
    {
    }

    public LilithGameBridgeNativeAssistantCommitRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid eventId,
        bool proactiveDialogueEnabled,
        LilithGameBridgeNativePerceptionSnapshot perception)
        : this(generationId, requestId, sequence, eventId, proactiveDialogueEnabled, perception, null)
    {
    }

    public LilithGameBridgeNativeAssistantCommitRequest(Guid generationId, Guid requestId,
        long sequence, Guid eventId, bool proactiveDialogueEnabled,
        LilithGameBridgeNativePerceptionSnapshot perception, LilithMiniGamePublicContext? miniGameContext)
        : base(generationId, requestId, sequence)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("Native assistant event identity is required.", nameof(eventId));
        EventId = eventId;
        ProactiveDialogueEnabled = proactiveDialogueEnabled;
        Perception = perception
            ?? throw new ArgumentNullException(nameof(perception));
        if (!proactiveDialogueEnabled && miniGameContext is not null)
            throw new ArgumentException("Game context requires an enabled native extension.");
        MiniGameContext = miniGameContext;
    }
}

public sealed class LilithGameBridgeForegroundProactiveRequest :
    LilithGameBridgeClientRequest
{
    public const string ForegroundProactiveMethod =
        "game.chat.foreground_proactive";

    public override string Method => ForegroundProactiveMethod;
    public Guid OperationId { get; }
    public long ObservedAtUnixMilliseconds { get; }
    public string ApplicationId { get; }
    public string ApplicationDisplayName { get; }
    public string ApplicationCategory { get; }
    public LilithGameBridgeDisplayLocale DisplayLocale { get; }
    public LilithMiniGamePublicContext? MiniGameContext { get; }

    public LilithGameBridgeForegroundProactiveRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid operationId,
        long observedAtUnixMilliseconds,
        string applicationId,
        string applicationDisplayName,
        string applicationCategory,
        LilithGameBridgeDisplayLocale displayLocale)
        : this(generationId, requestId, sequence, operationId, observedAtUnixMilliseconds,
            applicationId, applicationDisplayName, applicationCategory, displayLocale, null)
    {
    }

    public LilithGameBridgeForegroundProactiveRequest(
        Guid generationId, Guid requestId, long sequence, Guid operationId,
        long observedAtUnixMilliseconds, string applicationId, string applicationDisplayName,
        string applicationCategory, LilithGameBridgeDisplayLocale displayLocale,
        LilithMiniGamePublicContext? miniGameContext)
        : base(generationId, requestId, sequence)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Foreground proactive operation identity is required.",
                nameof(operationId));
        }
        if (observedAtUnixMilliseconds is <= 0 or > 253402300799999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAtUnixMilliseconds));
        }
        if (!Enum.IsDefined(typeof(LilithGameBridgeDisplayLocale), displayLocale))
            throw new ArgumentOutOfRangeException(nameof(displayLocale));

        OperationId = operationId;
        ObservedAtUnixMilliseconds = observedAtUnixMilliseconds;
        ApplicationId = LilithGameBridgeProtocol.ValidateApplicationToken(
            applicationId,
            nameof(applicationId),
            LilithGameBridgeProtocol.MaximumForegroundApplicationIdCharacters);
        ApplicationDisplayName =
            LilithGameBridgeProtocol.ValidateApplicationDisplayName(
                applicationDisplayName,
                nameof(applicationDisplayName));
        ApplicationCategory = LilithGameBridgeProtocol.ValidateApplicationToken(
            applicationCategory,
            nameof(applicationCategory),
            LilithGameBridgeProtocol.MaximumForegroundApplicationCategoryCharacters);
        DisplayLocale = displayLocale;
        MiniGameContext = miniGameContext;
    }
}

public sealed class LilithGameBridgeVoiceStartRequest : LilithGameBridgeClientRequest
{
    public const string VoiceStartMethod = "game.voice.start";

    public override string Method => VoiceStartMethod;
    public Guid CaptureId { get; }
    public LilithGameBridgeVoiceLanguage Language { get; }
    public int MaximumDurationMilliseconds { get; }

    public LilithGameBridgeVoiceStartRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid captureId,
        LilithGameBridgeVoiceLanguage language,
        int maximumDurationMilliseconds)
        : base(generationId, requestId, sequence)
    {
        if (captureId == Guid.Empty)
            throw new ArgumentException("Capture identity is required.", nameof(captureId));
        if (!Enum.IsDefined(typeof(LilithGameBridgeVoiceLanguage), language))
            throw new ArgumentOutOfRangeException(nameof(language));
        if (maximumDurationMilliseconds is
            < LilithGameBridgeProtocol.MinimumVoiceCaptureDurationMilliseconds
            or > LilithGameBridgeProtocol.MaximumVoiceCaptureDurationMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDurationMilliseconds));
        }
        CaptureId = captureId;
        Language = language;
        MaximumDurationMilliseconds = maximumDurationMilliseconds;
    }
}

public sealed class LilithGameBridgeVoiceStopRequest : LilithGameBridgeClientRequest
{
    public const string VoiceStopMethod = "game.voice.stop";

    public override string Method => VoiceStopMethod;
    public Guid CaptureId { get; }
    public LilithGameBridgeVoiceStopDisposition Disposition { get; }

    public LilithGameBridgeVoiceStopRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid captureId,
        LilithGameBridgeVoiceStopDisposition disposition)
        : base(generationId, requestId, sequence)
    {
        if (captureId == Guid.Empty)
            throw new ArgumentException("Capture identity is required.", nameof(captureId));
        if (!Enum.IsDefined(typeof(LilithGameBridgeVoiceStopDisposition), disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        CaptureId = captureId;
        Disposition = disposition;
    }
}

public sealed class LilithGameBridgeMemoPollRequest : LilithGameBridgeClientRequest
{
    public const string MemoPollMethod = "game.notes.poll";

    public override string Method => MemoPollMethod;
    public int WaitMilliseconds { get; }

    public LilithGameBridgeMemoPollRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        int waitMilliseconds)
        : base(generationId, requestId, sequence)
    {
        if (waitMilliseconds is < 0 or > LilithGameBridgeProtocol.MaximumMemoWaitMilliseconds)
            throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
        WaitMilliseconds = waitMilliseconds;
    }
}

public sealed class LilithGameBridgeMemoCompleteRequest : LilithGameBridgeClientRequest
{
    public const string MemoCompleteMethod = "game.notes.complete";

    public override string Method => MemoCompleteMethod;
    public LilithMemoResult Result { get; }
    public Guid OperationId => Result.OperationId;

    public LilithGameBridgeMemoCompleteRequest(
        Guid generationId,
        Guid requestId,
        long sequence,
        LilithMemoResult result)
        : base(generationId, requestId, sequence)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }
}

public sealed class LilithGameBridgeMemoPollResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method => LilithGameBridgeMemoPollRequest.MemoPollMethod;
    public bool Succeeded { get; }
    public LilithMemoCommand? Command { get; }
    public string Code { get; }

    public LilithGameBridgeMemoPollResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        bool succeeded,
        LilithMemoCommand? command,
        string code)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        var validatedCode = ValidateMemoBridgeCode(code, nameof(code));
        if (succeeded != string.Equals(validatedCode, "ok", StringComparison.Ordinal)
            || (!succeeded && command is not null))
        {
            throw new ArgumentException("Memo poll terminal semantics were rejected.");
        }
        RequestId = requestId;
        Succeeded = succeeded;
        Command = command;
        Code = validatedCode;
    }

    private static string ValidateMemoBridgeCode(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.IndexOf('\0') >= 0
            || Encoding.UTF8.GetByteCount(value) > LilithMemoProtocol.MaximumCodeUtf8Bytes)
        {
            throw new ArgumentException("Memo bridge code was rejected.", parameterName);
        }
        return value;
    }
}

public sealed class LilithGameBridgeMemoCompleteResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method => LilithGameBridgeMemoCompleteRequest.MemoCompleteMethod;
    public Guid OperationId { get; }
    public bool Accepted { get; }
    public string Code { get; }

    public LilithGameBridgeMemoCompleteResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid operationId,
        bool accepted,
        string code)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        if (operationId == Guid.Empty)
            throw new ArgumentException("Memo operation identity is required.", nameof(operationId));
        if (string.IsNullOrWhiteSpace(code)
            || code.IndexOf('\0') >= 0
            || Encoding.UTF8.GetByteCount(code) > LilithMemoProtocol.MaximumCodeUtf8Bytes
            || accepted != string.Equals(code, "ok", StringComparison.Ordinal))
        {
            throw new ArgumentException("Memo completion terminal semantics were rejected.");
        }
        RequestId = requestId;
        OperationId = operationId;
        Accepted = accepted;
        Code = code;
    }
}

public sealed class LilithGameBridgeResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public Guid TurnId { get; }
    public string Method => LilithGameBridgeRequest.SubmitMethod;
    public bool Succeeded { get; }
    public bool UserCommitted { get; }
    public LilithGameBridgeTerminal Terminal { get; }
    public string AssistantText { get; }
    public string SpeechText { get; }
    public string SpeechEmotion { get; }
    public string Code { get; }
    public LilithMiniGameDiagnosticReceipt? MiniGameDiagnostic { get; }
    public LilithMiniGameEventReceipt? MiniGameEventReceipt { get; }

    public LilithGameBridgeResponse(
        Guid generationId, Guid requestId, long sequence, Guid turnId,
        bool succeeded, bool userCommitted, LilithGameBridgeTerminal terminal,
        string assistantText, string speechText, string code, string speechEmotion = "")
        : this(generationId, requestId, sequence, turnId, succeeded, userCommitted,
            terminal, assistantText, speechText, code, speechEmotion, null)
    {
    }

    public LilithGameBridgeResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid turnId,
        bool succeeded,
        bool userCommitted,
        LilithGameBridgeTerminal terminal,
        string assistantText,
        string speechText,
        string code,
        string speechEmotion,
        LilithMiniGameDiagnosticReceipt? miniGameDiagnostic)
        : this(generationId, requestId, sequence, turnId, succeeded, userCommitted, terminal,
            assistantText, speechText, code, speechEmotion, miniGameDiagnostic, null)
    {
    }

    public LilithGameBridgeResponse(
        Guid generationId, Guid requestId, long sequence, Guid turnId, bool succeeded,
        bool userCommitted, LilithGameBridgeTerminal terminal, string assistantText,
        string speechText, string code, string speechEmotion,
        LilithMiniGameDiagnosticReceipt? miniGameDiagnostic,
        LilithMiniGameEventReceipt? miniGameEventReceipt)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        if (turnId == Guid.Empty)
            throw new ArgumentException("Turn identity is required.", nameof(turnId));
        if (!Enum.IsDefined(typeof(LilithGameBridgeTerminal), terminal))
            throw new ArgumentOutOfRangeException(nameof(terminal));

        var validatedText = LilithGameBridgeProtocol.ValidateConversationText(
            assistantText,
            nameof(assistantText),
            allowEmpty: true);
        var validatedSpeechText = LilithGameBridgeProtocol.ValidateConversationText(
            speechText,
            nameof(speechText),
            allowEmpty: true);
        var validatedCode = LilithGameBridgeProtocol.ValidateToken(code, nameof(code));
        if (!LilithJapaneseSpeechEmotion.IsValid(speechEmotion)
            || (speechEmotion.Length != 0 && (!succeeded || validatedSpeechText.Length == 0)))
            throw new ArgumentException("Speech delivery metadata was rejected.", nameof(speechEmotion));
        if (succeeded != (terminal == LilithGameBridgeTerminal.Completed)
            || (succeeded && !userCommitted)
            || succeeded != !string.IsNullOrWhiteSpace(validatedText)
            || (!succeeded && !string.IsNullOrWhiteSpace(validatedSpeechText))
            || succeeded != string.Equals(validatedCode, "ok", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Game bridge response terminal semantics were rejected.");
        }

        RequestId = requestId;
        TurnId = turnId;
        Succeeded = succeeded;
        UserCommitted = userCommitted;
        Terminal = terminal;
        AssistantText = validatedText;
        SpeechText = validatedSpeechText;
        SpeechEmotion = speechEmotion;
        Code = validatedCode;
        MiniGameDiagnostic = miniGameDiagnostic;
        if (miniGameEventReceipt is not null && !succeeded)
            throw new ArgumentException("Only a completed retained turn carries game event acknowledgement.");
        MiniGameEventReceipt = miniGameEventReceipt;
    }

    public LilithGameBridgeResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid turnId,
        bool succeeded,
        bool userCommitted,
        LilithGameBridgeTerminal terminal,
        string assistantText,
        string code)
        : this(
            generationId,
            requestId,
            sequence,
            turnId,
            succeeded,
            userCommitted,
            terminal,
            assistantText,
            succeeded ? assistantText : string.Empty,
            code)
    {
    }
}

public sealed class LilithGameBridgeHistoryEntry
{
    public string MessageId { get; }
    public string TurnId { get; }
    public string MessageKind { get; }
    public string Role { get; }
    public string Text { get; }
    public long OccurredAtUnixTimeMilliseconds { get; }

    public LilithGameBridgeHistoryEntry(
        string messageId,
        string turnId,
        string messageKind,
        string role,
        string text,
        long occurredAtUnixTimeMilliseconds)
    {
        if (!IsHermesIdentity(messageId))
            throw new ArgumentException("History message identity was rejected.", nameof(messageId));
        if (!IsHermesIdentity(turnId))
            throw new ArgumentException("History turn identity was rejected.", nameof(turnId));
        if (role is not ("user" or "assistant"))
            throw new ArgumentException("History message role was rejected.", nameof(role));
        if (messageKind != (role == "user" ? "user" : "primary"))
            throw new ArgumentException("History message kind was rejected.", nameof(messageKind));
        if (occurredAtUnixTimeMilliseconds is < 0 or > 253402300799999)
            throw new ArgumentOutOfRangeException(nameof(occurredAtUnixTimeMilliseconds));
        MessageId = messageId;
        TurnId = turnId;
        MessageKind = messageKind;
        Role = role;
        Text = LilithGameBridgeProtocol.ValidateConversationText(
            text,
            nameof(text),
            allowEmpty: false);
        OccurredAtUnixTimeMilliseconds = occurredAtUnixTimeMilliseconds;
    }

    private static bool IsHermesIdentity(string? value)
        => value is { Length: 32 }
            && value.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class LilithGameBridgeHistoryResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method => LilithGameBridgeHistoryRequest.HistoryMethod;
    public bool Succeeded { get; }
    public string NextCursor { get; }
    public bool HasMore { get; }
    public IReadOnlyList<LilithGameBridgeHistoryEntry> Entries { get; }
    public string Code { get; }

    public LilithGameBridgeHistoryResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        bool succeeded,
        string? nextCursor,
        bool hasMore,
        IEnumerable<LilithGameBridgeHistoryEntry> entries,
        string code)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        var values = (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray();
        if (values.Length > LilithGameBridgeProtocol.MaximumHistoryPageSize
            || values.Any(static value => value is null))
        {
            throw new ArgumentException("History response entries were rejected.", nameof(entries));
        }
        var validatedCode = LilithGameBridgeProtocol.ValidateToken(code, nameof(code));
        var validatedCursor = LilithGameBridgeProtocol.ValidateCursor(
            nextCursor,
            nameof(nextCursor));
        if (succeeded != string.Equals(validatedCode, "ok", StringComparison.Ordinal)
            || (!succeeded && (values.Length != 0 || hasMore || validatedCursor.Length != 0))
            || (succeeded && hasMore != (validatedCursor.Length != 0))
            || (succeeded && values.Length == 0 && hasMore))
        {
            throw new ArgumentException("History response terminal semantics were rejected.");
        }
        RequestId = requestId;
        Succeeded = succeeded;
        NextCursor = validatedCursor;
        HasMore = hasMore;
        Entries = Array.AsReadOnly(values);
        Code = validatedCode;
    }
}

public sealed class LilithGameBridgeNotificationEntry
{
    public long Sequence { get; }
    public string Type { get; }
    public string Schema { get; }
    public bool Succeeded { get; }
    public string Text { get; }
    public string SpeechText { get; }
    public LilithGameBridgeSpeechLocale SpeechLocale { get; }
    public string SpeechEmotion { get; }
    public LilithGameBridgeContinuationContext? Continuation { get; }
    public Guid? MiniGameEventId { get; }
    public LilithMiniGameEventReceipt? MiniGameEventReceipt { get; }

    public LilithGameBridgeNotificationEntry(
        long sequence,
        string type,
        string schema,
        bool succeeded,
        string text)
        : this(
            sequence,
            type,
            schema,
            succeeded,
            text,
            text,
            LilithGameBridgeSpeechLocale.Chinese)
    {
    }

    public LilithGameBridgeNotificationEntry(
        long sequence,
        string type,
        string schema,
        bool succeeded,
        string text,
        string speechText)
        : this(
            sequence,
            type,
            schema,
            succeeded,
            text,
            speechText,
            LilithGameBridgeSpeechLocale.Chinese)
    {
    }

    public LilithGameBridgeNotificationEntry(
        long sequence,
        string type,
        string schema,
        bool succeeded,
        string text,
        string speechText,
        LilithGameBridgeSpeechLocale speechLocale)
        : this(sequence, type, schema, succeeded, text, speechText, speechLocale, string.Empty, null)
    {
    }

    public LilithGameBridgeNotificationEntry(long sequence, string type, string schema,
        bool succeeded, string text, string speechText, LilithGameBridgeSpeechLocale speechLocale,
        string speechEmotion, LilithGameBridgeContinuationContext? continuation)
        : this(sequence, type, schema, succeeded, text, speechText, speechLocale,
            speechEmotion, continuation, null, null)
    {
    }

    public LilithGameBridgeNotificationEntry(long sequence, string type, string schema,
        bool succeeded, string text, string speechText, LilithGameBridgeSpeechLocale speechLocale,
        string speechEmotion, LilithGameBridgeContinuationContext? continuation,
        Guid? miniGameEventId, LilithMiniGameEventReceipt? miniGameEventReceipt)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));
        var expectedSchema = type switch
        {
            LilithGameBridgeProtocol.CronCompletionEventType =>
                LilithGameBridgeProtocol.CronCompletionSchema,
            LilithGameBridgeProtocol.NativeAssistantContinuationEventType =>
                LilithGameBridgeProtocol.NativeAssistantContinuationSchema,
            LilithGameBridgeProtocol.ForegroundProactiveEventType =>
                LilithGameBridgeProtocol.ForegroundProactiveSchema,
            LilithGameBridgeProtocol.ForegroundProactivePlayerReplyEventType =>
                LilithGameBridgeProtocol.ForegroundProactivePlayerReplySchema,
            LilithGameBridgeProtocol.MiniGameEventsSettledEventType =>
                LilithGameBridgeProtocol.MiniGameEventsSettledSchema,
            _ => throw new ArgumentException(
                "Game bridge notification type was rejected.",
                nameof(type))
        };
        if (!string.Equals(schema, expectedSchema, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Game bridge notification type/schema pair was rejected.",
                nameof(schema));
        }
        if ((string.Equals(
                    type,
                    LilithGameBridgeProtocol.NativeAssistantContinuationEventType,
                    StringComparison.Ordinal)
                || string.Equals(
                    type,
                    LilithGameBridgeProtocol.ForegroundProactiveEventType,
                    StringComparison.Ordinal))
            && !succeeded)
        {
            throw new ArgumentException(
                "An assistant continuation notification must represent a completed reply.",
                nameof(succeeded));
        }
        var playerReplySignal = string.Equals(
            type,
            LilithGameBridgeProtocol.ForegroundProactivePlayerReplyEventType,
            StringComparison.Ordinal);
        var gameSettlement = type == LilithGameBridgeProtocol.MiniGameEventsSettledEventType;
        if (gameSettlement && (!succeeded || text.Length != 0 || speechText.Length != 0
                || speechEmotion.Length != 0 || continuation is not null
                || miniGameEventId is null || miniGameEventId == Guid.Empty)
            || !gameSettlement && (miniGameEventId is not null || miniGameEventReceipt is not null))
            throw new ArgumentException("Game event settlement must be typed and content-free.");
        if (playerReplySignal
            && (!succeeded
                || text.Length != 0
                || speechText.Length != 0))
        {
            throw new ArgumentException(
                "A foreground proactive player-reply signal must be successful and content-free.",
                nameof(text));
        }
        Sequence = sequence;
        Type = type;
        Schema = schema;
        Succeeded = succeeded;
        Text = LilithGameBridgeProtocol.ValidateConversationText(
            text,
            nameof(text),
            allowEmpty: playerReplySignal || gameSettlement);
        SpeechText = LilithGameBridgeProtocol.ValidateConversationText(
            speechText,
            nameof(speechText),
            allowEmpty: true);
        if (!Enum.IsDefined(typeof(LilithGameBridgeSpeechLocale), speechLocale))
            throw new ArgumentOutOfRangeException(nameof(speechLocale));
        SpeechLocale = speechLocale;
        if (!LilithJapaneseSpeechEmotion.IsValid(speechEmotion)
            || speechText.Length == 0 && speechEmotion.Length != 0)
            throw new ArgumentException("Notification speech emotion is invalid.");
        if (continuation is not null && type is not (LilithGameBridgeProtocol.NativeAssistantContinuationEventType
            or LilithGameBridgeProtocol.ForegroundProactiveEventType))
            throw new ArgumentException("Only synthetic assistant notifications carry continuation identity.");
        SpeechEmotion = speechEmotion;
        Continuation = continuation;
        MiniGameEventId = miniGameEventId;
        MiniGameEventReceipt = miniGameEventReceipt;
    }
}

public sealed class LilithGameBridgeNotificationResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method => LilithGameBridgeNotificationRequest.NotificationMethod;
    public bool Succeeded { get; }
    public long OldestAvailableSequence { get; }
    public long LatestSequence { get; }
    public bool HasMore { get; }
    public IReadOnlyList<LilithGameBridgeNotificationEntry> Entries { get; }
    public string Code { get; }

    public LilithGameBridgeNotificationResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        bool succeeded,
        long oldestAvailableSequence,
        long latestSequence,
        bool hasMore,
        IEnumerable<LilithGameBridgeNotificationEntry> entries,
        string code)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        if (oldestAvailableSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(oldestAvailableSequence));
        if (latestSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(latestSequence));
        if ((oldestAvailableSequence == 0) != (latestSequence == 0)
            || oldestAvailableSequence > latestSequence)
        {
            throw new ArgumentException(
                "Game bridge notification retention range was rejected.");
        }
        var values = (entries ?? throw new ArgumentNullException(nameof(entries)))
            .ToArray();
        if (values.Length > LilithGameBridgeProtocol.MaximumNotificationPageSize
            || values.Any(static value => value is null)
            || values.Select(static value => value.Sequence).Distinct().Count()
                != values.Length
            || !values.Select(static value => value.Sequence)
                .SequenceEqual(values.Select(static value => value.Sequence)
                    .OrderBy(static value => value)))
        {
            throw new ArgumentException(
                "Game bridge notification entries were rejected.",
                nameof(entries));
        }
        if (values.Any(value =>
                value.Sequence < oldestAvailableSequence
                || value.Sequence > latestSequence))
        {
            throw new ArgumentException(
                "Game bridge notification entry range was rejected.",
                nameof(entries));
        }
        var validatedCode = LilithGameBridgeProtocol.ValidateToken(code, nameof(code));
        var expectedHasMore = values.Length > 0
            && values[^1].Sequence < latestSequence;
        if (succeeded != string.Equals(validatedCode, "ok", StringComparison.Ordinal)
            || (!succeeded && (values.Length != 0 || hasMore))
            || (succeeded && hasMore != expectedHasMore))
        {
            throw new ArgumentException(
                "Game bridge notification response semantics were rejected.");
        }
        RequestId = requestId;
        Succeeded = succeeded;
        OldestAvailableSequence = oldestAvailableSequence;
        LatestSequence = latestSequence;
        HasMore = hasMore;
        Entries = Array.AsReadOnly(values);
        Code = validatedCode;
    }
}

public sealed class LilithGameBridgeInterruptResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method => LilithGameBridgeInterruptRequest.InterruptMethod;
    public Guid TurnId { get; }
    public bool Succeeded { get; }
    public string Code { get; }

    public LilithGameBridgeInterruptResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid turnId,
        bool succeeded,
        string code)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        if (turnId == Guid.Empty)
            throw new ArgumentException("Turn identity is required.", nameof(turnId));
        var validatedCode = LilithGameBridgeProtocol.ValidateToken(code, nameof(code));
        if (succeeded != string.Equals(validatedCode, "ok", StringComparison.Ordinal))
            throw new ArgumentException("Interrupt response terminal semantics were rejected.");
        RequestId = requestId;
        TurnId = turnId;
        Succeeded = succeeded;
        Code = validatedCode;
    }
}

public sealed class LilithGameBridgeNativeAssistantCommitResponse :
    LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method => LilithGameBridgeNativeAssistantCommitRequest.CommitMethod;
    public Guid EventId { get; }
    public bool Accepted { get; }
    public bool AlreadyCommitted { get; }
    public bool Retryable { get; }
    public string Code { get; }
    public bool MiniGameEventsPending { get; }
    public LilithMiniGameEventReceipt? MiniGameEventReceipt { get; }

    public LilithGameBridgeNativeAssistantCommitResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid eventId,
        bool accepted,
        bool alreadyCommitted,
        bool retryable,
        string code)
        : this(generationId, requestId, sequence, eventId, accepted, alreadyCommitted,
            retryable, code, false, null)
    {
    }

    public LilithGameBridgeNativeAssistantCommitResponse(Guid generationId, Guid requestId,
        long sequence, Guid eventId, bool accepted, bool alreadyCommitted, bool retryable,
        string code, bool miniGameEventsPending, LilithMiniGameEventReceipt? miniGameEventReceipt)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        if (eventId == Guid.Empty)
            throw new ArgumentException("Native assistant event identity is required.", nameof(eventId));
        var validatedCode = LilithGameBridgeProtocol.ValidateToken(code, nameof(code));
        if (accepted != string.Equals(validatedCode, "ok", StringComparison.Ordinal)
            || (accepted && retryable)
            || (!accepted && alreadyCommitted))
        {
            throw new ArgumentException(
                "Native assistant commit response semantics were rejected.");
        }
        RequestId = requestId;
        EventId = eventId;
        Accepted = accepted;
        AlreadyCommitted = alreadyCommitted;
        Retryable = retryable;
        Code = validatedCode;
        if ((miniGameEventsPending || miniGameEventReceipt is not null) && !accepted
            || miniGameEventsPending && miniGameEventReceipt is not null)
            throw new ArgumentException("Native extension game settlement is invalid.");
        MiniGameEventsPending = miniGameEventsPending;
        MiniGameEventReceipt = miniGameEventReceipt;
    }
}

public sealed class LilithGameBridgeForegroundProactiveResponse :
    LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method =>
        LilithGameBridgeForegroundProactiveRequest.ForegroundProactiveMethod;
    public Guid OperationId { get; }
    public bool Accepted { get; }
    public bool Succeeded { get; }
    public LilithGameBridgeForegroundProactiveState State { get; }
    public bool AlreadyProcessed { get; }
    public bool Retryable { get; }
    public string Code { get; }
    public LilithMiniGameEventReceipt? MiniGameEventReceipt { get; }

    public LilithGameBridgeForegroundProactiveResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid operationId,
        bool accepted,
        bool succeeded,
        LilithGameBridgeForegroundProactiveState state,
        bool alreadyProcessed,
        bool retryable,
        string code)
        : this(generationId, requestId, sequence, operationId, accepted, succeeded,
            state, alreadyProcessed, retryable, code, null)
    {
    }

    public LilithGameBridgeForegroundProactiveResponse(
        Guid generationId, Guid requestId, long sequence, Guid operationId,
        bool accepted, bool succeeded, LilithGameBridgeForegroundProactiveState state,
        bool alreadyProcessed, bool retryable, string code,
        LilithMiniGameEventReceipt? miniGameEventReceipt)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Foreground proactive operation identity is required.",
                nameof(operationId));
        }
        if (!Enum.IsDefined(typeof(LilithGameBridgeForegroundProactiveState), state))
            throw new ArgumentOutOfRangeException(nameof(state));

        var validatedCode = LilithGameBridgeProtocol.ValidateToken(
            code,
            nameof(code));
        var expectedAccepted =
            state != LilithGameBridgeForegroundProactiveState.Abandoned;
        var expectedSucceeded =
            state == LilithGameBridgeForegroundProactiveState.Completed;
        if (accepted != expectedAccepted
            || succeeded != expectedSucceeded
            || accepted != string.Equals(validatedCode, "ok", StringComparison.Ordinal)
            || (retryable && (accepted || alreadyProcessed)))
        {
            throw new ArgumentException(
                "Foreground proactive response semantics were rejected.");
        }

        RequestId = requestId;
        OperationId = operationId;
        Accepted = accepted;
        Succeeded = succeeded;
        State = state;
        AlreadyProcessed = alreadyProcessed;
        Retryable = retryable;
        Code = validatedCode;
        if (miniGameEventReceipt is not null && !succeeded)
            throw new ArgumentException("Only a completed retained turn carries game event acknowledgement.");
        MiniGameEventReceipt = miniGameEventReceipt;
    }
}

public sealed class LilithGameBridgeVoiceStartResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method => LilithGameBridgeVoiceStartRequest.VoiceStartMethod;
    public Guid CaptureId { get; }
    public bool Succeeded { get; }
    public string Code { get; }

    public LilithGameBridgeVoiceStartResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid captureId,
        bool succeeded,
        string code)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        if (captureId == Guid.Empty)
            throw new ArgumentException("Capture identity is required.", nameof(captureId));
        var validatedCode = LilithGameBridgeProtocol.ValidateToken(code, nameof(code));
        if (succeeded != string.Equals(validatedCode, "ok", StringComparison.Ordinal))
            throw new ArgumentException("Voice start response semantics were rejected.");
        RequestId = requestId;
        CaptureId = captureId;
        Succeeded = succeeded;
        Code = validatedCode;
    }
}

public sealed class LilithGameBridgeVoiceStopResponse : LilithGameBridgeFrame
{
    internal override string Kind => "game.response";
    public Guid RequestId { get; }
    public string Method => LilithGameBridgeVoiceStopRequest.VoiceStopMethod;
    public Guid CaptureId { get; }
    public bool Succeeded { get; }
    public LilithGameBridgeTerminal Terminal { get; }
    public string Transcript { get; }
    public string Code { get; }

    public LilithGameBridgeVoiceStopResponse(
        Guid generationId,
        Guid requestId,
        long sequence,
        Guid captureId,
        bool succeeded,
        LilithGameBridgeTerminal terminal,
        string transcript,
        string code)
        : base(generationId, sequence)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request identity is required.", nameof(requestId));
        if (captureId == Guid.Empty)
            throw new ArgumentException("Capture identity is required.", nameof(captureId));
        if (!Enum.IsDefined(typeof(LilithGameBridgeTerminal), terminal))
            throw new ArgumentOutOfRangeException(nameof(terminal));
        var validatedTranscript = LilithGameBridgeProtocol.ValidateConversationText(
            transcript,
            nameof(transcript),
            allowEmpty: true);
        var validatedCode = LilithGameBridgeProtocol.ValidateToken(code, nameof(code));
        if (succeeded != (terminal == LilithGameBridgeTerminal.Completed)
            || succeeded != !string.IsNullOrWhiteSpace(validatedTranscript)
            || succeeded != string.Equals(validatedCode, "ok", StringComparison.Ordinal))
        {
            throw new ArgumentException("Voice stop response semantics were rejected.");
        }
        RequestId = requestId;
        CaptureId = captureId;
        Succeeded = succeeded;
        Terminal = terminal;
        Transcript = validatedTranscript;
        Code = validatedCode;
    }
}

public static class LilithGameBridgeProtocol
{
    public const string MiniGameEventsSettledEventType = "game.minigame.events.settled";
    public const string MiniGameEventsSettledSchema = "lilith.minigame.events.settled.v1";
    public const int Version = 1;
    public const int MaximumFrameBytes = 4 * 1024 * 1024;
    public const int MaximumConversationUtf8Bytes = 128 * 1024;
    public const int MaximumNativeAssistantEventCharacters = 8_192;
    public const int MaximumNativeAssistantNodeIdCharacters = 256;
    public const int MaximumNativeAssistantLineIdCharacters = 256;
    public const int MaximumNativeAssistantTriggerCharacters = 256;
    public const int MaximumNativeAssistantLocaleCharacters = 32;
    public const int MaximumForegroundApplicationIdCharacters = 128;
    public const int MaximumForegroundApplicationDisplayNameCharacters = 128;
    public const int MaximumForegroundApplicationCategoryCharacters = 64;
    public const int MaximumNoteContextCharacters = 360;
    public const int MaximumNoteDraftCharacters = 1200;
    public const int MaximumNoteDraftMilliseconds = 25_000;
    public const int MaximumHistoryPageSize = 25;
    public const int MaximumNotificationPageSize = 16;
    public const int MaximumNotificationWaitMilliseconds = 30_000;
    public const int MaximumMemoWaitMilliseconds = 30_000;
    public const string CronCompletionEventType = "cron.complete";
    public const string CronCompletionSchema = "lilith.cron.completion.v1";
    public const string NativeAssistantContinuationEventType =
        "native_assistant_extension.complete";
    public const string NativeAssistantContinuationSchema =
        "lilith.native-assistant-extension.completion.v1";
    public const string ForegroundProactiveEventType =
        "foreground_proactive.complete";
    public const string ForegroundProactiveSchema =
        "lilith.foreground-proactive.completion.v1";
    public const string ForegroundProactivePlayerReplyEventType =
        "foreground_proactive.player_reply";
    public const string ForegroundProactivePlayerReplySchema =
        "lilith.foreground-proactive.player-reply.v1";
    public const int MaximumJsonDepth = 8;
    public const int MinimumVoiceCaptureDurationMilliseconds = 5_000;
    public const int MaximumVoiceCaptureDurationMilliseconds = 90_000;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumJsonDepth
    };

    public static byte[] Encode(LilithGameBridgeFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", frame.Version);
            writer.WriteString("kind", frame.Kind);
            writer.WriteString("generationId", frame.GenerationId.ToString("N"));
            if (LilithDailyNoteGameFrames.TryWriteFields(writer, frame))
            {
                // Daily task authority is separate from player turns and MemoBook.
            }
            else if (LilithNoteInboxGameFrames.TryWriteFields(writer, frame))
            {
                // Independent NoteInbox frames do not widen the MemoBook contract.
            }
            else if (frame is LilithGameBridgePlayerNameRequest playerName)
            {
                writer.WriteString("requestId", playerName.RequestId.ToString("N"));
                writer.WriteNumber("sequence", playerName.Sequence);
                writer.WriteString("method", playerName.Method);
                writer.WriteString("gameEpoch", playerName.GameEpoch.ToString("N"));
                writer.WriteNumber("observationSequence", playerName.ObservationSequence);
                writer.WriteString("displayName", playerName.DisplayName);
            }
            else if (frame is LilithGameBridgePlayerNameResponse playerNameReceipt)
            {
                writer.WriteString("requestId", playerNameReceipt.RequestId.ToString("N"));
                writer.WriteNumber("sequence", playerNameReceipt.Sequence);
                writer.WriteString("method", playerNameReceipt.Method);
                writer.WriteString("gameEpoch", playerNameReceipt.GameEpoch.ToString("N"));
                writer.WriteNumber("observationSequence", playerNameReceipt.ObservationSequence);
                writer.WriteBoolean("accepted", playerNameReceipt.Accepted);
                writer.WriteNumber("revision", playerNameReceipt.Revision);
            }
            else if (frame is LilithGameBridgeRequest request)
            {
                writer.WriteString("requestId", request.RequestId.ToString("N"));
                writer.WriteNumber("sequence", request.Sequence);
                writer.WriteString("method", request.Method);
                writer.WriteString("turnId", request.TurnId.ToString("N"));
                writer.WriteString("surface", SurfaceToken(request.Surface));
                writer.WriteString("text", request.Text);
                writer.WriteString(
                    "displayLocale",
                    DisplayLocaleToken(request.DisplayLocale));
                writer.WriteString(
                    "speechLocale",
                    SpeechLocaleToken(request.SpeechLocale));
                if (request.MiniGameContext is not null)
                {
                    writer.WritePropertyName("miniGameContext");
                    request.MiniGameContext.Write(writer);
                }
                if (request.MiniGameDiagnostic is not null)
                {
                    writer.WritePropertyName("miniGameDiagnostic");
                    request.MiniGameDiagnostic.Write(writer);
                }
                if (request.MiniGameAnswerRepair is not null)
                {
                    writer.WritePropertyName("miniGameAnswerRepair");
                    request.MiniGameAnswerRepair.Write(writer);
                }
            }
            else if (frame is LilithGameBridgeHistoryRequest historyRequest)
            {
                writer.WriteString("requestId", historyRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", historyRequest.Sequence);
                writer.WriteString("method", historyRequest.Method);
                writer.WriteString("cursor", historyRequest.Cursor);
                writer.WriteNumber("pageSize", historyRequest.PageSize);
            }
            else if (frame is LilithGameBridgeNotificationRequest notificationRequest)
            {
                writer.WriteString(
                    "requestId",
                    notificationRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", notificationRequest.Sequence);
                writer.WriteString("method", notificationRequest.Method);
                writer.WriteNumber("afterSequence", notificationRequest.AfterSequence);
                writer.WriteNumber(
                    "waitMilliseconds",
                    notificationRequest.WaitMilliseconds);
                writer.WriteString(
                    "displayLocale",
                    DisplayLocaleToken(notificationRequest.DisplayLocale));
                writer.WriteString(
                    "speechLocale",
                    SpeechLocaleToken(notificationRequest.SpeechLocale));
                writer.WriteBoolean("tailOnly", notificationRequest.TailOnly);
            }
            else if (frame is LilithGameBridgeInterruptRequest interruptRequest)
            {
                writer.WriteString("requestId", interruptRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", interruptRequest.Sequence);
                writer.WriteString("method", interruptRequest.Method);
                writer.WriteString("turnId", interruptRequest.TurnId.ToString("N"));
            }
            else if (frame is LilithGameBridgeNativeAssistantCommitRequest nativeCommitRequest)
            {
                if (nativeCommitRequest.MiniGameContext is not null)
                {
                    writer.WritePropertyName("miniGameContext");
                    nativeCommitRequest.MiniGameContext.Write(writer);
                }
                writer.WriteString("requestId", nativeCommitRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", nativeCommitRequest.Sequence);
                writer.WriteString("method", nativeCommitRequest.Method);
                writer.WriteString("eventId", nativeCommitRequest.EventId.ToString("N"));
                writer.WriteBoolean(
                    "proactiveDialogueEnabled",
                    nativeCommitRequest.ProactiveDialogueEnabled);
                writer.WriteStartObject("perception");
                writer.WriteNumber(
                    "schemaVersion",
                    nativeCommitRequest.Perception.SchemaVersion);
                writer.WriteString(
                    "source",
                    nativeCommitRequest.Perception.Source);
                writer.WriteString("text", nativeCommitRequest.Perception.Text);
                writer.WriteNumber(
                    "occurredAtUnixMs",
                    nativeCommitRequest.Perception.OccurredAtUnixMilliseconds);
                writer.WriteString("nodeId", nativeCommitRequest.Perception.NodeId);
                writer.WriteString("lineId", nativeCommitRequest.Perception.LineId);
                writer.WriteString("trigger", nativeCommitRequest.Perception.Trigger);
                writer.WriteString("locale", nativeCommitRequest.Perception.Locale);
                if (nativeCommitRequest.Perception.Application is { } application)
                {
                    writer.WritePropertyName("application");
                    JsonSerializer.Serialize(writer, application.ToNativePayload());
                }
                writer.WriteEndObject();
            }
            else if (frame is LilithGameBridgeNoteDraftRequest noteDraftRequest)
            {
                writer.WriteString("requestId", noteDraftRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", noteDraftRequest.Sequence);
                writer.WriteString("method", noteDraftRequest.Method);
                writer.WriteString("eventId", noteDraftRequest.EventId.ToString("N"));
                writer.WriteString("category", noteDraftRequest.Category);
                writer.WriteString("topic", noteDraftRequest.Topic);
                writer.WriteString("context", noteDraftRequest.Context);
                writer.WriteString("displayLocale", NoteDraftDisplayLocaleToken(noteDraftRequest.DisplayLocale));
            }
            else if (frame is LilithGameBridgeForegroundProactiveRequest foregroundRequest)
            {
                writer.WriteString(
                    "requestId",
                    foregroundRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", foregroundRequest.Sequence);
                writer.WriteString("method", foregroundRequest.Method);
                writer.WriteString(
                    "operationId",
                    foregroundRequest.OperationId.ToString("N"));
                writer.WriteNumber(
                    "observedAtUnixMs",
                    foregroundRequest.ObservedAtUnixMilliseconds);
                writer.WriteString(
                    "applicationId",
                    foregroundRequest.ApplicationId);
                writer.WriteString(
                    "applicationDisplayName",
                    foregroundRequest.ApplicationDisplayName);
                writer.WriteString(
                    "applicationCategory",
                    foregroundRequest.ApplicationCategory);
                writer.WriteString(
                    "displayLocale",
                    DisplayLocaleToken(foregroundRequest.DisplayLocale));
                if (foregroundRequest.MiniGameContext is not null)
                {
                    writer.WritePropertyName("miniGameContext");
                    foregroundRequest.MiniGameContext.Write(writer);
                }
            }
            else if (frame is LilithGameBridgeVoiceStartRequest voiceStartRequest)
            {
                writer.WriteString("requestId", voiceStartRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", voiceStartRequest.Sequence);
                writer.WriteString("method", voiceStartRequest.Method);
                writer.WriteString("captureId", voiceStartRequest.CaptureId.ToString("N"));
                writer.WriteString("language", VoiceLanguageToken(voiceStartRequest.Language));
                writer.WriteNumber(
                    "maximumDurationMilliseconds",
                    voiceStartRequest.MaximumDurationMilliseconds);
            }
            else if (frame is LilithGameBridgeVoiceStopRequest voiceStopRequest)
            {
                writer.WriteString("requestId", voiceStopRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", voiceStopRequest.Sequence);
                writer.WriteString("method", voiceStopRequest.Method);
                writer.WriteString("captureId", voiceStopRequest.CaptureId.ToString("N"));
                writer.WriteString(
                    "disposition",
                    VoiceStopDispositionToken(voiceStopRequest.Disposition));
            }
            else if (frame is LilithGameBridgeMemoPollRequest memoPollRequest)
            {
                writer.WriteString("requestId", memoPollRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", memoPollRequest.Sequence);
                writer.WriteString("method", memoPollRequest.Method);
                writer.WriteNumber("waitMilliseconds", memoPollRequest.WaitMilliseconds);
            }
            else if (frame is LilithGameBridgeMemoCompleteRequest memoCompleteRequest)
            {
                writer.WriteString("requestId", memoCompleteRequest.RequestId.ToString("N"));
                writer.WriteNumber("sequence", memoCompleteRequest.Sequence);
                writer.WriteString("method", memoCompleteRequest.Method);
                writer.WritePropertyName("result");
                LilithMemoProtocol.WriteResult(writer, memoCompleteRequest.Result);
            }
            else if (frame is LilithGameBridgeResponse response)
            {
                writer.WriteString("requestId", response.RequestId.ToString("N"));
                writer.WriteNumber("sequence", response.Sequence);
                writer.WriteString("method", response.Method);
                writer.WriteBoolean("succeeded", response.Succeeded);
                writer.WriteString("turnId", response.TurnId.ToString("N"));
                writer.WriteBoolean("userCommitted", response.UserCommitted);
                writer.WriteString("terminal", TerminalToken(response.Terminal));
                writer.WriteString("assistantText", response.AssistantText);
                writer.WriteString("speechText", response.SpeechText);
                if (response.SpeechEmotion.Length != 0)
                    writer.WriteString("speechEmotion", response.SpeechEmotion);
                writer.WriteString("code", response.Code);
                if (response.MiniGameDiagnostic is not null)
                {
                    writer.WritePropertyName("miniGameDiagnostic");
                    response.MiniGameDiagnostic.Write(writer);
                }
                if (response.MiniGameEventReceipt is not null)
                {
                    writer.WritePropertyName("miniGameEventReceipt");
                    response.MiniGameEventReceipt.Write(writer);
                }
            }
            else if (frame is LilithGameBridgeHistoryResponse historyResponse)
            {
                writer.WriteString("requestId", historyResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", historyResponse.Sequence);
                writer.WriteString("method", historyResponse.Method);
                writer.WriteBoolean("succeeded", historyResponse.Succeeded);
                writer.WriteString("nextCursor", historyResponse.NextCursor);
                writer.WriteBoolean("hasMore", historyResponse.HasMore);
                writer.WritePropertyName("entries");
                writer.WriteStartArray();
                foreach (var entry in historyResponse.Entries)
                {
                    writer.WriteStartObject();
                    writer.WriteString("messageId", entry.MessageId);
                    writer.WriteString("turnId", entry.TurnId);
                    writer.WriteString("messageKind", entry.MessageKind);
                    writer.WriteString("role", entry.Role);
                    writer.WriteString("text", entry.Text);
                    writer.WriteNumber(
                        "occurredAtUnixTimeMilliseconds",
                        entry.OccurredAtUnixTimeMilliseconds);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteString("code", historyResponse.Code);
            }
            else if (frame is LilithGameBridgeNotificationResponse notificationResponse)
            {
                writer.WriteString(
                    "requestId",
                    notificationResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", notificationResponse.Sequence);
                writer.WriteString("method", notificationResponse.Method);
                writer.WriteBoolean("succeeded", notificationResponse.Succeeded);
                writer.WriteNumber(
                    "oldestAvailableSequence",
                    notificationResponse.OldestAvailableSequence);
                writer.WriteNumber(
                    "latestSequence",
                    notificationResponse.LatestSequence);
                writer.WriteBoolean("hasMore", notificationResponse.HasMore);
                writer.WritePropertyName("entries");
                writer.WriteStartArray();
                foreach (var entry in notificationResponse.Entries)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("sequence", entry.Sequence);
                    writer.WriteString("type", entry.Type);
                    writer.WriteString("schema", entry.Schema);
                    writer.WriteBoolean("succeeded", entry.Succeeded);
                    writer.WriteString("text", entry.Text);
                    writer.WriteString("speechText", entry.SpeechText);
                    writer.WriteString(
                        "speechLocale",
                        SpeechLocaleToken(entry.SpeechLocale));
                    if (entry.Continuation is not null)
                    {
                        entry.Continuation.Write(writer);
                        writer.WriteString("speechEmotion", entry.SpeechEmotion);
                    }
                    if (entry.MiniGameEventId is { } gameEventId)
                        writer.WriteString("miniGameEventId", gameEventId.ToString("N"));
                    if (entry.MiniGameEventReceipt is not null)
                    {
                        writer.WritePropertyName("miniGameEventReceipt");
                        entry.MiniGameEventReceipt.Write(writer);
                    }
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteString("code", notificationResponse.Code);
            }
            else if (frame is LilithGameBridgeInterruptResponse interruptResponse)
            {
                writer.WriteString("requestId", interruptResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", interruptResponse.Sequence);
                writer.WriteString("method", interruptResponse.Method);
                writer.WriteString("turnId", interruptResponse.TurnId.ToString("N"));
                writer.WriteBoolean("succeeded", interruptResponse.Succeeded);
                writer.WriteString("code", interruptResponse.Code);
            }
            else if (frame is LilithGameBridgeNativeAssistantCommitResponse nativeCommitResponse)
            {
                if (nativeCommitResponse.MiniGameEventsPending)
                    writer.WriteBoolean("miniGameEventsPending", true);
                if (nativeCommitResponse.MiniGameEventReceipt is not null)
                {
                    writer.WritePropertyName("miniGameEventReceipt");
                    nativeCommitResponse.MiniGameEventReceipt.Write(writer);
                }
                writer.WriteString("requestId", nativeCommitResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", nativeCommitResponse.Sequence);
                writer.WriteString("method", nativeCommitResponse.Method);
                writer.WriteString("eventId", nativeCommitResponse.EventId.ToString("N"));
                writer.WriteBoolean("accepted", nativeCommitResponse.Accepted);
                writer.WriteBoolean(
                    "alreadyCommitted",
                    nativeCommitResponse.AlreadyCommitted);
                writer.WriteBoolean("retryable", nativeCommitResponse.Retryable);
                writer.WriteString("code", nativeCommitResponse.Code);
            }
            else if (frame is LilithGameBridgeNoteDraftResponse noteDraftResponse)
            {
                writer.WriteString("requestId", noteDraftResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", noteDraftResponse.Sequence);
                writer.WriteString("method", noteDraftResponse.Method);
                writer.WriteString("eventId", noteDraftResponse.EventId.ToString("N"));
                writer.WriteString("state", NoteDraftStateToken(noteDraftResponse.State));
                writer.WriteString("text", noteDraftResponse.Text);
            }
            else if (frame is LilithGameBridgeForegroundProactiveResponse foregroundResponse)
            {
                writer.WriteString(
                    "requestId",
                    foregroundResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", foregroundResponse.Sequence);
                writer.WriteString("method", foregroundResponse.Method);
                writer.WriteString(
                    "operationId",
                    foregroundResponse.OperationId.ToString("N"));
                writer.WriteBoolean("accepted", foregroundResponse.Accepted);
                writer.WriteBoolean("succeeded", foregroundResponse.Succeeded);
                writer.WriteString(
                    "state",
                    ForegroundProactiveStateToken(foregroundResponse.State));
                writer.WriteBoolean(
                    "alreadyProcessed",
                    foregroundResponse.AlreadyProcessed);
                writer.WriteBoolean("retryable", foregroundResponse.Retryable);
                writer.WriteString("code", foregroundResponse.Code);
                if (foregroundResponse.MiniGameEventReceipt is not null)
                {
                    writer.WritePropertyName("miniGameEventReceipt");
                    foregroundResponse.MiniGameEventReceipt.Write(writer);
                }
            }
            else if (frame is LilithGameBridgeVoiceStartResponse voiceStartResponse)
            {
                writer.WriteString("requestId", voiceStartResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", voiceStartResponse.Sequence);
                writer.WriteString("method", voiceStartResponse.Method);
                writer.WriteString("captureId", voiceStartResponse.CaptureId.ToString("N"));
                writer.WriteBoolean("succeeded", voiceStartResponse.Succeeded);
                writer.WriteString("code", voiceStartResponse.Code);
            }
            else if (frame is LilithGameBridgeVoiceStopResponse voiceStopResponse)
            {
                writer.WriteString("requestId", voiceStopResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", voiceStopResponse.Sequence);
                writer.WriteString("method", voiceStopResponse.Method);
                writer.WriteString("captureId", voiceStopResponse.CaptureId.ToString("N"));
                writer.WriteBoolean("succeeded", voiceStopResponse.Succeeded);
                writer.WriteString("terminal", TerminalToken(voiceStopResponse.Terminal));
                writer.WriteString("transcript", voiceStopResponse.Transcript);
                writer.WriteString("code", voiceStopResponse.Code);
            }
            else if (frame is LilithGameBridgeMemoPollResponse memoPollResponse)
            {
                writer.WriteString("requestId", memoPollResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", memoPollResponse.Sequence);
                writer.WriteString("method", memoPollResponse.Method);
                writer.WriteBoolean("succeeded", memoPollResponse.Succeeded);
                writer.WritePropertyName("command");
                if (memoPollResponse.Command is null)
                    writer.WriteNullValue();
                else
                    LilithMemoProtocol.WriteCommand(writer, memoPollResponse.Command);
                writer.WriteString("code", memoPollResponse.Code);
            }
            else if (frame is LilithGameBridgeMemoCompleteResponse memoCompleteResponse)
            {
                writer.WriteString("requestId", memoCompleteResponse.RequestId.ToString("N"));
                writer.WriteNumber("sequence", memoCompleteResponse.Sequence);
                writer.WriteString("method", memoCompleteResponse.Method);
                writer.WriteString("operationId", memoCompleteResponse.OperationId.ToString("N"));
                writer.WriteBoolean("accepted", memoCompleteResponse.Accepted);
                writer.WriteString("code", memoCompleteResponse.Code);
            }
            else if (frame is LilithGameBridgeHello hello)
            {
                writer.WriteNumber("sequence", hello.Sequence);
                writer.WriteBoolean("ready", hello.Ready);
            }
            else
            {
                throw new ArgumentException("Game bridge frame type was rejected.", nameof(frame));
            }
            writer.WriteEndObject();
        }

        if (stream.Length is <= 0 or > MaximumFrameBytes)
            throw new InvalidDataException("Game bridge frame length was rejected.");
        return stream.ToArray();
    }

    public static LilithGameBridgeFrame Decode(ReadOnlyMemory<byte> frame)
    {
        if (frame.Length is <= 0 or > MaximumFrameBytes)
            throw new InvalidDataException("Game bridge frame length was rejected.");
        var span = frame.Span;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            throw new InvalidDataException("Game bridge byte-order mark was rejected.");

        try
        {
            using var document = JsonDocument.Parse(frame, DocumentOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Game bridge object was rejected.");
            var root = document.RootElement;
            var kind = RequiredString(root, "kind");
            return kind switch
            {
                "runtime.hello" => DecodeHello(root),
                "game.request" => DecodeRequest(root),
                "game.response" => DecodeResponse(root),
                _ => throw new InvalidDataException("Game bridge frame kind was rejected.")
            };
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Game bridge JSON was rejected.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Game bridge shape was rejected.", exception);
        }
    }

    public static async Task WriteAsync(
        Stream stream,
        LilithGameBridgeFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("Game bridge stream must be writable.", nameof(stream));
        var bytes = Encode(frame);
        var prefix = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await stream.WriteAsync(prefix.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<LilithGameBridgeFrame> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("Game bridge stream must be readable.", nameof(stream));
        var prefix = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, prefix, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length is <= 0 or > MaximumFrameBytes)
            throw new InvalidDataException("Game bridge frame prefix was rejected.");
        var bytes = new byte[length];
        await ReadExactlyAsync(stream, bytes, cancellationToken).ConfigureAwait(false);
        return Decode(bytes);
    }

    internal static string ValidateConversationText(
        string? value,
        string parameterName,
        bool allowEmpty)
    {
        if (value is null
            || (!allowEmpty && string.IsNullOrWhiteSpace(value))
            || value.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("Game bridge conversation text was rejected.", parameterName);
        }
        try
        {
            if (StrictUtf8.GetByteCount(value) > MaximumConversationUtf8Bytes)
                throw new ArgumentOutOfRangeException(parameterName);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Game bridge text was not valid Unicode.", parameterName, exception);
        }
        return value;
    }

    internal static string ValidateNoteText(
        string? value, string parameterName, int maximumCharacters, bool allowEmpty)
    {
        var validated = ValidateConversationText(value, parameterName, allowEmpty);
        if (validated.Length > maximumCharacters)
            throw new ArgumentOutOfRangeException(parameterName);
        if (validated.Any(static c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new ArgumentException("Note draft control characters were rejected.", parameterName);
        return validated;
    }

    internal static string ValidateToken(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 128
            || value.Any(character =>
                !((character >= 'a' && character <= 'z')
                  || (character >= '0' && character <= '9')
                  || character is '.' or '_' or '-')))
        {
            throw new ArgumentException("Game bridge token was rejected.", parameterName);
        }
        return value;
    }

    internal static string ValidateApplicationToken(
        string? value,
        string parameterName,
        int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value)
            || maximumCharacters <= 0
            || value.Length > maximumCharacters
            || value.Any(character =>
                !((character >= 'a' && character <= 'z')
                  || (character >= '0' && character <= '9')
                  || character is '.' or '_' or '-')))
        {
            throw new ArgumentException(
                "Game bridge application token was rejected.",
                parameterName);
        }
        return value;
    }

    internal static string ValidateApplicationDisplayName(
        string? value,
        string parameterName)
    {
        var validated = ValidateMetadata(
            value,
            parameterName,
            MaximumForegroundApplicationDisplayNameCharacters,
            allowEmpty: false);
        if (!string.Equals(validated, validated.Trim(), StringComparison.Ordinal)
            || validated.IndexOfAny(new[] { '\\', '/', ':' }) >= 0)
        {
            throw new ArgumentException(
                "Game bridge application display name was rejected.",
                parameterName);
        }
        return validated;
    }

    internal static string ValidateMetadata(
        string? value,
        string parameterName,
        int maximumCharacters,
        bool allowEmpty)
    {
        if (value is null
            || maximumCharacters <= 0
            || (!allowEmpty && string.IsNullOrWhiteSpace(value))
            || value.Length > maximumCharacters
            || value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Game bridge metadata was rejected.",
                parameterName);
        }
        try
        {
            _ = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Game bridge metadata was not valid Unicode.",
                parameterName,
                exception);
        }
        return value;
    }

    internal static string ValidateCursor(string? value, string parameterName)
    {
        value ??= string.Empty;
        if (value.Length > 128
            || value.Any(character =>
                !((character >= 'a' && character <= 'z')
                  || (character >= 'A' && character <= 'Z')
                  || (character >= '0' && character <= '9')
                  || character is '.' or '_' or '-')))
        {
            throw new ArgumentException("Game bridge cursor was rejected.", parameterName);
        }
        return value;
    }

    private static LilithGameBridgeHello DecodeHello(JsonElement root)
    {
        RequireExact(root, "version", "kind", "generationId", "sequence", "ready");
        RequireVersion(root);
        return new LilithGameBridgeHello(
            RequiredGuid(root, "generationId"),
            RequiredInt64(root, "sequence"),
            RequiredBoolean(root, "ready"));
    }

    private static LilithGameBridgeClientRequest DecodeRequest(JsonElement root)
    {
        var method = RequiredString(root, "method");
        return method switch
        {
            LilithGameBridgeRequest.SubmitMethod => DecodeSubmitRequest(root),
            LilithGameBridgePlayerNameRequest.SyncMethod => DecodePlayerNameRequest(root),
            LilithGameBridgeNoteDraftRequest.NoteDraftMethod => DecodeNoteDraftRequest(root),
            LilithGameBridgeHistoryRequest.HistoryMethod => DecodeHistoryRequest(root),
            LilithGameBridgeNotificationRequest.NotificationMethod =>
                DecodeNotificationRequest(root),
            LilithGameBridgeInterruptRequest.InterruptMethod => DecodeInterruptRequest(root),
            LilithGameBridgeNativeAssistantCommitRequest.CommitMethod =>
                DecodeNativeAssistantCommitRequest(root),
            LilithGameBridgeForegroundProactiveRequest.ForegroundProactiveMethod =>
                DecodeForegroundProactiveRequest(root),
            LilithGameBridgeVoiceStartRequest.VoiceStartMethod => DecodeVoiceStartRequest(root),
            LilithGameBridgeVoiceStopRequest.VoiceStopMethod => DecodeVoiceStopRequest(root),
            LilithGameBridgeMemoPollRequest.MemoPollMethod => DecodeMemoPollRequest(root),
            LilithGameBridgeMemoCompleteRequest.MemoCompleteMethod =>
                DecodeMemoCompleteRequest(root),
            LilithGameBridgeNoteInboxPollRequest.PollMethod => LilithNoteInboxGameFrames.ReadRequest(root, poll: true),
            LilithGameBridgeNoteInboxCompleteRequest.CompleteMethod => LilithNoteInboxGameFrames.ReadRequest(root, poll: false),
            LilithGameBridgeDailyNoteOpportunityRequest.OpportunityMethod => LilithDailyNoteGameFrames.ReadRequest(root, LilithGameBridgeDailyNoteOpportunityRequest.OpportunityMethod),
            LilithGameBridgeDailyNotePollRequest.PollMethod => LilithDailyNoteGameFrames.ReadRequest(root, LilithGameBridgeDailyNotePollRequest.PollMethod),
            LilithGameBridgeDailyNoteCompleteRequest.CompleteMethod => LilithDailyNoteGameFrames.ReadRequest(root, LilithGameBridgeDailyNoteCompleteRequest.CompleteMethod),
            _ => throw new InvalidDataException("Game bridge method was rejected.")
        };
    }

    private static LilithGameBridgeRequest DecodeSubmitRequest(JsonElement root)
    {
        var hasContext = root.TryGetProperty("miniGameContext", out var context);
        var hasDiagnostic = root.TryGetProperty("miniGameDiagnostic", out var diagnostic);
        var hasRepair = root.TryGetProperty("miniGameAnswerRepair", out var repair);
        RequireExact(
            root,
            new[] { "version", "kind", "generationId", "requestId", "sequence",
                "method", "turnId", "surface", "text", "displayLocale", "speechLocale" }
                .Concat(hasContext ? new[] { "miniGameContext" } : Array.Empty<string>())
                .Concat(hasDiagnostic ? new[] { "miniGameDiagnostic" } : Array.Empty<string>())
                .Concat(hasRepair ? new[] { "miniGameAnswerRepair" } : Array.Empty<string>()).ToArray());
        RequireVersion(root);
        return new LilithGameBridgeRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "turnId"),
            ParseSurface(RequiredString(root, "surface")),
            RequiredString(root, "text"),
            ParseDisplayLocale(RequiredString(root, "displayLocale")),
            ParseSpeechLocale(RequiredString(root, "speechLocale")),
            hasContext ? LilithMiniGamePublicContext.Read(context) : null,
            hasDiagnostic ? LilithMiniGameDiagnosticRequest.Parse(diagnostic) : null,
            hasRepair ? LilithMiniGameAnswerRepair.Read(repair) : null);
    }

    private static LilithGameBridgePlayerNameRequest DecodePlayerNameRequest(JsonElement root)
    {
        RequireExact(root, "version", "kind", "generationId", "requestId", "sequence",
            "method", "gameEpoch", "observationSequence", "displayName");
        RequireVersion(root);
        return new LilithGameBridgePlayerNameRequest(RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"), RequiredInt64(root, "sequence"),
            RequiredGuid(root, "gameEpoch"), RequiredInt64(root, "observationSequence"),
            RequiredString(root, "displayName"));
    }

    private static LilithGameBridgePlayerNameResponse DecodePlayerNameResponse(JsonElement root)
    {
        RequireExact(root, "version", "kind", "generationId", "requestId", "sequence",
            "method", "gameEpoch", "observationSequence", "accepted", "revision");
        RequireVersion(root);
        return new LilithGameBridgePlayerNameResponse(RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"), RequiredInt64(root, "sequence"),
            RequiredGuid(root, "gameEpoch"), RequiredInt64(root, "observationSequence"),
            RequiredBoolean(root, "accepted"), RequiredInt64AllowZero(root, "revision", long.MaxValue));
    }

    private static LilithGameBridgeNoteDraftRequest DecodeNoteDraftRequest(JsonElement root)
    {
        RequireExact(root, "version", "kind", "generationId", "requestId", "sequence",
            "method", "eventId", "category", "topic", "context", "displayLocale");
        RequireVersion(root);
        return new LilithGameBridgeNoteDraftRequest(
            RequiredGuid(root, "generationId"), RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"), RequiredGuid(root, "eventId"),
            RequiredString(root, "category"), RequiredString(root, "topic"),
            RequiredString(root, "context"),
            ParseNoteDraftDisplayLocale(RequiredString(root, "displayLocale")));
    }

    private static LilithGameBridgeHistoryRequest DecodeHistoryRequest(JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "cursor", "pageSize");
        RequireVersion(root);
        return new LilithGameBridgeHistoryRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredString(root, "cursor"),
            RequiredInt32(root, "pageSize", 1, MaximumHistoryPageSize));
    }

    private static LilithGameBridgeNotificationRequest DecodeNotificationRequest(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "afterSequence", "waitMilliseconds", "displayLocale",
            "speechLocale", "tailOnly");
        RequireVersion(root);
        return new LilithGameBridgeNotificationRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredInt64AllowZero(root, "afterSequence", long.MaxValue),
            RequiredInt32(
                root,
                "waitMilliseconds",
                0,
                MaximumNotificationWaitMilliseconds),
            ParseDisplayLocale(RequiredString(root, "displayLocale")),
            ParseSpeechLocale(RequiredString(root, "speechLocale")),
            RequiredBoolean(root, "tailOnly"));
    }

    private static LilithGameBridgeInterruptRequest DecodeInterruptRequest(JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "turnId");
        RequireVersion(root);
        return new LilithGameBridgeInterruptRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "turnId"));
    }

    private static LilithGameBridgeNativeAssistantCommitRequest
        DecodeNativeAssistantCommitRequest(JsonElement root)
    {
        var fields = new List<string> {
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "eventId", "proactiveDialogueEnabled", "perception" };
        var hasContext = root.TryGetProperty("miniGameContext", out var context);
        if (hasContext) fields.Add("miniGameContext");
        RequireExact(root, fields.ToArray());
        RequireVersion(root);
        var perception = root.GetProperty("perception");
        if (perception.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                "Game bridge native perception was rejected.");
        var hasApplication = perception.TryGetProperty("application", out var applicationValue);
        RequireExact(perception, hasApplication
            ? new[] { "schemaVersion", "source", "text", "occurredAtUnixMs", "nodeId", "lineId", "trigger", "locale", "application" }
            : new[] { "schemaVersion", "source", "text", "occurredAtUnixMs", "nodeId", "lineId", "trigger", "locale" });
        return new LilithGameBridgeNativeAssistantCommitRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "eventId"),
            RequiredBoolean(root, "proactiveDialogueEnabled"),
            new LilithGameBridgeNativePerceptionSnapshot(
                RequiredInt32(
                    perception,
                    "schemaVersion",
                    LilithGameBridgeNativePerceptionSnapshot.CurrentSchemaVersion,
                    LilithGameBridgeNativePerceptionSnapshot.CurrentSchemaVersion),
                RequiredString(perception, "source"),
                RequiredString(perception, "text"),
                RequiredInt64AllowZero(
                    perception,
                    "occurredAtUnixMs",
                    253402300799999),
                RequiredString(perception, "nodeId"),
                RequiredString(perception, "lineId"),
                RequiredString(perception, "trigger"),
                RequiredString(perception, "locale"),
                hasApplication ? LilithGameBridgeNativeApplicationObservation.DecodeNative(applicationValue) : null),
            hasContext ? LilithMiniGamePublicContext.Read(context) : null);
    }

    private static LilithGameBridgeVoiceStartRequest DecodeVoiceStartRequest(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "captureId", "language", "maximumDurationMilliseconds");
        RequireVersion(root);
        return new LilithGameBridgeVoiceStartRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "captureId"),
            ParseVoiceLanguage(RequiredString(root, "language")),
            RequiredInt32(
                root,
                "maximumDurationMilliseconds",
                MinimumVoiceCaptureDurationMilliseconds,
                MaximumVoiceCaptureDurationMilliseconds));
    }

    private static LilithGameBridgeForegroundProactiveRequest
        DecodeForegroundProactiveRequest(JsonElement root)
    {
        var hasContext = root.TryGetProperty("miniGameContext", out var context);
        RequireExact(
            root,
            new[] { "version", "kind", "generationId", "requestId", "sequence",
            "method", "operationId", "observedAtUnixMs", "applicationId",
            "applicationDisplayName", "applicationCategory", "displayLocale" }
                .Concat(hasContext ? new[] { "miniGameContext" } : Array.Empty<string>()).ToArray());
        RequireVersion(root);
        return new LilithGameBridgeForegroundProactiveRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "operationId"),
            RequiredInt64AllowZero(
                root,
                "observedAtUnixMs",
                253402300799999),
            RequiredString(root, "applicationId"),
            RequiredString(root, "applicationDisplayName"),
            RequiredString(root, "applicationCategory"),
            ParseDisplayLocale(RequiredString(root, "displayLocale")),
            hasContext ? LilithMiniGamePublicContext.Read(context) : null);
    }

    private static LilithGameBridgeVoiceStopRequest DecodeVoiceStopRequest(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "captureId", "disposition");
        RequireVersion(root);
        return new LilithGameBridgeVoiceStopRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "captureId"),
            ParseVoiceStopDisposition(RequiredString(root, "disposition")));
    }

    private static LilithGameBridgeMemoPollRequest DecodeMemoPollRequest(JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "waitMilliseconds");
        RequireVersion(root);
        return new LilithGameBridgeMemoPollRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredInt32(root, "waitMilliseconds", 0, MaximumMemoWaitMilliseconds));
    }

    private static LilithGameBridgeMemoCompleteRequest DecodeMemoCompleteRequest(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "result");
        RequireVersion(root);
        var result = root.GetProperty("result");
        if (result.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Game bridge memo result was rejected.");
        return new LilithGameBridgeMemoCompleteRequest(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            LilithMemoProtocol.ReadResult(result));
    }

    private static LilithGameBridgeFrame DecodeResponse(JsonElement root)
    {
        var method = RequiredString(root, "method");
        return method switch
        {
            LilithGameBridgeRequest.SubmitMethod => DecodeSubmitResponse(root),
            LilithGameBridgePlayerNameRequest.SyncMethod => DecodePlayerNameResponse(root),
            LilithGameBridgeNoteDraftRequest.NoteDraftMethod => DecodeNoteDraftResponse(root),
            LilithGameBridgeHistoryRequest.HistoryMethod => DecodeHistoryResponse(root),
            LilithGameBridgeNotificationRequest.NotificationMethod =>
                DecodeNotificationResponse(root),
            LilithGameBridgeInterruptRequest.InterruptMethod => DecodeInterruptResponse(root),
            LilithGameBridgeNativeAssistantCommitRequest.CommitMethod =>
                DecodeNativeAssistantCommitResponse(root),
            LilithGameBridgeForegroundProactiveRequest.ForegroundProactiveMethod =>
                DecodeForegroundProactiveResponse(root),
            LilithGameBridgeVoiceStartRequest.VoiceStartMethod => DecodeVoiceStartResponse(root),
            LilithGameBridgeVoiceStopRequest.VoiceStopMethod => DecodeVoiceStopResponse(root),
            LilithGameBridgeMemoPollRequest.MemoPollMethod => DecodeMemoPollResponse(root),
            LilithGameBridgeMemoCompleteRequest.MemoCompleteMethod =>
                DecodeMemoCompleteResponse(root),
            LilithGameBridgeNoteInboxPollRequest.PollMethod => LilithNoteInboxGameFrames.ReadResponse(root, poll: true),
            LilithGameBridgeNoteInboxCompleteRequest.CompleteMethod => LilithNoteInboxGameFrames.ReadResponse(root, poll: false),
            LilithGameBridgeDailyNoteOpportunityRequest.OpportunityMethod => LilithDailyNoteGameFrames.ReadResponse(root, LilithGameBridgeDailyNoteOpportunityRequest.OpportunityMethod),
            LilithGameBridgeDailyNotePollRequest.PollMethod => LilithDailyNoteGameFrames.ReadResponse(root, LilithGameBridgeDailyNotePollRequest.PollMethod),
            LilithGameBridgeDailyNoteCompleteRequest.CompleteMethod => LilithDailyNoteGameFrames.ReadResponse(root, LilithGameBridgeDailyNoteCompleteRequest.CompleteMethod),
            _ => throw new InvalidDataException("Game bridge method was rejected.")
        };
    }

    private static LilithGameBridgeResponse DecodeSubmitResponse(JsonElement root)
    {
        var hasEmotion = root.TryGetProperty("speechEmotion", out _);
        var hasDiagnostic = root.TryGetProperty("miniGameDiagnostic", out var diagnostic);
        var hasReceipt = root.TryGetProperty("miniGameEventReceipt", out var receipt);
        RequireExact(
            root,
            (hasEmotion ? new[] { "speechEmotion" } : Array.Empty<string>()).Concat(new[] {
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "succeeded", "turnId", "userCommitted", "terminal",
            "assistantText", "speechText", "code" })
                .Concat(hasDiagnostic ? new[] { "miniGameDiagnostic" } : Array.Empty<string>())
                .Concat(hasReceipt ? new[] { "miniGameEventReceipt" } : Array.Empty<string>()).ToArray());
        RequireVersion(root);
        return new LilithGameBridgeResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "turnId"),
            RequiredBoolean(root, "succeeded"),
            RequiredBoolean(root, "userCommitted"),
            ParseTerminal(RequiredString(root, "terminal")),
            RequiredString(root, "assistantText"),
            RequiredString(root, "speechText"),
            RequiredString(root, "code"),
            hasEmotion ? RequiredString(root, "speechEmotion") : string.Empty,
            hasDiagnostic ? LilithMiniGameDiagnosticReceipt.Parse(diagnostic) : null,
            hasReceipt ? LilithMiniGameEventReceipt.Read(receipt) : null);
    }

    private static LilithGameBridgeNoteDraftResponse DecodeNoteDraftResponse(JsonElement root)
    {
        RequireExact(root, "version", "kind", "generationId", "requestId", "sequence",
            "method", "eventId", "state", "text");
        RequireVersion(root);
        return new LilithGameBridgeNoteDraftResponse(
            RequiredGuid(root, "generationId"), RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"), RequiredGuid(root, "eventId"),
            ParseNoteDraftState(RequiredString(root, "state")), RequiredString(root, "text"));
    }

    private static LilithGameBridgeHistoryResponse DecodeHistoryResponse(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "succeeded", "nextCursor", "hasMore",
            "entries", "code");
        RequireVersion(root);
        var entriesValue = root.GetProperty("entries");
        if (entriesValue.ValueKind != JsonValueKind.Array
            || entriesValue.GetArrayLength() > MaximumHistoryPageSize)
        {
            throw new InvalidDataException("Game bridge history entries were rejected.");
        }
        var entries = new List<LilithGameBridgeHistoryEntry>();
        foreach (var entryValue in entriesValue.EnumerateArray())
        {
            RequireExact(
                entryValue,
                "messageId", "turnId", "messageKind", "role", "text",
                "occurredAtUnixTimeMilliseconds");
            entries.Add(new LilithGameBridgeHistoryEntry(
                RequiredString(entryValue, "messageId"),
                RequiredString(entryValue, "turnId"),
                RequiredString(entryValue, "messageKind"),
                RequiredString(entryValue, "role"),
                RequiredString(entryValue, "text"),
                RequiredInt64AllowZero(
                    entryValue,
                    "occurredAtUnixTimeMilliseconds",
                    253402300799999)));
        }
        return new LilithGameBridgeHistoryResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredBoolean(root, "succeeded"),
            RequiredString(root, "nextCursor"),
            RequiredBoolean(root, "hasMore"),
            entries,
            RequiredString(root, "code"));
    }

    private static LilithGameBridgeNotificationResponse DecodeNotificationResponse(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "succeeded", "oldestAvailableSequence",
            "latestSequence", "hasMore", "entries", "code");
        RequireVersion(root);
        var entriesValue = root.GetProperty("entries");
        if (entriesValue.ValueKind != JsonValueKind.Array
            || entriesValue.GetArrayLength() > MaximumNotificationPageSize)
        {
            throw new InvalidDataException(
                "Game bridge notification entries were rejected.");
        }
        var entries = new List<LilithGameBridgeNotificationEntry>();
        foreach (var entryValue in entriesValue.EnumerateArray())
        {
            var hasContinuation = entryValue.TryGetProperty("continuation", out var continuationValue);
            var fields = new List<string>(hasContinuation
                ? new[] { "sequence", "type", "schema", "succeeded", "text", "speechText", "speechLocale", "continuation", "speechEmotion" }
                : new[] { "sequence", "type", "schema", "succeeded", "text", "speechText", "speechLocale" });
            var hasGameId = entryValue.TryGetProperty("miniGameEventId", out _);
            var hasReceipt = entryValue.TryGetProperty("miniGameEventReceipt", out var receipt);
            if (hasGameId) fields.Add("miniGameEventId");
            if (hasReceipt) fields.Add("miniGameEventReceipt");
            RequireExact(entryValue, fields.ToArray());
            entries.Add(new LilithGameBridgeNotificationEntry(
                RequiredInt64(entryValue, "sequence"),
                RequiredString(entryValue, "type"),
                RequiredString(entryValue, "schema"),
                RequiredBoolean(entryValue, "succeeded"),
                RequiredString(entryValue, "text"),
                RequiredString(entryValue, "speechText"),
                ParseSpeechLocale(
                    RequiredString(entryValue, "speechLocale")),
                hasContinuation ? RequiredString(entryValue, "speechEmotion") : string.Empty,
                hasContinuation ? LilithGameBridgeContinuationContext.Read(continuationValue) : null,
                hasGameId ? RequiredGuid(entryValue, "miniGameEventId") : null,
                hasReceipt ? LilithMiniGameEventReceipt.Read(receipt) : null));
        }
        return new LilithGameBridgeNotificationResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredBoolean(root, "succeeded"),
            RequiredInt64AllowZero(
                root,
                "oldestAvailableSequence",
                long.MaxValue),
            RequiredInt64AllowZero(root, "latestSequence", long.MaxValue),
            RequiredBoolean(root, "hasMore"),
            entries,
            RequiredString(root, "code"));
    }

    private static LilithGameBridgeInterruptResponse DecodeInterruptResponse(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "turnId", "succeeded", "code");
        RequireVersion(root);
        return new LilithGameBridgeInterruptResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "turnId"),
            RequiredBoolean(root, "succeeded"),
            RequiredString(root, "code"));
    }

    private static LilithGameBridgeNativeAssistantCommitResponse
        DecodeNativeAssistantCommitResponse(JsonElement root)
    {
        var fields = new List<string> {
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "eventId", "accepted", "alreadyCommitted", "retryable",
            "code" };
        var hasPending = root.TryGetProperty("miniGameEventsPending", out _);
        var hasReceipt = root.TryGetProperty("miniGameEventReceipt", out var receipt);
        if (hasPending) fields.Add("miniGameEventsPending");
        if (hasReceipt) fields.Add("miniGameEventReceipt");
        RequireExact(root, fields.ToArray());
        RequireVersion(root);
        return new LilithGameBridgeNativeAssistantCommitResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "eventId"),
            RequiredBoolean(root, "accepted"),
            RequiredBoolean(root, "alreadyCommitted"),
            RequiredBoolean(root, "retryable"),
            RequiredString(root, "code"),
            hasPending && RequiredBoolean(root, "miniGameEventsPending"),
            hasReceipt ? LilithMiniGameEventReceipt.Read(receipt) : null);
    }

    private static LilithGameBridgeVoiceStartResponse DecodeVoiceStartResponse(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "captureId", "succeeded", "code");
        RequireVersion(root);
        return new LilithGameBridgeVoiceStartResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "captureId"),
            RequiredBoolean(root, "succeeded"),
            RequiredString(root, "code"));
    }

    private static LilithGameBridgeForegroundProactiveResponse
        DecodeForegroundProactiveResponse(JsonElement root)
    {
        var hasReceipt = root.TryGetProperty("miniGameEventReceipt", out var receipt);
        RequireExact(
            root,
            new[] { "version", "kind", "generationId", "requestId", "sequence",
            "method", "operationId", "accepted", "succeeded", "state",
            "alreadyProcessed", "retryable", "code" }
                .Concat(hasReceipt ? new[] { "miniGameEventReceipt" } : Array.Empty<string>()).ToArray());
        RequireVersion(root);
        return new LilithGameBridgeForegroundProactiveResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "operationId"),
            RequiredBoolean(root, "accepted"),
            RequiredBoolean(root, "succeeded"),
            ParseForegroundProactiveState(RequiredString(root, "state")),
            RequiredBoolean(root, "alreadyProcessed"),
            RequiredBoolean(root, "retryable"),
            RequiredString(root, "code"),
            hasReceipt ? LilithMiniGameEventReceipt.Read(receipt) : null);
    }

    private static LilithGameBridgeVoiceStopResponse DecodeVoiceStopResponse(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "captureId", "succeeded", "terminal", "transcript", "code");
        RequireVersion(root);
        return new LilithGameBridgeVoiceStopResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "captureId"),
            RequiredBoolean(root, "succeeded"),
            ParseTerminal(RequiredString(root, "terminal")),
            RequiredString(root, "transcript"),
            RequiredString(root, "code"));
    }

    private static LilithGameBridgeMemoPollResponse DecodeMemoPollResponse(JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "succeeded", "command", "code");
        RequireVersion(root);
        var commandValue = root.GetProperty("command");
        LilithMemoCommand? command = commandValue.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Object => LilithMemoProtocol.ReadCommand(commandValue),
            _ => throw new InvalidDataException("Game bridge memo command was rejected.")
        };
        return new LilithGameBridgeMemoPollResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredBoolean(root, "succeeded"),
            command,
            RequiredString(root, "code"));
    }

    private static LilithGameBridgeMemoCompleteResponse DecodeMemoCompleteResponse(
        JsonElement root)
    {
        RequireExact(
            root,
            "version", "kind", "generationId", "requestId", "sequence",
            "method", "operationId", "accepted", "code");
        RequireVersion(root);
        return new LilithGameBridgeMemoCompleteResponse(
            RequiredGuid(root, "generationId"),
            RequiredGuid(root, "requestId"),
            RequiredInt64(root, "sequence"),
            RequiredGuid(root, "operationId"),
            RequiredBoolean(root, "accepted"),
            RequiredString(root, "code"));
    }

    private static void RequireExact(JsonElement root, params string[] names)
    {
        var expected = new HashSet<string>(names, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!expected.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException("Game bridge property was rejected.");
        }
        if (!seen.SetEquals(expected))
            throw new InvalidDataException("Game bridge property was missing.");
    }

    private static void RequireVersion(JsonElement root)
    {
        if (root.GetProperty("version").ValueKind != JsonValueKind.Number
            || !root.GetProperty("version").TryGetInt32(out var version)
            || version != Version)
        {
            throw new InvalidDataException("Game bridge version was rejected.");
        }
    }

    private static string RequiredString(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Game bridge string was rejected.");
        return value.GetString()!;
    }

    private static Guid RequiredGuid(JsonElement root, string name)
    {
        var text = RequiredString(root, name);
        if (!Guid.TryParseExact(text, "N", out var value) || value == Guid.Empty)
            throw new InvalidDataException("Game bridge identity was rejected.");
        return value;
    }

    private static long RequiredInt64(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result)
            || result <= 0)
        {
            throw new InvalidDataException("Game bridge sequence was rejected.");
        }
        return result;
    }

    private static int RequiredInt32(
        JsonElement root,
        string name,
        int minimum,
        int maximum)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result < minimum
            || result > maximum)
        {
            throw new InvalidDataException("Game bridge integer was rejected.");
        }
        return result;
    }

    private static long RequiredInt64AllowZero(
        JsonElement root,
        string name,
        long maximum)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result)
            || result < 0
            || result > maximum)
        {
            throw new InvalidDataException("Game bridge integer was rejected.");
        }
        return result;
    }

    private static bool RequiredBoolean(JsonElement root, string name)
    {
        var value = root.GetProperty(name);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Game bridge boolean was rejected.");
        return value.GetBoolean();
    }

    private static string SurfaceToken(LilithGameBridgeInputSurface surface)
        => surface switch
        {
            LilithGameBridgeInputSurface.Text => "text",
            LilithGameBridgeInputSurface.Voice => "voice",
            _ => throw new ArgumentOutOfRangeException(nameof(surface))
        };

    private static LilithGameBridgeInputSurface ParseSurface(string value)
        => value switch
        {
            "text" => LilithGameBridgeInputSurface.Text,
            "voice" => LilithGameBridgeInputSurface.Voice,
            _ => throw new InvalidDataException("Game bridge input surface was rejected.")
        };

    public static string DisplayLocaleToken(
        LilithGameBridgeDisplayLocale locale)
        => locale switch
        {
            LilithGameBridgeDisplayLocale.TraditionalChinese => "zh-Hant",
            LilithGameBridgeDisplayLocale.SimplifiedChinese => "zh-Hans",
            LilithGameBridgeDisplayLocale.Japanese => "ja",
            LilithGameBridgeDisplayLocale.English => "en",
            _ => throw new ArgumentOutOfRangeException(nameof(locale))
        };

    public static string NoteDraftDisplayLocaleToken(LilithGameBridgeDisplayLocale locale)
        => locale switch
        {
            LilithGameBridgeDisplayLocale.TraditionalChinese => "zh-TW",
            LilithGameBridgeDisplayLocale.SimplifiedChinese => "zh-CN",
            LilithGameBridgeDisplayLocale.Japanese => "ja",
            LilithGameBridgeDisplayLocale.English => "en",
            _ => throw new ArgumentOutOfRangeException(nameof(locale))
        };

    private static LilithGameBridgeDisplayLocale ParseNoteDraftDisplayLocale(string value)
        => value switch
        {
            "zh-TW" => LilithGameBridgeDisplayLocale.TraditionalChinese,
            "zh-CN" => LilithGameBridgeDisplayLocale.SimplifiedChinese,
            "ja" => LilithGameBridgeDisplayLocale.Japanese,
            "en" => LilithGameBridgeDisplayLocale.English,
            _ => throw new InvalidDataException("Note draft locale was rejected.")
        };

    public static string NoteDraftStateToken(LilithGameBridgeNoteDraftState state)
        => state switch
        {
            LilithGameBridgeNoteDraftState.Ready => "ready",
            LilithGameBridgeNoteDraftState.Silent => "silent",
            LilithGameBridgeNoteDraftState.Busy => "busy",
            LilithGameBridgeNoteDraftState.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    public static LilithGameBridgeNoteDraftState ParseNoteDraftState(string value)
        => value switch
        {
            "ready" => LilithGameBridgeNoteDraftState.Ready,
            "silent" => LilithGameBridgeNoteDraftState.Silent,
            "busy" => LilithGameBridgeNoteDraftState.Busy,
            "failed" => LilithGameBridgeNoteDraftState.Failed,
            _ => throw new InvalidDataException("Note draft state was rejected.")
        };

    private static LilithGameBridgeDisplayLocale ParseDisplayLocale(string value)
        => value switch
        {
            "zh-Hant" => LilithGameBridgeDisplayLocale.TraditionalChinese,
            "zh-Hans" => LilithGameBridgeDisplayLocale.SimplifiedChinese,
            "ja" => LilithGameBridgeDisplayLocale.Japanese,
            "en" => LilithGameBridgeDisplayLocale.English,
            _ => throw new InvalidDataException(
                "Game bridge display locale was rejected.")
        };

    public static string SpeechLocaleToken(
        LilithGameBridgeSpeechLocale locale)
        => locale switch
        {
            LilithGameBridgeSpeechLocale.Chinese => "zh",
            LilithGameBridgeSpeechLocale.Japanese => "ja",
            _ => throw new ArgumentOutOfRangeException(nameof(locale))
        };

    private static LilithGameBridgeSpeechLocale ParseSpeechLocale(string value)
        => value switch
        {
            "zh" => LilithGameBridgeSpeechLocale.Chinese,
            "ja" => LilithGameBridgeSpeechLocale.Japanese,
            _ => throw new InvalidDataException(
                "Game bridge speech locale was rejected.")
        };

    private static string TerminalToken(LilithGameBridgeTerminal terminal)
        => terminal switch
        {
            LilithGameBridgeTerminal.Completed => "completed",
            LilithGameBridgeTerminal.Failed => "failed",
            LilithGameBridgeTerminal.Interrupted => "interrupted",
            _ => throw new ArgumentOutOfRangeException(nameof(terminal))
        };

    private static LilithGameBridgeTerminal ParseTerminal(string value)
        => value switch
        {
            "completed" => LilithGameBridgeTerminal.Completed,
            "failed" => LilithGameBridgeTerminal.Failed,
            "interrupted" => LilithGameBridgeTerminal.Interrupted,
            _ => throw new InvalidDataException("Game bridge terminal was rejected.")
        };

    private static string ForegroundProactiveStateToken(
        LilithGameBridgeForegroundProactiveState state)
        => state switch
        {
            LilithGameBridgeForegroundProactiveState.Streaming => "streaming",
            LilithGameBridgeForegroundProactiveState.Completed => "completed",
            LilithGameBridgeForegroundProactiveState.Silent => "silent",
            LilithGameBridgeForegroundProactiveState.Abandoned => "abandoned",
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    private static LilithGameBridgeForegroundProactiveState
        ParseForegroundProactiveState(string value)
        => value switch
        {
            "streaming" => LilithGameBridgeForegroundProactiveState.Streaming,
            "completed" => LilithGameBridgeForegroundProactiveState.Completed,
            "silent" => LilithGameBridgeForegroundProactiveState.Silent,
            "abandoned" => LilithGameBridgeForegroundProactiveState.Abandoned,
            _ => throw new InvalidDataException(
                "Game bridge foreground proactive state was rejected.")
        };

    private static string VoiceLanguageToken(LilithGameBridgeVoiceLanguage language)
        => language switch
        {
            LilithGameBridgeVoiceLanguage.Japanese => "ja",
            LilithGameBridgeVoiceLanguage.TraditionalChinese => "zh-TW",
            LilithGameBridgeVoiceLanguage.Automatic => "auto",
            LilithGameBridgeVoiceLanguage.English => "en",
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

    private static LilithGameBridgeVoiceLanguage ParseVoiceLanguage(string value)
        => value switch
        {
            "ja" => LilithGameBridgeVoiceLanguage.Japanese,
            "zh-TW" => LilithGameBridgeVoiceLanguage.TraditionalChinese,
            "auto" => LilithGameBridgeVoiceLanguage.Automatic,
            "en" => LilithGameBridgeVoiceLanguage.English,
            _ => throw new InvalidDataException("Game bridge voice language was rejected.")
        };

    private static string VoiceStopDispositionToken(
        LilithGameBridgeVoiceStopDisposition disposition)
        => disposition switch
        {
            LilithGameBridgeVoiceStopDisposition.Transcribe => "transcribe",
            LilithGameBridgeVoiceStopDisposition.Cancel => "cancel",
            _ => throw new ArgumentOutOfRangeException(nameof(disposition))
        };

    private static LilithGameBridgeVoiceStopDisposition ParseVoiceStopDisposition(
        string value)
        => value switch
        {
            "transcribe" => LilithGameBridgeVoiceStopDisposition.Transcribe,
            "cancel" => LilithGameBridgeVoiceStopDisposition.Cancel,
            _ => throw new InvalidDataException(
                "Game bridge voice stop disposition was rejected.")
        };

    private static async Task ReadExactlyAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Game bridge frame ended early.");
            offset += read;
        }
    }
}
