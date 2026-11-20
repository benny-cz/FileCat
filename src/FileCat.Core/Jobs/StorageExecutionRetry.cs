using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

/// <summary>Only pre-call ownership/preparation may retry. No admitted mutation or uncertain result enters here.</summary>
internal static class StorageExecutionRetry
{
    internal static T Run<T>(Job job, string path, Func<T> prepare)
    {
        int attempts = 0;
        while (true)
        {
            job.Checkpoint();
            try { return prepare(); }
            catch (IOException error) when (IsSharing(error))
            {
                if (attempts++ < 2) { job.Token.WaitHandle.WaitOne(40); continue; }
                var decision = job.Ask(new ErrorRequest("Storage change is blocked", "Another open owner is blocking this storage change. Retry, skip this item, or stop the job. Completed changes are kept.",
                    path, true, "storage-" + job.Request.StorageTransform!.Action + "-sharing"));
                job.Checkpoint();
                if (decision.Action == DecisionAction.Retry) { attempts = 0; continue; }
                throw new StorageInspectionException(new(StorageReasonKind.TransientFailure, "sharing-skipped", "This item was skipped after a sharing conflict.", false));
            }
        }
    }
    internal static bool IsSharing(IOException error) => (error.HResult & 0xffff) is 32 or 33 ||
        error is StorageInspectionException { Problem.Code: "sharing-violation" };
}
