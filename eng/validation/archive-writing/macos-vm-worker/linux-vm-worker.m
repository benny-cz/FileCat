/* FileCat-owned disposable Linux VM experiment, stream-only managed-worker validation, no host shares. */
#import <Foundation/Foundation.h>
#import <Virtualization/Virtualization.h>
#include <stdio.h>
#include <string.h>
#include <unistd.h>
@interface FileCatProbeDelegate : NSObject <VZVirtualMachineDelegate>
@property BOOL done;
@property NSString *failure;
@end
@implementation FileCatProbeDelegate
- (void)guestDidStopVirtualMachine:(VZVirtualMachine *)machine { (void)machine; self.done = YES; }
- (void)virtualMachine:(VZVirtualMachine *)machine didStopWithError:(NSError *)error
{ (void)machine; self.failure = error.localizedDescription; self.done = YES; }
@end
int main(int argc, char **argv)
{
    if (argc != 4 || argv[1][0] != '/' || argv[2][0] != '/' ||
        (strcmp(argv[3], "512") && strcmp(argv[3], "1536"))) return 64;
    @autoreleasepool {
        VZVirtualMachineConfiguration *config = [VZVirtualMachineConfiguration new];
        VZLinuxBootLoader *loader = [[VZLinuxBootLoader alloc] initWithKernelURL:[NSURL fileURLWithPath:[NSString stringWithUTF8String:argv[1]]]];
        loader.initialRamdiskURL = [NSURL fileURLWithPath:[NSString stringWithUTF8String:argv[2]]];
        loader.commandLine = @"console=hvc0 quiet loglevel=3 rdinit=/init cgroup_no_v1=all cgroup_enable=memory panic=-1";
        config.bootLoader = loader; config.CPUCount = 1;
        config.memorySize = (strcmp(argv[3], "512") ? 1536ULL : 512ULL) * 1024 * 1024;
        VZVirtioConsoleDeviceSerialPortConfiguration *serial = [[VZVirtioConsoleDeviceSerialPortConfiguration alloc] init];
        serial.attachment = [[VZFileHandleSerialPortAttachment alloc] initWithFileHandleForReading:nil fileHandleForWriting:NSFileHandle.fileHandleWithStandardError];
        VZVirtioConsoleDeviceSerialPortConfiguration *protocol = [[VZVirtioConsoleDeviceSerialPortConfiguration alloc] init];
        protocol.attachment = [[VZFileHandleSerialPortAttachment alloc] initWithFileHandleForReading:NSFileHandle.fileHandleWithStandardInput fileHandleForWriting:NSFileHandle.fileHandleWithStandardOutput];
        config.serialPorts = @[serial, protocol];
        NSError *error = nil;
        BOOL valid = [config validateWithError:&error];
        FileCatProbeDelegate *delegate = [FileCatProbeDelegate new]; delegate.failure = @"";
        __block BOOL started = NO, startFailed = NO, forcedStop = NO;
        __block NSString *startError = @"";
        VZVirtualMachine *machine = nil;
        if (valid) {
            machine = [[VZVirtualMachine alloc] initWithConfiguration:config]; machine.delegate = delegate;
            [machine startWithCompletionHandler:^(NSError *failure) {
                started = failure == nil; startFailed = failure != nil;
                startError = failure.localizedDescription ?: @"";
            }];
            NSDate *deadline = [NSDate dateWithTimeIntervalSinceNow:120];
            while (!delegate.done && !startFailed && deadline.timeIntervalSinceNow > 0)
                [[NSRunLoop currentRunLoop] runMode:NSDefaultRunLoopMode beforeDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
            if (!delegate.done && !startFailed) {
                [machine stopWithCompletionHandler:^(NSError *failure) { forcedStop = failure == nil; delegate.done = YES; }];
                NSDate *stopDeadline = [NSDate dateWithTimeIntervalSinceNow:5];
                while (!delegate.done && stopDeadline.timeIntervalSinceNow > 0)
                    [[NSRunLoop currentRunLoop] runMode:NSDefaultRunLoopMode beforeDate:[NSDate dateWithTimeIntervalSinceNow:0.01]];
            }
        }
        NSDictionary *facts = @{@"uid": @(getuid()), @"euid": @(geteuid()), @"pid": @(getpid()),
            @"valid": @(valid), @"started": @(started), @"guestStopped": @(delegate.done), @"forcedStop": @(forcedStop),
            @"state": @(machine.state), @"memoryBytes": @(config.memorySize), @"cpus": @(config.CPUCount),
            @"networkDevices": @(config.networkDevices.count), @"storageDevices": @(config.storageDevices.count),
            @"balloonDevices": @(config.memoryBalloonDevices.count), @"startError": startError,
            @"validationError": error.localizedDescription ?: @"", @"guestError": delegate.failure};
        NSData *json = [NSJSONSerialization dataWithJSONObject:facts options:NSJSONWritingSortedKeys error:NULL];
        if (!json) return 70;
        fputs("FILECAT_HOST_FACTS ", stderr); fwrite(json.bytes, 1, json.length, stderr); fputc('\n', stderr); fflush(stderr);
        return valid && started && delegate.done && !forcedStop && !delegate.failure.length ? 0 : 1;
    }
}
