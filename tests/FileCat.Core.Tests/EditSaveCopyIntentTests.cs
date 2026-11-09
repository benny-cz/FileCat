using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Edit;

namespace FileCat.Core.Tests;

public sealed class EditSaveCopyIntentTests
{
    public static IEnumerable<object[]> Scenarios()=>new[]{"roundtrip","future-version","missing-field","duplicate-field","null-source",
        "bad-digest","empty-nonce","relative-destination","nonlocal-destination","same-leaf","wrong-leaf-nonce","invalid-utf8","unpaired-name","mutation-corpus"}
        .Select(value=>new object[]{value});
    [Theory,MemberData(nameof(Scenarios))]
    public void Saved_copy_intents_are_bounded_strict_readonly_evidence(string scenario)
    {
        var nonce=Guid.Parse("11111111-1111-1111-1111-111111111111");string digest=new('A',64);
        string prefix=".filecat-archive-"+nonce.ToString("N")+"-";
        var intent=new EditSaveCopyIntent(1,nonce,new string('2',32),digest,"capture-1","source-1",113,digest,digest,digest,
            1700000000,700,Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Path.GetTempPath(),"copy-intent"))),
            "parent-1","saved.txt",prefix+new string('a',32)+".stage",prefix+new string('b',32)+".manifest",prefix+new string('c',32)+".manifest");
        byte[] original=intent.Encode();
        if(scenario=="roundtrip"){Assert.Equal(intent,EditSaveCopyIntent.Decode(original));return;}
        if(scenario=="mutation-corpus")
        {
            var random=new Random(91108);
            for(int i=0;i<10000;i++)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();byte[] changed=(byte[])original.Clone();
                for(int j=0;j<1+i%4;j++)changed[random.Next(changed.Length)]=(byte)random.Next(256);
                if(i%3==0)Array.Resize(ref changed,random.Next(changed.Length));
                EditSaveCopyIntent decoded;
                try{decoded=EditSaveCopyIntent.Decode(changed);}catch(IOException){continue;}
                Assert.Equal(decoded,EditSaveCopyIntent.Decode(decoded.Encode()));
            }
            return;
        }
        var node=JsonNode.Parse(original)!.AsObject();
        switch(scenario)
        {
            case "future-version":node["Version"]=2;break;
            case "missing-field":node.Remove("SourceMetadata");break;
            case "null-source":node["SourceIdentity"]=null;break;
            case "bad-digest":node["Sha256"]=new string('Z',64);break;
            case "empty-nonce":node["Nonce"]=Guid.Empty;break;
            case "relative-destination":node["DestinationDirectory"]="relative";break;
            case "nonlocal-destination":node["DestinationDirectory"]=OperatingSystem.IsWindows()?"\\\\server\\share":"//server/share";break;
            case "same-leaf":node["TargetLeaf"]=intent.StageLeaf;break;
            case "wrong-leaf-nonce":node["StageLeaf"]=".filecat-archive-"+new string('3',32)+"-"+new string('a',32)+".stage";break;
        }
        byte[] bytes=scenario switch
        {
            "duplicate-field"=>Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("\"Version\":1","\"Version\":1,\"Version\":1",StringComparison.Ordinal)),
            "unpaired-name"=>Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(original).Replace("\"Version\"","\"\\uD800\"",StringComparison.Ordinal)),
            _=>Encoding.UTF8.GetBytes(node.ToJsonString())
        };
        if(scenario=="invalid-utf8")bytes[2]=0xff;
        Assert.ThrowsAny<IOException>(()=>EditSaveCopyIntent.Decode(bytes));
    }
}
