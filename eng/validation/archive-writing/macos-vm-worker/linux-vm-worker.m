/* FileCat-owned disposable Linux VM experiment, stream-only managed-worker validation, no host shares. */
#import <Foundation/Foundation.h>
#import <CoreFoundation/CoreFoundation.h>
#import <Virtualization/Virtualization.h>
#include <errno.h>
#include <fcntl.h>
#include <limits.h>
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/event.h>
#include <time.h>
#include <unistd.h>
static double monotonicNow(void)
{
    struct timespec value;
    return clock_gettime(CLOCK_MONOTONIC, &value) == 0 ? value.tv_sec + value.tv_nsec / 1000000000.0 : -1;
}
static void pump(void) { CFRunLoopRunInMode(kCFRunLoopDefaultMode, 0.01, true); }
static BOOL beforeDeadline(double began, double seconds)
{
    double now = monotonicNow();
    return began >= 0 && now >= began && now - began < seconds;
}
static BOOL pollParent(int monitor, pid_t parent, BOOL *exited, BOOL *exitEvent)
{
    struct kevent event;
    const struct timespec immediate = {0, 0};
    int count;
    do { count = kevent(monitor, NULL, 0, &event, 1, &immediate); } while (count < 0 && errno == EINTR);
    if (count < 0) return NO;
    if (count == 1) {
        if (event.ident != (uintptr_t)parent || event.filter != EVFILT_PROC || (event.flags & EV_ERROR) || !(event.fflags & NOTE_EXIT)) return NO;
        *exited = YES; *exitEvent = YES;
    }
    if (getppid() != parent) *exited = YES;
    return YES;
}
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
    if (argc != 5 || argv[1][0] != '/' || argv[2][0] != '/' ||
        (strcmp(argv[3], "512") && strcmp(argv[3], "1536"))) return 64;
    errno = 0; char *end = NULL;
    long parsed = strtol(argv[4], &end, 10);
    char canonical[32]; snprintf(canonical, sizeof(canonical), "%ld", parsed);
    if (errno || !end || *end || parsed <= 1 || parsed > INT_MAX || strcmp(canonical, argv[4])) return 64;
    pid_t parent = (pid_t)parsed;
    if (getuid() == 0 || geteuid() != getuid() || getppid() != parent) return 65;
    int monitor = kqueue();
    if (monitor < 0) return 65;
    struct kevent change, receipt;
    EV_SET(&change, parent, EVFILT_PROC, EV_ADD | EV_ENABLE | EV_ONESHOT | EV_RECEIPT, NOTE_EXIT, 0, NULL);
    const struct timespec immediate = {0, 0};
    if (fcntl(monitor, F_SETFD, FD_CLOEXEC) != 0 || kevent(monitor, &change, 1, &receipt, 1, &immediate) != 1 ||
        receipt.ident != (uintptr_t)parent || receipt.filter != EVFILT_PROC || !(receipt.flags & EV_ERROR) || receipt.data != 0 || getppid() != parent) {
        close(monitor); return 65;
    }
    fprintf(stderr, "FILECAT_PARENT_GUARD_READY pid=%d parent=%d\n", getpid(), parent); fflush(stderr);
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
        __block BOOL started = NO, startFailed = NO, forcedStop = NO, stopCompleted = NO;
        __block NSString *startError = @"", *stopError = @"";
        BOOL parentExited = NO, parentExitEvent = NO, monitorFailed = NO, stopRequested = NO;
        NSString *stopReason = @"none";
        VZVirtualMachine *machine = nil;
        double began = monotonicNow();
        monitorFailed = began < 0 || !pollParent(monitor, parent, &parentExited, &parentExitEvent);
        if (valid && !monitorFailed && !parentExited) {
            machine = [[VZVirtualMachine alloc] initWithConfiguration:config]; machine.delegate = delegate;
            [machine startWithCompletionHandler:^(NSError *failure) {
                started = failure == nil; startFailed = failure != nil;
                startError = failure.localizedDescription ?: @"";
            }];
            while (!delegate.done && !startFailed && !parentExited && !monitorFailed) {
                double now = monotonicNow();
                if (now < 0) { monitorFailed = YES; break; }
                if (now - began >= 120) { stopReason = @"wall"; break; }
                monitorFailed = !pollParent(monitor, parent, &parentExited, &parentExitEvent);
                if (!parentExited && !monitorFailed) pump();
            }
            if (!delegate.done && !startFailed) {
                stopReason = parentExited ? @"parent" : monitorFailed ? @"monitor" : @"wall";
                double stopBegan = monotonicNow();
                // A death while start is in flight must not issue an invalid state transition. Wait only within
                // this bounded stop window, then require an observed stopped state rather than inventing success.
                while (!machine.canStop && machine.state != VZVirtualMachineStateStopped && !startFailed && beforeDeadline(stopBegan, 5)) pump();
                if (machine.canStop) {
                    stopRequested = YES;
                    [machine stopWithCompletionHandler:^(NSError *failure) {
                        stopCompleted = YES; forcedStop = failure == nil; stopError = failure.localizedDescription ?: @"";
                    }];
                    while (!stopCompleted && beforeDeadline(stopBegan, 5)) pump();
                }
            }
        }
        if (parentExited && [stopReason isEqualToString:@"none"]) stopReason = @"parent";
        close(monitor);
        NSDictionary *facts = @{@"uid": @(getuid()), @"euid": @(geteuid()), @"pid": @(getpid()),
            @"valid": @(valid), @"started": @(started), @"guestStopped": @(delegate.done), @"forcedStop": @(forcedStop),
            @"state": @(machine.state), @"memoryBytes": @(config.memorySize), @"cpus": @(config.CPUCount),
            @"networkDevices": @(config.networkDevices.count), @"storageDevices": @(config.storageDevices.count),
            @"balloonDevices": @(config.memoryBalloonDevices.count), @"startError": startError, @"parent": @(parent),
            @"parentExited": @(parentExited), @"parentExitEvent": @(parentExitEvent), @"parentMonitorFailed": @(monitorFailed),
            @"stopReason": stopReason, @"stopRequested": @(stopRequested), @"stopCompleted": @(stopCompleted), @"stopError": stopError,
            @"validationError": error.localizedDescription ?: @"", @"guestError": delegate.failure};
        NSData *json = [NSJSONSerialization dataWithJSONObject:facts options:NSJSONWritingSortedKeys error:NULL];
        if (!json) return 70;
        fputs("FILECAT_HOST_FACTS ", stderr); fwrite(json.bytes, 1, json.length, stderr); fputc('\n', stderr); fflush(stderr);
        if (parentExited) return !monitorFailed && machine.state == VZVirtualMachineStateStopped && !stopError.length ? 69 : 1;
        return valid && started && delegate.done && !forcedStop && !delegate.failure.length && !monitorFailed ? 0 : 1;
    }
}
