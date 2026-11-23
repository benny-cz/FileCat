#import <FileProvider/FileProvider.h>
#import <Foundation/Foundation.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#import <os/log.h>

// Version 3 of the fixture provider, serving two domains.
// - fixture-12760a2e-d ("ready") keeps version 2 exactly: read-only dataless placeholders, and a folder and a package
//   that nothing has enumerated, reported through a sync-anchor change. FCFixtureItem and FCFixtureEnumerator are
//   version 2's, unchanged.
// - fixture-permissions-w ("writable") serves items an ordinary user may change: writable.txt, which a harness downloads
//   before it changes permissions; sentinel.bin, a placeholder nothing should fetch; and an empty folder. Its provider
//   accepts metadata changes, logs each one with its changed fields, and keeps each item's file-system flags and
//   metadata version in a state file, so a relaunched extension reports what it accepted.
static NSString *const FileID = @"fixture-file";
static NSString *const FolderID = @"fixture-folder";
static NSString *const ChildID = @"fixture-child";
static NSString *const UnvisitedID = @"fixture-unvisited-folder";
static NSString *const UnvisitedChildID = @"fixture-unvisited-child";
static NSString *const PackageID = @"fixture-unvisited-package";
static NSString *const PackageChildID = @"fixture-unvisited-package-child";
static NSString *const DomainName = @"FileCat Native Fixture 12760a2e D";
static NSString *const Payload = @"FileCat local File Provider fixture\n";

static NSString *const WritableDomain = @"fixture-permissions-w";
static NSString *const WritableName = @"FileCat Permissions Fixture W";
static NSString *const WritableFileID = @"w-file";
static NSString *const WritableSentinelID = @"w-sentinel";
static NSString *const WritableFolderID = @"w-folder";

static NSData *Anchor(NSString *value)
{
    return [value dataUsingEncoding:NSUTF8StringEncoding];
}

static NSError *Missing(void)
{
    return [NSError errorWithDomain:NSFileProviderErrorDomain code:NSFileProviderErrorNoSuchItem userInfo:nil];
}

static NSError *ReadOnly(void)
{
    return [NSError errorWithDomain:NSCocoaErrorDomain code:NSFileWriteNoPermissionError userInfo:nil];
}

static void Record(NSString *operation, NSString *identifier)
{
    os_log_t logger = os_log_create("org.filecat.validation.nativeprovider", "fixture");
    os_log(logger, "%{public}@ %{public}@", operation, identifier);
}

static BOOL IsContainer(NSString *identifier)
{
    return [@[FolderID, UnvisitedID, PackageID, NSFileProviderRootContainerItemIdentifier] containsObject:identifier];
}

static NSArray<NSString *> *Known(void)
{
    return @[FileID, FolderID, ChildID, UnvisitedID, UnvisitedChildID, PackageID, PackageChildID, NSFileProviderRootContainerItemIdentifier];
}

@interface FCFixtureItem : NSObject <NSFileProviderItem>
@property(nonatomic, copy, readonly) NSFileProviderItemIdentifier itemIdentifier;
- (instancetype)initWithIdentifier:(NSFileProviderItemIdentifier)identifier;
@end

@implementation FCFixtureItem
- (instancetype)initWithIdentifier:(NSFileProviderItemIdentifier)identifier
{
    if ((self = [super init])) _itemIdentifier = [identifier copy];
    return self;
}
- (NSFileProviderItemIdentifier)parentItemIdentifier
{
    if ([self.itemIdentifier isEqualToString:ChildID]) return FolderID;
    if ([self.itemIdentifier isEqualToString:UnvisitedChildID]) return UnvisitedID;
    if ([self.itemIdentifier isEqualToString:PackageChildID]) return PackageID;
    return NSFileProviderRootContainerItemIdentifier;
}
- (NSString *)filename
{
    NSDictionary<NSString *, NSString *> *names = @{
        FileID: @"dataless-file.bin", FolderID: @"dataless-folder", ChildID: @"child.bin",
        UnvisitedID: @"unvisited-folder", UnvisitedChildID: @"inner.bin",
        PackageID: @"unvisited.bundle", PackageChildID: @"payload.bin",
    };
    return names[self.itemIdentifier] ?: DomainName;
}
- (UTType *)contentType
{
    if ([self.itemIdentifier isEqualToString:PackageID]) return UTTypeBundle;
    return IsContainer(self.itemIdentifier) ? UTTypeFolder : UTTypeData;
}
- (NSNumber *)documentSize
{
    return IsContainer(self.itemIdentifier) ? nil : @([Payload lengthOfBytesUsingEncoding:NSUTF8StringEncoding]);
}
- (NSFileProviderItemCapabilities)capabilities
{
    return NSFileProviderItemCapabilitiesAllowsReading;
}
- (NSFileProviderItemVersion *)itemVersion
{
    return [[NSFileProviderItemVersion alloc] initWithContentVersion:Anchor(@"1") metadataVersion:Anchor(@"1")];
}
@end

