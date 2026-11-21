#import <FileProvider/FileProvider.h>
#import <Foundation/Foundation.h>
#import <UniformTypeIdentifiers/UniformTypeIdentifiers.h>
#import <os/log.h>

// Version 2 of the "ready" fixture provider for domain fixture-12760a2e-d. It keeps the version 1 items and adds a
// folder and a package that nothing has enumerated, reported through a sync-anchor change, so they stay dataless.
static NSString *const FileID = @"fixture-file";
static NSString *const FolderID = @"fixture-folder";
static NSString *const ChildID = @"fixture-child";
static NSString *const UnvisitedID = @"fixture-unvisited-folder";
static NSString *const UnvisitedChildID = @"fixture-unvisited-child";
static NSString *const PackageID = @"fixture-unvisited-package";
static NSString *const PackageChildID = @"fixture-unvisited-package-child";
static NSString *const DomainName = @"FileCat Native Fixture 12760a2e D";
static NSString *const Payload = @"FileCat local File Provider fixture\n";

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

@interface FCFixtureProvider : NSObject <NSFileProviderReplicatedExtension>
@end

@implementation FCFixtureProvider
- (instancetype)initWithDomain:(NSFileProviderDomain *)domain
{
    Record(@"init", domain.identifier);
    return [super init];
}
- (void)invalidate {}
- (id<NSFileProviderEnumerator>)enumeratorForContainerItemIdentifier:(NSFileProviderItemIdentifier)identifier request:(NSFileProviderRequest *)request error:(NSError **)error
{
    FCFixtureEnumerator *enumerator = [FCFixtureEnumerator new];
    enumerator.identifier = identifier;
    return enumerator;
}
- (NSProgress *)itemForIdentifier:(NSFileProviderItemIdentifier)identifier request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSFileProviderItem, NSError *))completion
{
    if ([Known() containsObject:identifier]) completion([[FCFixtureItem alloc] initWithIdentifier:identifier], nil);
    else completion(nil, Missing());
    return [NSProgress progressWithTotalUnitCount:0];
}
- (NSProgress *)fetchContentsForItemWithIdentifier:(NSFileProviderItemIdentifier)identifier version:(NSFileProviderItemVersion *)version request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSURL *, NSFileProviderItem, NSError *))completion
{
    Record(@"fetch", identifier);
    if ([Known() containsObject:identifier] && !IsContainer(identifier))
    {
        NSURL *base = [NSURL fileURLWithPath:NSTemporaryDirectory() isDirectory:YES];
        NSURL *file = [base URLByAppendingPathComponent:[NSUUID UUID].UUIDString];
        NSError *error = nil;
        if ([Payload writeToURL:file atomically:YES encoding:NSUTF8StringEncoding error:&error])
            completion(file, [[FCFixtureItem alloc] initWithIdentifier:identifier], nil);
        else completion(nil, nil, error);
    }
    else completion(nil, nil, Missing());
    return [NSProgress progressWithTotalUnitCount:0];
}
- (NSProgress *)createItemBasedOnTemplate:(NSFileProviderItem)item fields:(NSFileProviderItemFields)fields contents:(NSURL *)url options:(NSFileProviderCreateItemOptions)options request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSFileProviderItem, NSFileProviderItemFields, BOOL, NSError *))completion
{
    completion(nil, fields, NO, ReadOnly());
    return [NSProgress progressWithTotalUnitCount:0];
}
- (NSProgress *)modifyItem:(NSFileProviderItem)item baseVersion:(NSFileProviderItemVersion *)version changedFields:(NSFileProviderItemFields)fields contents:(NSURL *)url options:(NSFileProviderModifyItemOptions)options request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSFileProviderItem, NSFileProviderItemFields, BOOL, NSError *))completion
{
    completion(nil, fields, NO, ReadOnly());
    return [NSProgress progressWithTotalUnitCount:0];
}
- (NSProgress *)deleteItemWithIdentifier:(NSFileProviderItemIdentifier)identifier baseVersion:(NSFileProviderItemVersion *)version options:(NSFileProviderDeleteItemOptions)options request:(NSFileProviderRequest *)request completionHandler:(void (^)(NSError *))completion
{
    completion(ReadOnly());
    return [NSProgress progressWithTotalUnitCount:0];
}
@end

extern int NSExtensionMain(int argc, const char **argv);
int main(int argc, const char **argv) { Record(@"launch", @"fixture-12760a2e-d"); return NSExtensionMain(argc, argv); }
