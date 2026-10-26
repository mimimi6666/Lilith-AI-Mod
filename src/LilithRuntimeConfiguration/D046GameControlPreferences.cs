using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace LilithRuntime.Configuration;

public enum D046FunctionKey
{
    F1 = 1,
    F2 = 2,
    F3 = 3,
    F4 = 4,
    F5 = 5,
    F6 = 6,
    F7 = 7,
    F8 = 8,
    F9 = 9,
    F10 = 10,
    F11 = 11,
    F12 = 12,
    F13 = 13,
    F14 = 14,
    F15 = 15,
    A = 100,
    B = 101,
    C = 102,
    D = 103,
    E = 104,
    F = 105,
    G = 106,
    H = 107,
    I = 108,
    J = 109,
    K = 110,
    L = 111,
    M = 112,
    N = 113,
    O = 114,
    P = 115,
    Q = 116,
    R = 117,
    S = 118,
    T = 119,
    U = 120,
    V = 121,
    W = 122,
    X = 123,
    Y = 124,
    Z = 125,
    Digit0 = 200,
    Digit1 = 201,
    Digit2 = 202,
    Digit3 = 203,
    Digit4 = 204,
    Digit5 = 205,
    Digit6 = 206,
    Digit7 = 207,
    Digit8 = 208,
    Digit9 = 209,
    NumPad0 = 300,
    NumPad1 = 301,
    NumPad2 = 302,
    NumPad3 = 303,
    NumPad4 = 304,
    NumPad5 = 305,
    NumPad6 = 306,
    NumPad7 = 307,
    NumPad8 = 308,
    NumPad9 = 309,
    NumPadDivide = 310,
    NumPadMultiply = 311,
    NumPadSubtract = 312,
    NumPadAdd = 313,
    NumPadDecimal = 314,
    LeftArrow = 400,
    RightArrow = 401,
    UpArrow = 402,
    DownArrow = 403,
    Home = 404,
    End = 405,
    PageUp = 406,
    PageDown = 407,
    Insert = 408,
    Delete = 409,
    Space = 500,
    Tab = 501,
    Enter = 502,
    Backspace = 503,
    OemSemicolon = 600,
    OemPlus = 601,
    OemComma = 602,
    OemMinus = 603,
    OemPeriod = 604,
    OemQuestion = 605,
    OemTilde = 606,
    OemOpenBrackets = 607,
    OemPipe = 608,
    OemCloseBrackets = 609,
    OemQuotes = 610
}

/// <summary>
/// Closed, portable single-key catalog shared by Control Center and the game
/// projection. Values omitted here are never assignable even if an operating
/// system or UI framework can report them.
/// </summary>
public static class D046FunctionKeyCatalog
{
    private readonly struct Definition
    {
        internal D046FunctionKey Key { get; }
        internal int WindowsVirtualKey { get; }
        internal string DisplayLabel { get; }

        internal Definition(
            D046FunctionKey key,
            int windowsVirtualKey,
            string displayLabel)
        {
            Key = key;
            WindowsVirtualKey = windowsVirtualKey;
            DisplayLabel = displayLabel;
        }
    }

    private static readonly Definition[] Definitions = CreateDefinitions();
    private static readonly IReadOnlyList<D046FunctionKey> Assignable =
        Array.AsReadOnly(Definitions.Select(value => value.Key).ToArray());

    public static IReadOnlyList<D046FunctionKey> AllAssignable => Assignable;

    public static bool IsAssignable(D046FunctionKey key)
        => Definitions.Any(value => value.Key == key);

    public static string GetDisplayLabel(D046FunctionKey key)
    {
        foreach (var definition in Definitions)
        {
            if (definition.Key == key)
                return definition.DisplayLabel;
        }

        throw new ArgumentOutOfRangeException(
            nameof(key),
            key,
            "The key is not in the assignable D-046 catalog.");
    }

