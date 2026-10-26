using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LilithTextInjector;

internal enum ControlCenterSettingValueKind
{
    Invalid = 0,
    Boolean = 1,
    Integer = 2,
    Decimal = 3,
    Option = 4,
    OpaqueReference = 5,
    KeyChord = 6,
    SafeText = 7
}

internal enum ControlCenterSettingCategory
{
    Invalid = 0,
    General = 1,
    Provider = 2,
    Character = 3,
    Voice = 4,
    Memory = 5,
    Initiative = 6,
    Motion = 7,
    Privacy = 8,
    Developer = 9,
    Presentation = 10
}

internal enum ControlCenterSensitivityClass
{
    Invalid = 0,
    PublicMetadata = 1,
    PrivateConfiguration = 2,
    OpaqueCredentialReferenceOnly = 3
}

internal enum ControlCenterApplyMode
{
    Invalid = 0,
    PresentationOnly = 1,
    RuntimeRequest = 2,
    ProcessRestart = 3,
    OfflineImportOnly = 4
}

internal enum ControlCenterRestartRequirement
{
    Invalid = 0,
    None = 1,
    GameProcess = 2,
    ControlCenterProcess = 3
}

internal enum ControlCenterAuthorityDomain
{
    Invalid = 0,
    Presentation = 1,
    RuntimeConfiguration = 2,
    ProviderRegistry = 3,
    CharacterRuntime = 4,
    Memory = 5,
    Initiative = 6,
    Motion = 7,
    Privacy = 8,
    Developer = 9
}

internal enum ControlCenterVisibilityPolicy
{
    Invalid = 0,
    Basic = 1,
    Advanced = 2,
    DeveloperOnly = 3,
    HiddenCompatibility = 4
}

internal enum ControlCenterValidationKind
{
    Invalid = 0,
    Boolean = 1,
    IntegerRange = 2,
    DecimalRange = 3,
    ClosedOptions = 4,
    OpaqueReference = 5,
    KeyChord = 6,
    BoundedSafeText = 7
}

internal enum ControlCenterSchemaBuildAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    UnsupportedSchemaVersion = 2,
    InvalidDescriptorSetVersion = 3,
    EmptyDescriptorSet = 4,
    CapacityExceeded = 5,
    DuplicateSettingId = 6
}

internal enum ControlCenterSnapshotBuildAdmission
{
    Invalid = 0,
    AcceptedForContractReplay = 1,
    SchemaMismatch = 2,
    DuplicateSettingId = 3,
    UnknownSettingId = 4,
    InvalidValue = 5,
    DuplicateFeatureId = 6
}

internal enum ControlCenterEffectiveValueState
{
    Invalid = 0,
    Effective = 1,
    Unavailable = 2,
    PendingRestart = 3,
    Rejected = 4
}

internal enum ControlCenterFeatureStateKind
{
    Invalid = 0,
    Off = 1,
    Observe = 2,
    Assist = 3,
    Fault = 4,
    Capacity = 5,
    Unavailable = 6
}

internal enum ControlCenterSettingsAdmission
{
    Invalid = 0,
    ValidatedForContractReplay = 1,
    SchemaMismatch = 2,
    DesiredRevisionMismatch = 3,
    ConfigurationGenerationMismatch = 4,
    RequestNotYetValid = 5,
    RequestExpired = 6,
    DuplicateChange = 7,
    UnknownSetting = 8,
    InvalidValue = 9,
    RuntimeUnavailable = 10,
    RuntimeIdentityMismatch = 11,
    RuntimeGenerationMismatch = 12,
    RuntimeConfigurationMismatch = 13
}

internal enum ControlCenterPerSettingAdmission
{
    Invalid = 0,
    ValidatedForContractReplay = 1,
    UnknownSetting = 2,
    InvalidValue = 3,
    DuplicateChange = 4
}

internal enum DesktopSurfacePresentationState
{
    Invalid = 0,
    Hidden = 1,
    Ready = 2,
    Visible = 3,
    Transitioning = 4,
    Closing = 5
}

internal enum DesktopSurfaceActivationKind
{
    Invalid = 0,
    PrimaryDoubleClick = 1
}

internal enum DesktopSurfaceActivationAdmission
{
    Invalid = 0,
    DeniedByStageABoundary = 1,
    SessionNotYetValid = 2,
    SessionExpired = 3,
    RequestNotYetValid = 4,
    RequestExpired = 5,
    SurfaceIdentityMismatch = 6,
    RuntimeIdentityMismatch = 7,
    RuntimeGenerationMismatch = 8
}

internal static class ControlCenterStageAContractGuard
{
    internal const int SchemaVersion = 1;
    internal const int MaximumIdentifierLength = 128;
    internal const int MaximumSafeTextLength = 512;
    internal const int MaximumDescriptors = 512;
    internal const int MaximumAssignments = 512;
    internal const int MaximumChangesPerTransaction = 64;
    internal const int MaximumFeatureStates = 128;

    internal static string Identifier(
        string? value,
        string parameterName)
    {
        var result = DecisionCoreContractGuard.Identifier(
            value,
            MaximumIdentifierLength,
            parameterName);
        if (result.IndexOf('/') >= 0
            || result.IndexOf('\\') >= 0
            || result.IndexOf(':') >= 0)
        {
            throw new ArgumentException(
                "Control Center identities cannot contain path or URI separators.",
                parameterName);
        }
        return result;
    }

    internal static string Sha256(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 64
            || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "A SHA-256 hexadecimal value is required.",
                parameterName);
        }
        return value.ToUpperInvariant();
    }

    internal static string SafeText(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || maximumLength <= 0
            || maximumLength > MaximumSafeTextLength
            || value.Length > maximumLength
            || value.Any(char.IsControl)
            || value.Contains(":\\", StringComparison.Ordinal)
            || value.Contains(":/", StringComparison.Ordinal)
            || value.IndexOf('\\') >= 0)
        {
            throw new ArgumentException(
                "Bounded display text cannot contain paths, URIs or control characters.",
                parameterName);
        }
        return value;
    }

    internal static T Defined<T>(T value, string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(typeof(T), value)
            || Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return value;
    }

    internal static int Positive(int value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static long Positive(long value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static int NonNegative(int value, string parameterName)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(parameterName);
        return value;
    }

    internal static DateTimeOffset Timestamp(
        DateTimeOffset value,
        string parameterName)
        => DecisionCoreContractGuard.Timestamp(value, parameterName);
}

internal static class ControlCenterStageACanonicalHash
{
    internal static string Of(
        string domain,
        params string?[] values)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Hash domain is required.", nameof(domain));
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        var material = new StringBuilder();
        Append(material, domain);
        foreach (var value in values)
            Append(material, value ?? string.Empty);
        using var sha = SHA256.Create();
        return Convert.ToHexString(
            sha.ComputeHash(Encoding.UTF8.GetBytes(material.ToString())));
    }

    internal static string Number(long value)
        => value.ToString(CultureInfo.InvariantCulture);

    internal static string Number(decimal value)
        => value.ToString("G29", CultureInfo.InvariantCulture);

    internal static string Timestamp(DateTimeOffset value)
        => value.ToUniversalTime().Ticks.ToString(
            CultureInfo.InvariantCulture);

    private static void Append(StringBuilder target, string value)
    {
        target.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(value);
    }
}

internal abstract class ControlCenterOrdinalIdentity
{
    internal string Value { get; }

    protected ControlCenterOrdinalIdentity(
        string value,
        string parameterName)
    {
        Value = ControlCenterStageAContractGuard.Identifier(
            value,
            parameterName);
    }

    public sealed override string ToString() => Value;

    public sealed override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Value);

    public sealed override bool Equals(object? obj)
        => obj != null
            && obj.GetType() == GetType()
            && string.Equals(
                Value,
                ((ControlCenterOrdinalIdentity)obj).Value,
                StringComparison.Ordinal);
}

