#import <FileProvider/FileProvider.h>
#import <Foundation/Foundation.h>

// Manager for the fixture domains: add, remove, root, and (version 2) signal, which asks the system to enumerate the
// working set's changes so it learns about items the provider added. The optional second argument picks the domain:
// "ready" (the default, fixture-12760a2e-d) or, from version 3, "writable" (fixture-permissions-w).
int main(int argc, const char **argv)
{
    @autoreleasepool
    {
        if (argc != 2 && argc != 3) return 2;
        NSString *verb = [NSString stringWithUTF8String:argv[1]];
        NSString *which = argc == 3 ? [NSString stringWithUTF8String:argv[2]] : @"ready";
        NSFileProviderDomain *domain;
        if ([which isEqualToString:@"ready"])
            domain = [[NSFileProviderDomain alloc] initWithIdentifier:@"fixture-12760a2e-d" displayName:@"FileCat Native Fixture 12760a2e D"];
        else if ([which isEqualToString:@"writable"])
            domain = [[NSFileProviderDomain alloc] initWithIdentifier:@"fixture-permissions-w" displayName:@"FileCat Permissions Fixture W"];
        else return 2;
        dispatch_semaphore_t finished = dispatch_semaphore_create(0);
        __block NSError *failure;
        void (^complete)(NSError *) = ^(NSError *error) {
            failure = error;
            dispatch_semaphore_signal(finished);
        };

        if ([verb isEqualToString:@"add"])
            [NSFileProviderManager addDomain:domain completionHandler:complete];
        else if ([verb isEqualToString:@"remove"])
            [NSFileProviderManager removeDomain:domain completionHandler:complete];
        else if ([verb isEqualToString:@"root"] || [verb isEqualToString:@"signal"])
        {
            NSFileProviderManager *manager = [NSFileProviderManager managerForDomain:domain];
            if (!manager) { fputs("The owned provider domain is unavailable.\n", stderr); return 2; }
            if ([verb isEqualToString:@"root"])
                [manager getUserVisibleURLForItemIdentifier:NSFileProviderRootContainerItemIdentifier completionHandler:^(NSURL *url, NSError *error) {
                    if (url && !error) puts(url.path.fileSystemRepresentation);
                    complete(error);
                }];
            else
                [manager signalEnumeratorForContainerItemIdentifier:NSFileProviderWorkingSetContainerItemIdentifier completionHandler:complete];
        }
        else return 2;

        NSDate *end = [NSDate dateWithTimeIntervalSinceNow:15];
        while (dispatch_semaphore_wait(finished, DISPATCH_TIME_NOW) != 0)
        {
            if (end.timeIntervalSinceNow <= 0) { fputs("File Provider call timed out.\n", stderr); return 3; }
            [[NSRunLoop currentRunLoop] runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.05]];
        }
        if (failure) { fprintf(stderr, "%s\n", failure.description.UTF8String); return 1; }
        puts("FILECAT_FIXTURE_OK");
        return 0;
    }
}
