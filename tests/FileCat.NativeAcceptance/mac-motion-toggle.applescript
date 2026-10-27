-- Toggle the actual macOS Accessibility > Motion checkbox after verifying its
-- starting value. Run on the logged-in, unlocked desktop with Motion open.
on run argv
    if (count of argv) is not 1 then error "Expected read or starting value 0 or 1"
    set requested to item 1 of argv as text
    if requested is not "read" and requested is not "0" and requested is not "1" then error "Invalid request"
    tell application "System Events"
        if not (exists process "System Settings") then error "Open System Settings > Accessibility > Motion first"
        tell process "System Settings"
            set allElements to entire contents of window 1
            repeat with el in allElements
                try
                    if (role of el as text) is "AXCheckBox" and (name of el as text) is "Reduce motion" then
                        set prior to value of el as integer
                        if requested is "read" then return prior as text
                        set expectedValue to requested as integer
                        if prior is not expectedValue then error "Reduce motion had an unexpected starting value"
                        click el
                        delay 0.5
                        set currentValue to value of el as integer
                        if currentValue is prior then error "Reduce motion did not toggle"
                        return currentValue as text
                    end if
                on error errText
                    if errText contains "unexpected starting value" or errText contains "did not toggle" then error errText
                end try
            end repeat
            error "The native Reduce motion checkbox is unavailable"
        end tell
    end tell
end run
