using System.Security.Cryptography;
using System.Text;
using System.Globalization;

namespace LilithControlCenter;

public sealed record D048ExpressionHistory(int Revision, string CreatedAtUtc, string Reason,
    string ContentSha256, int? RevertedFrom);
public sealed record D048ExpressionVersion(int Revision, string Content, string ContentSha256,
    string BaseIdentityFingerprint);
public sealed record D048ExpressionCatalog(int Revision, string Content, string ContentSha256,
    string BaseIdentityFingerprint, IReadOnlyList<D048ExpressionHistory> History);

public interface ID048ExpressionProjection
{
    Task<D048ExpressionCatalog> GetExpressionAsync(CancellationToken cancellationToken);
    Task<D048ExpressionVersion> GetExpressionVersionAsync(int revision, CancellationToken cancellationToken);
    Task<D048ExpressionCatalog> SaveExpressionAsync(string operationId, int expectedRevision,
        string content, string reason, CancellationToken cancellationToken);
    Task<D048ExpressionCatalog> RevertExpressionAsync(string operationId, int expectedRevision,
        int targetRevision, string reason, CancellationToken cancellationToken);
}

internal static class D048ExpressionLimits
{
    internal const int MaximumContentBytes = 32768, MaximumReasonBytes = 1024, MaximumRevisions = 256;
    internal const string BaseIdentity = "CDC1DD4F53596F2E0F214C1C9CE4474BEC97634CF3CBD8A10A026FB62318DBCF";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(content))).ToLowerInvariant();
    internal static void Text(string value, int limit, bool reason = false)
    {
        if (value is null || Utf8.GetByteCount(value) > limit || value.EnumerateRunes().Any(r =>
                Rune.GetUnicodeCategory(r) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate
                && r.Value is not (9 or 10 or 13))) throw new ArgumentException("Invalid expression text.");
    }
    internal static void Revision(int revision)
    { if (revision is < 0 or > MaximumRevisions) throw new ArgumentOutOfRangeException(nameof(revision)); }
    internal static void Operation(string operationId)
    {
        if (string.IsNullOrEmpty(operationId) || operationId.Length > 128
            || !char.IsAsciiLetterOrDigit(operationId[0])
            || operationId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or ':' or '-')))
            throw new ArgumentException("Invalid expression operation ID.");
    }
}

public sealed class D048ExpressionOperationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
