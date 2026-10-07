using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ContentSearchCancellationTests
{
    [Theory]
    [InlineData("bytes", true)]
    [InlineData("backward", true)]
    [InlineData("text", true)]
    [InlineData("bytes", false)]
    [InlineData("backward", false)]
    [InlineData("text", false)]
    public async Task Canceling_a_search_stops_after_its_actual_active_page_call(string mode, bool cancel)
    {
        string temp=Path.GetFullPath(Path.GetTempPath()),root=Path.Join(temp,"filecat-search-page-stop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var bytes=Encoding.UTF8.GetBytes(new string('x',4*PagedReader.PageSize));"owned match"u8.CopyTo(bytes.AsSpan(64));
        string path=Path.Join(root,"owned.txt");File.WriteAllBytes(path,bytes);
        var source=new HeldSource(path);using var reader=new PagedReader(source);using var cts=new CancellationTokenSource();
        Task<long>? work=null;Exception? error=null;long? found=null;
        try
        {
            work=Task.Run(()=>mode switch
            {
                "bytes"=>ContentSearch.FindBytes(reader,0,"owned match"u8,cts.Token),
                "backward"=>ContentSearch.FindBytesBackward(reader,bytes.Length,"owned match"u8,cts.Token),
                _=>ContentSearch.FindText(reader,new UTF8Encoding(false),0,"owned match",true,cts.Token),
            });
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10),TestContext.Current.CancellationToken);
            int readsWhileHeld=source.Reads;Assert.Equal(1,readsWhileHeld);
            if(cancel)cts.Cancel();
            Assert.Equal(0,source.Disposals);source.Release.Set();
            try { found=await work.WaitAsync(TimeSpan.FromSeconds(10),TestContext.Current.CancellationToken); } catch(Exception ex) { error=ex; }
            bool unchanged=SHA256.HashData(bytes).SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)));
            TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { mode,cancel,readsWhileHeld,ReadsAfterCompletion=source.Reads,Found=found,Error=error?.GetType().FullName,OwnedContentUnchanged=unchanged,source.Active,source.Disposals,source.DisposalsDuringRead,OwnedRealFileOnly=true,NativeDesktop=false }));
            Assert.True(unchanged);Assert.Equal(0,source.Active);Assert.Equal(0,source.DisposalsDuringRead);
            if(cancel) { Assert.IsAssignableFrom<OperationCanceledException>(error);Assert.Equal(1,source.Reads); }
            else { Assert.Null(error);Assert.Equal(64,found);Assert.Equal(4,source.Reads); }
        }
        finally
        {
            source.Release.Set();if(work is not null) { try { await work.WaitAsync(TimeSpan.FromSeconds(10)); } catch(OperationCanceledException){} }
            reader.Dispose();Assert.Equal(1,source.Disposals);source.Release.Dispose();
            Assert.Equal(temp.TrimEnd(Path.DirectorySeparatorChar),Path.GetDirectoryName(Path.GetFullPath(root)));Directory.Delete(root,recursive:true);
        }
    }

    private sealed class HeldSource(string path):IContentSource
    {
        private readonly FileContentSource _inner=new(path);private int _reads,_active,_disposals,_during;
        public readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public readonly ManualResetEventSlim Release=new();
        public int Reads=>Volatile.Read(ref _reads);public int Active=>Volatile.Read(ref _active);public int Disposals=>Volatile.Read(ref _disposals);public int DisposalsDuringRead=>Volatile.Read(ref _during);
        public string DisplayName=>_inner.DisplayName;public long Length=>_inner.Length;public bool CanSeek=>true;public string? LocalPath=>_inner.LocalPath;public ContentRevision? GetRevision()=>_inner.GetRevision();
        public int Read(long offset,Span<byte> buffer)
        {
            int read=Interlocked.Increment(ref _reads);Interlocked.Increment(ref _active);
            try { if(read==1) { Entered.TrySetResult();if(!Release.Wait(TimeSpan.FromSeconds(20)))throw new TimeoutException("Owned search page was not released."); }return _inner.Read(offset,buffer); }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Dispose() { if(Interlocked.Increment(ref _disposals)!=1)return;if(Active!=0)Interlocked.Increment(ref _during);_inner.Dispose(); }
    }
}
