using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed class EditSessionRecoveryRecordTests
{
    [Theory, InlineData("future-profile"), InlineData("invalid-encryption"), InlineData("missing-id"), InlineData("wrong-id"),
        InlineData("array"), InlineData("truncated"), InlineData("deep"), InlineData("oversized"), InlineData("invalid-utf8"),
        InlineData("null-member"), InlineData("null-baseline"), InlineData("null-working"), InlineData("null-remote"),
        InlineData("outside-working"), InlineData("relative-working"), InlineData("nul-working"), InlineData("traversal-working"),
        InlineData("folder-working"), InlineData("folder-separator-working"),
        InlineData("invalid-utf8-name"),InlineData("invalid-utf8-id"),InlineData("invalid-utf8-working"),
        InlineData("unpaired-high-id"),InlineData("unpaired-low-id"),InlineData("unpaired-name")]
    public void Unreadable_or_future_record_shapes_remain_visible_without_mutating_working_files(string shape)
    {
        using var root = new TempDir(); string id=Guid.NewGuid().ToString("N");
        string directory=Directory.CreateDirectory(Path.Combine(root.Path,id)).FullName;
        string working=Path.Combine(directory,"owned.txt"); File.WriteAllText(working,"keep the complete working bytes");
        var record=new EditSessionRecord { Id=id,WorkingPath=working,MemberPath="owned.txt",ArchivePath=Path.Combine(root.Path,"owned.zip") };
        var node=JsonSerializer.SerializeToNode(record)!.AsObject();
        switch(shape)
        {
            case "future-profile": node["ArchiveFormat"]="Rar5";node["ArchiveProfile"]=JsonNode.Parse("{\"Format\":\"Rar5\",\"Version\":99}");break;
            case "invalid-encryption": node["ArchiveFormat"]="SevenZip";node["ArchiveProfile"]=JsonNode.Parse("{\"Format\":\"SevenZip\",\"Version\":2,\"Encryption\":{\"Mode\":99}}");break;
            case "missing-id": node.Remove("Id");break;
            case "wrong-id": node["Id"]="different-folder";break;
            case "deep": JsonObject child=new();node["UnknownFutureShape"]=child;for(int i=0;i<40;i++){var next=new JsonObject();child["child"]=next;child=next;}break;
            case "oversized": node["UnknownFutureShape"]=new string('x',1024*1024);break;
            case "null-member": node["MemberPath"]=null;break;
            case "null-baseline": node["Baseline"]=null;break;
            case "null-working": node["WorkingPath"]=null;break;
            case "null-remote": node["RemotePath"]=null;break;
            case "outside-working": node["WorkingPath"]=Path.Combine(root.Path,"outside.txt");break;
            case "relative-working": node["WorkingPath"]="owned.txt";break;
            case "nul-working": node["WorkingPath"]=directory+Path.DirectorySeparatorChar+"bad\0.txt";break;
            case "traversal-working": node["WorkingPath"]=Path.Combine(directory,"..","outside.txt");break;
            // The session folder itself is the recovery fallback location, never a working file (found by native fuzzing).
            case "folder-working": node["WorkingPath"]=directory;break;
            case "folder-separator-working": node["WorkingPath"]=directory+Path.DirectorySeparatorChar;break;
        }
        byte[] bytes=shape switch
        {
            "array"=>"[]"u8.ToArray(),"truncated"=>"{\"Id\":"u8.ToArray(),"invalid-utf8"=>[0xff,0xfe,0xff],
            _=>Encoding.UTF8.GetBytes(node.ToJsonString()),
        };
        if(shape=="invalid-utf8-name")bytes[2]=0xff;
        if(shape is "invalid-utf8-id" or "invalid-utf8-working")
        {
            byte[] field=Encoding.UTF8.GetBytes(shape=="invalid-utf8-id"?"\"Id\":":"\"WorkingPath\":");
            int start=bytes.AsSpan().IndexOf(field)+field.Length;Assert.True(start>=field.Length);Assert.Equal((byte)'"',bytes[start]);bytes[start+1]=0xff;
        }
        if(shape is "unpaired-high-id" or "unpaired-low-id")
            bytes=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\""+id+"\"",shape=="unpaired-high-id"?"\"\\uD800\"":"\"\\uDC00\"",StringComparison.Ordinal));
        if(shape=="unpaired-name")bytes=Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"Id\":","\"\\uD800\":",StringComparison.Ordinal));
        string path=Path.Combine(directory,"session.json");File.WriteAllBytes(path,bytes);
        var store=new EditSessionStore(root.Path,new PortableFileOperations());var loaded=Assert.Single(store.LoadAll());
        Assert.False(loaded.IsSupported);Assert.NotEmpty(loaded.UnsupportedReason!);Assert.Equal(id,loaded.Id);
        Assert.Equal(directory,store.SessionDirectory(loaded));
        Assert.Throws<NotSupportedException>(()=>store.Save(loaded));Assert.Throws<NotSupportedException>(()=>store.Discard(loaded));
        Assert.Equal(bytes,File.ReadAllBytes(path));Assert.Equal("keep the complete working bytes",File.ReadAllText(working));
        Assert.True(loaded.WorkingPath==directory||loaded.WorkingPath==working);
    }

    [Theory,InlineData(false),InlineData(true)]
    public void An_unpublished_folder_does_not_appear_as_a_broken_session(bool hasWorkingFile)
    {
        using var root=new TempDir();string directory=Directory.CreateDirectory(Path.Combine(root.Path,Guid.NewGuid().ToString("N"))).FullName;
        if(hasWorkingFile)File.WriteAllText(Path.Combine(directory,"owned.txt"),"copy still being prepared");
        Assert.Empty(new EditSessionStore(root.Path,new PortableFileOperations()).LoadAll());
        Assert.True(Directory.Exists(directory));
    }
}
