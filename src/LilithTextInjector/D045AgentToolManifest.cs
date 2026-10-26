using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using LilithPermissionControl;

namespace LilithTextInjector;

/// <summary>
/// Immutable, per-turn projection of the player-owned permission snapshot.
/// Provider schemas and prompts must be generated from this same object so a
/// disabled or newly-added leaf can never appear as executable authority.
/// </summary>
internal sealed class D045AgentToolManifest
{
    private readonly ReadOnlyCollection<D045CapabilityDescriptor> _tools;
    private readonly ReadOnlyCollection<string> _tokens;
    private readonly HashSet<D045PermissionCapability> _capabilities;

    internal long PermissionRevision { get; }
    internal IReadOnlyList<D045CapabilityDescriptor> Tools => _tools;
    internal IReadOnlyList<string> CapabilityTokens => _tokens;
    internal bool IsEmpty => _tools.Count == 0;

    private D045AgentToolManifest(
        long permissionRevision,
        IEnumerable<D045CapabilityDescriptor> tools)
    {
        if (permissionRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(permissionRevision));

        var ordered = tools
            .OrderBy(value => value.Ordinal)
            .ToArray();
        if (ordered.Select(value => value.Capability).Distinct().Count()
            != ordered.Length)
        {
            throw new ArgumentException(
                "A frozen tool manifest cannot contain duplicate leaves.",
                nameof(tools));
        }

        PermissionRevision = permissionRevision;
        _tools = Array.AsReadOnly(ordered);
        _tokens = Array.AsReadOnly(ordered
            .Select(value => value.ToolToken)
            .ToArray());
        _capabilities = new HashSet<D045PermissionCapability>(
            ordered.Select(value => value.Capability));
    }

    internal bool Contains(D045PermissionCapability capability)
        => _capabilities.Contains(capability);

    internal bool ContainsExactToken(string? token)
        => token is not null
            && D045CapabilityDescriptorCatalog.TryResolveExactToolToken(
                token,
                out var capability)
            && Contains(capability);

    internal string BuildToolbookPrompt()
    {
        if (_tools.Count == 0)
            return "No player-authorized workstation tools are available.";

        var builder = new StringBuilder(
            "Player-authorized tools for this exact permission revision:\n");
        foreach (var tool in _tools)
        {
            builder.Append("- ")
                .Append(tool.ToolToken)
                .Append(": ")
                .Append(tool.ToolUseDetails)
                .Append(" Risk: ")
                .Append(tool.RiskTier)
                .AppendLine(".");
        }
        builder.Append(
            "Only these exact tokens may be invoked. Categories are UI-only "
            + "and never grant authority. Disabled, future, inferred or "
            + "wildcard capabilities are unavailable.");
        return builder.ToString();
    }

    internal D045AgentToolManifest Intersect(
        IEnumerable<D045PermissionCapability> relevantCapabilities)
    {
        if (relevantCapabilities is null)
            throw new ArgumentNullException(nameof(relevantCapabilities));
        var relevant = new HashSet<D045PermissionCapability>(
            relevantCapabilities);
        return new D045AgentToolManifest(
            PermissionRevision,
            _tools.Where(value => relevant.Contains(value.Capability)));
    }

    internal static D045AgentToolManifest Freeze(
        D045PermissionLoadResult permissionState)
    {
        if (permissionState is null)
            throw new ArgumentNullException(nameof(permissionState));
        var snapshot = permissionState.Snapshot;
        if (snapshot.SchemaVersion
                != D045PermissionControlSnapshot.CurrentSchemaVersion
            || snapshot.Enabled.Length
                != D045PermissionControlSnapshot.ExactCapabilityCount)
        {
            return new D045AgentToolManifest(
                permissionRevision: 0,
                Array.Empty<D045CapabilityDescriptor>());
        }

        return new D045AgentToolManifest(
            snapshot.Revision,
            D045CapabilityDescriptorCatalog.Descriptors.Where(value =>
                snapshot.IsEnabled(value.Capability)));
    }
}