internal sealed class ControlCenterSettingId :
    ControlCenterOrdinalIdentity,
    IEquatable<ControlCenterSettingId>
{
    internal ControlCenterSettingId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ControlCenterSettingId? other)
        => Equals((object?)other);
}

internal sealed class ControlCenterFeatureId :
    ControlCenterOrdinalIdentity,
    IEquatable<ControlCenterFeatureId>
{
    internal ControlCenterFeatureId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ControlCenterFeatureId? other)
        => Equals((object?)other);
}

internal sealed class ControlCenterTransactionId :
    ControlCenterOrdinalIdentity,
    IEquatable<ControlCenterTransactionId>
{
    internal ControlCenterTransactionId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ControlCenterTransactionId? other)
        => Equals((object?)other);
}

internal sealed class ControlCenterRuntimeInstanceId :
    ControlCenterOrdinalIdentity,
    IEquatable<ControlCenterRuntimeInstanceId>
{
    internal ControlCenterRuntimeInstanceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ControlCenterRuntimeInstanceId? other)
        => Equals((object?)other);
}

internal sealed class ControlCenterRuntimeSessionId :
    ControlCenterOrdinalIdentity,
    IEquatable<ControlCenterRuntimeSessionId>
{
    internal ControlCenterRuntimeSessionId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(ControlCenterRuntimeSessionId? other)
        => Equals((object?)other);
}

internal sealed class DesktopSurfaceInstanceId :
    ControlCenterOrdinalIdentity,
    IEquatable<DesktopSurfaceInstanceId>
{
    internal DesktopSurfaceInstanceId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(DesktopSurfaceInstanceId? other)
        => Equals((object?)other);
}

internal sealed class DesktopExperiencePackageId :
    ControlCenterOrdinalIdentity,
    IEquatable<DesktopExperiencePackageId>
{
    internal DesktopExperiencePackageId(string value)
        : base(value, nameof(value))
    {
    }

    public bool Equals(DesktopExperiencePackageId? other)
        => Equals((object?)other);
}

internal sealed class ControlCenterSettingValue
{
    internal ControlCenterSettingValueKind Kind { get; }
    internal bool? BooleanValue { get; }
    internal long? IntegerValue { get; }
    internal decimal? DecimalValue { get; }
    internal string? SafeTokenValue { get; }
    internal string Fingerprint { get; }

    private ControlCenterSettingValue(
        ControlCenterSettingValueKind kind,
        bool? booleanValue,
        long? integerValue,
        decimal? decimalValue,
        string? safeTokenValue)
    {
        Kind = ControlCenterStageAContractGuard.Defined(
            kind,
            nameof(kind));
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        DecimalValue = decimalValue;
        SafeTokenValue = safeTokenValue;
        var populated = new object?[]
        {
            booleanValue,
            integerValue,
            decimalValue,
            safeTokenValue
        }.Count(value => value != null);
        if (populated != 1)
        {
            throw new ArgumentException(
                "A setting value must populate exactly one typed value.");
        }
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/SETTING-VALUE/1",
            ControlCenterStageACanonicalHash.Number((int)Kind),
            CanonicalValue());
    }

    internal static ControlCenterSettingValue Boolean(bool value)
        => new(
            ControlCenterSettingValueKind.Boolean,
            value,
            null,
            null,
            null);

    internal static ControlCenterSettingValue Integer(long value)
        => new(
            ControlCenterSettingValueKind.Integer,
            null,
            value,
            null,
            null);

    internal static ControlCenterSettingValue Decimal(decimal value)
        => new(
            ControlCenterSettingValueKind.Decimal,
            null,
            null,
            value,
            null);

    internal static ControlCenterSettingValue Option(string value)
        => new(
            ControlCenterSettingValueKind.Option,
            null,
            null,
            null,
            ControlCenterStageAContractGuard.Identifier(
                value,
                nameof(value)));

    internal static ControlCenterSettingValue OpaqueReference(string value)
        => new(
            ControlCenterSettingValueKind.OpaqueReference,
            null,
            null,
            null,
            ControlCenterStageAContractGuard.Identifier(
                value,
                nameof(value)));

    internal static ControlCenterSettingValue KeyChord(string value)
        => new(
            ControlCenterSettingValueKind.KeyChord,
            null,
            null,
            null,
            ControlCenterStageAContractGuard.Identifier(
                value,
                nameof(value)));

    internal static ControlCenterSettingValue SafeText(string value)
        => new(
            ControlCenterSettingValueKind.SafeText,
            null,
            null,
            null,
            ControlCenterStageAContractGuard.SafeText(
                value,
                ControlCenterStageAContractGuard.MaximumSafeTextLength,
                nameof(value)));

    internal bool SemanticEquals(ControlCenterSettingValue? other)
        => other != null
            && string.Equals(
                Fingerprint,
                other.Fingerprint,
                StringComparison.Ordinal);

    public override string ToString()
        => $"{Kind}:{Fingerprint.Substring(0, 12)}";

    private string CanonicalValue()
        => Kind switch
        {
            ControlCenterSettingValueKind.Boolean =>
                BooleanValue!.Value ? "true" : "false",
            ControlCenterSettingValueKind.Integer =>
                ControlCenterStageACanonicalHash.Number(
                    IntegerValue!.Value),
            ControlCenterSettingValueKind.Decimal =>
                ControlCenterStageACanonicalHash.Number(
                    DecimalValue!.Value),
            _ => SafeTokenValue!
        };
}

internal sealed class ControlCenterValidationRule
{
    internal ControlCenterValidationKind Kind { get; }
    internal ControlCenterSettingValueKind ValueKind { get; }
    internal decimal? Minimum { get; }
    internal decimal? Maximum { get; }
    internal int? MaximumLength { get; }
    internal IReadOnlyList<string> AllowedTokens { get; }
    internal string Fingerprint { get; }