@interface FCFixtureEnumerator : NSObject <NSFileProviderEnumerator>
@property(nonatomic, copy) NSString *identifier;
@end

@implementation FCFixtureEnumerator
- (void)invalidate {}
- (void)enumerateItemsForObserver:(id<NSFileProviderEnumerationObserver>)observer startingAtPage:(NSFileProviderPage)page
{
    Record(@"enumerate", self.identifier);
    NSArray<NSString *> *children = nil;
    if ([self.identifier isEqualToString:NSFileProviderRootContainerItemIdentifier]) children = @[FileID, FolderID, UnvisitedID, PackageID];
    else if ([self.identifier isEqualToString:FolderID]) children = @[ChildID];
    else if ([self.identifier isEqualToString:UnvisitedID]) children = @[UnvisitedChildID];
    else if ([self.identifier isEqualToString:PackageID]) children = @[PackageChildID];
    NSMutableArray *items = [NSMutableArray array];
    for (NSString *child in children ?: @[]) [items addObject:[[FCFixtureItem alloc] initWithIdentifier:child]];
    if (items.count > 0) [observer didEnumerateItems:items];
    [observer finishEnumeratingUpToPage:nil];
}
- (void)enumerateChangesForObserver:(id<NSFileProviderChangeObserver>)observer fromSyncAnchor:(NSFileProviderSyncAnchor)anchor
{
    NSString *from = [[NSString alloc] initWithData:anchor encoding:NSUTF8StringEncoding] ?: @"";
    Record([@"changes-from-" stringByAppendingString:from], self.identifier);
    // Version 1 domains are at anchor "1": announce the two containers that version 2 adds, never their contents.
    if ([from isEqualToString:@"1"] && ([self.identifier isEqualToString:NSFileProviderWorkingSetContainerItemIdentifier] ||
        [self.identifier isEqualToString:NSFileProviderRootContainerItemIdentifier]))
        [observer didUpdateItems:@[[[FCFixtureItem alloc] initWithIdentifier:UnvisitedID], [[FCFixtureItem alloc] initWithIdentifier:PackageID]]];
    [observer finishEnumeratingChangesUpToSyncAnchor:Anchor(@"2") moreComing:NO];
}
- (void)currentSyncAnchorWithCompletionHandler:(void (^)(NSFileProviderSyncAnchor))completionHandler
{
    completionHandler(Anchor(@"2"));
}
@end

// The writable domain's state: per item, the file-system flags the provider last accepted and a metadata version that
// grows with every accepted change.
static NSObject *StateLock(void)
{
    static NSObject *lock;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ lock = [NSObject new]; });
    return lock;
}

static NSURL *StateURL(void)
{
    NSURL *support = [[NSFileManager defaultManager] URLsForDirectory:NSApplicationSupportDirectory inDomains:NSUserDomainMask].firstObject;
    [[NSFileManager defaultManager] createDirectoryAtURL:support withIntermediateDirectories:YES attributes:nil error:nil];
    return [support URLByAppendingPathComponent:@"fixture-permissions-w.plist"];
}

static NSDictionary *StateFor(NSString *identifier)
{
    @synchronized (StateLock())
    {
        NSDictionary *all = [NSDictionary dictionaryWithContentsOfURL:StateURL()] ?: @{};
        return all[identifier];
    }
}

static void Accept(NSString *identifier, NSNumber *flags)
{
    @synchronized (StateLock())
    {
        NSMutableDictionary *all = [([NSDictionary dictionaryWithContentsOfURL:StateURL()] ?: @{}) mutableCopy];
        NSMutableDictionary *entry = [(all[identifier] ?: @{}) mutableCopy];
        if (flags) entry[@"flags"] = flags;
        entry[@"metadata"] = @([entry[@"metadata"] integerValue] + 1);
        all[identifier] = entry;
        NSError *error = nil;
        if (![all writeToURL:StateURL() error:&error]) Record([@"state-write-failed " stringByAppendingString:error.localizedDescription ?: @"?"], identifier);
    }
}

static BOOL IsWritableContainer(NSString *identifier)
{
    return [@[WritableFolderID, NSFileProviderRootContainerItemIdentifier] containsObject:identifier];
}

static NSArray<NSString *> *WritableKnown(void)
{
    return @[WritableFileID, WritableSentinelID, WritableFolderID, NSFileProviderRootContainerItemIdentifier];
}

