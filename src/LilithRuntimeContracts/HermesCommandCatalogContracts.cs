using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LilithRuntimeContracts;

/// <summary>A native commands-only read, not execution or permission authority.</summary>
public sealed class HermesCommandCatalogSnapshot
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion => HermesCommandCatalogContract.SchemaVersion;
    [JsonPropertyName("read_only")]
    public bool ReadOnly => true;
    [JsonPropertyName("identity")]
    public HermesCapabilityCatalogIdentity Identity { get; }
    [JsonPropertyName("commands")]
    public HermesCapabilityCommandCatalog Commands { get; }

    public HermesCommandCatalogSnapshot(HermesCapabilityCatalogIdentity identity,
        HermesCapabilityCommandCatalog commands)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }
}

public static class HermesCommandCatalogContract
{
    public const string Method = "commands.catalog";
    public const int SchemaVersion = 1;

    public static HermesCommandCatalogSnapshot Decode(JsonElement payload)
        => HermesCapabilityCatalogContract.DecodeCommandSnapshot(payload);
}

public static partial class HermesCapabilityCatalogContract
{
    internal static HermesCommandCatalogSnapshot DecodeCommandSnapshot(JsonElement payload)
    {
        try
        {
            var root = ExactObject(payload, "schema_version", "read_only", "identity", "commands");
            if (RequiredInteger(root, "schema_version", 1, HermesCommandCatalogContract.SchemaVersion)
                    != HermesCommandCatalogContract.SchemaVersion || !RequiredBoolean(root, "read_only"))
                throw new InvalidDataException("Hermes command catalog schema was rejected.");
            var identity = ExactObject(root["identity"], "runtime_version", "hermes_version", "hermes_commit");
            return new HermesCommandCatalogSnapshot(new HermesCapabilityCatalogIdentity(
                    RequiredString(identity, "runtime_version", MaximumVersionUtf8Bytes),
                    RequiredString(identity, "hermes_version", MaximumVersionUtf8Bytes),
                    RequiredString(identity, "hermes_commit", 40)),
                DecodeCommands(root["commands"]));
        }
        catch (Exception exception) when (exception is ArgumentException or EncoderFallbackException)
        {
            throw new InvalidDataException("Hermes command catalog was rejected.", exception);
        }
    }
}