    private ControlCenterValidationRule(
        ControlCenterValidationKind kind,
        ControlCenterSettingValueKind valueKind,
        decimal? minimum,
        decimal? maximum,
        int? maximumLength,
        IEnumerable<string>? allowedTokens)
    {
        Kind = ControlCenterStageAContractGuard.Defined(
            kind,
            nameof(kind));
        ValueKind = ControlCenterStageAContractGuard.Defined(
            valueKind,
            nameof(valueKind));
        Minimum = minimum;
        Maximum = maximum;
        MaximumLength = maximumLength;
        var tokens = (allowedTokens ?? Array.Empty<string>())
            .Select(
                value => ControlCenterStageAContractGuard.Identifier(
                    value,
                    nameof(allowedTokens)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (tokens.Distinct(StringComparer.Ordinal).Count() != tokens.Length)
            throw new ArgumentException("Allowed tokens must be unique.");
        AllowedTokens = new ReadOnlyCollection<string>(tokens);
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/VALIDATION-RULE/1",
            ControlCenterStageACanonicalHash.Number((int)Kind),
            ControlCenterStageACanonicalHash.Number((int)ValueKind),
            minimum.HasValue
                ? ControlCenterStageACanonicalHash.Number(minimum.Value)
                : string.Empty,
            maximum.HasValue
                ? ControlCenterStageACanonicalHash.Number(maximum.Value)
                : string.Empty,
            maximumLength?.ToString(CultureInfo.InvariantCulture),
            string.Join(",", tokens));
    }

    internal static ControlCenterValidationRule Boolean()
        => new(
            ControlCenterValidationKind.Boolean,
            ControlCenterSettingValueKind.Boolean,
            null,
            null,
            null,
            null);

    internal static ControlCenterValidationRule IntegerRange(
        long minimum,
        long maximum)
    {
        if (minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        return new(
            ControlCenterValidationKind.IntegerRange,
            ControlCenterSettingValueKind.Integer,
            minimum,
            maximum,
            null,
            null);
    }

    internal static ControlCenterValidationRule DecimalRange(
        decimal minimum,
        decimal maximum)
    {
        if (minimum > maximum)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        return new(
            ControlCenterValidationKind.DecimalRange,
            ControlCenterSettingValueKind.Decimal,
            minimum,
            maximum,
            null,
            null);
    }

    internal static ControlCenterValidationRule ClosedOptions(
        IEnumerable<string> options)
    {
        var values = (options
                ?? throw new ArgumentNullException(nameof(options)))
            .ToArray();
        if (values.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        return new(
            ControlCenterValidationKind.ClosedOptions,
            ControlCenterSettingValueKind.Option,
            null,
            null,
            null,
            values);
    }

    internal static ControlCenterValidationRule OpaqueReference()
        => new(
            ControlCenterValidationKind.OpaqueReference,
            ControlCenterSettingValueKind.OpaqueReference,
            null,
            null,
            null,
            null);

    internal static ControlCenterValidationRule KeyChord()
        => new(
            ControlCenterValidationKind.KeyChord,
            ControlCenterSettingValueKind.KeyChord,
            null,
            null,
            null,
            null);

    internal static ControlCenterValidationRule BoundedSafeText(
        int maximumLength)
    {
        if (maximumLength <= 0
            || maximumLength
                > ControlCenterStageAContractGuard.MaximumSafeTextLength)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }
        return new(
            ControlCenterValidationKind.BoundedSafeText,
            ControlCenterSettingValueKind.SafeText,
            null,
            null,
            maximumLength,
            null);
    }

    internal bool Accepts(ControlCenterSettingValue? value)
    {
        if (value == null || value.Kind != ValueKind)
            return false;
        return Kind switch
        {
            ControlCenterValidationKind.Boolean =>
                value.BooleanValue.HasValue,
            ControlCenterValidationKind.IntegerRange =>
                value.IntegerValue.HasValue
                && Minimum.HasValue
                && Maximum.HasValue
                && value.IntegerValue.Value >= Minimum.Value
                && value.IntegerValue.Value <= Maximum.Value,
            ControlCenterValidationKind.DecimalRange =>
                value.DecimalValue.HasValue
                && Minimum.HasValue
                && Maximum.HasValue
                && value.DecimalValue.Value >= Minimum.Value
                && value.DecimalValue.Value <= Maximum.Value,
            ControlCenterValidationKind.ClosedOptions =>
                value.SafeTokenValue != null
                && AllowedTokens.Contains(
                    value.SafeTokenValue,
                    StringComparer.Ordinal),
            ControlCenterValidationKind.OpaqueReference
                or ControlCenterValidationKind.KeyChord =>
                value.SafeTokenValue != null,
            ControlCenterValidationKind.BoundedSafeText =>
                value.SafeTokenValue != null
                && MaximumLength.HasValue
                && value.SafeTokenValue.Length <= MaximumLength.Value,
            _ => false
        };
    }
}

internal sealed class ControlCenterSettingDescriptor
{
    internal ControlCenterSettingId SettingId { get; }
    internal ControlCenterSettingCategory Category { get; }
    internal ControlCenterSettingValueKind ValueKind { get; }
    internal ControlCenterSettingValue? DefaultValue { get; }
    internal ControlCenterValidationRule ValidationRule { get; }
    internal ControlCenterSensitivityClass SensitivityClass { get; }
    internal ControlCenterApplyMode ApplyMode { get; }
    internal ControlCenterRestartRequirement RestartRequirement { get; }
    internal ControlCenterAuthorityDomain AuthorityDomain { get; }
    internal ControlCenterVisibilityPolicy VisibilityPolicy { get; }
    internal string Fingerprint { get; }

    internal ControlCenterSettingDescriptor(
        ControlCenterSettingId settingId,
        ControlCenterSettingCategory category,
        ControlCenterSettingValueKind valueKind,
        ControlCenterSettingValue? defaultValue,
        ControlCenterValidationRule validationRule,
        ControlCenterSensitivityClass sensitivityClass,
        ControlCenterApplyMode applyMode,
        ControlCenterRestartRequirement restartRequirement,
        ControlCenterAuthorityDomain authorityDomain,
        ControlCenterVisibilityPolicy visibilityPolicy)
    {
        SettingId = settingId
            ?? throw new ArgumentNullException(nameof(settingId));
        Category = ControlCenterStageAContractGuard.Defined(
            category,
            nameof(category));
        ValueKind = ControlCenterStageAContractGuard.Defined(
            valueKind,
            nameof(valueKind));
        ValidationRule = validationRule
            ?? throw new ArgumentNullException(nameof(validationRule));
        SensitivityClass = ControlCenterStageAContractGuard.Defined(
            sensitivityClass,
            nameof(sensitivityClass));
        ApplyMode = ControlCenterStageAContractGuard.Defined(
            applyMode,
            nameof(applyMode));
        RestartRequirement = ControlCenterStageAContractGuard.Defined(
            restartRequirement,
            nameof(restartRequirement));
        AuthorityDomain = ControlCenterStageAContractGuard.Defined(
            authorityDomain,
            nameof(authorityDomain));
        VisibilityPolicy = ControlCenterStageAContractGuard.Defined(
            visibilityPolicy,
            nameof(visibilityPolicy));
        if (ValidationRule.ValueKind != ValueKind)
        {
            throw new ArgumentException(
                "Validation rule and setting value kinds must match.",
                nameof(validationRule));
        }
        if (defaultValue != null
            && !ValidationRule.Accepts(defaultValue))
        {
            throw new ArgumentException(
                "Default value does not satisfy its validation rule.",
                nameof(defaultValue));
        }
        if (SensitivityClass
                == ControlCenterSensitivityClass
                    .OpaqueCredentialReferenceOnly
            && (ValueKind
                    != ControlCenterSettingValueKind.OpaqueReference
                || defaultValue != null))
        {
            throw new ArgumentException(
                "Credential settings may contain only an opaque reference and cannot define a default value.");
        }
        if (ApplyMode == ControlCenterApplyMode.PresentationOnly
            && AuthorityDomain
                != ControlCenterAuthorityDomain.Presentation)
        {
            throw new ArgumentException(
                "Presentation-only settings must remain in the Presentation authority domain.");
        }
        DefaultValue = defaultValue;
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/SETTING-DESCRIPTOR/1",
            SettingId.Value,
            ControlCenterStageACanonicalHash.Number((int)Category),
            ControlCenterStageACanonicalHash.Number((int)ValueKind),
            DefaultValue?.Fingerprint,
            ValidationRule.Fingerprint,
            ControlCenterStageACanonicalHash.Number(
                (int)SensitivityClass),
            ControlCenterStageACanonicalHash.Number((int)ApplyMode),
            ControlCenterStageACanonicalHash.Number(
                (int)RestartRequirement),
            ControlCenterStageACanonicalHash.Number(
                (int)AuthorityDomain),
            ControlCenterStageACanonicalHash.Number(
                (int)VisibilityPolicy));
    }
}

internal sealed class ControlCenterSchemaBuildResult
{
    internal ControlCenterSchemaBuildAdmission Admission { get; }
    internal ControlCenterSchema? Schema { get; }

    internal ControlCenterSchemaBuildResult(
        ControlCenterSchemaBuildAdmission admission,
        ControlCenterSchema? schema)
    {
        Admission = admission;
        Schema = schema;
    }
}

internal sealed class ControlCenterSchema
{
    internal int SchemaVersion { get; }
    internal int DescriptorSetVersion { get; }
    internal IReadOnlyList<ControlCenterSettingDescriptor> Descriptors {
        get;
    }
    internal string Fingerprint { get; }

    private readonly IReadOnlyDictionary<string, ControlCenterSettingDescriptor>
        _byId;

