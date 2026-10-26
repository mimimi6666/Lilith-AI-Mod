using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BepInEx.Configuration;

namespace LilithTextInjector;

/// <summary>
/// Permanently seals the pre-Hermes plaintext API-key entries. The entries stay
/// bound so ConfigFile.Reload cannot recreate them as orphaned values, but their
/// acceptable-value policy admits only the empty string and every consumer reads
/// through <see cref="Read"/>, which never returns ConfigEntry state.
/// </summary>
internal sealed class LegacyPlaintextProviderCredentialSeal
{
    private const int MaximumRetiredEntries = 32;
    private readonly ConfigFile _config;
    private readonly object _gate = new();
    private bool _scrubbing;

    internal IReadOnlyList<ConfigEntry<string>> Entries { get; }

    private LegacyPlaintextProviderCredentialSeal(
        ConfigFile config,
        ConfigEntry<string>[] entries)
    {
        _config = config;
        Entries = new ReadOnlyCollection<ConfigEntry<string>>(entries);
    }

    internal static LegacyPlaintextProviderCredentialSeal Install(
        ConfigFile config,
        IEnumerable<ConfigEntry<string>> entries)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(entries);
        var exactEntries = entries
            .ToArray();
        if (exactEntries.Length == 0
            || exactEntries.Length > MaximumRetiredEntries
            || exactEntries.Any(static entry => entry is null)
            || exactEntries.Select(static entry => entry.Definition.Section)
                .Distinct(StringComparer.Ordinal).Count() != exactEntries.Length
            || exactEntries.Any(static entry =>
                !string.Equals(
                    entry.Definition.Key,
                    "ApiKey",
                    StringComparison.Ordinal)
                || entry.Description.AcceptableValues is null
                || !string.Equals(
                    entry.Description.AcceptableValues.Clamp(
                        "retired-nonempty-probe") as string,
                    string.Empty,
                    StringComparison.Ordinal)
                || !string.Equals(
                    entry.Description.AcceptableValues.Clamp(string.Empty)
                        as string,
                    string.Empty,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Retired credential entries are not hard-clamped to empty.",
                nameof(entries));
        }

        var seal = new LegacyPlaintextProviderCredentialSeal(
            config,
            exactEntries);
        foreach (var entry in exactEntries)
            entry.SettingChanged += seal.Entry_SettingChanged;
        config.ConfigReloaded += seal.Config_ConfigReloaded;
        seal.ScrubAndSave();
        return seal;
    }

    internal static string Read(ConfigEntry<string>? ignored = null)
        => string.Empty;

    private void Entry_SettingChanged(object? sender, EventArgs eventArgs)
        => ScrubAndSave();

    private void Config_ConfigReloaded(object? sender, EventArgs eventArgs)
        => ScrubAndSave();

    private void ScrubAndSave()
    {
        lock (_gate)
        {
            if (_scrubbing)
                return;
            _scrubbing = true;
            var restoreSaveOnConfigSet = _config.SaveOnConfigSet;
            _config.SaveOnConfigSet = false;
            try
            {
                foreach (var entry in Entries)
                {
                    if (!string.IsNullOrEmpty(entry.Value))
                        entry.Value = string.Empty;
                }
                // Always rewrite after install/reload. This consumes orphaned
                // legacy entries and prevents a later Config.Save from
                // resurrecting plaintext supplied by an external file edit.
                _config.Save();
            }
            finally
            {
                _config.SaveOnConfigSet = restoreSaveOnConfigSet;
                _scrubbing = false;
            }
        }
    }
}