@interface FCWritableItem : NSObject <NSFileProviderItem>
@property(nonatomic, copy, readonly) NSFileProviderItemIdentifier itemIdentifier;
- (instancetype)initWithIdentifier:(NSFileProviderItemIdentifier)identifier;
@end

@implementation FCWritableItem
- (instancetype)initWithIdentifier:(NSFileProviderItemIdentifier)identifier
{
    if ((self = [super init])) _itemIdentifier = [identifier copy];
    return self;
}
- (NSFileProviderItemIdentifier)parentItemIdentifier
{
    return NSFileProviderRootContainerItemIdentifier;
}
- (NSString *)filename
{
    NSDictionary<NSString *, NSString *> *names = @{
        WritableFileID: @"writable.txt", WritableSentinelID: @"sentinel.bin", WritableFolderID: @"writable-folder",
    };
    return names[self.itemIdentifier] ?: WritableName;
}
- (UTType *)contentType
{
    return IsWritableContainer(self.itemIdentifier) ? UTTypeFolder : UTTypeData;
}
- (NSNumber *)documentSize
{
    return IsWritableContainer(self.itemIdentifier) ? nil : @([Payload lengthOfBytesUsingEncoding:NSUTF8StringEncoding]);
}
- (NSFileProviderItemCapabilities)capabilities
{
    return NSFileProviderItemCapabilitiesAllowsReading | NSFileProviderItemCapabilitiesAllowsWriting |
        NSFileProviderItemCapabilitiesAllowsRenaming | NSFileProviderItemCapabilitiesAllowsReparenting |
        NSFileProviderItemCapabilitiesAllowsTrashing | NSFileProviderItemCapabilitiesAllowsDeleting;
}
- (NSFileProviderFileSystemFlags)fileSystemFlags
{
    NSNumber *accepted = StateFor(self.itemIdentifier)[@"flags"];
    if (accepted) return (NSFileProviderFileSystemFlags)accepted.unsignedIntegerValue;
    NSFileProviderFileSystemFlags flags = NSFileProviderFileSystemUserReadable | NSFileProviderFileSystemUserWritable;
    return IsWritableContainer(self.itemIdentifier) ? flags | NSFileProviderFileSystemUserExecutable : flags;
}
- (NSFileProviderItemVersion *)itemVersion
{
    NSInteger metadata = [StateFor(self.itemIdentifier)[@"metadata"] integerValue];
    return [[NSFileProviderItemVersion alloc] initWithContentVersion:Anchor(@"1")
        metadataVersion:Anchor([NSString stringWithFormat:@"m%ld", (long)metadata])];
}
@end

@interface FCWritableEnumerator : NSObject <NSFileProviderEnumerator>
@property(nonatomic, copy) NSString *identifier;
@end

@implementation FCWritableEnumerator
- (void)invalidate {}
- (void)enumerateItemsForObserver:(id<NSFileProviderEnumerationObserver>)observer startingAtPage:(NSFileProviderPage)page
{
    Record(@"enumerate", self.identifier);
    NSArray<NSString *> *children = [self.identifier isEqualToString:NSFileProviderRootContainerItemIdentifier]
        ? @[WritableFileID, WritableSentinelID, WritableFolderID] : @[];
    NSMutableArray *items = [NSMutableArray array];
    for (NSString *child in children) [items addObject:[[FCWritableItem alloc] initWithIdentifier:child]];
    if (items.count > 0) [observer didEnumerateItems:items];
    [observer finishEnumeratingUpToPage:nil];
}
- (void)enumerateChangesForObserver:(id<NSFileProviderChangeObserver>)observer fromSyncAnchor:(NSFileProviderSyncAnchor)anchor
{
    Record(@"changes", self.identifier);
    [observer finishEnumeratingChangesUpToSyncAnchor:anchor moreComing:NO];
}
- (void)currentSyncAnchorWithCompletionHandler:(void (^)(NSFileProviderSyncAnchor))completionHandler
{
    completionHandler(Anchor(@"w1"));
}
@end

@interface FCFixtureProvider : NSObject <NSFileProviderReplicatedExtension>
@property(nonatomic, readonly) BOOL writable;
@end

