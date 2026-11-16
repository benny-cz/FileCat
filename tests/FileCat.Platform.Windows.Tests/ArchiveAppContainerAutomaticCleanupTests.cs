using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory, MemberData(nameof(ArchiveNativeJobCases.Profiles), MemberType=typeof(ArchiveNativeJobCases))]
    public async Task Actual_native_jobs_automatically_clean_creation_and_update_artifacts(ArchiveWriteFormat format, int mode)
    {
        _ = Probe(); var ct = TestContext.Current.CancellationToken;
        string? assets=Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);string executable=Path.Combine(assets!,"FileCat.exe");
        string root=Directory.CreateTempSubdirectory("filecat-native-cleanup-job-").FullName;
        try
        {
            string source=Path.Combine(root,"input"),target=Path.Combine(root,"output.zip"),journals=Path.Combine(root,"journals");
            byte[] first="created bytes"u8.ToArray(),second="added bytes"u8.ToArray();File.WriteAllBytes(source,first);
            File.SetLastWriteTimeUtc(source,DateTime.UnixEpoch.AddSeconds(1700000000));
            File.WriteAllText(source+":Zone.Identifier","[ZoneTransfer]\r\nZoneId=3\r\n");
            var profile=ArchiveNativeJobCases.Profile(format,mode);
            var manager=new JobManager(new PortableFileOperations(),new ProviderRegistry(),journals,new([WindowsArchiveJobBackend.Create(format,executable)]));
            manager.DecisionRequested+=pending=>Assert.Fail(pending.Request.Message);
            var create=new ArchivePlan(target,null,[new(ArchiveChangeKind.AddFile,"first",source)],Profile:profile);
            var expected=Expected(create,null);
            using var password=mode==0?null:new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var created=manager.SubmitArchive(create,password);await Finished(created);
            var firstJournal=Assert.Single(Directory.GetFiles(journals));
            var creation=JournalRecovery.ReadArchiveTransactions(firstJournal);
            Assert.True(Assert.Single(creation.CreationCleanups).IsResolved);Assert.Equal(ArchiveTransactionPhase.Committed,Assert.Single(creation.Transactions).Phase);
            await Fresh(expected,"clean-created");
            ArchiveFileObservation baseline;
            using(var directory=WindowsArchiveDirectory.Open(root))
            using(var file=directory.OpenSource(Path.GetFileName(target)))baseline=((IArchiveRetainedFile)file).Observe(ct);
            Assert.Equal(3,ArchiveWindowsOriginZone.Parse(baseline.Origin).DeclaredZone);
            string added=Path.Combine(root,"added");File.WriteAllBytes(added,second);File.SetLastWriteTimeUtc(added,DateTime.UnixEpoch.AddSeconds(1700000001));
            var assessment=await Assessment(File.ReadAllBytes(target));
            var update=new ArchivePlan(target,ArchiveBaseline.Of(target),[new(ArchiveChangeKind.AddFile,"second",added)],Profile:profile){NativeBaseline=baseline,MoveSources=true};
            expected=Expected(update,assessment);
            using var updatePassword=mode==0?null:new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var updated=manager.SubmitArchive(update,updatePassword);await Finished(updated);Assert.False(File.Exists(added));
            var updateJournal=Assert.Single(Directory.GetFiles(journals).Except([firstJournal]));
            Assert.True(Assert.Single(JournalRecovery.ReadArchiveTransactions(updateJournal).ContentCleanups).IsResolved);
            Assert.Equal(new[]{0},updated.CompletedRootIndices);await Fresh(expected,"clean-updated");
            Assert.DoesNotContain(Directory.GetFiles(root),path=>Path.GetFileName(path).StartsWith(".filecat-archive-",StringComparison.Ordinal));
            Assert.Empty(JournalRecovery.Scan(journals));Assert.Equal(first,File.ReadAllBytes(source));
            var retainedManager=new JobManager(new PortableFileOperations(),new ProviderRegistry(),journals,
                new([WindowsArchiveJobBackend.Create(format,executable,retainArtifacts:true)]));
            string retainedTarget=Path.Combine(root,"retained.zip");
            using var retainedPassword=mode==0?null:new ArchiveJobPassword(ArchiveNativeJobCases.Password);
            var retained=retainedManager.SubmitArchive(new(retainedTarget,null,[new(ArchiveChangeKind.AddFile,"first",source)],Profile:profile),retainedPassword);
            await ArchiveNativeJobCases.Finished(retained);
            string recoveryJournal=Assert.Single(Directory.GetFiles(journals).Except([firstJournal,updateJournal]));
            var pending=Assert.Single(JournalRecovery.ReadArchiveTransactions(recoveryJournal).CreationCleanups);
            Assert.All(pending.Artifacts,artifact=>Assert.True(File.Exists(artifact.Path)));
            using(var cleanup=new ArchiveCreationCleanup(WindowsArchiveCreationCleanupRecovery.OpenCurrentLease(recoveryJournal,pending.Nonce,ct)))
            {Assert.True(cleanup.Execute(ct).IsResolved);cleanup.Finish("Actual native creation sidecars reconciled.");}
            Assert.All(pending.Artifacts,artifact=>Assert.False(File.Exists(artifact.Path)));
            Assert.Empty(JournalRecovery.Scan(journals));Assert.True(File.Exists(retainedTarget));

            ArchiveWriteManifest Expected(ArchivePlan plan,ArchiveAssessment? sourceAssessment)
            {
                var planning=new ArchiveJobPlanning(plan,ct);
                using var selected=ArchiveSelectionForest.Capture(planning.Sources,WindowsArchiveSelectionTree.Open,ct,planning.RetainedNameChars,planning.RetainedHeapBytes);
                return planning.Review(sourceAssessment,selected,selected.Inputs,ct).CreatePlan(false,false,ct).Expected;
            }
            async Task<ArchiveAssessment> Assessment(byte[] bytes)
            {
                using var key=mode==0?null:new ArchiveJobPassword(ArchiveNativeJobCases.Password);
                return await new ArchiveAssessmentClient((nonce,cpu)=>ArchiveAppContainer.StartAssessment(executable,nonce,cpu,2),protocolVersion:2)
                    .AssessAsync(new(profile,bytes.Length,Convert.ToHexString(SHA256.HashData(bytes))),()=>new MemoryStream(bytes,false),ct,key?.CreateGrant());
            }
            async Task Fresh(ArchiveWriteManifest manifest,string name)
            {
                byte[] bytes=File.ReadAllBytes(target);Assert.Equal(manifest.Digest,(await Assessment(bytes)).Manifest.Digest);
                ArchiveNativeJobCases.Export(profile,name,bytes,manifest,"FILECAT_ARCHIVE_CLEANUP_EXPORT_ROOT");
            }
            async Task Finished(Job job)
            {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMinutes(2));
                while(!job.State.IsFinished())await Task.Delay(10,deadline.Token);
                Assert.True(job.State==JobState.Completed,job.State+": "+job.Summary+" "+JobIssue.Summarize(job.Issues));
                Assert.Equal(0,job.FailedRootCount);
            }
        }
        finally{TestCleanup.DeleteTree(root);}
    }
}
