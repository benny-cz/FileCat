using System.Globalization;

namespace FileCat.Core.FileSystem;

public enum PrincipalKind { WindowsSid, UnixUser, UnixGroup }

/// <summary>A canonical identity. Names and account service availability never alter equality.</summary>
public readonly record struct Principal : IComparable<Principal>, IComparable
{
    public Principal(PrincipalKind kind, string id, string authority = "")
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(authority);
        Kind = kind;
        Authority = authority;
        if (kind == PrincipalKind.WindowsSid)
        {
            string[] parts = id.Split('-');
            if (parts.Length is < 3 or > 18 || !parts[0].Equals("S", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid SID.", nameof(id));
            for (int i = 1; i < parts.Length; i++)
                parts[i] = ulong.Parse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            Id = "S-" + string.Join('-', parts.AsSpan(1).ToArray());
        }
        else
        {
            if (kind is not (PrincipalKind.UnixUser or PrincipalKind.UnixGroup)) throw new ArgumentOutOfRangeException(nameof(kind));
            Id = uint.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }
    }

    public PrincipalKind Kind { get; }
    public string Id { get; }
    public string Authority { get; }
    public string DisplayId => Kind == PrincipalKind.WindowsSid && Id.StartsWith("S-1-5-21-", StringComparison.Ordinal) && Id.Count(c => c == '-') == 7
        ? "S-1-5-21-…-" + Id[(Id.LastIndexOf('-') + 1)..] : Id;

    public int CompareTo(Principal other)
    {
        int order = Kind.CompareTo(other.Kind);
        if (order != 0) return order;
        if (Kind == PrincipalKind.WindowsSid)
        {
            var a = Id.AsSpan(2);
            var b = other.Id.AsSpan(2);
            while (!a.IsEmpty && !b.IsEmpty)
            {
                int ai = a.IndexOf('-'), bi = b.IndexOf('-');
                var an = ai < 0 ? a : a[..ai];
                var bn = bi < 0 ? b : b[..bi];
                order = CompareNumber(an, bn);
                if (order != 0) return order;
                a = ai < 0 ? default : a[(ai + 1)..];
                b = bi < 0 ? default : b[(bi + 1)..];
            }
            order = a.IsEmpty.CompareTo(b.IsEmpty) * -1;
        }
        else order = CompareNumber(Id, other.Id);
        return order != 0 ? order : string.CompareOrdinal(Authority, other.Authority);
    }

    private static int CompareNumber(ReadOnlySpan<char> a, ReadOnlySpan<char> b) =>
        a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.SequenceCompareTo(b);

    public int CompareTo(object? obj) => obj is Principal other ? CompareTo(other) : throw new ArgumentException("Expected a principal.", nameof(obj));
}

public enum NameStatus { NotRequested, Pending, Resolved, NotFound, Unavailable }
public sealed record PrincipalName(NameStatus Status, string? Name = null, string? Type = null, string? Reason = null);
public enum OwnerReadStatus { Ok, Denied, Unsupported, Gone, Failed }
public readonly record struct OwnerRead(OwnerReadStatus Status, Principal? Principal = null, string? Reason = null);

public interface IOwnership
{
    bool IsSupported { get; }
    OwnerRead ReadOwner(string path);
    OwnerRead ReadGroup(string path);
    PrincipalName LookUp(Principal principal);
}

public sealed class NoOwnership : IOwnership
{
    public bool IsSupported => false;
    public OwnerRead ReadOwner(string path) => new(OwnerReadStatus.Unsupported, Reason: "Ownership is not available on this platform.");
    public OwnerRead ReadGroup(string path) => ReadOwner(path);
    public PrincipalName LookUp(Principal principal) => new(NameStatus.Unavailable, Reason: "Account lookup is not available on this platform.");
}

/// <summary>A fixed culture/name answer captured for one ordering operation.</summary>
public sealed class PrincipalOrderKey(Principal principal, PrincipalName name, CompareInfo comparison) : IComparable
{
    public Principal Principal { get; } = principal;
    private readonly string? _name = name.Status == NameStatus.Resolved ? name.Name : null;
    public int CompareTo(object? obj)
    {
        if (obj is not PrincipalOrderKey other) throw new ArgumentException("Expected an ownership key.", nameof(obj));
        if (_name is null != (other._name is null)) return _name is null ? 1 : -1;
        int order = _name is null ? 0 : comparison.Compare(_name, other._name, CompareOptions.IgnoreCase);
        return order != 0 ? order : Principal.CompareTo(other.Principal);
    }
}
