using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Recovery.Unix;

/// <summary>A D-Bus error reply: its name (org.freedesktop.UDisks2.Error.NotAuthorized, say) and message.</summary>
public sealed class DBusException(string name, string message) : IOException(message)
{
    internal DBusException(string name, string message, string sender) : this(name, message) => Sender = sender;
    public string Name { get; } = name;
    internal string Sender { get; } = "";
}

/// <summary>A value to marshal: a string, a boolean, or a dictionary of strings to variants (a{sv}).</summary>
internal abstract record DBusValue
{
    public sealed record Str(string Value) : DBusValue;
    public sealed record Bool(bool Value) : DBusValue;
    public sealed record Dict(IReadOnlyList<(string Key, DBusValue Value)> Entries) : DBusValue;
}

/// <summary>A reply: its body's bytes (read with <see cref="DBusReader"/>) and the file descriptors that came with it.</summary>
internal sealed record DBusReply(byte[] Message, int BodyStart, string Signature, string Sender, List<int> Fds)
{
    public DBusReader Body() => new(Message, BodyStart);
}

/// <summary>
/// A minimal synchronous D-Bus client for UDisks2 and the Linux permissions broker. Little-endian messages only.
/// </summary>
internal sealed class DBusConnection : IDisposable
{
    private const byte MethodCall = 1, MethodReturn = 2, Error = 3;
    /// <summary>Lets the service ask the user (polkit shows its dialog) instead of refusing at once.</summary>
    public const byte AllowInteractiveAuthorization = 0x4;

    private readonly Socket _socket;
    private readonly int _maximumBodyBytes;
    private uint _serial;