    public static bool TryFromWindowsVirtualKey(
        int virtualKey,
        out D046FunctionKey key)
    {
        foreach (var definition in Definitions)
        {
            if (definition.WindowsVirtualKey == virtualKey)
            {
                key = definition.Key;
                return true;
            }
        }

        key = default;
        return false;
    }

    public static bool TryGetWindowsVirtualKey(
        D046FunctionKey key,
        out int virtualKey)
    {
        foreach (var definition in Definitions)
        {
            if (definition.Key == key)
            {
                virtualKey = definition.WindowsVirtualKey;
                return true;
            }
        }

        virtualKey = 0;
        return false;
    }

    private static Definition[] CreateDefinitions()
    {
        var definitions = new List<Definition>(91);
        for (var index = 0; index < 26; index++)
        {
            definitions.Add(new Definition(
                (D046FunctionKey)((int)D046FunctionKey.A + index),
                0x41 + index,
                ((char)('A' + index)).ToString()));
        }
        for (var index = 0; index < 10; index++)
        {
            definitions.Add(new Definition(
                (D046FunctionKey)((int)D046FunctionKey.Digit0 + index),
                0x30 + index,
                index.ToString()));
        }
        for (var index = 0; index < 10; index++)
        {
            definitions.Add(new Definition(
                (D046FunctionKey)((int)D046FunctionKey.NumPad0 + index),
                0x60 + index,
                "NumPad " + index));
        }
        definitions.AddRange(new[]
        {
            new Definition(D046FunctionKey.NumPadDivide, 0x6F, "NumPad /"),
            new Definition(D046FunctionKey.NumPadMultiply, 0x6A, "NumPad *"),
            new Definition(D046FunctionKey.NumPadSubtract, 0x6D, "NumPad -"),
            new Definition(D046FunctionKey.NumPadAdd, 0x6B, "NumPad +"),
            new Definition(D046FunctionKey.NumPadDecimal, 0x6E, "NumPad .")
        });
        for (var number = 1; number <= 15; number++)
        {
            definitions.Add(new Definition(
                (D046FunctionKey)number,
                0x6F + number,
                "F" + number));
        }
        definitions.AddRange(new[]
        {
            new Definition(D046FunctionKey.LeftArrow, 0x25, "Left"),
            new Definition(D046FunctionKey.RightArrow, 0x27, "Right"),
            new Definition(D046FunctionKey.UpArrow, 0x26, "Up"),
            new Definition(D046FunctionKey.DownArrow, 0x28, "Down"),
            new Definition(D046FunctionKey.Home, 0x24, "Home"),
            new Definition(D046FunctionKey.End, 0x23, "End"),
            new Definition(D046FunctionKey.PageUp, 0x21, "Page Up"),
            new Definition(D046FunctionKey.PageDown, 0x22, "Page Down"),
            new Definition(D046FunctionKey.Insert, 0x2D, "Insert"),
            new Definition(D046FunctionKey.Delete, 0x2E, "Delete"),
            new Definition(D046FunctionKey.Space, 0x20, "Space"),
            new Definition(D046FunctionKey.Tab, 0x09, "Tab"),
            new Definition(D046FunctionKey.Enter, 0x0D, "Enter"),
            new Definition(D046FunctionKey.Backspace, 0x08, "Backspace"),
            new Definition(D046FunctionKey.OemSemicolon, 0xBA, ";"),
            new Definition(D046FunctionKey.OemPlus, 0xBB, "="),
            new Definition(D046FunctionKey.OemComma, 0xBC, ","),
            new Definition(D046FunctionKey.OemMinus, 0xBD, "-"),
            new Definition(D046FunctionKey.OemPeriod, 0xBE, "."),
            new Definition(D046FunctionKey.OemQuestion, 0xBF, "/"),
            new Definition(D046FunctionKey.OemTilde, 0xC0, "`"),
            new Definition(D046FunctionKey.OemOpenBrackets, 0xDB, "["),
            new Definition(D046FunctionKey.OemPipe, 0xDC, "\\"),
            new Definition(D046FunctionKey.OemCloseBrackets, 0xDD, "]"),
            new Definition(D046FunctionKey.OemQuotes, 0xDE, "'")
        });
        return definitions.ToArray();
    }
}

