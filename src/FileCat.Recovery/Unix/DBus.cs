using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace FileCat.Recovery.Unix;

/// <summary>A D-Bus error reply: its name (org.freedesktop.UDisks2.Error.NotAuthorized, say) and message.</summary>
public sealed class DBusException(string name, string message) : IOException(message)
{
    public string Name { get; } = name;
}

/// <summary>A value to marshal: a string, a boolean, or a dictionary of strings to variants (a{sv}).</summary>
internal abstract record DBusValue
{
    public sealed record Str(string Value) : DBusValue;
    public sealed record Bool(bool Value) : DBusValue;
    public sealed record Dict(IReadOnlyList<(string Key, DBusValue Value)> Entries) : DBusValue;
}

/// <summary>A reply: its body's bytes (read with <see cref="DBusReader"/>) and the file descriptors that came with it.</summary>
internal sealed record DBusReply(byte[] Message, int BodyStart, string Signature, List<int> Fds)
{
    public DBusReader Body() => new(Message, BodyStart);
}

/// <summary>
/// A minimal D-Bus client (D-47): one connection to the system bus, authenticated as this user, making method calls with
/// strings, booleans, and a{sv} dictionaries, and taking replies with strings, integers, and file descriptors. That is
/// what asking UDisks2 for a drive needs; nothing else of D-Bus is spoken. Little-endian messages only.
/// </summary>
internal sealed class DBusConnection : IDisposable
{
    private const byte MethodCall = 1, MethodReturn = 2, Error = 3;
    /// <summary>Lets the service ask the user (polkit shows its dialog) instead of refusing at once.</summary>
    public const byte AllowInteractiveAuthorization = 0x4;

    private readonly Socket _socket;
    private uint _serial;

    private DBusConnection(Socket socket) => _socket = socket;

    public string UniqueName { get; private set; } = "";

    /// <summary>Connects to the system bus (DBUS_SYSTEM_BUS_ADDRESS, or its usual socket) and says hello.</summary>
    public static DBusConnection System()
    {
        string path = "/var/run/dbus/system_bus_socket";
        if (Environment.GetEnvironmentVariable("DBUS_SYSTEM_BUS_ADDRESS") is { } address)
            foreach (var part in address.Split(';')[0].Split(':', 2).Skip(1).SelectMany(p => p.Split(',')))
                if (part.StartsWith("path=", StringComparison.Ordinal)) path = part[5..];
        return Connect(path);
    }

