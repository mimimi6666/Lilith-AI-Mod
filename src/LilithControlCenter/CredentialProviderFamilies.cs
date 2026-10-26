namespace LilithControlCenter;

// In-memory presentation only. Exact provider identities and endpoints come
// from the attested native catalog, never from a C# vendor/region lookup table.
internal sealed record CredentialProviderFamily(
    string Id, string DisplayName, IReadOnlyList<D054HermesCredentialProvider> Providers)
{
    public string StatusText => Providers.Count == 1
        ? ControlCenterLocalization.Current[Providers[0].Configured ? "ui.configured" : "ui.not.configured"]
        : ControlCenterLocalization.Current.Format("credential.region.configuredCount",
            Providers.Count(p => p.Configured), Providers.Count);

    internal static IReadOnlyList<CredentialProviderFamily> From(D054HermesCredentialCatalog catalog)
    {
        return catalog.Providers.GroupBy(p => p.FamilyId, StringComparer.Ordinal).Select(group =>
        {
            var providers = group.ToArray();
            if (providers.Select(p => p.FamilyName).Distinct(StringComparer.Ordinal).Count() != 1
                || providers.Select(p => p.Region?.VariantId ?? "default").Distinct(StringComparer.Ordinal).Count() != providers.Length)
                throw new System.IO.InvalidDataException("Hermes credential family metadata was inconsistent.");
            return new CredentialProviderFamily(group.Key, providers[0].FamilyName, Array.AsReadOnly(providers));
        }).ToArray();
    }
}