    private ControlCenterSchema(
        int schemaVersion,
        int descriptorSetVersion,
        IReadOnlyList<ControlCenterSettingDescriptor> descriptors)
    {
        SchemaVersion = schemaVersion;
        DescriptorSetVersion = descriptorSetVersion;
        Descriptors = descriptors;
        _byId = new ReadOnlyDictionary<string, ControlCenterSettingDescriptor>(
            descriptors.ToDictionary(
                descriptor => descriptor.SettingId.Value,
                StringComparer.Ordinal));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/CONTROL-CENTER-SCHEMA/1",
            ControlCenterStageACanonicalHash.Number(SchemaVersion),
            ControlCenterStageACanonicalHash.Number(
                DescriptorSetVersion),
            string.Join(
                ",",
                descriptors.Select(value => value.Fingerprint)));
    }

    internal static ControlCenterSchemaBuildResult Create(
        int schemaVersion,
        int descriptorSetVersion,
        IEnumerable<ControlCenterSettingDescriptor> descriptors)
    {
        if (schemaVersion
            != ControlCenterStageAContractGuard.SchemaVersion)
        {
            return Reject(
                ControlCenterSchemaBuildAdmission
                    .UnsupportedSchemaVersion);
        }
        if (descriptorSetVersion <= 0)
        {
            return Reject(
                ControlCenterSchemaBuildAdmission
                    .InvalidDescriptorSetVersion);
        }
        if (descriptors == null)
            throw new ArgumentNullException(nameof(descriptors));
        var values = descriptors.ToArray();
        if (values.Length == 0 || values.Any(value => value == null))
        {
            return Reject(
                ControlCenterSchemaBuildAdmission.EmptyDescriptorSet);
        }
        if (values.Length
            > ControlCenterStageAContractGuard.MaximumDescriptors)
        {
            return Reject(
                ControlCenterSchemaBuildAdmission.CapacityExceeded);
        }
        if (values
            .Select(value => value.SettingId.Value)
            .Distinct(StringComparer.Ordinal)
            .Count() != values.Length)
        {
            return Reject(
                ControlCenterSchemaBuildAdmission.DuplicateSettingId);
        }
        var ordered = values
            .OrderBy(value => value.SettingId.Value, StringComparer.Ordinal)
            .ToArray();
        return new ControlCenterSchemaBuildResult(
            ControlCenterSchemaBuildAdmission
                .AcceptedForContractReplay,
            new ControlCenterSchema(
                schemaVersion,
                descriptorSetVersion,
                new ReadOnlyCollection<ControlCenterSettingDescriptor>(
                    ordered)));
    }

    internal bool TryGetDescriptor(
        ControlCenterSettingId settingId,
        out ControlCenterSettingDescriptor? descriptor)
    {
        if (settingId == null)
            throw new ArgumentNullException(nameof(settingId));
        return _byId.TryGetValue(settingId.Value, out descriptor);
    }

    private static ControlCenterSchemaBuildResult Reject(
        ControlCenterSchemaBuildAdmission admission)
        => new(admission, null);
}

internal sealed class ControlCenterSettingAssignment
{
    internal ControlCenterSettingId SettingId { get; }
    internal ControlCenterSettingValue Value { get; }
    internal string Fingerprint { get; }

    internal ControlCenterSettingAssignment(
        ControlCenterSettingId settingId,
        ControlCenterSettingValue value)
    {
        SettingId = settingId
            ?? throw new ArgumentNullException(nameof(settingId));
        Value = value
            ?? throw new ArgumentNullException(nameof(value));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/SETTING-ASSIGNMENT/1",
            SettingId.Value,
            Value.Fingerprint);
    }
}

internal sealed class DesiredSettingsSnapshotBuildResult
{
    internal ControlCenterSnapshotBuildAdmission Admission { get; }
    internal DesiredSettingsSnapshot? Snapshot { get; }

    internal DesiredSettingsSnapshotBuildResult(
        ControlCenterSnapshotBuildAdmission admission,
        DesiredSettingsSnapshot? snapshot)
    {
        Admission = admission;
        Snapshot = snapshot;
    }
}

internal sealed class DesiredSettingsSnapshot
{
    internal int SchemaVersion { get; }
    internal int DescriptorSetVersion { get; }
    internal string SchemaFingerprint { get; }
    internal long Revision { get; }
    internal long ConfigurationGeneration { get; }
    internal IReadOnlyList<ControlCenterSettingAssignment> Assignments {
        get;
    }
    internal string Fingerprint { get; }

    private readonly IReadOnlyDictionary<string, ControlCenterSettingAssignment>
        _byId;

    private DesiredSettingsSnapshot(
        ControlCenterSchema schema,
        long revision,
        long configurationGeneration,
        IReadOnlyList<ControlCenterSettingAssignment> assignments)
    {
        SchemaVersion = schema.SchemaVersion;
        DescriptorSetVersion = schema.DescriptorSetVersion;
        SchemaFingerprint = schema.Fingerprint;
        Revision = revision;
        ConfigurationGeneration = configurationGeneration;
        Assignments = assignments;
        _byId =
            new ReadOnlyDictionary<string, ControlCenterSettingAssignment>(
                assignments.ToDictionary(
                    value => value.SettingId.Value,
                    StringComparer.Ordinal));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/DESIRED-SETTINGS/1",
            ControlCenterStageACanonicalHash.Number(SchemaVersion),
            ControlCenterStageACanonicalHash.Number(
                DescriptorSetVersion),
            SchemaFingerprint,
            ControlCenterStageACanonicalHash.Number(Revision),
            ControlCenterStageACanonicalHash.Number(
                ConfigurationGeneration),
            string.Join(
                ",",
                assignments.Select(value => value.Fingerprint)));
    }

    internal static DesiredSettingsSnapshotBuildResult Create(
        ControlCenterSchema schema,
        long revision,
        long configurationGeneration,
        IEnumerable<ControlCenterSettingAssignment> assignments)
    {
        if (schema == null)
            throw new ArgumentNullException(nameof(schema));
        ControlCenterStageAContractGuard.Positive(
            revision,
            nameof(revision));
        ControlCenterStageAContractGuard.Positive(
            configurationGeneration,
            nameof(configurationGeneration));
        if (assignments == null)
            throw new ArgumentNullException(nameof(assignments));
        var values = assignments.ToArray();
        if (values.Length
            > ControlCenterStageAContractGuard.MaximumAssignments)
        {
            return Reject(
                ControlCenterSnapshotBuildAdmission.InvalidValue);
        }
        if (values.Any(value => value == null))
        {
            return Reject(
                ControlCenterSnapshotBuildAdmission.InvalidValue);
        }
        if (values
            .Select(value => value.SettingId.Value)
            .Distinct(StringComparer.Ordinal)
            .Count() != values.Length)
        {
            return Reject(
                ControlCenterSnapshotBuildAdmission.DuplicateSettingId);
        }
        foreach (var assignment in values)
        {
            if (!schema.TryGetDescriptor(
                    assignment.SettingId,
                    out var descriptor)
                || descriptor == null)
            {
                return Reject(
                    ControlCenterSnapshotBuildAdmission.UnknownSettingId);
            }
            if (!descriptor.ValidationRule.Accepts(assignment.Value))
            {
                return Reject(
                    ControlCenterSnapshotBuildAdmission.InvalidValue);
            }
        }
        var ordered = values
            .OrderBy(value => value.SettingId.Value, StringComparer.Ordinal)
            .ToArray();
        return new DesiredSettingsSnapshotBuildResult(
            ControlCenterSnapshotBuildAdmission
                .AcceptedForContractReplay,
            new DesiredSettingsSnapshot(
                schema,
                revision,
                configurationGeneration,
                new ReadOnlyCollection<ControlCenterSettingAssignment>(
                    ordered)));
    }

    internal bool TryGet(
        ControlCenterSettingId settingId,
        out ControlCenterSettingAssignment? assignment)
    {
        if (settingId == null)
            throw new ArgumentNullException(nameof(settingId));
        return _byId.TryGetValue(settingId.Value, out assignment);
    }

    private static DesiredSettingsSnapshotBuildResult Reject(
        ControlCenterSnapshotBuildAdmission admission)
        => new(admission, null);
}

