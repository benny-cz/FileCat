-- Native macOS Copy review check against a running, isolated FileCat fixture.
-- Usage: osascript mac-ax-copy-review.applescript SOURCE DESTINATION REPORT.json
-- The logged-in desktop user must permit Accessibility access to the caller.

on findElement(wantedRole, wantedName, useLast)
    tell application "System Events"
        tell process "FileCat"
            set match to missing value
            set elementsList to entire contents of window 1
            repeat with candidate in elementsList
                try
                    set candidateRole to role of candidate as text
                    set candidateName to name of candidate as text
                    if candidateRole is wantedRole and candidateName is wantedName then
                        set match to candidate
                        if not useLast then exit repeat
                    end if
                end try
            end repeat
            if match is missing value then error "Missing " & wantedRole & ": " & wantedName
            return match
        end tell
    end tell
end findElement

on requireFixture(sourcePath, destinationPath)
    do shell script "/bin/test -f " & quoted form of (sourcePath & "/a.txt")
    do shell script "/bin/test -f " & quoted form of (sourcePath & "/b.txt")
    if (do shell script "/usr/bin/xxd -p " & quoted form of (sourcePath & "/a.txt")) is not "616c7068610a" then error "Unexpected a.txt fixture bytes"
    if (do shell script "/usr/bin/xxd -p " & quoted form of (sourcePath & "/b.txt")) is not "626574610a" then error "Unexpected b.txt fixture bytes"
    do shell script "/bin/test -d " & quoted form of destinationPath
    do shell script "/bin/test -z \"$(/bin/ls -A " & quoted form of destinationPath & ")\""
end requireFixture

on run argv
    if (count of argv) is not 3 then error "Usage: mac-ax-copy-review.applescript SOURCE DESTINATION REPORT.json"
    set sourcePath to item 1 of argv
    set destinationPath to item 2 of argv
    set reportPath to item 3 of argv
    my requireFixture(sourcePath, destinationPath)

    tell application "System Events"
        tell process "FileCat"
            my findElement("AXList", "Panel 1, source, " & sourcePath, false)
            my findElement("AXList", "Panel 2, target, " & destinationPath, false)
            set sourceRow to my findElement("AXRow", "a.txt", false)
            set focused of sourceRow to true
            if not (focused of sourceRow) then error "The source row did not receive focus"
            click (my findElement("AXButton", "Copy…", false))
            my findElement("AXStaticText", "Copy \"a.txt\" (6 bytes)", false)
            my findElement("AXStaticText", "→ into the folder " & destinationPath, false)
            my findElement("AXTextField", "Destination", false)
            click (my findElement("AXButton", "Cancel", false))
        end tell
    end tell
    do shell script "/bin/test -z \"$(/bin/ls -A " & quoted form of destinationPath & ")\""

    tell application "System Events"
        tell process "FileCat"
            click (my findElement("AXButton", "Copy…", false))
            my findElement("AXStaticText", "Copy \"a.txt\" (6 bytes)", false)
            my findElement("AXStaticText", "→ into the folder " & destinationPath, false)
            click (my findElement("AXButton", "Copy", true))
        end tell
    end tell

    set copiedPath to destinationPath & "/a.txt"
    repeat with attempt from 1 to 100
        try
            do shell script "/usr/bin/cmp -s " & quoted form of (sourcePath & "/a.txt") & " " & quoted form of copiedPath
            exit repeat
        on error
            if attempt is 100 then error "The approved copy did not preserve the selected file's bytes"
            delay 0.2
        end try
    end repeat
    do shell script "/bin/test ! -e " & quoted form of (destinationPath & "/b.txt")
    if (do shell script "/usr/bin/xxd -p " & quoted form of (sourcePath & "/a.txt")) is not "616c7068610a" then error "The source file changed"
    if (do shell script "/usr/bin/xxd -p " & quoted form of (sourcePath & "/b.txt")) is not "626574610a" then error "The unselected source changed"
    set resultJson to "{\"passed\":true,\"platform\":\"macOS\",\"focusedItem\":\"a.txt\",\"completeTargetReviewed\":true,\"cancelPreservedTarget\":true,\"selectedBytesPreserved\":true,\"unselectedFileExcluded\":true}"
    do shell script "/bin/echo " & quoted form of resultJson & " > " & quoted form of reportPath
    return resultJson
end run
