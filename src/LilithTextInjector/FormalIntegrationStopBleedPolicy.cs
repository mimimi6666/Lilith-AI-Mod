using System;

namespace LilithTextInjector;

internal enum FormalDesktopToolAdmission
{
    DisabledByConfiguration = 0,
    LegacyEnabled = 1,
    SuppressedByFormalIntegration = 2
}

internal sealed class FormalIntegrationStopBleedSnapshot
{
    internal bool FormalIntegrationActive { get; }
    internal bool AdvancedDesktopToolsConfigured { get; }
    internal FormalDesktopToolAdmission DesktopToolAdmission { get; }
    internal bool DesktopToolsEffective =>
        DesktopToolAdmission == FormalDesktopToolAdmission.LegacyEnabled;
    internal bool LegacyRollingContextEnabled => !FormalIntegrationActive;

    internal FormalIntegrationStopBleedSnapshot(
        bool formalIntegrationActive,
        bool advancedDesktopToolsConfigured)
    {
        FormalIntegrationActive = formalIntegrationActive;
        AdvancedDesktopToolsConfigured = advancedDesktopToolsConfigured;
        DesktopToolAdmission = formalIntegrationActive
            ? FormalDesktopToolAdmission.SuppressedByFormalIntegration
            : advancedDesktopToolsConfigured
                ? FormalDesktopToolAdmission.LegacyEnabled
                : FormalDesktopToolAdmission.DisabledByConfiguration;
    }
}

internal static class FormalIntegrationStopBleedPolicy
{
    internal static FormalIntegrationStopBleedSnapshot Evaluate(
        bool formalIntegrationActive,
        bool advancedDesktopToolsConfigured)
        => new(
            formalIntegrationActive,
            advancedDesktopToolsConfigured);

    internal static bool ShouldMaintainLegacyRollingContext(
        bool formalIntegrationActive)
        => !formalIntegrationActive;
}