internal sealed class ControlCenterRuntimeIdentity
{
    internal ControlCenterRuntimeInstanceId RuntimeInstanceId { get; }
    internal long RuntimeGeneration { get; }
    internal ControlCenterRuntimeSessionId SessionId { get; }
    internal string ProcessStartFingerprint { get; }
    internal string Fingerprint { get; }

    internal ControlCenterRuntimeIdentity(
        ControlCenterRuntimeInstanceId runtimeInstanceId,
        long runtimeGeneration,
        ControlCenterRuntimeSessionId sessionId,
        string processStartFingerprint)
    {
        RuntimeInstanceId = runtimeInstanceId
            ?? throw new ArgumentNullException(nameof(runtimeInstanceId));
        RuntimeGeneration = ControlCenterStageAContractGuard.Positive(
            runtimeGeneration,
            nameof(runtimeGeneration));
        SessionId = sessionId
            ?? throw new ArgumentNullException(nameof(sessionId));
        ProcessStartFingerprint =
            ControlCenterStageAContractGuard.Sha256(
                processStartFingerprint,
                nameof(processStartFingerprint));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/RUNTIME-IDENTITY/1",
            RuntimeInstanceId.Value,
            ControlCenterStageACanonicalHash.Number(RuntimeGeneration),
            SessionId.Value,
            ProcessStartFingerprint);
    }

    internal bool Matches(ControlCenterRuntimeIdentity? other)
        => other != null
            && string.Equals(
                Fingerprint,
                other.Fingerprint,
                StringComparison.Ordinal);
}

internal sealed class ControlCenterEffectiveSetting
{
    internal ControlCenterSettingId SettingId { get; }
    internal ControlCenterEffectiveValueState State { get; }
    internal ControlCenterSettingValue? Value { get; }
    internal string Fingerprint { get; }

    internal ControlCenterEffectiveSetting(
        ControlCenterSettingId settingId,
        ControlCenterEffectiveValueState state,
        ControlCenterSettingValue? value)
    {
        SettingId = settingId
            ?? throw new ArgumentNullException(nameof(settingId));
        State = ControlCenterStageAContractGuard.Defined(
            state,
            nameof(state));
        if ((State == ControlCenterEffectiveValueState.Effective)
            != (value != null))
        {
            throw new ArgumentException(
                "Only Effective state may carry a setting value.",
                nameof(value));
        }
        Value = value;
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/EFFECTIVE-SETTING/1",
            SettingId.Value,
            ControlCenterStageACanonicalHash.Number((int)State),
            Value?.Fingerprint);
    }
}

internal sealed class ControlCenterFeatureState
{
    internal ControlCenterFeatureId FeatureId { get; }
    internal ControlCenterFeatureStateKind State { get; }
    internal string Fingerprint { get; }

    internal ControlCenterFeatureState(
        ControlCenterFeatureId featureId,
        ControlCenterFeatureStateKind state)
    {
        FeatureId = featureId
            ?? throw new ArgumentNullException(nameof(featureId));
        State = ControlCenterStageAContractGuard.Defined(
            state,
            nameof(state));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/FEATURE-STATE/1",
            FeatureId.Value,
            ControlCenterStageACanonicalHash.Number((int)State));
    }
}

internal sealed class ControlCenterDiagnosticsSummary
{
    internal int ActiveRecords { get; }
    internal int RetiredRecords { get; }
    internal int FaultCount { get; }
    internal int CapacityUsed { get; }
    internal int CapacityLimit { get; }
    internal string Fingerprint { get; }

    internal ControlCenterDiagnosticsSummary(
        int activeRecords,
        int retiredRecords,
        int faultCount,
        int capacityUsed,
        int capacityLimit)
    {
        ActiveRecords = ControlCenterStageAContractGuard.NonNegative(
            activeRecords,
            nameof(activeRecords));
        RetiredRecords = ControlCenterStageAContractGuard.NonNegative(
            retiredRecords,
            nameof(retiredRecords));
        FaultCount = ControlCenterStageAContractGuard.NonNegative(
            faultCount,
            nameof(faultCount));
        CapacityUsed = ControlCenterStageAContractGuard.NonNegative(
            capacityUsed,
            nameof(capacityUsed));
        CapacityLimit = ControlCenterStageAContractGuard.NonNegative(
            capacityLimit,
            nameof(capacityLimit));
        if (CapacityUsed > CapacityLimit)
            throw new ArgumentOutOfRangeException(nameof(capacityUsed));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/DIAGNOSTICS-SUMMARY/1",
            ControlCenterStageACanonicalHash.Number(ActiveRecords),
            ControlCenterStageACanonicalHash.Number(RetiredRecords),
            ControlCenterStageACanonicalHash.Number(FaultCount),
            ControlCenterStageACanonicalHash.Number(CapacityUsed),
            ControlCenterStageACanonicalHash.Number(CapacityLimit));
    }
}

internal sealed class EffectiveRuntimeSnapshotBuildResult
{
    internal ControlCenterSnapshotBuildAdmission Admission { get; }
    internal EffectiveRuntimeSnapshot? Snapshot { get; }

    internal EffectiveRuntimeSnapshotBuildResult(
        ControlCenterSnapshotBuildAdmission admission,
        EffectiveRuntimeSnapshot? snapshot)
    {
        Admission = admission;
        Snapshot = snapshot;
    }
}

internal sealed class EffectiveRuntimeSnapshot
{
    internal int SchemaVersion { get; }
    internal int DescriptorSetVersion { get; }
    internal string SchemaFingerprint { get; }
    internal ControlCenterRuntimeIdentity RuntimeIdentity { get; }
    internal long ConfigurationGeneration { get; }
    internal DateTimeOffset ObservedAt { get; }
    internal IReadOnlyList<ControlCenterEffectiveSetting> EffectiveValues {
        get;
    }
    internal IReadOnlyList<ControlCenterFeatureState> FeatureStates { get; }
    internal ControlCenterDiagnosticsSummary DiagnosticsSummary { get; }
    internal string Fingerprint { get; }

