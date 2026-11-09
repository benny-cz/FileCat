using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Archives;

internal static unsafe partial class WindowsArchiveNative
{
    internal const int StorageStreamCatalogueBytes=64*1024;
    /// <summary>Size observations only. Unknown named streams are counted, never admitted for publication/cleanup.</summary>
    internal static (long Data,long Auxiliary) MeasureStorageStreams(SafeFileHandle file,bool directory,CancellationToken ct)
    {
        byte[] buffer=new byte[StorageStreamCatalogueBytes];ct.ThrowIfCancellationRequested();
        fixed(byte* start=buffer)
        {
            IoStatus io=default;int status=NtQueryInformationFile(file,&io,start,(uint)buffer.Length,22);
            if(directory&&status==0&&io.Information==0)return(0,0);
            if(status!=0||io.Information<24||io.Information>buffer.Length)throw Invalid();
            int total=checked((int)io.Information),offset=0,count=0;long data=0,auxiliary=0;bool foundData=false;
            while(true)
            {
                ct.ThrowIfCancellationRequested();if(++count>1024||offset>total-24)throw Invalid();
                byte* entry=start+offset;uint next=*(uint*)entry,nameLength=*(uint*)(entry+4);
                long size=*(long*)(entry+8),allocated=*(long*)(entry+16);
                if((nameLength&1)!=0||nameLength>total-offset-24||size<0||allocated<0)throw Invalid();
                var name=new ReadOnlySpan<char>((char*)(entry+24),(int)nameLength/2);
                if(name.IsEmpty||name.SequenceEqual("::$DATA"))
                {if(foundData)throw Invalid();foundData=true;data=size;}
                else
                {if(size>long.MaxValue-auxiliary)throw Invalid();auxiliary+=size;}
                if(next==0)break;
                if((next&7)!=0||next<24+nameLength||next>total-offset-24)throw Invalid();
                offset+=checked((int)next);
            }
            if(!directory&&!foundData)throw Invalid();return(data,auxiliary);
        }
        static IOException Invalid()=>new("The complete stored edit stream sizes cannot be reviewed within their native budget. All files are kept.");
    }
}