    /// <summary>Connects to the bus listening at <paramref name="path"/> (a session bus in tests).</summary>
    public static DBusConnection Connect(string path)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(path));
            var bus = new DBusConnection(socket);
            bus.Authenticate();
            bus.UniqueName = bus.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "Hello", []).Body().String();
            return bus;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>SASL EXTERNAL: the bus knows who connects from the socket itself; file descriptors are asked for too.</summary>
    private void Authenticate()
    {
        string uid = UnixNative.UserId().ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        Send("\0AUTH EXTERNAL " + Convert.ToHexString(Encoding.ASCII.GetBytes(uid)).ToLowerInvariant() + "\r\n");
        if (!ReadLine().StartsWith("OK ", StringComparison.Ordinal)) throw new IOException("The system bus did not accept this user.");
        Send("NEGOTIATE_UNIX_FD\r\n");
        if (ReadLine() != "AGREE_UNIX_FD") throw new IOException("The system bus cannot pass file descriptors.");
        Send("BEGIN\r\n");
    }

    private void Send(string text) => _socket.Send(Encoding.ASCII.GetBytes(text));

    private string ReadLine()
    {
        var line = new StringBuilder();
        var one = new byte[1];
        while (line.Length < 512)
        {
            if (_socket.Receive(one) <= 0) throw new IOException("The system bus closed the connection.");
            if (one[0] == '\n') return line.ToString().TrimEnd('\r');
            line.Append((char)one[0]);
        }
        throw new IOException("The system bus answered with an overlong line.");
    }

    /// <summary>
    /// Calls a method and waits for its reply, however long (polkit may be asking the user); disposing the connection
    /// from another thread ends the wait. An error reply throws <see cref="DBusException"/>.
    /// </summary>
    public DBusReply Call(string destination, string path, string @interface, string member, IReadOnlyList<DBusValue> arguments, byte flags = 0)
    {
        uint serial = ++_serial;
        _socket.Send(MethodCallMessage(serial, destination, path, @interface, member, arguments, flags));
        while (true)
        {
            var reply = Receive();
            if (reply.ReplySerial != serial)
            {
                foreach (int fd in reply.Fds) UnixNative.Close(fd); // signals, and replies to nothing asked
                continue;
            }
            if (reply.Type == Error)
            {
                foreach (int fd in reply.Fds) UnixNative.Close(fd);
                string text = reply.Signature.StartsWith('s') ? new DBusReader(reply.Message, reply.BodyStart).String() : reply.ErrorName;
                throw new DBusException(reply.ErrorName, text);
            }
            if (reply.Type == MethodReturn) return new DBusReply(reply.Message, reply.BodyStart, reply.Signature, reply.Fds);
        }
    }

    /// <summary>A method call as it goes on the wire: the header (path, destination, interface, member, signature), then the body.</summary>
    internal static byte[] MethodCallMessage(uint serial, string destination, string path, string @interface, string member, IReadOnlyList<DBusValue> arguments, byte flags = 0)
    {
        var body = new DBusWriter();
        var signature = new StringBuilder();
        foreach (var argument in arguments)
        {
            signature.Append(Signature(argument));
            body.Value(argument);
        }
        var header = new DBusWriter();
        header.Byte((byte)'l');
        header.Byte(MethodCall);
        header.Byte(flags);
        header.Byte(1);
        header.UInt32((uint)body.Length);
        header.UInt32(serial);
        var fields = header.BeginArray(8);
        void Field(byte code, char type, string value)
        {
            header.Pad(8);
            header.Byte(code);
            header.Signature(type.ToString());
            if (type == 'g') header.Signature(value);
            else header.String(value);
        }
        Field(1, 'o', path);
        Field(6, 's', destination);
        Field(2, 's', @interface);
        Field(3, 's', member);
        if (signature.Length > 0) Field(8, 'g', signature.ToString());
        header.EndArray(fields);
        header.Pad(8);
        return [.. header.Bytes, .. body.Bytes];
    }

    private static string Signature(DBusValue value) => value switch
    {
        DBusValue.Str => "s",
        DBusValue.Bool => "b",
        DBusValue.Dict => "a{sv}",
        _ => throw new NotSupportedException(),
    };

    private sealed record Incoming(byte Type, uint ReplySerial, string ErrorName, string Signature, byte[] Message, int BodyStart, List<int> Fds);

    private Incoming Receive()
    {
        var fds = new List<int>();
        var fixedPart = new byte[16];
        Fill(fixedPart, fds);
        if (fixedPart[0] != (byte)'l') throw new IOException("The system bus sent a big-endian message, which FileCat does not read.");
        uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(fixedPart.AsSpan(4));
        uint fieldsLength = BinaryPrimitives.ReadUInt32LittleEndian(fixedPart.AsSpan(12));
        if (bodyLength > 64 * 1024 * 1024 || fieldsLength > 64 * 1024) throw new IOException("The system bus sent an oversized message.");
        int headerEnd = (int)((16 + fieldsLength + 7) / 8 * 8);
        var message = new byte[headerEnd + bodyLength];
        fixedPart.CopyTo(message, 0);
        Fill(message.AsSpan(16), fds);
        var reader = new DBusReader(message, 0) { Position = 16 };
        uint replySerial = 0;
        string errorName = "", signature = "";
        while (reader.Position < 16 + fieldsLength)
        {
            reader.Pad(8);
            byte code = reader.Byte();
            string type = reader.Signature();
            switch (type)
            {
                case "u":
                    uint u = reader.UInt32();
                    if (code == 5) replySerial = u;
                    break;
                case "s" or "o":
                    string s = reader.String();
                    if (code == 4) errorName = s;
                    break;
                case "g":
                    string g = reader.Signature();
                    if (code == 8) signature = g;
                    break;
                default:
                    throw new IOException($"The system bus sent a header field of type {type}, which FileCat does not read.");
            }
        }
        return new Incoming(fixedPart[1], replySerial, errorName, signature, message, headerEnd, fds);
    }

    private void Fill(Span<byte> buffer, List<int> fds)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            int n = UnixNative.Receive(_socket, buffer[done..], fds);
            if (n <= 0) throw new IOException("The system bus closed the connection.");
            done += n;
        }
    }

    public void Dispose() => _socket.Dispose();
}

