using System;
using System.IO;
using System.Text.Json;

namespace LilithTextInjector;

internal sealed class D029IsolatedTestProfile
{
    internal const string EnvironmentVariableName =
        "LILITH_D029_ISOLATED_PROFILE_ID";
    internal const string Schema =
        "lilith-d029-isolated-test-profile-v1";
    internal const string ActivationTicketSchema =
        "lilith-d029-isolated-activation-ticket-v1";
    internal static readonly TimeSpan MaximumActivationLifetime =
        TimeSpan.FromHours(2);
    private static readonly TimeSpan MaximumClockSkew =
        TimeSpan.FromMinutes(5);

    internal string ProfileId { get; }
    internal string ScopeDirectory { get; }
    internal string PlayerDataDirectory { get; }
    internal string NativeNotesDirectory { get; }
    internal string ApplicationLauncherPath { get; }

    private D029IsolatedTestProfile(
        string profileId,
        string scopeDirectory)
    {
        ProfileId = profileId;
        ScopeDirectory = scopeDirectory;
        PlayerDataDirectory =
            Path.Combine(scopeDirectory, "player-data");
        NativeNotesDirectory =
            Path.Combine(scopeDirectory, "native-notes");
        ApplicationLauncherPath =
            Path.Combine(scopeDirectory, "config", "applications.json");
    }

    internal static string BuildActivationTicketPath(
        string localApplicationDataDirectory)
        => Path.Combine(
            PlayerDataStorage.BuildRootDirectory(
                localApplicationDataDirectory),
            "TestData",
            "D029",
            "active-isolated-profile-v1.json");

    internal static bool HasActivationTicket(
        string localApplicationDataDirectory)
        => File.Exists(
            BuildActivationTicketPath(
                localApplicationDataDirectory));

    internal static string? ResolveRequestedProfileId(
        string? environmentProfileId,
        string localApplicationDataDirectory,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(environmentProfileId))
            return environmentProfileId;

        var ticketPath = BuildActivationTicketPath(
            localApplicationDataDirectory);
        if (!File.Exists(ticketPath))
            return null;

        var ticket = JsonSerializer.Deserialize<ActivationTicket>(
            File.ReadAllText(ticketPath));
        if (ticket == null
            || !string.Equals(
                ticket.Schema,
                ActivationTicketSchema,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The D-029 isolated activation ticket has an invalid "
                + "schema.");
        }
        if (ticket.IssuedAtUtc > now + MaximumClockSkew
            || ticket.ExpiresAtUtc <= now
            || ticket.ExpiresAtUtc <= ticket.IssuedAtUtc
            || ticket.ExpiresAtUtc - ticket.IssuedAtUtc
                > MaximumActivationLifetime)
        {
            throw new InvalidOperationException(
                "The D-029 isolated activation ticket is expired or "
                + "outside its fixed lifetime.");
        }

        return ticket.ProfileId;
    }

    internal static bool TryResolve(
        string? requestedProfileId,
        string localApplicationDataDirectory,
        out D029IsolatedTestProfile? profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(requestedProfileId))
            return false;
        if (!Guid.TryParseExact(
                requestedProfileId.Trim(),
                "N",
                out var parsed))
        {
            throw new InvalidOperationException(
                "The D-029 isolated Profile ID must be one GUID in N "
                + "format.");
        }

        var productRoot =
            PlayerDataStorage.BuildRootDirectory(
                localApplicationDataDirectory);
        var profilesRoot = Path.GetFullPath(
            Path.Combine(
                productRoot,
                "TestData",
                "D029",
                "profiles"));
        var normalizedId = parsed.ToString("N");
        var scope = Path.GetFullPath(
            Path.Combine(profilesRoot, normalizedId));
        var requiredPrefix = profilesRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!scope.StartsWith(
                requiredPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The D-029 isolated Profile escaped its fixed test root.");
        }

        profile = new D029IsolatedTestProfile(
            normalizedId,
            scope);
        return true;
    }

    internal sealed class ActivationTicket
    {
        public string Schema { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public DateTimeOffset IssuedAtUtc { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}

internal static class D029IsolatedNoteSink
{
    internal const int MaximumContentCharacters = 32768;

    internal static string Write(
        D029IsolatedTestProfile profile,
        string eventId,
        string text,
        DateTimeOffset occurredAt,
        Guid nonce)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        text ??= string.Empty;
        if (text.Length > MaximumContentCharacters)
        {
            throw new InvalidOperationException(
                "The isolated D-029 Note exceeded its fixed content bound.");
        }

        var scope = Path.GetFullPath(profile.ScopeDirectory)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(profile.NativeNotesDirectory);
        if (!directory.StartsWith(
                scope,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The D-029 Note sink escaped its isolated Profile root.");
        }

        Directory.CreateDirectory(directory);
        var eventIdentity = LivingPromptHash.Of(
                "D029/ISOLATED-NOTE/1|" + (eventId ?? string.Empty))
            .Substring(0, 16);
        var fileName = "note_"
            + occurredAt.ToUniversalTime().ToString(
                "yyyyMMdd_HHmmss_fff",
                System.Globalization.CultureInfo.InvariantCulture)
            + "_"
            + eventIdentity
            + ".txt";
        var finalPath = Path.Combine(directory, fileName);
        var temporaryPath = finalPath
            + ".tmp-"
            + nonce.ToString("N");
        try
        {
            File.WriteAllText(
                temporaryPath,
                text,
                new System.Text.UTF8Encoding(false));
            File.Move(temporaryPath, finalPath);
            return finalPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