    private DBusConnection(Socket socket, int maximumBodyBytes)
    {
        _socket = socket;
        _maximumBodyBytes = maximumBodyBytes;
    }

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
    public static DBusConnection Connect(string path, int maximumBodyBytes = 64 * 1024 * 1024, CancellationToken cancellation = default)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var bus = new DBusConnection(socket, maximumBodyBytes);
        using var cancel = cancellation.Register(bus.Dispose);
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(path));
            bus.Authenticate();
            bus.UniqueName = bus.Call("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "Hello", []).Body().String();
            return bus;
        }
        catch
        {
            bus.Dispose();
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

    private void Send(string text) => SendBytes(Encoding.ASCII.GetBytes(text));

    private void SendBytes(ReadOnlySpan<byte> bytes, SafeFileHandle? descriptor = null)
    {
        int offset = descriptor is null ? 0 : UnixNative.SendWithFd(_socket, bytes, descriptor);
        while (offset < bytes.Length)
        {
            int written = _socket.Send(bytes[offset..]);
            if (written <= 0) throw new IOException("The system bus closed the connection.");
            offset += written;
        }
    }

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
        SendBytes(MethodCallMessage(serial, destination, path, @interface, member, arguments, flags));
        return AwaitReply(serial, null);
    }

    internal DBusReply CallRaw(string destination, string path, string @interface, string member, string signature, DBusWriter body,
        SafeFileHandle? descriptor = null, string? expectedSender = null)
    {
        uint serial = ++_serial;
        SendBytes(MethodCallMessage(serial, destination, path, @interface, member, signature, body, 0, descriptor is not null), descriptor);
        return AwaitReply(serial, expectedSender);
    }

    private DBusReply AwaitReply(uint serial, string? expectedSender)
    {
        while (true)
        {
            var reply = Receive();
            if (reply.ReplySerial != serial || reply.Type is not (Error or MethodReturn))
            {
                foreach (int fd in reply.Fds) UnixNative.Close(fd); // signals, and replies to nothing asked
                continue;
            }
            if (expectedSender is not null && reply.Sender != expectedSender &&
                !(reply.Type == Error && reply.Sender == "org.freedesktop.DBus"))
            {
                foreach (int fd in reply.Fds) UnixNative.Close(fd);
                throw new IOException("The D-Bus method reply did not come from the expected sender.");
            }
            if (reply.Type == Error)
            {
                foreach (int fd in reply.Fds) UnixNative.Close(fd);
                string text = reply.Signature.StartsWith('s') ? new DBusReader(reply.Message, reply.BodyStart).String() : reply.ErrorName;
                throw new DBusException(reply.ErrorName, text, reply.Sender);
            }
            return new DBusReply(reply.Message, reply.BodyStart, reply.Signature, reply.Sender, reply.Fds);
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
        return MethodCallMessage(serial, destination, path, @interface, member, signature.ToString(), body, flags, false);
    }

    private static byte[] MethodCallMessage(uint serial, string destination, string path, string @interface, string member,
        string signature, DBusWriter body, byte flags, bool hasDescriptor)
    {
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
        if (signature.Length > 0) Field(8, 'g', signature);
        if (hasDescriptor)
        {
            header.Pad(8);
            header.Byte(9); // UNIX_FDS header field
            header.Signature("u");
            header.UInt32(1);
        }
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

    private sealed record Incoming(byte Type, uint ReplySerial, string ErrorName, string Signature, string Sender,
        byte[] Message, int BodyStart, List<int> Fds);

    private Incoming Receive()
    {
        var fds = new List<int>();
        try { return ReceiveMessage(fds); }
        catch
        {
            foreach (int fd in fds) UnixNative.Close(fd);
            throw;
        }
    }

    private Incoming ReceiveMessage(List<int> fds)
    {
        var fixedPart = new byte[16];
        Fill(fixedPart, fds);
        if (fixedPart[0] != (byte)'l') throw new IOException("The system bus sent a big-endian message, which FileCat does not read.");
        uint bodyLength = BinaryPrimitives.ReadUInt32LittleEndian(fixedPart.AsSpan(4));
        uint fieldsLength = BinaryPrimitives.ReadUInt32LittleEndian(fixedPart.AsSpan(12));
        if (bodyLength > (uint)_maximumBodyBytes || fieldsLength > 64 * 1024) throw new IOException("The system bus sent an oversized message.");
        int headerEnd = (int)((16 + fieldsLength + 7) / 8 * 8);
        var message = new byte[headerEnd + bodyLength];
        fixedPart.CopyTo(message, 0);
        Fill(message.AsSpan(16), fds);
        int fieldsEnd = 16 + (int)fieldsLength;
        var reader = new DBusReader(message, 16, fieldsEnd);
        uint replySerial = 0, descriptorCount = 0;
        string errorName = "", signature = "", sender = "";
        while (reader.Position < fieldsEnd)
        {
            reader.Pad(8);
            byte code = reader.Byte();
            string type = reader.Signature();
            switch (type)
            {
                case "u":
                    uint u = reader.UInt32();
                    if (code == 5) replySerial = u;
                    if (code == 9) descriptorCount = u;
                    break;
                case "s" or "o":
                    string s = reader.String();
                    if (code == 4) errorName = s;
                    if (code == 7) sender = s;
                    break;
                case "g":
                    string g = reader.Signature();
                    if (code == 8) signature = g;
                    break;
                default:
                    throw new IOException($"The system bus sent a header field of type {type}, which FileCat does not read.");
            }
        }
        if (descriptorCount != (uint)fds.Count) throw new IOException("The D-Bus message's descriptor count did not match its payload.");
        return new Incoming(fixedPart[1], replySerial, errorName, signature, sender, message, headerEnd, fds);
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

    public void Dispose()
    {
        // A blocking recvmsg owns a SafeHandle reference. Shutdown releases it even when another thread cancels.
        try { _socket.Shutdown(SocketShutdown.Both); }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
        finally { _socket.Dispose(); }
    }
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

    public void UInt16(ushort value)
    {
        Pad(2);
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, value);
        _bytes.AddRange(b.ToArray());
    }

    public void UInt64(ulong value)
    {
        Pad(8);
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(b, value);
        _bytes.AddRange(b.ToArray());
    }

    public void ByteArray(ReadOnlySpan<byte> value)
    {
        UInt32((uint)value.Length);
        _bytes.AddRange(value.ToArray());
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
internal sealed class DBusReader(byte[] message, int start, int? limit = null)
{
    private readonly int _end = limit ?? message.Length;
    public int Position { get; set; } = start;
    public int Remaining => _end - Position;

    public void Pad(int align) => Position = (Position + align - 1) / align * align;

    public byte Byte() => Position < _end ? message[Position++] : throw new IOException("A D-Bus message ended early.");

    public uint UInt32()
    {
        Pad(4);
        if (Remaining < 4) throw new IOException("A D-Bus message ended early.");
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(Position));
        Position += 4;
        return value;
    }

    public ushort UInt16()
    {
        Pad(2);
        if (Remaining < 2) throw new IOException("A D-Bus message ended early.");
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(message.AsSpan(Position));
        Position += 2;
        return value;
    }

    public ulong UInt64()
    {
        Pad(8);
        if (Remaining < 8) throw new IOException("A D-Bus message ended early.");
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(message.AsSpan(Position));
        Position += 8;
        return value;
    }

    public ReadOnlySpan<byte> ByteArray(int maximum)
    {
        uint length = UInt32();
        if (length > (uint)maximum || Remaining < 0 || length > (uint)Remaining)
            throw new IOException("A D-Bus byte array was incomplete or exceeded its limit.");
        var data = message.AsSpan(Position, (int)length);
        Position += (int)length;
        return data;
    }

    public string String()
    {
        int length = (int)UInt32();
        if (length < 0 || length >= Remaining || message[Position + length] != 0) throw new IOException("A D-Bus message ended early.");
        string value = Encoding.UTF8.GetString(message, Position, length);
        Position += length + 1;
        return value;
    }

    public string Signature()
    {
        int length = Byte();
        if (length >= Remaining || message[Position + length] != 0) throw new IOException("A D-Bus message ended early.");
        string value = Encoding.ASCII.GetString(message, Position, length);
        Position += length + 1;
        return value;
    }
}