@implementation FCFixtureProvider
- (instancetype)initWithDomain:(NSFileProviderDomain *)domain
{
    Record(@"init", domain.identifier);
    if ((self = [super init])) _writable = [domain.identifier isEqualToString:WritableDomain];
    return self;
}
- (void)invalidate {}
- (id<NSFileProviderEnumerator>)enumeratorForContainerItemIdentifier:(NSFileProviderItemIdentifier)identifier request:(NSFileProviderRequest *)request error:(NSError **)error
{
    if (self.writable)
    {
        FCWritableEnumerator *enumerator = [FCWritableEnumerator new];
        enumerator.identifier = identifier;
        return enumerator;
    }
    FCFixtureEnumerator *enumerator = [FCFixtureEnumerator new];
    enumerator.identifier = identifier;
    return enumerator;
}
- (NSProgress *)itemForIdentifier:(NSFileProviderItemIdentifier)identifier request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSFileProviderItem, NSError *))completion
{
    if (self.writable)
    {
        if ([WritableKnown() containsObject:identifier]) completion([[FCWritableItem alloc] initWithIdentifier:identifier], nil);
        else completion(nil, Missing());
        return [NSProgress progressWithTotalUnitCount:0];
    }
    if ([Known() containsObject:identifier]) completion([[FCFixtureItem alloc] initWithIdentifier:identifier], nil);
    else completion(nil, Missing());
    return [NSProgress progressWithTotalUnitCount:0];
}
- (NSProgress *)fetchContentsForItemWithIdentifier:(NSFileProviderItemIdentifier)identifier version:(NSFileProviderItemVersion *)version request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSURL *, NSFileProviderItem, NSError *))completion
{
    Record(@"fetch", identifier);
    BOOL known = self.writable ? ([WritableKnown() containsObject:identifier] && !IsWritableContainer(identifier))
        : ([Known() containsObject:identifier] && !IsContainer(identifier));
    if (known)
    {
        NSURL *base = [NSURL fileURLWithPath:NSTemporaryDirectory() isDirectory:YES];
        NSURL *file = [base URLByAppendingPathComponent:[NSUUID UUID].UUIDString];
        NSError *error = nil;
        id<NSFileProviderItem> item = self.writable ? [[FCWritableItem alloc] initWithIdentifier:identifier]
            : [[FCFixtureItem alloc] initWithIdentifier:identifier];
        if ([Payload writeToURL:file atomically:YES encoding:NSUTF8StringEncoding error:&error]) completion(file, item, nil);
        else completion(nil, nil, error);
    }
    else completion(nil, nil, Missing());
    return [NSProgress progressWithTotalUnitCount:0];
}
- (NSProgress *)createItemBasedOnTemplate:(NSFileProviderItem)item fields:(NSFileProviderItemFields)fields contents:(NSURL *)url options:(NSFileProviderCreateItemOptions)options request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSFileProviderItem, NSFileProviderItemFields, BOOL, NSError *))completion
{
    if (self.writable) Record(@"create-refused", item.filename ?: @"?");
    completion(nil, fields, NO, ReadOnly());
    return [NSProgress progressWithTotalUnitCount:0];
}
- (NSProgress *)modifyItem:(NSFileProviderItem)item baseVersion:(NSFileProviderItemVersion *)version changedFields:(NSFileProviderItemFields)fields contents:(NSURL *)url options:(NSFileProviderModifyItemOptions)options request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSFileProviderItem, NSFileProviderItemFields, BOOL, NSError *))completion
{
    if (!self.writable)
    {
        completion(nil, fields, NO, ReadOnly());
        return [NSProgress progressWithTotalUnitCount:0];
    }
    NSString *identifier = item.itemIdentifier;
    NSNumber *flags = nil;
    if ((fields & NSFileProviderItemFileSystemFlags) && [item respondsToSelector:@selector(fileSystemFlags)]) flags = @(item.fileSystemFlags);
    Record([NSString stringWithFormat:@"modify fields=0x%lx flags=%@", (unsigned long)fields, flags ?: @"-"], identifier);
    if (![WritableKnown() containsObject:identifier])
    {
        completion(nil, fields, NO, Missing());
        return [NSProgress progressWithTotalUnitCount:0];
    }
    // Every changed field is accepted; content changes are acknowledged without keeping the new bytes.
    Accept(identifier, flags);
    completion([[FCWritableItem alloc] initWithIdentifier:identifier], 0, NO, nil);
    return [NSProgress progressWithTotalUnitCount:0];
}
- (NSProgress *)deleteItemWithIdentifier:(NSFileProviderItemIdentifier)identifier baseVersion:(NSFileProviderItemVersion *)version options:(NSFileProviderDeleteItemOptions)options request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSError *))completion
{
    if (self.writable) Record(@"delete-refused", identifier);
    completion(ReadOnly());
    return [NSProgress progressWithTotalUnitCount:0];
}
@end

extern int NSExtensionMain(int argc, const char **argv);
int main(int argc, const char **argv) { Record(@"launch", @"fixture-12760a2e-d"); return NSExtensionMain(argc, argv); }