    private EffectiveRuntimeSnapshot(
        ControlCenterSchema schema,
        ControlCenterRuntimeIdentity runtimeIdentity,
        long configurationGeneration,
        DateTimeOffset observedAt,
        IReadOnlyList<ControlCenterEffectiveSetting> effectiveValues,
        IReadOnlyList<ControlCenterFeatureState> featureStates,
        ControlCenterDiagnosticsSummary diagnosticsSummary)
    {
        SchemaVersion = schema.SchemaVersion;
        DescriptorSetVersion = schema.DescriptorSetVersion;
        SchemaFingerprint = schema.Fingerprint;
        RuntimeIdentity = runtimeIdentity;
        ConfigurationGeneration = configurationGeneration;
        ObservedAt = observedAt;
        EffectiveValues = effectiveValues;
        FeatureStates = featureStates;
        DiagnosticsSummary = diagnosticsSummary;
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/EFFECTIVE-RUNTIME/1",
            ControlCenterStageACanonicalHash.Number(SchemaVersion),
            ControlCenterStageACanonicalHash.Number(
                DescriptorSetVersion),
            SchemaFingerprint,
            RuntimeIdentity.Fingerprint,
            ControlCenterStageACanonicalHash.Number(
                ConfigurationGeneration),
            ControlCenterStageACanonicalHash.Timestamp(ObservedAt),
            string.Join(
                ",",
                effectiveValues.Select(value => value.Fingerprint)),
            string.Join(
                ",",
                featureStates.Select(value => value.Fingerprint)),
            DiagnosticsSummary.Fingerprint);
    }

    internal static EffectiveRuntimeSnapshotBuildResult Create(
        ControlCenterSchema schema,
        ControlCenterRuntimeIdentity runtimeIdentity,
        long configurationGeneration,
        DateTimeOffset observedAt,
        IEnumerable<ControlCenterEffectiveSetting> effectiveValues,
        IEnumerable<ControlCenterFeatureState> featureStates,
        ControlCenterDiagnosticsSummary diagnosticsSummary)
    {
        if (schema == null)
            throw new ArgumentNullException(nameof(schema));
        if (runtimeIdentity == null)
            throw new ArgumentNullException(nameof(runtimeIdentity));
        ControlCenterStageAContractGuard.Positive(
            configurationGeneration,
            nameof(configurationGeneration));
        observedAt = ControlCenterStageAContractGuard.Timestamp(
            observedAt,
            nameof(observedAt));
        if (effectiveValues == null)
            throw new ArgumentNullException(nameof(effectiveValues));
        if (featureStates == null)
            throw new ArgumentNullException(nameof(featureStates));
        if (diagnosticsSummary == null)
            throw new ArgumentNullException(nameof(diagnosticsSummary));
        var settings = effectiveValues.ToArray();
        var features = featureStates.ToArray();
        if (settings.Length
                > ControlCenterStageAContractGuard.MaximumAssignments
            || settings.Any(value => value == null))
        {
            return Reject(
                ControlCenterSnapshotBuildAdmission.InvalidValue);
        }
        if (features.Length
                > ControlCenterStageAContractGuard.MaximumFeatureStates
            || features.Any(value => value == null))
        {
            return Reject(
                ControlCenterSnapshotBuildAdmission.InvalidValue);
        }
        if (settings
            .Select(value => value.SettingId.Value)
            .Distinct(StringComparer.Ordinal)
            .Count() != settings.Length)
        {
            return Reject(
                ControlCenterSnapshotBuildAdmission.DuplicateSettingId);
        }
        if (features
            .Select(value => value.FeatureId.Value)
            .Distinct(StringComparer.Ordinal)
            .Count() != features.Length)
        {
            return Reject(
                ControlCenterSnapshotBuildAdmission.DuplicateFeatureId);
        }
        foreach (var setting in settings)
        {
            if (!schema.TryGetDescriptor(
                    setting.SettingId,
                    out var descriptor)
                || descriptor == null)
            {
                return Reject(
                    ControlCenterSnapshotBuildAdmission.UnknownSettingId);
            }
            if (setting.Value != null
                && !descriptor.ValidationRule.Accepts(setting.Value))
            {
                return Reject(
                    ControlCenterSnapshotBuildAdmission.InvalidValue);
            }
        }
        var orderedSettings = settings
            .OrderBy(value => value.SettingId.Value, StringComparer.Ordinal)
            .ToArray();
        var orderedFeatures = features
            .OrderBy(value => value.FeatureId.Value, StringComparer.Ordinal)
            .ToArray();
        return new EffectiveRuntimeSnapshotBuildResult(
            ControlCenterSnapshotBuildAdmission
                .AcceptedForContractReplay,
            new EffectiveRuntimeSnapshot(
                schema,
                runtimeIdentity,
                configurationGeneration,
                observedAt,
                new ReadOnlyCollection<ControlCenterEffectiveSetting>(
                    orderedSettings),
                new ReadOnlyCollection<ControlCenterFeatureState>(
                    orderedFeatures),
                diagnosticsSummary));
    }

    private static EffectiveRuntimeSnapshotBuildResult Reject(
        ControlCenterSnapshotBuildAdmission admission)
        => new(admission, null);
}

internal sealed class ControlCenterSettingChange
{
    internal ControlCenterSettingId SettingId { get; }
    internal ControlCenterSettingValue DesiredValue { get; }
    internal string Fingerprint { get; }

    internal ControlCenterSettingChange(
        ControlCenterSettingId settingId,
        ControlCenterSettingValue desiredValue)
    {
        SettingId = settingId
            ?? throw new ArgumentNullException(nameof(settingId));
        DesiredValue = desiredValue
            ?? throw new ArgumentNullException(nameof(desiredValue));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/SETTING-CHANGE/1",
            SettingId.Value,
            DesiredValue.Fingerprint);
    }
}

internal sealed class SettingsChangeTransaction
{
    internal ControlCenterTransactionId TransactionId { get; }
    internal int SchemaVersion { get; }
    internal int DescriptorSetVersion { get; }
    internal string SchemaFingerprint { get; }
    internal long BaseDesiredRevision { get; }
    internal long ExpectedConfigurationGeneration { get; }
    internal ControlCenterRuntimeIdentity? ExpectedRuntimeIdentity { get; }
    internal IReadOnlyList<ControlCenterSettingChange> Changes { get; }
    internal DateTimeOffset RequestedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal SettingsChangeTransaction(
        ControlCenterTransactionId transactionId,
        ControlCenterSchema schema,
        long baseDesiredRevision,
        long expectedConfigurationGeneration,
        ControlCenterRuntimeIdentity? expectedRuntimeIdentity,
        IEnumerable<ControlCenterSettingChange> changes,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt)
    {
        TransactionId = transactionId
            ?? throw new ArgumentNullException(nameof(transactionId));
        if (schema == null)
            throw new ArgumentNullException(nameof(schema));
        SchemaVersion = schema.SchemaVersion;
        DescriptorSetVersion = schema.DescriptorSetVersion;
        SchemaFingerprint = schema.Fingerprint;
        BaseDesiredRevision = ControlCenterStageAContractGuard.Positive(
            baseDesiredRevision,
            nameof(baseDesiredRevision));
        ExpectedConfigurationGeneration =
            ControlCenterStageAContractGuard.Positive(
                expectedConfigurationGeneration,
                nameof(expectedConfigurationGeneration));
        ExpectedRuntimeIdentity = expectedRuntimeIdentity;
        if (changes == null)
            throw new ArgumentNullException(nameof(changes));
        var values = changes.ToArray();
        if (values.Length == 0
            || values.Length
                > ControlCenterStageAContractGuard
                    .MaximumChangesPerTransaction
            || values.Any(value => value == null))
        {
            throw new ArgumentOutOfRangeException(nameof(changes));
        }
        Changes =
            new ReadOnlyCollection<ControlCenterSettingChange>(values);
        RequestedAt = ControlCenterStageAContractGuard.Timestamp(
            requestedAt,
            nameof(requestedAt));
        ExpiresAt = ControlCenterStageAContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= RequestedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/SETTINGS-TRANSACTION/1",
            TransactionId.Value,
            ControlCenterStageACanonicalHash.Number(SchemaVersion),
            ControlCenterStageACanonicalHash.Number(
                DescriptorSetVersion),
            SchemaFingerprint,
            ControlCenterStageACanonicalHash.Number(
                BaseDesiredRevision),
            ControlCenterStageACanonicalHash.Number(
                ExpectedConfigurationGeneration),
            ExpectedRuntimeIdentity?.Fingerprint,
            string.Join(",", values.Select(value => value.Fingerprint)),
            ControlCenterStageACanonicalHash.Timestamp(RequestedAt),
            ControlCenterStageACanonicalHash.Timestamp(ExpiresAt));
    }
}

internal sealed class ControlCenterPerSettingAdmissionResult
{
    internal ControlCenterSettingId SettingId { get; }
    internal ControlCenterPerSettingAdmission Admission { get; }
    internal string Fingerprint { get; }

    internal ControlCenterPerSettingAdmissionResult(
        ControlCenterSettingId settingId,
        ControlCenterPerSettingAdmission admission)
    {
        SettingId = settingId
            ?? throw new ArgumentNullException(nameof(settingId));
        Admission = ControlCenterStageAContractGuard.Defined(
            admission,
            nameof(admission));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/PER-SETTING-ADMISSION/1",
            SettingId.Value,
            ControlCenterStageACanonicalHash.Number((int)Admission));
    }
}

internal sealed class SettingsAdmissionReceipt
{
    internal ControlCenterTransactionId TransactionId { get; }
    internal ControlCenterSettingsAdmission Admission { get; }
    internal IReadOnlyList<ControlCenterPerSettingAdmissionResult>
        PerSettingResults { get; }
    internal long? NewDesiredRevision => null;
    internal long? NewConfigurationGeneration => null;
    internal bool AllowsConfigurationWrite => false;
    internal bool AllowsRuntimeMutation => false;
    internal string Fingerprint { get; }

