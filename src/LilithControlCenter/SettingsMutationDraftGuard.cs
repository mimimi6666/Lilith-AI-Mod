namespace LilithControlCenter;

internal enum SettingsMutationSurface { Credentials, CustomProvider, TelegramToken }

// Presentation-only admission. These flags describe editors, not native state
// or permission to mutate. The caller still uses its existing native receipt.
internal readonly record struct SettingsMutationDraftState(bool Credentials, bool CustomProvider,
    bool TelegramToken, bool TelegramVoice, bool UsageBudget, bool NativeTools, bool Memory, bool Expression);

internal static class SettingsMutationDraftGuard
{
    internal static string? ConflictLabel(SettingsMutationSurface origin, SettingsMutationDraftState state)
    {
        if (origin != SettingsMutationSurface.Credentials && state.Credentials) return "settings.editor.apiKey";
        if (origin != SettingsMutationSurface.CustomProvider && state.CustomProvider) return "provider.setup.title";
        if (origin != SettingsMutationSurface.TelegramToken && state.TelegramToken) return "settings.editor.telegramToken";
        if (state.TelegramVoice) return "settings.editor.telegramVoice";
        if (state.UsageBudget) return "budget.title";
        if (state.NativeTools) return "tools.title";
        if (state.Memory) return "settings.editor.memory";
        if (state.Expression) return "expression.title";
        return null;
    }
}
