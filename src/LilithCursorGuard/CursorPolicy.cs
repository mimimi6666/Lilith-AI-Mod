// Derived from SmallYuanSY/LilithCursorGuard (MIT). See packaging/legal/LilithCursorGuard-MIT.txt.
namespace LilithCursorGuard;

internal static class CursorPolicy
{
    internal static bool ShouldPassThrough(uint foregroundProcessId, uint lilithProcessId) =>
        foregroundProcessId != 0 && foregroundProcessId == lilithProcessId;
}