    internal SettingsAdmissionReceipt(
        ControlCenterTransactionId transactionId,
        ControlCenterSettingsAdmission admission,
        IEnumerable<ControlCenterPerSettingAdmissionResult>? perSettingResults)
    {
        TransactionId = transactionId
            ?? throw new ArgumentNullException(nameof(transactionId));
        Admission = ControlCenterStageAContractGuard.Defined(
            admission,
            nameof(admission));
        var values = (perSettingResults
                ?? Array.Empty<ControlCenterPerSettingAdmissionResult>())
            .ToArray();
        if (values.Any(value => value == null))
            throw new ArgumentException(nameof(perSettingResults));
        PerSettingResults =
            new ReadOnlyCollection<ControlCenterPerSettingAdmissionResult>(
                values);
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/SETTINGS-ADMISSION-RECEIPT/1",
            TransactionId.Value,
            ControlCenterStageACanonicalHash.Number((int)Admission),
            string.Join(",", values.Select(value => value.Fingerprint)));
    }
}

internal static class ControlCenterSettingsAdmissionPolicy
{
    internal static SettingsAdmissionReceipt Evaluate(
        ControlCenterSchema schema,
        DesiredSettingsSnapshot desired,
        EffectiveRuntimeSnapshot? effective,
        SettingsChangeTransaction transaction,
        DateTimeOffset evaluatedAt)
    {
        if (schema == null)
            throw new ArgumentNullException(nameof(schema));
        if (desired == null)
            throw new ArgumentNullException(nameof(desired));
        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));
        evaluatedAt = ControlCenterStageAContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        if (transaction.SchemaVersion != schema.SchemaVersion
            || transaction.DescriptorSetVersion
                != schema.DescriptorSetVersion
            || !string.Equals(
                transaction.SchemaFingerprint,
                schema.Fingerprint,
                StringComparison.Ordinal)
            || desired.SchemaVersion != schema.SchemaVersion
            || desired.DescriptorSetVersion
                != schema.DescriptorSetVersion
            || !string.Equals(
                desired.SchemaFingerprint,
                schema.Fingerprint,
                StringComparison.Ordinal))
        {
            return Reject(
                transaction,
                ControlCenterSettingsAdmission.SchemaMismatch);
        }
        if (evaluatedAt < transaction.RequestedAt)
        {
            return Reject(
                transaction,
                ControlCenterSettingsAdmission.RequestNotYetValid);
        }
        if (evaluatedAt >= transaction.ExpiresAt)
        {
            return Reject(
                transaction,
                ControlCenterSettingsAdmission.RequestExpired);
        }
        if (transaction.BaseDesiredRevision != desired.Revision)
        {
            return Reject(
                transaction,
                ControlCenterSettingsAdmission.DesiredRevisionMismatch);
        }
        if (transaction.ExpectedConfigurationGeneration
            != desired.ConfigurationGeneration)
        {
            return Reject(
                transaction,
                ControlCenterSettingsAdmission
                    .ConfigurationGenerationMismatch);
        }
        if (transaction.ExpectedRuntimeIdentity != null)
        {
            if (effective == null)
            {
                return Reject(
                    transaction,
                    ControlCenterSettingsAdmission.RuntimeUnavailable);
            }
            if (!transaction.ExpectedRuntimeIdentity.RuntimeInstanceId.Equals(
                    effective.RuntimeIdentity.RuntimeInstanceId)
                || !transaction.ExpectedRuntimeIdentity.SessionId.Equals(
                    effective.RuntimeIdentity.SessionId))
            {
                return Reject(
                    transaction,
                    ControlCenterSettingsAdmission
                        .RuntimeIdentityMismatch);
            }
            if (transaction.ExpectedRuntimeIdentity.RuntimeGeneration
                != effective.RuntimeIdentity.RuntimeGeneration)
            {
                return Reject(
                    transaction,
                    ControlCenterSettingsAdmission
                        .RuntimeGenerationMismatch);
            }
            if (effective.ConfigurationGeneration
                != transaction.ExpectedConfigurationGeneration)
            {
                return Reject(
                    transaction,
                    ControlCenterSettingsAdmission
                        .RuntimeConfigurationMismatch);
            }
        }
        var duplicateIds = transaction.Changes
            .GroupBy(
                value => value.SettingId.Value,
                StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (duplicateIds.Count > 0)
        {
            return new SettingsAdmissionReceipt(
                transaction.TransactionId,
                ControlCenterSettingsAdmission.DuplicateChange,
                transaction.Changes.Select(
                    change => new ControlCenterPerSettingAdmissionResult(
                        change.SettingId,
                        duplicateIds.Contains(change.SettingId.Value)
                            ? ControlCenterPerSettingAdmission
                                .DuplicateChange
                            : ControlCenterPerSettingAdmission
                                .ValidatedForContractReplay)));
        }
        var results =
            new List<ControlCenterPerSettingAdmissionResult>();
        var overall =
            ControlCenterSettingsAdmission.ValidatedForContractReplay;
        foreach (var change in transaction.Changes)
        {
            if (!schema.TryGetDescriptor(
                    change.SettingId,
                    out var descriptor)
                || descriptor == null)
            {
                results.Add(
                    new ControlCenterPerSettingAdmissionResult(
                        change.SettingId,
                        ControlCenterPerSettingAdmission.UnknownSetting));
                overall = ControlCenterSettingsAdmission.UnknownSetting;
                continue;
            }
            if (!descriptor.ValidationRule.Accepts(change.DesiredValue))
            {
                results.Add(
                    new ControlCenterPerSettingAdmissionResult(
                        change.SettingId,
                        ControlCenterPerSettingAdmission.InvalidValue));
                if (overall
                    == ControlCenterSettingsAdmission
                        .ValidatedForContractReplay)
                {
                    overall =
                        ControlCenterSettingsAdmission.InvalidValue;
                }
                continue;
            }
            results.Add(
                new ControlCenterPerSettingAdmissionResult(
                    change.SettingId,
                    ControlCenterPerSettingAdmission
                        .ValidatedForContractReplay));
        }
        return new SettingsAdmissionReceipt(
            transaction.TransactionId,
            overall,
            results);
    }

    private static SettingsAdmissionReceipt Reject(
        SettingsChangeTransaction transaction,
        ControlCenterSettingsAdmission admission)
        => new(transaction.TransactionId, admission, null);
}

internal sealed class DesktopSurfaceSession
{
    internal DesktopSurfaceInstanceId SurfaceInstanceId { get; }
    internal ControlCenterRuntimeIdentity ActiveRuntimeIdentity { get; }
    internal DesktopExperiencePackageId ExperiencePackageId { get; }
    internal DateTimeOffset LeaseIssuedAt { get; }
    internal DateTimeOffset LeaseExpiresAt { get; }
    internal DesktopSurfacePresentationState PresentationState { get; }
    internal bool AllowsDesktopArtifactCreation => false;
    internal bool AllowsActivation => false;
    internal string Fingerprint { get; }

