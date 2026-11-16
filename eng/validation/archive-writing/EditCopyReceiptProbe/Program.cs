using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

string path=Path.GetFullPath(args[0]);
var assembly=Assembly.LoadFrom(path);
var type=assembly.GetType("FileCat.Core.Edit.EditSaveCopyRecord",throwOnError:true)!;
var decode=(Decoder)type.GetMethod("Decode",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate(typeof(Decoder));
string hash=new('A',64);
var fields=new Dictionary<string,string>{["Version"]="1",["Nonce"]="\"11111111-1111-1111-1111-111111111111\"",["Sequence"]="1",
    ["Phase"]="\"prepared\"",["ParentIdentity"]="\"parent-1\"",["SourceIdentity"]="\"source-1\"",["StageLeaf"]="\"private.stage\"",
    ["TargetLeaf"]="\"notes.txt\"",["OutputIdentity"]="\"output-1\"",["Length"]="23",["Sha256"]='"'+hash+'"',
    ["MetadataDigest"]='"'+hash+'"',["OriginDigest"]='"'+hash+'"',["ModifiedSeconds"]="1700000000",["ModifiedNanoseconds"]="700",["PublishedMetadataDigest"]="null"};
string Render()=>"{"+string.Join(",",fields.Select(item=>JsonSerializer.Serialize(item.Key)+":"+item.Value))+"}";
byte[] original=Encoding.UTF8.GetBytes(Render());decode(original);
int accepted=0,rejected=0;int unexpectedCount=0;var unexpected=new List<object>();
void Check(string name,byte[] bytes)
{
    try{decode(bytes);accepted++;}
    catch(IOException){rejected++;}
    catch(Exception error){unexpectedCount++;if(unexpected.Count<8)unexpected.Add(new{name,type=error.GetType().FullName,message=error.Message});}
}
string[] tokens=["\"\\uD800\"","\"\\uDC00\"","\"\\uD800x\"","\"\\uDC00\\uD800\"","\"\\u0000\"","\"\\uFFFD\"","\"\"","null","0","{}","[]","true"];
foreach(string field in fields.Keys.ToArray())
{
    string before=fields[field];
    foreach(string token in tokens){fields[field]=token;Check(field+":"+token,Encoding.UTF8.GetBytes(Render()));}
    fields[field]=before;
}
var random=new Random(81108);
for(int i=0;i<10000;i++)
{
    byte[] bytes=(byte[])original.Clone();
    for(int j=0;j<1+i%4;j++)bytes[random.Next(bytes.Length)]=(byte)random.Next(256);
    if(i%3==0)Array.Resize(ref bytes,random.Next(bytes.Length));
    Check("mutation-"+i,bytes);
}
Check("oversized",new byte[65537]);
Console.WriteLine(JsonSerializer.Serialize(new{runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    assembly=path,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),accepted,rejected,unexpectedCount,unexpected}));
return unexpectedCount==0?0:1;
delegate object Decoder(ReadOnlySpan<byte> bytes);