/// <summary>
/// Cross-process, crash-recoverable suppression signal held only while the
/// Control Center is waiting for one physical key-up. The mutex identity is
/// scoped to the current user's local-application-data root and Windows
/// session; it carries no setting value and grants no input authority.
/// </summary>
public sealed class D046GameControlCaptureLease : IDisposable
{
    private static readonly string CaptureMutexName = CreateCaptureMutexName();
    private static readonly object CaptureObserverGate = new();
    private static readonly Lazy<Mutex> CaptureObserver = new(
        CreateCaptureObserver,
        LazyThreadSafetyMode.ExecutionAndPublication);
    private static int _processLeaseHeld;
    private static int _captureObserverCreationCount;
    private Mutex? _ownedMutex;
    private readonly int _ownerThreadId;

    private D046GameControlCaptureLease(Mutex ownedMutex)
    {
        _ownedMutex = ownedMutex;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public static bool TryAcquire(out D046GameControlCaptureLease? lease)
    {
        lease = null;
        if (Interlocked.CompareExchange(ref _processLeaseHeld, 1, 0) != 0)
            return false;
        Mutex? candidate = null;
        try
        {
            candidate = new Mutex(initiallyOwned: false, CaptureMutexName);
            if (!TryAcquireMutex(candidate))
                return false;
            lease = new D046GameControlCaptureLease(candidate);
            candidate = null;
            return true;
        }
        catch (Exception exception) when (IsCaptureLeaseException(exception))
        {
            return false;
        }
        finally
        {
            candidate?.Dispose();
            if (lease == null)
                Interlocked.Exchange(ref _processLeaseHeld, 0);
        }
    }

    public static bool IsActive()
    {
        if (Volatile.Read(ref _processLeaseHeld) != 0)
            return true;
        try
        {
            lock (CaptureObserverGate)
            {
                var observer = CaptureObserver.Value;
                if (!TryAcquireMutex(observer))
                    return true;
                observer.ReleaseMutex();
                return false;
            }
        }
        catch (Exception exception) when (IsCaptureLeaseException(exception))
        {
            // If the game cannot prove that capture is inactive, ordinary
            // hotkey presses stay suppressed for this poll.
            return true;
        }
    }

    public void Dispose()
    {
        var owned = _ownedMutex;
        if (owned == null)
            return;
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "The game-control capture lease must be released by its acquiring thread.");
        }

        _ownedMutex = null;
        try
        {
            owned.ReleaseMutex();
        }
        finally
        {
            owned.Dispose();
            Interlocked.Exchange(ref _processLeaseHeld, 0);
        }
    }

    private static string CreateCaptureMutexName()
    {
#if D046_KEY_CAPTURE_ISOLATED_TEST_SEAM
        // Only the linked Motion test assembly defines this symbol. Its real
        // mutex must not contend with a running player's per-frame observer.
        return "Local\\LilithAiMod.Tests.D046KeyCapture."
            + Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
            + "." + Guid.NewGuid().ToString("N");
#else
        var currentUserRoot = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(currentUserRoot))
            currentUserRoot = Environment.UserName;
        var normalized = currentUserRoot.Trim().ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "Local\\LilithAiMod.D046GameControl.KeyCapture."
            + Convert.ToHexString(digest.AsSpan(0, 16));
