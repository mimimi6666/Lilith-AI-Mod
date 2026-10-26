namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    // Called at menu/notice creation on Unity's thread. Existing open choices
    // retain their identity and callback; a locale change never resets a match.
    internal static string MiniGameText(string key) => MiniGameDialogueSubtitles.UiText(key,
        GameSetting.Language ?? string.Empty);
}
