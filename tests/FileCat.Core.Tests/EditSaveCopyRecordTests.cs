using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Edit;

namespace FileCat.Core.Tests;

public sealed class EditSaveCopyRecordTests
{
    public static IEnumerable<object[]> Scenarios()=>new[]{"roundtrip","unknown-version","missing-field","duplicate-field","unknown-field",
        "null-digest","invalid-digest","invalid-identity","invalid-stage-leaf","invalid-target-leaf","same-source-output",
        "phase-sequence","missing-published-metadata","unexpected-published-metadata","length-overflow","subtick-time",
        "oversized","truncated","root-array","wrong-field-type","invalid-utf8-name","invalid-utf8-value",
        "unpaired-high-name","unpaired-low-name","mutation-corpus"}.Select(value=>new object[]{value});

    [Theory,MemberData(nameof(Scenarios))]
    public void Saved_copy_receipts_are_bounded_strict_evidence_without_mutation_authority(string scenario)
    {
        string digest=new('A',64);
        var record=new EditSaveCopyRecord(1,Guid.Parse("11111111-1111-1111-1111-111111111111"),1,"prepared","parent-1","source-1","private.stage","notes.txt",
            "output-1",23,digest,digest,digest,1700000000,700,null);
        byte[] bytes=record.Encode();var node=JsonNode.Parse(bytes)!.AsObject();
        if(scenario=="mutation-corpus")
        {
            var random=new Random(81108);
            for(int i=0;i<10000;i++)
            {
                TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                byte[] mutated=(byte[])bytes.Clone();
                for(int j=0;j<1+i%4;j++)mutated[random.Next(mutated.Length)]=(byte)random.Next(256);
                if(i%3==0)Array.Resize(ref mutated,random.Next(mutated.Length));
                EditSaveCopyRecord decoded;
                try{decoded=EditSaveCopyRecord.Decode(mutated);}
                catch(IOException){continue;} // Complete bounded refusal; any escaping runtime exception fails the case.
                Assert.Equal(decoded,EditSaveCopyRecord.Decode(decoded.Encode()));
            }
            return;
        }
        if(scenario=="roundtrip")
        {
            Assert.Equal(record,EditSaveCopyRecord.Decode(bytes));
            Assert.Equal(record with{Sequence=2,Phase="needs-review"},EditSaveCopyRecord.Decode(record.Outcome(null).Encode()));
            return;
        }
        switch(scenario)
        {
            case "unknown-version":node["Version"]=2;break;
            case "missing-field":node.Remove("ModifiedSeconds");break;
            case "unknown-field":node["AuthorityToDelete"]=true;break;
            case "null-digest":node["Sha256"]=null;break;
            case "invalid-digest":node["Sha256"]=new string('Z',64);break;
            case "invalid-identity":node["ParentIdentity"]="";break;
            case "invalid-stage-leaf":node["StageLeaf"]="../notes.txt";break;
            case "invalid-target-leaf":node["TargetLeaf"]="../notes.txt";break;
            case "same-source-output":node["SourceIdentity"]=record.OutputIdentity;break;
            case "phase-sequence":node["Sequence"]=2;break;
            case "missing-published-metadata":node["Sequence"]=2;node["Phase"]="published";break;
            case "unexpected-published-metadata":node["PublishedMetadataDigest"]=digest;break;
            case "length-overflow":node["Length"]=EditSessionStore.MaxMemberBytes+1;break;
            case "subtick-time":node["ModifiedNanoseconds"]=1;break;
            case "wrong-field-type":node["Version"]="1";break;
        }
        bytes=scenario switch
        {
            "duplicate-field"=>Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"Version\":1","\"Version\":1,\"Version\":1",StringComparison.Ordinal)),
            "oversized"=>new byte[EditSaveCopyRecord.MaximumBytes+1],
            "unpaired-high-name"=>Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"Version\"","\"\\uD800\"",StringComparison.Ordinal)),
            "unpaired-low-name"=>Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"Version\"","\"\\uDC00\"",StringComparison.Ordinal)),
            "truncated"=>bytes[..^1],"root-array"=>"[]"u8.ToArray(),_=>Encoding.UTF8.GetBytes(node.ToJsonString())
        };
        if(scenario=="invalid-utf8-name")bytes[2]=0xff;
        if(scenario=="invalid-utf8-value")bytes[bytes.AsSpan().IndexOf("parent-1"u8)]=0xff;
        Assert.ThrowsAny<IOException>(()=>EditSaveCopyRecord.Decode(bytes));
    }
}