#endif
    }

    internal static int CaptureObserverCreationCountForTests =>
        Volatile.Read(ref _captureObserverCreationCount);

    private static Mutex CreateCaptureObserver()
    {
        Interlocked.Increment(ref _captureObserverCreationCount);
        return new Mutex(initiallyOwned: false, CaptureMutexName);
    }

    private static bool TryAcquireMutex(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static bool IsCaptureLeaseException(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or WaitHandleCannotBeOpenedException
            or InvalidOperationException;
}

public enum D046GameVoiceLanguage
{
    Japanese = 1,
    TraditionalChinese = 2
}

public enum D046SpeechInputLanguage
{
    Japanese = 1,
    TraditionalChinese = 2,
    Automatic = 3,
    English = 4
}

public sealed class D046GameControlPreferencesDraft
{
    public D046FunctionKey PushToTalkKey { get; set; }
    public D046FunctionKey TextChatKey { get; set; }
    public D046FunctionKey ConversationHistoryKey { get; set; }
    public D046FunctionKey StopKey { get; set; }
    public D046GameVoiceLanguage VoiceLanguage { get; set; }
    public D046SpeechInputLanguage SpeechInputLanguage { get; set; }
    public bool ProactiveDialogueEnabled { get; set; }

    public D046GameControlPreferencesDraft Copy()
        => new()
        {
            PushToTalkKey = PushToTalkKey,
            TextChatKey = TextChatKey,
            ConversationHistoryKey = ConversationHistoryKey,
            StopKey = StopKey,
            VoiceLanguage = VoiceLanguage,
            SpeechInputLanguage = SpeechInputLanguage,
            ProactiveDialogueEnabled = ProactiveDialogueEnabled
        };
}

public sealed class D046GameControlPreferencesDocument
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public long Revision { get; set; }
    public D046FunctionKey PushToTalkKey { get; set; } = D046FunctionKey.F6;
    public D046FunctionKey TextChatKey { get; set; } = D046FunctionKey.F7;
    public D046FunctionKey ConversationHistoryKey { get; set; } = D046FunctionKey.F10;
    public D046FunctionKey StopKey { get; set; } = D046FunctionKey.F8;
    public D046GameVoiceLanguage VoiceLanguage { get; set; } =
        D046GameVoiceLanguage.TraditionalChinese;
    public D046SpeechInputLanguage SpeechInputLanguage { get; set; } =
        D046SpeechInputLanguage.Automatic;
    public bool ProactiveDialogueEnabled { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UnixEpoch;

    public static D046GameControlPreferencesDocument Defaults()
        => Defaults(CultureInfo.CurrentUICulture);

    public static D046GameControlPreferencesDocument Defaults(
        CultureInfo uiCulture)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);
        return new D046GameControlPreferencesDocument
        {
            SpeechInputLanguage = InitialSpeechInputLanguage(uiCulture)
        };
    }

    public static D046SpeechInputLanguage InitialSpeechInputLanguage(
        CultureInfo uiCulture)
    {
        ArgumentNullException.ThrowIfNull(uiCulture);
        return uiCulture.TwoLetterISOLanguageName.ToLowerInvariant() switch
        {
            "zh" => D046SpeechInputLanguage.TraditionalChinese,
            "ja" => D046SpeechInputLanguage.Japanese,
            "en" => D046SpeechInputLanguage.English,
            _ => D046SpeechInputLanguage.Automatic
        };
    }

    public D046GameControlPreferencesDocument Copy()
        => new()
        {
            SchemaVersion = SchemaVersion,
            Revision = Revision,
            PushToTalkKey = PushToTalkKey,
            TextChatKey = TextChatKey,
            ConversationHistoryKey = ConversationHistoryKey,
            StopKey = StopKey,
            VoiceLanguage = VoiceLanguage,
            SpeechInputLanguage = SpeechInputLanguage,
            ProactiveDialogueEnabled = ProactiveDialogueEnabled,
            UpdatedUtc = UpdatedUtc
        };

    public D046GameControlPreferencesDraft ToDraft()
        => new()
        {
            PushToTalkKey = PushToTalkKey,
            TextChatKey = TextChatKey,
            ConversationHistoryKey = ConversationHistoryKey,
            StopKey = StopKey,
            VoiceLanguage = VoiceLanguage,
            SpeechInputLanguage = SpeechInputLanguage,
            ProactiveDialogueEnabled = ProactiveDialogueEnabled
        };
}

public sealed class D046GameControlValidationResult
{
    public bool Accepted { get; }
    public IReadOnlyList<string> Errors { get; }

