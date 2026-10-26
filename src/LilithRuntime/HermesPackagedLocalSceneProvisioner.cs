using System.Security.Cryptography;
using System.Text.Json;

namespace LilithRuntime;

/// <summary>Bind published local-scene assets to this profile without changing tool opt-in.</summary>
internal static class HermesPackagedLocalSceneProvisioner
{
    internal static async Task<bool> TryProvisionAsync(LilithRuntimeOptions options, CancellationToken token,
        Action<string>? checkpoint = null)
    {
        try
        {
            var component = new DirectoryInfo(Path.GetFullPath(options.HermesComponentRootPath));
            var runtime = component.Parent?.Parent?.Parent?.FullName;
            if (runtime is null) return false;
            var assets = Path.Combine(runtime, "LocalSceneAssets", "v1");
            var template = Path.Combine(assets, "local-scene-package.json");
            if (!File.Exists(template)) return false;
            LilithRuntimeWorkspacePathGuard.EnsureSafe(template);
            if (new FileInfo(template).Length > 32768) return false;
            using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(template, token).ConfigureAwait(false));
            var value = doc.RootElement;
            if (value.GetProperty("schemaVersion").GetString() != "lilith-local-scene-assets/v1") return false;
            var python = Path.Combine(assets, "ocr", ".venv", "Scripts", "python.exe");
            LilithRuntimeWorkspacePathGuard.EnsureSafe(python);
            var pythonHash = await HashAsync(python, token).ConfigureAwait(false);
            if (!pythonHash.Equals(value.GetProperty("pythonSha256").GetString(), StringComparison.OrdinalIgnoreCase)) return false;
            var pins = value.GetProperty("files").Deserialize<Dictionary<string, string>>()!;
            if (pins is null || pins.Count is < 8 or > 100) return false;
            foreach (var (relative, digest) in pins)
            {
                if (Path.IsPathFullyQualified(relative) || relative.Split('/').Any(part => part is "" or "." or "..")
                    || relative.Contains('\\') || relative.Contains(':') || digest is not { Length: 64 } || !digest.All(Uri.IsHexDigit)
                    || !relative.StartsWith("models/", StringComparison.Ordinal) && !relative.StartsWith("ocr/models/", StringComparison.Ordinal)
                        && !relative.StartsWith("runtime/vulkan/", StringComparison.Ordinal)) return false;
                var path = Path.Combine(assets, relative.Replace('/', Path.DirectorySeparatorChar));
                LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
                if (!File.Exists(path)) return false;
            }
            var capture = Path.Combine(runtime, "LocalSceneCapture");
            var capturePins = new Dictionary<string, string>();
            foreach (var name in new[] { "LilithLocalSceneCapture.exe", "LilithLocalSceneCapture.dll",
                "LilithLocalSceneCapture.deps.json", "LilithLocalSceneCapture.runtimeconfig.json" })
            {
                var path = Path.Combine(capture, name);
                LilithRuntimeWorkspacePathGuard.EnsureSafe(path);
                capturePins[name] = await HashAsync(path, token).ConfigureAwait(false);
            }
            var root = Path.Combine(Path.GetFullPath(options.HermesHomePath), "tools", "dependencies", "local-scene-v1");
            var binding = Path.Combine(root, "runtime.json");
            var ownership = Path.Combine(root, "packaged-binding.sha256");
            LilithRuntimeWorkspacePathGuard.EnsureSafe(binding);
            LilithRuntimeWorkspacePathGuard.EnsureSafe(ownership);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schema = 1, asset_root = assets, python_sha256 = pythonHash, files = pins,
                capture = new { root = capture, files = capturePins }
            });
            return await PublishOwnedBindingAsync(root, binding, ownership, bytes, token, checkpoint).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            // This optional capability must not prevent ordinary chat startup.
            return false;
        }
    }

    private static async Task<bool> PublishOwnedBindingAsync(string root, string binding, string ownership,
        byte[] bytes, CancellationToken token, Action<string>? checkpoint)
    {
        const int MaximumBindingBytes = 64 * 1024;
        if (bytes.Length > MaximumBindingBytes) return false;
        if (File.Exists(binding) && !File.Exists(ownership)) return false;
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        token.ThrowIfCancellationRequested();
        var lockPath = Path.Combine(root, "packaged-binding.writer.lock");
        LilithRuntimeWorkspacePathGuard.EnsureSafe(lockPath);
        Directory.CreateDirectory(root);
        using var writerLease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (writerLease.Length != 0) return false;
        LilithRuntimeWorkspacePathGuard.EnsureSafe(binding);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(ownership);
        var exists = File.Exists(binding);
        checkpoint?.Invoke("before-binding-lock");
        token.ThrowIfCancellationRequested();
        // CreateNew cannot overwrite a file that appears after the absence check.
        // Existing bindings are compared and updated through one exclusive handle;
        // a non-cooperating writer cannot replace/edit the path between those steps.
        using var owner = new FileStream(ownership, exists ? FileMode.Open : FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (owner.Length > 128) return false;
        var ownerBytes = new byte[(int)owner.Length];
        owner.ReadExactly(ownerBytes);
        var expected = System.Text.Encoding.ASCII.GetString(ownerBytes).Trim();
        if (exists && (expected.Length != 64 || !expected.All(Uri.IsHexDigit))) return false;
        using var output = new FileStream(binding, exists ? FileMode.Open : FileMode.CreateNew,
            FileAccess.ReadWrite, FileShare.None);
        if (output.Length > MaximumBindingBytes) return false;
        if (exists)
        {
            var current = Convert.ToHexString(await SHA256.HashDataAsync(output, token).ConfigureAwait(false));
            if (!current.Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
            if (current.Equals(hash, StringComparison.OrdinalIgnoreCase)) return true;
        }
        checkpoint?.Invoke("binding-locked");
        // Only these two small generated files are written. Once writing begins,
        // cancellation cannot leave a deliberately truncated binding. A power/disk
        // failure can leave a hash mismatch; next startup preserves it and disables
        // this optional capability instead of adopting arbitrary modified bytes.
        output.Position = 0;
        await output.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
        output.SetLength(bytes.Length);
        output.Flush(flushToDisk: true);
        var digestBytes = System.Text.Encoding.ASCII.GetBytes(hash + "\n");
        owner.Position = 0;
        await owner.WriteAsync(digestBytes, CancellationToken.None).ConfigureAwait(false);
        owner.SetLength(digestBytes.Length);
        owner.Flush(flushToDisk: true);
        return true;
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)).ToLowerInvariant();
    }
}
