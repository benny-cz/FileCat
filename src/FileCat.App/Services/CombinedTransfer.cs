using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.Core.State;

namespace FileCat.App.Services;

internal sealed record CombinedTransfer(AppearanceTransferV1 Appearance, WorkspaceTransfer Workspace)
{
    private const string Header = "FileCat-AppearanceWorkspace-";
    private const string Prefix = Header + "1 ";
    internal static readonly int MaxCharacters = Prefix.Length + TransferPackets.MaxCharacters;

    internal string Encode() => Prefix + new TransferPackets(Appearance.Encode(), Workspace.Encode()).Encode();

    internal static bool HasHeader(string? text) => text.AsSpan().TrimStart().StartsWith(Header, StringComparison.Ordinal);

    internal static CombinedTransfer? Decode(string? text)
    {
        if (text is null || text.Length > MaxCharacters) return null;
        text = text.Trim();
        if (!text.StartsWith(Prefix, StringComparison.Ordinal) ||
            TransferPackets.Decode(text[Prefix.Length..]) is not { } packets ||
            !AppearanceTransferV1.TryParse(packets.Appearance, ThemeManager.Names, out var appearance) ||
            appearance is null || WorkspaceTransfer.Decode(packets.Workspace) is not { } workspace)
            return null;
        return new CombinedTransfer(appearance, workspace);
    }
}

/// <summary>A single bounded record so an elevated launch can read both remembered values together.</summary>
internal sealed record TransferPackets(string? Appearance, string? Workspace)
{
    // A JSON escape may use six characters for one UTF-16 code unit from a packet.
    internal const int MaxCharacters = 6 * (AppearanceTransferV1.MaxCharacters + WorkspaceTransfer.MaxCharacters) + 64;
    private static readonly TransferPacketsJsonContext Json = new(new JsonSerializerOptions
    {
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 2,
    });

    internal string Encode() => JsonSerializer.Serialize(this, Json.TransferPackets);

    internal static TransferPackets? Decode(string text)
    {
        if (text.Length > MaxCharacters) return null;
        try
        {
            var packets = JsonSerializer.Deserialize(text, Json.TransferPackets);
            if (packets is null || packets.Appearance?.Length > AppearanceTransferV1.MaxCharacters ||
                packets.Workspace?.Length > WorkspaceTransfer.MaxCharacters)
                return null;
            return packets;
        }
        catch (JsonException) { return null; }
    }
}

[JsonSerializable(typeof(TransferPackets))]
internal sealed partial class TransferPacketsJsonContext : JsonSerializerContext;
