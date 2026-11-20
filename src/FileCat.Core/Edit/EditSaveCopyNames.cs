using System.Text;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Edit;

internal static class EditSaveCopyNames
{
    // A user-selected destination follows the native filesystem's name limits, not extraction portability rules.
    internal static void Validate(string value)
    {
        if(PathUtil.ValidateNewName(value) is { } problem)throw new ArgumentException(problem,nameof(value));
        if(value.Any(c=>c<32||c==127||c is '/' or '\\'))throw new ArgumentException("A single unambiguous saved-copy filename is required.",nameof(value));
        try
        {
            int bytes=ArchiveWritePolicy.Utf8.GetByteCount(value);
            if(!OperatingSystem.IsWindows()&&bytes>255)throw new ArgumentException("The saved-copy filename exceeds the Unix byte limit.",nameof(value));
        }
        catch(EncoderFallbackException error){throw new ArgumentException("The saved-copy filename has invalid Unicode.",nameof(value),error);}
    }
}
