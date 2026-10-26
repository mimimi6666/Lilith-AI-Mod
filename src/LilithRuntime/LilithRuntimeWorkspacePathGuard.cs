namespace LilithRuntime;

internal static class LilithRuntimeWorkspacePathGuard
{
    internal static void EnsureSafe(
        string workspacePath,
        Func<string, FileAttributes?>? readAttributes = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath)
            || !Path.IsPathFullyQualified(workspacePath))
        {
            throw new InvalidDataException(
                "The Runtime workspace path is not absolute.");
        }

        var inspect = readAttributes ?? ReadExistingAttributes;
        var current = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(workspacePath));
        while (!string.IsNullOrWhiteSpace(current))
        {
            FileAttributes? attributes;
            try
            {
                attributes = inspect(current);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or NotSupportedException
                                              or ArgumentException)
            {
                throw new InvalidDataException(
                    "The Runtime workspace path ancestry could not be verified.",
                    exception);
            }

            if (attributes.HasValue
                && (attributes.Value & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "The Runtime workspace path must not contain a reparse point.");
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent)
                || PathsEqual(current, parent))
            {
                break;
            }
            current = Path.TrimEndingDirectorySeparator(parent);
        }
    }

    internal static FileAttributes? ReadExistingAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
}
