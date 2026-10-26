using System.Text.Json;

namespace LilithRuntime;

// Read-only, offline preparation: only sealed package text and generated seed
// config are exported. This never opens a player profile or starts a service.
internal static class LilithIdentityTransitionExport
{
    internal static int Run(string[] arguments)
    {
        if (arguments.Length != 5 || arguments[0] is not ("identity-export" or "identity-export-v3" or "identity-export-v4" or "identity-export-v5" or "identity-export-v6" or "identity-export-v7" or "identity-export-v8" or "identity-export-v9")
            || arguments[1] != "--packages-root"
            || arguments[3] != "--runtime-root"
            || !Path.IsPathFullyQualified(arguments[2]) || !Path.IsPathFullyQualified(arguments[4]))
        {
            Console.Error.WriteLine("Use identity-export, identity-export-v3, identity-export-v4, identity-export-v5, identity-export-v6, identity-export-v7, identity-export-v8 or identity-export-v9 --packages-root <absolute path> --runtime-root <absolute path>.");
            return 2;
        }
        try
        {
            var export = arguments[0] switch
            {
                "identity-export-v9" => BuildV9(arguments[2], arguments[4]),
                "identity-export-v8" => BuildV8(arguments[2], arguments[4]),
                "identity-export-v7" => BuildV7(arguments[2], arguments[4]),
                "identity-export-v6" => BuildV6(arguments[2], arguments[4]),
                "identity-export-v5" => BuildV5(arguments[2], arguments[4]),
                "identity-export-v4" => BuildV4(arguments[2], arguments[4]),
                "identity-export-v3" => BuildV3(arguments[2], arguments[4]),
                _ => Build(arguments[2], arguments[4])
            };
            Console.WriteLine(JsonSerializer.Serialize(export));
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or ArgumentException or InvalidOperationException or JsonException)
        {
            Console.Error.WriteLine("The pinned identity export was refused; no profile was changed.");
            return 1;
        }
    }

    internal static object Build(string packagesRoot, string runtimeRoot)
        => BuildReviewedPair(packagesRoot, runtimeRoot, destinationVersion: 2);

    internal static object BuildV3(string packagesRoot, string runtimeRoot)
        => BuildReviewedPair(packagesRoot, runtimeRoot, destinationVersion: 3);

    internal static object BuildV4(string packagesRoot, string runtimeRoot)
        => BuildReviewedPair(packagesRoot, runtimeRoot, destinationVersion: 4);

    internal static object BuildV5(string packagesRoot, string runtimeRoot)
        => BuildReviewedPair(packagesRoot, runtimeRoot, destinationVersion: 5);

    internal static object BuildV6(string packagesRoot, string runtimeRoot)
        => BuildReviewedPair(packagesRoot, runtimeRoot, destinationVersion: 6);

    internal static object BuildV7(string packagesRoot, string runtimeRoot)
        => BuildReviewedPair(packagesRoot, runtimeRoot, destinationVersion: 7);

    internal static object BuildV8(string packagesRoot, string runtimeRoot)
        => BuildReviewedPair(packagesRoot, runtimeRoot, destinationVersion: 8);

    internal static object BuildV9(string packagesRoot, string runtimeRoot)
        => BuildReviewedPair(packagesRoot, runtimeRoot, destinationVersion: 9);

    private static object BuildReviewedPair(string packagesRoot, string runtimeRoot, int destinationVersion)
    {
        if (!Path.IsPathFullyQualified(packagesRoot) || !Path.IsPathFullyQualified(runtimeRoot))
            throw new ArgumentException("Identity export roots must be absolute.");
        packagesRoot = Path.GetFullPath(packagesRoot);
        runtimeRoot = Path.GetFullPath(runtimeRoot);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(packagesRoot);
        LilithRuntimeWorkspacePathGuard.EnsureSafe(runtimeRoot);
        var (oldPackage, newPackage) = destinationVersion switch
        {
            2 => (LilithIdentityPackageV1Loader.Load(Path.Combine(packagesRoot, "v1")),
                LilithIdentityPackageV2Loader.Load(Path.Combine(packagesRoot, "v2"))),
            3 => (LilithIdentityPackageV2Loader.Load(Path.Combine(packagesRoot, "v2")),
                LilithIdentityPackageV3Loader.Load(Path.Combine(packagesRoot, "v3"))),
            4 => (LilithIdentityPackageV3Loader.Load(Path.Combine(packagesRoot, "v3")),
                LilithIdentityPackageV4Loader.Load(Path.Combine(packagesRoot, "v4"))),
            5 => (LilithIdentityPackageV4Loader.Load(Path.Combine(packagesRoot, "v4")),
                LilithIdentityPackageV5Loader.Load(Path.Combine(packagesRoot, "v5"))),
            6 => (LilithIdentityPackageV5Loader.Load(Path.Combine(packagesRoot, "v5")),
                LilithIdentityPackageV6Loader.Load(Path.Combine(packagesRoot, "v6"))),
            7 => (LilithIdentityPackageV6Loader.Load(Path.Combine(packagesRoot, "v6")),
                LilithIdentityPackageV7Loader.Load(Path.Combine(packagesRoot, "v7"))),
            8 => (LilithIdentityPackageV7Loader.Load(Path.Combine(packagesRoot, "v7")),
                LilithIdentityPackageV8Loader.Load(Path.Combine(packagesRoot, "v8"))),
            9 => (LilithIdentityPackageV8Loader.Load(Path.Combine(packagesRoot, "v8")),
                LilithIdentityPackageV9Loader.Load(Path.Combine(packagesRoot, "v9"))),
            _ => throw new ArgumentException("Only exact reviewed adjacent identity pairs may be exported.")
        };
        var options = LilithRuntimeOptions.CreateDefault() with
        {
            VoiceComponentRootPath = PinnedHermesVoiceLayout.ResolveDefaultRootPath(runtimeRoot)
        };
        return new
        {
            schemaVersion = 1,
            old = Descriptor(oldPackage),
            @new = Descriptor(newPackage),
            oldCanonicalConfig = LilithProfileProvisioner.BuildCanonicalForIdentityTransition(options, oldPackage),
            newCanonicalConfig = LilithProfileProvisioner.BuildCanonicalForIdentityTransition(options, newPackage)
        };
    }

    private static object Descriptor(LilithIdentityPackageV1 package) => new
    {
        packageId = package.PackageId,
        version = package.PackageVersion,
        fingerprint = package.IdentityFingerprintSha256,
        soulSha256 = package.SoulAssetSha256,
        personalitySha256 = package.HermesPersonalityContentSha256,
        soulContent = package.SoulContent,
        personalityContent = package.HermesPersonalityContent
    };
}
