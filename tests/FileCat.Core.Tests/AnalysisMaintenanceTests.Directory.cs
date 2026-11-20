using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisMaintenanceTests
{
    private sealed class Node(byte[] bytes)
    {
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal byte[] Bytes = bytes;
        internal int Readers;
        internal bool Exclusive;
    }
    private sealed class Directory : AnalysisPrivateDirectory
    {
        internal readonly Dictionary<string, Node> Files = new(StringComparer.Ordinal);
        internal Action<string, string>? BeforeMove, AfterMove;
        internal Action<string, string>? BeforeReplace, AfterReplace;
        internal Action<string>? BeforePublish;
        internal Action<string>? BeforeReplacementOpen;
        internal Action<string>? BeforeRemove, AfterRemove;
        internal Action<string>? BeforeRead;
        internal int LargestRead;
        internal bool LowSpace;
        internal int Publications, Leases;
        internal override void Verify() { }
        internal override (UInt128 Capacity, UInt128 Available) Space() => (1UL << 40, LowSpace ? 0 : 1UL << 40);
        internal override AnalysisPrivateFile CreateStaging() => new File(this);
        internal override string[] Entries(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Files.Keys.ToArray(); }
        internal override AnalysisPrivateFile OpenRead(string name) => new File(this, name, false);
        internal override AnalysisPrivateFile OpenMaintenance(string name) { AnalysisPrivateNames.Maintenance(name); return new File(this, name, true); }
        internal override AnalysisPrivateFile OpenReplacementFile(string name) { AnalysisPrivateNames.ReplacementData(name); BeforeReplacementOpen?.Invoke(name); return new File(this, name, true); }
        public override void Dispose() { }
        private sealed class File : AnalysisPrivateFile
        {
            private readonly Directory _owner;
            private readonly MemoryStream _stream;
            private readonly Stream? _readerStream;
            private readonly Node? _node;
            private readonly bool _exclusive, _reader;
            private string? _name;
            private bool _closed, _removed, _capacity;
            private string? _publishedIdentity;
            internal File(Directory owner) { _owner = owner; _stream = new(); owner.Leases++; }
            internal File(Directory owner, string name, bool exclusive)
            {
                _owner = owner; _name = name; _exclusive = exclusive;
                _node = owner.Files.TryGetValue(name, out var node) ? node : throw new FileNotFoundException();
                _reader = !exclusive && AnalysisPrivateNames.NeedsReadLease(name);
                if ((exclusive || _reader) && (_node.Exclusive || exclusive && _node.Readers != 0)) throw new IOException("In use");
                if (exclusive) _node.Exclusive = true;
                if (_reader) _node.Readers++;
                _stream = new(_node.Bytes, writable: false); owner.Leases++;
                _readerStream = new ReadStream(_stream, count => { owner.LargestRead = Math.Max(owner.LargestRead, count); owner.BeforeRead?.Invoke(_name!); });
            }
            public override Stream Stream => _readerStream ?? _stream;
            internal override string Identity { get { Verify(); return _node!.Id; } }
            internal override string PublishedIdentity => _publishedIdentity ?? throw new InvalidOperationException("Not published");
            internal override string? MaintenanceName => _exclusive ? _name : null;
            internal override bool RemovalRequested => _removed;
            public override void Verify() => ObjectDisposedException.ThrowIf(_closed, this);
            public override void Flush() => Verify();
            public override void PublishNew(string name)
            {
                _owner.BeforePublish?.Invoke(name);
                Verify(); if (_node is not null || ! _owner.Files.TryAdd(name, new(_stream.ToArray()))) throw new IOException("Existing name");
                _publishedIdentity = _owner.Files[name].Id;
                _owner.Publications++;
            }
            internal override bool TryLockCapacity()
            {
                Verify(); if (_node!.Exclusive) return false; _node.Exclusive = _capacity = true; return true;
            }
            private void Exact()
            {
                Verify(); if (!_exclusive || _removed || !_owner.Files.TryGetValue(_name!, out var node) || !ReferenceEquals(node, _node))
                    throw new IOException("Not the held object");
            }
            internal override void MoveOwnedNew(string name)
            {
                Exact(); AnalysisPrivateNames.MaintenanceMove(_name!, name); _owner.BeforeMove?.Invoke(_name!, name);
                if (!_owner.Files.TryAdd(name, _node!)) throw new IOException("Destination exists");
                string old = _name!; _owner.Files.Remove(old); _name = name; _owner.AfterMove?.Invoke(old, name);
            }
            internal override void RemoveOwned()
            {
                Exact(); _owner.BeforeRemove?.Invoke(_name!); _owner.Files.Remove(_name!); _removed = true; _owner.AfterRemove?.Invoke(_name!);
            }
            internal override void ReplaceOwned(AnalysisPrivateFile destination)
            {
                if (destination is not File target || !ReferenceEquals(_owner, target._owner)) throw new ArgumentException("Wrong directory");
                Exact(); target.Exact(); AnalysisPrivateNames.ReplacementTarget(_name!, target._name!);
                _owner.BeforeReplace?.Invoke(_name!, target._name!);
                string old = _name!; _owner.Files[target._name!] = _node!; _owner.Files.Remove(old); _name = target._name; target._removed = true;
                _owner.AfterReplace?.Invoke(old, _name!);
            }
            public override void Dispose()
            {
                if (_closed) return; _closed = true;
                if (_reader) _node!.Readers--;
                if (_exclusive || _capacity) _node!.Exclusive = false;
                _owner.Leases--; _stream.Dispose();
            }
        }
        private sealed class ReadStream(Stream inner, Action<int> before) : Stream
        {
            public override bool CanRead => true;
            public override bool CanWrite => false;
            public override bool CanSeek => inner.CanSeek;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
            public override int Read(Span<byte> buffer) { before(buffer.Length); return inner.Read(buffer); }
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long length) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
