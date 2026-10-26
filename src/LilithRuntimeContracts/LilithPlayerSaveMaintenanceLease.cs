namespace LilithRuntimeContracts;

// An OS-released profile lock, not an authorization receipt or a player setting.
// The service and offline save command hold it for their entire writer lifetime.
public sealed class LilithPlayerSaveMaintenanceLease : IDisposable
{
    public const string FileName = ".player-save-maintenance.lock";
    public const string TransactionDirectoryName = ".player-save-transaction";
    private readonly FileStream _stream;

    private LilithPlayerSaveMaintenanceLease(FileStream stream) => _stream = stream;

    public static LilithPlayerSaveMaintenanceLease? TryAcquire(string profilePath)
    {
        var profile = ValidateProfilePath(profilePath);
        Directory.CreateDirectory(profile);
        ValidateProfilePath(profile);
        var path = Path.Combine(profile, FileName);
        RejectReparsePoint(path);
        try
        {
            return new LilithPlayerSaveMaintenanceLease(new FileStream(path,
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
        {
            return null;
        }
    }

    public static bool HasPendingTransaction(string profilePath)
    {
        var path = Path.Combine(ValidateProfilePath(profilePath), TransactionDirectoryName);
        RejectReparsePoint(path);
        return Directory.Exists(path) || File.Exists(path);
    }

    internal static string ValidateProfilePath(string profilePath)
    {
        if (string.IsNullOrWhiteSpace(profilePath) || !Path.IsPathFullyQualified(profilePath))
            throw new ArgumentException("An absolute profile path is required.", nameof(profilePath));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profilePath));
        if (full == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!))
            throw new ArgumentException("A profile cannot be a volume root.", nameof(profilePath));
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
            RejectReparsePoint(current.FullName);
        return full;
    }

    private static void RejectReparsePoint(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A player-save path cannot be a link.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    public void Dispose() => _stream.Dispose();
}