    internal D046GameControlValidationResult(IEnumerable<string> errors)
    {
        Errors = errors.ToArray();
        Accepted = Errors.Count == 0;
    }
}

public static class D046GameControlPreferencesPolicy
{
    public static D046GameControlValidationResult Validate(
        D046GameControlPreferencesDraft? draft)
    {
        var errors = new List<string>();
        if (draft is null)
        {
            errors.Add("Game control preferences are required.");
            return new D046GameControlValidationResult(errors);
        }

        DemandKnownKey(draft.PushToTalkKey, nameof(draft.PushToTalkKey), errors);
        DemandKnownKey(draft.TextChatKey, nameof(draft.TextChatKey), errors);
        DemandKnownKey(
            draft.ConversationHistoryKey,
            nameof(draft.ConversationHistoryKey),
            errors);
        DemandKnownKey(draft.StopKey, nameof(draft.StopKey), errors);
        if (!Enum.IsDefined(typeof(D046GameVoiceLanguage), draft.VoiceLanguage))
            errors.Add("VoiceLanguage is unknown.");
        if (!Enum.IsDefined(
                typeof(D046SpeechInputLanguage),
                draft.SpeechInputLanguage))
        {
            errors.Add("SpeechInputLanguage is unknown.");
        }

        var distinct = new HashSet<D046FunctionKey>
        {
            draft.PushToTalkKey,
            draft.TextChatKey,
            draft.ConversationHistoryKey,
            draft.StopKey
        };
        if (distinct.Count != 4)
        {
            errors.Add(
                "Push-to-talk, text-input, history and stop keys must be distinct.");
        }
        return new D046GameControlValidationResult(errors);
    }

    private static void DemandKnownKey(
        D046FunctionKey key,
        string name,
        ICollection<string> errors)
    {
        if (!D046FunctionKeyCatalog.IsAssignable(key))
            errors.Add(name + " is unknown.");
    }
}

public enum D046GameControlLoadDisposition
{
    Ready = 1,
    MissingDefaults = 2,
    PreviousSchemaMigrated = 3,
    InvalidDefaultsFailClosed = 4
}

public sealed class D046GameControlLoadResult
{
    public D046GameControlPreferencesDocument Snapshot { get; }
    public D046GameControlLoadDisposition Disposition { get; }
    public bool RequiresExplicitRepair =>
        Disposition == D046GameControlLoadDisposition.InvalidDefaultsFailClosed;

    internal D046GameControlLoadResult(
        D046GameControlPreferencesDocument snapshot,
        D046GameControlLoadDisposition disposition)
    {
        Snapshot = snapshot.Copy();
        Disposition = disposition;
    }
}

public enum D046GameControlApplyAdmission
{
    Applied = 1,
    ValidationRejected = 2,
    RevisionConflict = 3,
    StoreUnavailable = 4,
    AppliedRequiresGameRestart = 5
}

public sealed class D046GameControlApplyResult
{
    public D046GameControlApplyAdmission Admission { get; }
    public D046GameControlPreferencesDocument Snapshot { get; }
    public IReadOnlyList<string> Errors { get; }

    internal D046GameControlApplyResult(
        D046GameControlApplyAdmission admission,
        D046GameControlPreferencesDocument snapshot,
        IEnumerable<string>? errors = null)
    {
        Admission = admission;
        Snapshot = snapshot.Copy();
        Errors = (errors ?? Array.Empty<string>()).ToArray();
    }
}

/// <summary>
/// Portable desired-state store for Control Center and the future standalone
/// Runtime. It does not apply keys or voice state to the game by itself.
/// </summary>
public sealed class D046GameControlPreferencesStore
{
    public const string StoreFileName = "current-v1.json";
    public const int MaximumStoreBytes = 16 * 1024;
    private static readonly TimeSpan ConcurrencyLockTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();
    private readonly string _rootDirectory;
    private readonly string _storePath;
    private readonly string _concurrencyMutexName;
    private readonly D046SpeechInputLanguage _initialSpeechInputLanguage;
    private readonly object _gate = new();

