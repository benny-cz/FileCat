using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Edit;

/// <summary>Parent-owned edit bytes, bound to one explicit upload to the originating server file.</summary>
internal interface IRetainedEditUpload : IArchiveRetainedFile
{
    void RequireRequest(JobRequest request);
}
