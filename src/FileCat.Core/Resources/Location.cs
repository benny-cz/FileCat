using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileCat.Core.Resources;

/// <summary>Well-known provider schemes.</summary>
public static class Schemes
{
    public const string FileSystem = "fs";
    /// <summary>Drive/volume list ("This PC").</summary>
    public const string Computer = "computer";
    /// <summary>Share listing of a <c>\\server</c> root, which cannot be enumerated as a directory.</summary>
    public const string Network = "net";
    public const string Zip = "zip";
    /// <summary>Read-only archives other than ZIP: TAR family, 7z, RAR, single compressed files, disc images (P8).</summary>
    public const string Archive = "arc";
    public const string Registry = "reg";
    public const string ResultSet = "results";
    public const string Sftp = "sftp";
    public const string Ftp = "ftp";
    public const string Recovery = "recovery";
    /// <summary>A raw volume or disk as a recovery source (\\?\Volume{…}); only a container, never listed by itself (P10).</summary>
    public const string Device = "device";
    /// <summary>Phones, cameras, and players over MTP (Windows Portable Devices, P8).</summary>
    public const string Mtp = "mtp";
    /// <summary>A file's alternate data streams and extended attributes (D-55); the file is the container.</summary>
    public const string HiddenData = "hidden";
    /// <summary>A volume's change journal (D-56): its entries as rows; the path is the volume's root.</summary>
    public const string Journal = "usn";
}

/// <summary>
/// Identity of a navigable container. <see cref="Path"/> is the provider's raw locator and is never
/// case-folded or normalized here (PI-10); equality is exact and ordinal. Providers decide whether two
/// different locators alias the same resource.
/// </summary>
[JsonConverter(typeof(LocationJsonConverter))]
public sealed class Location : IEquatable<Location>
{
    public Location(string scheme, string path, Location? container = null, string? session = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(scheme);
        ArgumentNullException.ThrowIfNull(path);
        Scheme = scheme;
        Path = path;
        Container = container;
        Session = session;
    }

    public string Scheme { get; }

    /// <summary>Provider-specific raw locator.</summary>
    public string Path { get; }

    /// <summary>The containing resource for nested locations, e.g. the ZIP file of an archive folder.</summary>
    public Location? Container { get; }

    /// <summary>Provider session or view qualifier (result-set id, Registry view, connection profile).</summary>
    public string? Session { get; }

    public bool IsFileSystem => Scheme == Schemes.FileSystem;

    public static Location FileSystem(string path) => new(Schemes.FileSystem, path);

    public Location WithPath(string path) => new(Scheme, path, Container, Session);

    /// <summary>The outermost location in the container chain (e.g. the file-system file of an archive).</summary>
    public Location Root
    {
        get
        {
            var l = this;
            while (l.Container is not null) l = l.Container;
            return l;
        }
    }

    public bool Equals(Location? other) =>
        other is not null && (ReferenceEquals(this, other) ||
            string.Equals(Scheme, other.Scheme, StringComparison.Ordinal) &&
            string.Equals(Path, other.Path, StringComparison.Ordinal) &&
            string.Equals(Session, other.Session, StringComparison.Ordinal) &&
            Equals(Container, other.Container));

    public override bool Equals(object? obj) => Equals(obj as Location);

    public override int GetHashCode() => HashCode.Combine(Scheme, Path, Session, Container);

    public static bool operator ==(Location? a, Location? b) => a is null ? b is null : a.Equals(b);

    public static bool operator !=(Location? a, Location? b) => !(a == b);

    public string Serialize() => JsonSerializer.Serialize(this, LocationJsonConverter.Options);

    public static Location? Deserialize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonSerializer.Deserialize<Location>(text, LocationJsonConverter.Options); }
        catch (JsonException) { return null; }
    }

    public override string ToString() =>
        Container is null ? $"{Scheme}:{Path}" : $"{Container}!{Scheme}:{Path}";
}

/// <summary>Culture-invariant JSON form: {"s":scheme,"p":path,"c":container,"v":session}.</summary>
public sealed class LocationJsonConverter : JsonConverter<Location>
{
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public override Location? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Location object expected.");
        string? scheme = null, path = null, session = null;
        Location? container = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "s": scheme = reader.GetString(); break;
                case "p": path = reader.GetString(); break;
                case "v": session = reader.GetString(); break;
                case "c": container = Read(ref reader, typeToConvert, options); break;
                default: reader.Skip(); break;
            }
        }
        if (string.IsNullOrEmpty(scheme) || path is null) throw new JsonException("Incomplete location.");
        return new Location(scheme, path, container, session);
    }

    public override void Write(Utf8JsonWriter writer, Location value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("s", value.Scheme);
        writer.WriteString("p", value.Path);
        if (value.Session is not null) writer.WriteString("v", value.Session);
        if (value.Container is not null)
        {
            writer.WritePropertyName("c");
            Write(writer, value.Container, options);
        }
        writer.WriteEndObject();
    }
}