    public string StorePath => _storePath;

    public D046GameControlPreferencesStore(string rootDirectory)
        : this(rootDirectory, CultureInfo.CurrentUICulture)
    {
    }

    public D046GameControlPreferencesStore(
        string rootDirectory,
        CultureInfo initialUiCulture)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Store root is required.", nameof(rootDirectory));
        ArgumentNullException.ThrowIfNull(initialUiCulture);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _storePath = Path.Combine(_rootDirectory, StoreFileName);
        _concurrencyMutexName = CreateConcurrencyMutexName(_storePath);
        _initialSpeechInputLanguage =
            D046GameControlPreferencesDocument.InitialSpeechInputLanguage(
                initialUiCulture);
    }

    public static D046GameControlPreferencesStore ForCurrentUser()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("Local application data is unavailable.");
        return new D046GameControlPreferencesStore(Path.Combine(
            localApplicationData,
            "LilithAiMod",
            "Config",
            "game-control-v2"));
    }

    public D046GameControlLoadResult Load()
    {
        lock (_gate)
            return LoadUnsafe();
    }

    public D046GameControlApplyResult TryApply(
        long expectedRevision,
        D046GameControlPreferencesDraft? draft,
        DateTimeOffset now)
        => TryApplyCore(expectedRevision, draft, now, observedVoiceOnly: false);

    // The game may mirror only a verified official voice selection. It cannot
    // create/repair a profile, change shortcuts or wait five seconds on Unity.
    public D046GameControlApplyResult TryMirrorOfficialVoice(
        long expectedRevision,
        D046GameVoiceLanguage voiceLanguage,
        DateTimeOffset now)
    {
        var draft = D046GameControlPreferencesDocument.Defaults().ToDraft();
        draft.VoiceLanguage = voiceLanguage;
        return TryApplyCore(expectedRevision, draft, now, observedVoiceOnly: true);
    }

    private D046GameControlApplyResult TryApplyCore(
        long expectedRevision,
        D046GameControlPreferencesDraft? draft,
        DateTimeOffset now,
        bool observedVoiceOnly)
    {
        var validation = D046GameControlPreferencesPolicy.Validate(draft);
        if (!validation.Accepted || draft is null)
        {
            return new D046GameControlApplyResult(
                D046GameControlApplyAdmission.ValidationRejected,
                Load().Snapshot,
                validation.Errors);
        }

        lock (_gate)
        {
            D046GameControlLoadResult? current = null;
            try
            {
                using var mutex = new Mutex(
                    initiallyOwned: false,
                    _concurrencyMutexName);
                if (!TryAcquireConcurrencyMutex(mutex,
                        observedVoiceOnly ? TimeSpan.Zero : ConcurrencyLockTimeout))
                {
                    return new D046GameControlApplyResult(
                        D046GameControlApplyAdmission.StoreUnavailable,
                        InitialDefaults());
                }

                try
                {
                    current = LoadUnsafe();
                    if (observedVoiceOnly
                        && current.Disposition != D046GameControlLoadDisposition.Ready)
                    {
                        return new D046GameControlApplyResult(
                            D046GameControlApplyAdmission.StoreUnavailable,
                            current.Snapshot);
                    }
                    if (current.Snapshot.Revision != expectedRevision)
                    {
                        return new D046GameControlApplyResult(
                            D046GameControlApplyAdmission.RevisionConflict,
                            current.Snapshot);
                    }

                    if (observedVoiceOnly)
                    {
                        if (current.Snapshot.VoiceLanguage == draft.VoiceLanguage)
                            return new D046GameControlApplyResult(
                                D046GameControlApplyAdmission.Applied, current.Snapshot);
                        var mirrored = current.Snapshot.Copy();
                        mirrored.VoiceLanguage = draft.VoiceLanguage;
                        mirrored.Revision = checked(expectedRevision + 1);
                        mirrored.UpdatedUtc = now.ToUniversalTime();
                        SaveAtomic(mirrored);
                        return new D046GameControlApplyResult(
                            D046GameControlApplyAdmission.Applied, mirrored);
                    }

                    var next = new D046GameControlPreferencesDocument
                    {
                        SchemaVersion =
                            D046GameControlPreferencesDocument.CurrentSchemaVersion,
                        Revision = checked(expectedRevision + 1),
                        PushToTalkKey = draft.PushToTalkKey,
                        TextChatKey = draft.TextChatKey,
                        ConversationHistoryKey = draft.ConversationHistoryKey,
                        StopKey = draft.StopKey,
                        VoiceLanguage = draft.VoiceLanguage,
                        SpeechInputLanguage = draft.SpeechInputLanguage,
                        ProactiveDialogueEnabled =
                            draft.ProactiveDialogueEnabled,
                        UpdatedUtc = now.ToUniversalTime()
                    };
                    SaveAtomic(next);
                    return new D046GameControlApplyResult(
                        current.RequiresExplicitRepair
                            ? D046GameControlApplyAdmission.AppliedRequiresGameRestart
                            : D046GameControlApplyAdmission.Applied,
                        next);
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
            catch (Exception exception) when (IsStoreException(exception))
            {
                return new D046GameControlApplyResult(
                    D046GameControlApplyAdmission.StoreUnavailable,
                    current?.Snapshot
                        ?? InitialDefaults());
            }
        }
    }

    private D046GameControlLoadResult LoadUnsafe()
    {
        if (!File.Exists(_storePath))
        {
            return new D046GameControlLoadResult(
                InitialDefaults(),
                D046GameControlLoadDisposition.MissingDefaults);
        }

        try
        {
            var file = new FileInfo(_storePath);
            if (file.Length <= 0 || file.Length > MaximumStoreBytes)
                throw new InvalidDataException("Game control store size is invalid.");
            var bytes = File.ReadAllBytes(_storePath);
            DemandJsonShape(bytes);
            var document = JsonSerializer.Deserialize<D046GameControlPreferencesDocument>(
                bytes,
                SerializerOptions)
                ?? throw new InvalidDataException("Game control store is empty.");
            if (document.SchemaVersion == 1)
            {
                document.StopKey = ResolveMigratedStopKey(document);
                DemandPersisted(document, expectedSchema: 1);
                document.SchemaVersion =
                    D046GameControlPreferencesDocument.CurrentSchemaVersion;
                document.VoiceLanguage = D046GameVoiceLanguage.TraditionalChinese;
                document.SpeechInputLanguage =
                    _initialSpeechInputLanguage;
                return new D046GameControlLoadResult(
                    document,
                    D046GameControlLoadDisposition.PreviousSchemaMigrated);
            }
            if (document.SchemaVersion == 2)
            {
                document.SpeechInputLanguage = _initialSpeechInputLanguage;
                document.StopKey = ResolveMigratedStopKey(document);
                DemandPersisted(document, expectedSchema: 2);
                document.SchemaVersion =
                    D046GameControlPreferencesDocument.CurrentSchemaVersion;
                return new D046GameControlLoadResult(
                    document,
                    D046GameControlLoadDisposition.PreviousSchemaMigrated);
            }
            if (document.SchemaVersion == 3)
            {
                document.StopKey = ResolveMigratedStopKey(document);
                DemandPersisted(document, expectedSchema: 3);
                document.SchemaVersion =
                    D046GameControlPreferencesDocument.CurrentSchemaVersion;
                return new D046GameControlLoadResult(
                    document,
                    D046GameControlLoadDisposition.PreviousSchemaMigrated);
            }
            if (document.SchemaVersion == 4)
            {
                DemandPersisted(document, expectedSchema: 4);
                document.SchemaVersion =
                    D046GameControlPreferencesDocument.CurrentSchemaVersion;
                document.ProactiveDialogueEnabled = false;
                return new D046GameControlLoadResult(
                    document,
                    D046GameControlLoadDisposition.PreviousSchemaMigrated);
            }

            DemandPersisted(
                document,
                D046GameControlPreferencesDocument.CurrentSchemaVersion);
            return new D046GameControlLoadResult(
                document,
                D046GameControlLoadDisposition.Ready);
        }
        catch (Exception exception) when (IsStoreException(exception))
        {
            return new D046GameControlLoadResult(
                InitialDefaults(),
                D046GameControlLoadDisposition.InvalidDefaultsFailClosed);
        }
    }

    private D046GameControlPreferencesDocument InitialDefaults()
        => new()
        {
            SpeechInputLanguage = _initialSpeechInputLanguage
        };

    private static void DemandPersisted(
        D046GameControlPreferencesDocument document,
        int expectedSchema)
    {
        if (document.SchemaVersion != expectedSchema)
            throw new InvalidDataException("Game control schema is unsupported.");
        if (document.Revision < 1)
            throw new InvalidDataException("Game control revision is invalid.");
        var validation = D046GameControlPreferencesPolicy.Validate(document.ToDraft());
        if (!validation.Accepted)
            throw new InvalidDataException(string.Join(" ", validation.Errors));
        if (document.UpdatedUtc == default)
            throw new InvalidDataException("Game control update time is invalid.");
    }

    private static void DemandJsonShape(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Game control store root must be an object.");

        var names = json.RootElement
            .EnumerateObject()
            .Select(value => value.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (!json.RootElement.TryGetProperty("schemaVersion", out var schemaElement)
            || !schemaElement.TryGetInt32(out var schema))
        {
            throw new InvalidDataException("Game control schema is missing.");
        }

        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "schemaVersion",
            "revision",
            "pushToTalkKey",
            "textChatKey",
            "conversationHistoryKey",
            "updatedUtc"
        };
        if (schema == D046GameControlPreferencesDocument.CurrentSchemaVersion)
        {
            expected.Add("voiceLanguage");
            expected.Add("speechInputLanguage");
            expected.Add("stopKey");
            expected.Add("proactiveDialogueEnabled");
        }
        else if (schema == 4)
        {
            expected.Add("voiceLanguage");
            expected.Add("speechInputLanguage");
            expected.Add("stopKey");
        }
        else if (schema == 3)
        {
            expected.Add("voiceLanguage");
            expected.Add("speechInputLanguage");
        }
        else if (schema == 2)
        {
            expected.Add("voiceLanguage");
        }
        else if (schema != 1)
            throw new InvalidDataException("Game control schema is unsupported.");
        if (!names.SetEquals(expected))
            throw new InvalidDataException("Game control store shape is invalid.");
    }

    private void SaveAtomic(D046GameControlPreferencesDocument document)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        if (bytes.Length > MaximumStoreBytes)
            throw new InvalidDataException("Game control store exceeds its byte ceiling.");
        Directory.CreateDirectory(_rootDirectory);
        var temporaryPath = Path.Combine(
            _rootDirectory,
            StoreFileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static D046FunctionKey ResolveMigratedStopKey(
        D046GameControlPreferencesDocument document)
    {
        var occupied = new HashSet<D046FunctionKey>
        {
            document.PushToTalkKey,
            document.TextChatKey,
            document.ConversationHistoryKey
        };
        foreach (var candidate in new[]
                 {
                     D046FunctionKey.F8,
                     D046FunctionKey.F9,
                     D046FunctionKey.F11,
                     D046FunctionKey.F12
                 })
        {
            if (!occupied.Contains(candidate))
                return candidate;
        }

        throw new InvalidDataException(
            "A distinct migrated stop key could not be selected.");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    private static string CreateConcurrencyMutexName(string storePath)
    {
        var normalized = Path.GetFullPath(storePath).ToUpperInvariant();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return "Local\\LilithAiMod.D046GameControl."
            + Convert.ToHexString(digest.AsSpan(0, 16));
    }

    private static bool TryAcquireConcurrencyMutex(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static bool IsStoreException(Exception exception)
        => exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or InvalidDataException
            or ArgumentException
            or OverflowException;
}