    internal DesktopSurfaceSession(
        DesktopSurfaceInstanceId surfaceInstanceId,
        ControlCenterRuntimeIdentity activeRuntimeIdentity,
        DesktopExperiencePackageId experiencePackageId,
        DateTimeOffset leaseIssuedAt,
        DateTimeOffset leaseExpiresAt,
        DesktopSurfacePresentationState presentationState)
    {
        SurfaceInstanceId = surfaceInstanceId
            ?? throw new ArgumentNullException(nameof(surfaceInstanceId));
        ActiveRuntimeIdentity = activeRuntimeIdentity
            ?? throw new ArgumentNullException(nameof(activeRuntimeIdentity));
        ExperiencePackageId = experiencePackageId
            ?? throw new ArgumentNullException(nameof(experiencePackageId));
        LeaseIssuedAt = ControlCenterStageAContractGuard.Timestamp(
            leaseIssuedAt,
            nameof(leaseIssuedAt));
        LeaseExpiresAt = ControlCenterStageAContractGuard.Timestamp(
            leaseExpiresAt,
            nameof(leaseExpiresAt));
        if (LeaseExpiresAt <= LeaseIssuedAt)
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt));
        PresentationState = ControlCenterStageAContractGuard.Defined(
            presentationState,
            nameof(presentationState));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/DESKTOP-SURFACE-SESSION/1",
            SurfaceInstanceId.Value,
            ActiveRuntimeIdentity.Fingerprint,
            ExperiencePackageId.Value,
            ControlCenterStageACanonicalHash.Timestamp(LeaseIssuedAt),
            ControlCenterStageACanonicalHash.Timestamp(LeaseExpiresAt),
            ControlCenterStageACanonicalHash.Number(
                (int)PresentationState));
    }
}

internal sealed class DesktopSurfaceActivationRequest
{
    internal DesktopSurfaceInstanceId SurfaceInstanceId { get; }
    internal ControlCenterRuntimeIdentity RuntimeIdentity { get; }
    internal DesktopSurfaceActivationKind ActivationKind { get; }
    internal long InputSequence { get; }
    internal DateTimeOffset RequestedAt { get; }
    internal DateTimeOffset ExpiresAt { get; }
    internal string Fingerprint { get; }

    internal DesktopSurfaceActivationRequest(
        DesktopSurfaceInstanceId surfaceInstanceId,
        ControlCenterRuntimeIdentity runtimeIdentity,
        DesktopSurfaceActivationKind activationKind,
        long inputSequence,
        DateTimeOffset requestedAt,
        DateTimeOffset expiresAt)
    {
        SurfaceInstanceId = surfaceInstanceId
            ?? throw new ArgumentNullException(nameof(surfaceInstanceId));
        RuntimeIdentity = runtimeIdentity
            ?? throw new ArgumentNullException(nameof(runtimeIdentity));
        ActivationKind = ControlCenterStageAContractGuard.Defined(
            activationKind,
            nameof(activationKind));
        InputSequence = ControlCenterStageAContractGuard.Positive(
            inputSequence,
            nameof(inputSequence));
        RequestedAt = ControlCenterStageAContractGuard.Timestamp(
            requestedAt,
            nameof(requestedAt));
        ExpiresAt = ControlCenterStageAContractGuard.Timestamp(
            expiresAt,
            nameof(expiresAt));
        if (ExpiresAt <= RequestedAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/DESKTOP-ACTIVATION-REQUEST/1",
            SurfaceInstanceId.Value,
            RuntimeIdentity.Fingerprint,
            ControlCenterStageACanonicalHash.Number(
                (int)ActivationKind),
            ControlCenterStageACanonicalHash.Number(InputSequence),
            ControlCenterStageACanonicalHash.Timestamp(RequestedAt),
            ControlCenterStageACanonicalHash.Timestamp(ExpiresAt));
    }
}

internal sealed class DesktopSurfaceActivationReceipt
{
    internal DesktopSurfaceActivationAdmission Admission { get; }
    internal bool AllowsDesktopArtifactCreation => false;
    internal bool AllowsActivation => false;
    internal bool AllowsCapabilityOutput => false;
    internal string Fingerprint { get; }

    internal DesktopSurfaceActivationReceipt(
        DesktopSurfaceActivationAdmission admission,
        DesktopSurfaceSession session,
        DesktopSurfaceActivationRequest request)
    {
        Admission = ControlCenterStageAContractGuard.Defined(
            admission,
            nameof(admission));
        Fingerprint = ControlCenterStageACanonicalHash.Of(
            "D028/DESKTOP-ACTIVATION-RECEIPT/1",
            ControlCenterStageACanonicalHash.Number((int)Admission),
            session.Fingerprint,
            request.Fingerprint);
    }
}

internal static class DesktopSurfaceStageAAdmissionPolicy
{
    internal static DesktopSurfaceActivationReceipt Evaluate(
        DesktopSurfaceSession session,
        DesktopSurfaceActivationRequest request,
        DateTimeOffset evaluatedAt)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        evaluatedAt = ControlCenterStageAContractGuard.Timestamp(
            evaluatedAt,
            nameof(evaluatedAt));
        if (evaluatedAt < session.LeaseIssuedAt)
        {
            return Receipt(
                DesktopSurfaceActivationAdmission.SessionNotYetValid);
        }
        if (evaluatedAt >= session.LeaseExpiresAt)
        {
            return Receipt(
                DesktopSurfaceActivationAdmission.SessionExpired);
        }
        if (evaluatedAt < request.RequestedAt)
        {
            return Receipt(
                DesktopSurfaceActivationAdmission.RequestNotYetValid);
        }
        if (evaluatedAt >= request.ExpiresAt)
        {
            return Receipt(
                DesktopSurfaceActivationAdmission.RequestExpired);
        }
        if (!session.SurfaceInstanceId.Equals(request.SurfaceInstanceId))
        {
            return Receipt(
                DesktopSurfaceActivationAdmission.SurfaceIdentityMismatch);
        }
        if (!session.ActiveRuntimeIdentity.RuntimeInstanceId.Equals(
                request.RuntimeIdentity.RuntimeInstanceId)
            || !session.ActiveRuntimeIdentity.SessionId.Equals(
                request.RuntimeIdentity.SessionId))
        {
            return Receipt(
                DesktopSurfaceActivationAdmission.RuntimeIdentityMismatch);
        }
        if (session.ActiveRuntimeIdentity.RuntimeGeneration
            != request.RuntimeIdentity.RuntimeGeneration)
        {
            return Receipt(
                DesktopSurfaceActivationAdmission.RuntimeGenerationMismatch);
        }
        return Receipt(
            DesktopSurfaceActivationAdmission.DeniedByStageABoundary);

        DesktopSurfaceActivationReceipt Receipt(
            DesktopSurfaceActivationAdmission admission)
            => new(admission, session, request);
    }
}

internal sealed class ControlCenterStageAAuthorityCounters
{
    internal static ControlCenterStageAAuthorityCounters Zero { get; } =
        new();

    internal int PluginCalls => 0;
    internal int IpcMessages => 0;
    internal int ConfigurationReads => 0;
    internal int ConfigurationWrites => 0;
    internal int CredentialResolutions => 0;
    internal int NetworkCalls => 0;
    internal int ProviderCalls => 0;
    internal int PromptWrites => 0;
    internal int MemoryReads => 0;
    internal int MemoryWrites => 0;
    internal int DecisionOutputs => 0;
    internal int QueueWrites => 0;
    internal int MotionOutputs => 0;
    internal int ExecutionOutputs => 0;
    internal int PlayerDataWrites => 0;
    internal int DesktopArtifactsCreated => 0;
    internal int UiExecutablesLaunched => 0;
    internal int Total => 0;
    internal string Fingerprint { get; } =
        ControlCenterStageACanonicalHash.Of(
            "D028/CONTROL-CENTER-STAGE-A-AUTHORITY/1",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0",
            "0");

    internal bool IsZero =>
        PluginCalls == 0
        && IpcMessages == 0
        && ConfigurationReads == 0
        && ConfigurationWrites == 0
        && CredentialResolutions == 0
        && NetworkCalls == 0
        && ProviderCalls == 0
        && PromptWrites == 0
        && MemoryReads == 0
        && MemoryWrites == 0
        && DecisionOutputs == 0
        && QueueWrites == 0
        && MotionOutputs == 0
        && ExecutionOutputs == 0
        && PlayerDataWrites == 0
        && DesktopArtifactsCreated == 0
        && UiExecutablesLaunched == 0;

    private ControlCenterStageAAuthorityCounters()
    {
    }
}
