namespace LilithRuntime;

// A previous Python invocation can leave derived bytecode beside a pinned
// plugin. Never trust or execute it: remove it only after the caller verifies
// every source file and the release manifest. Unknown files still fail closure.
internal sealed class LilithPluginBytecodeCache
{
    private readonly string _root;
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _files = new(StringComparer.Ordinal);

    internal LilithPluginBytecodeCache(string root, IEnumerable<string> sourcePaths)
    {
        _root = Path.GetFullPath(root);
        foreach (var source in sourcePaths.Where(static path =>
                     path.EndsWith(".py", StringComparison.Ordinal)))
        {
            var slash = source.LastIndexOf('/');
            var parent = slash < 0 ? "" : source[..(slash + 1)];
            var module = source[(slash + 1)..^3];
            var cache = parent + "__pycache__";
            _directories.Add(cache);
            // All supported bundled Hermes releases use CPython 3.11.
            foreach (var optimization in new[] { "", ".opt-1", ".opt-2" })
                _files.Add(cache + "/" + module + ".cpython-311" + optimization + ".pyc");
        }
    }

    internal bool IsCacheDirectory(string relative) => _directories.Contains(relative);
    internal bool IsCacheFile(string relative) => _files.Contains(relative);

    internal void RemoveAfterSourceVerification()
    {
        foreach (var relative in _files)
        {
            var path = ResolveAndCheck(relative);
            if (File.Exists(path)) File.Delete(path);
        }
        foreach (var relative in _directories)
        {
            var path = ResolveAndCheck(relative);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: false);
        }
    }

    private string ResolveAndCheck(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relative));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar, comparison))
            throw new LilithRuntimeException("A plugin bytecode cache escaped its root.");
        var current = path;
        while (true)
        {
            if ((File.Exists(current) || Directory.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new LilithRuntimeException("A plugin bytecode cache crossed a reparse point.");
            if (string.Equals(current, _root, comparison)) break;
            current = Path.GetDirectoryName(current)
                ?? throw new LilithRuntimeException("A plugin bytecode cache path was invalid.");
        }
        return path;
    }
}