/// <summary>Marshals values the D-Bus way: little-endian, each aligned to its size from the start of the message part.</summary>
internal sealed class DBusWriter
{
    private readonly List<byte> _bytes = [];

    public int Length => _bytes.Count;
    public byte[] Bytes => [.. _bytes];

    public void Pad(int align)
    {
        while (_bytes.Count % align != 0) _bytes.Add(0);
    }

    public void Byte(byte value) => _bytes.Add(value);

    public void UInt32(uint value)
    {
        Pad(4);
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        _bytes.AddRange(b.ToArray());
    }

    public void String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        UInt32((uint)bytes.Length);
        _bytes.AddRange(bytes);
        _bytes.Add(0);
    }

    public void Signature(string value)
    {
        _bytes.Add((byte)value.Length);
        _bytes.AddRange(Encoding.ASCII.GetBytes(value));
        _bytes.Add(0);
    }

    /// <summary>Starts an array: its length (filled in by <see cref="EndArray"/>) and the padding its first element needs.</summary>
    public (int LengthAt, int Start) BeginArray(int elementAlign)
    {
        Pad(4);
        int lengthAt = _bytes.Count;
        UInt32(0);
        Pad(elementAlign);
        return (lengthAt, _bytes.Count);
    }

    public void EndArray((int LengthAt, int Start) array)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, (uint)(_bytes.Count - array.Start));
        for (int i = 0; i < 4; i++) _bytes[array.LengthAt + i] = b[i];
    }

    public void Value(DBusValue value)
    {
        switch (value)
        {
            case DBusValue.Str s:
                String(s.Value);
                break;
            case DBusValue.Bool b:
                UInt32(b.Value ? 1u : 0u);
                break;
            case DBusValue.Dict d:
                var array = BeginArray(8);
                foreach (var (key, entry) in d.Entries)
                {
                    Pad(8);
                    String(key);
                    Signature(entry switch { DBusValue.Str => "s", DBusValue.Bool => "b", _ => throw new NotSupportedException() });
                    Value(entry);
                }
                EndArray(array);
                break;
        }
    }
}

/// <summary>Reads marshalled values from a message part, aligned from the start of the message.</summary>
internal sealed class DBusReader(byte[] message, int start)
{
    public int Position { get; set; } = start;

    public void Pad(int align) => Position = (Position + align - 1) / align * align;

    public byte Byte() => Position < message.Length ? message[Position++] : throw new IOException("A D-Bus message ended early.");

    public uint UInt32()
    {
        Pad(4);
        if (Position + 4 > message.Length) throw new IOException("A D-Bus message ended early.");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(Position));
        Position += 4;
        return value;
    }

    public string String()
    {
        int length = (int)UInt32();
        if (length < 0 || Position + length + 1 > message.Length) throw new IOException("A D-Bus message ended early.");
        string value = Encoding.UTF8.GetString(message, Position, length);
        Position += length + 1;
        return value;
    }

    public string Signature()
    {
        int length = Byte();
        if (Position + length + 1 > message.Length) throw new IOException("A D-Bus message ended early.");
        string value = Encoding.ASCII.GetString(message, Position, length);
        Position += length + 1;
        return value;
    }
}
