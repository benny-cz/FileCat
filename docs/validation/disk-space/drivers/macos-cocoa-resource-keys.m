#import <AppKit/AppKit.h>
#import <Carbon/Carbon.h>

int FileCatPostResourceKey(void *windowHandle, int selector)
{
    @autoreleasepool
    {
        if (![NSThread isMainThread] || !windowHandle || (selector != 0 && selector != 1))
            return 64;
        NSWindow *window = NSApp.keyWindow;
        if (!window || (windowHandle != (__bridge void *)window && windowHandle != (__bridge void *)window.contentView) ||
            ![window.title isEqualToString:@"Disk space — FileCat"])
            return 78;
        unsigned short key = selector == 0 ? kVK_Return : kVK_Escape;
        unichar character = selector == 0 ? '\r' : 0x1b;
        NSString *text = [NSString stringWithCharacters:&character length:1];
        double when = NSProcessInfo.processInfo.systemUptime;
        for (int i = 0; i < 2; i++)
        {
            NSEvent *event = [NSEvent keyEventWithType:(i == 0 ? NSEventTypeKeyDown : NSEventTypeKeyUp)
                location:NSZeroPoint modifierFlags:0 timestamp:when windowNumber:window.windowNumber context:nil
                characters:text charactersIgnoringModifiers:text isARepeat:NO keyCode:key];
            if (!event)
                return 70;
            [NSApp postEvent:event atStart:NO];
        }
        return 0;
    }
}
