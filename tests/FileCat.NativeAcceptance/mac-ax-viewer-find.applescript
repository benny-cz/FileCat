-- Native macOS text-viewer search check against a running FileCat fixture.
-- Usage: osascript mac-ax-viewer-find.applescript SOURCE REPORT.json
-- The source must contain a.txt with exactly "alpha" and a final newline.

on hasViewer()
    tell application "System Events"
        tell process "FileCat"
            repeat with currentWindow in windows
                if (name of currentWindow as text) contains "FileCat Viewer" then return true
            end repeat
        end tell
    end tell
    return false
end hasViewer

on findElement(wantedRole, wantedName, inViewer)
    repeat with attempt from 1 to 4
        tell application "System Events"
            tell process "FileCat"
                repeat with currentWindow in windows
                    set viewerWindow to (name of currentWindow as text) contains "FileCat Viewer"
                    if viewerWindow is inViewer then
                        set foundElement to missing value
                        set elementsList to entire contents of currentWindow
                        repeat with candidate in elementsList
                            try
                                set candidateRole to role of candidate as text
                                set candidateName to name of candidate as text
                                if candidateRole is wantedRole and candidateName is wantedName then
                                    set foundElement to candidate
                                    exit repeat
                                end if
                            end try
                        end repeat
                        if foundElement is not missing value then return foundElement
                    end if
                end repeat
            end tell
        end tell
        delay 0.2
    end repeat
    error "Missing " & wantedRole & ": " & wantedName
end findElement

on closeViewer()
    tell application "System Events"
        tell process "FileCat"
            repeat with currentWindow in windows
                if (name of currentWindow as text) contains "FileCat Viewer" then
                    set closeTarget to missing value
                    repeat with candidate in UI elements of currentWindow
                        try
                            if (description of candidate as text) is "close button" then set closeTarget to candidate
                        end try
                    end repeat
                    if closeTarget is missing value then error "Viewer close control absent"
                    click closeTarget
                    return
                end if
            end repeat
        end tell
    end tell
    error "Viewer window absent during close"
end closeViewer

on run argv
    if (count of argv) is not 2 then error "Usage: mac-ax-viewer-find.applescript SOURCE REPORT.json"
    set sourcePath to item 1 of argv
    set reportPath to item 2 of argv
    set selectedPath to sourcePath & "/a.txt"
    if (do shell script "/usr/bin/xxd -p " & quoted form of selectedPath) is not "616c7068610a" then error "Unexpected text fixture bytes"
    if my hasViewer() then error "A viewer was already open; use a fresh fixture"

    tell application "System Events"
        tell process "FileCat"
            my findElement("AXList", "Panel 1, source, " & sourcePath, false)
            set sourceRow to my findElement("AXRow", "a.txt", false)
            set focused of sourceRow to true
            if not (focused of sourceRow) then error "Source row did not receive focus"
            click (my findElement("AXButton", "View", false))
            my findElement("AXStaticText", "Mode: Text", true)
            click (my findElement("AXButton", "Show or hide viewer Find bar", true))
            set queryField to my findElement("AXTextField", "Find in file", true)
            click queryField
            -- AXValue may be missing value for an empty field. Use actual input
            -- to replace either a remembered query or that empty state.
            keystroke "a" using {command down}
            key code 51
            keystroke "alpha"
            if (value of queryField as text) is not "alpha" then error "The typed query was not accepted"
            my findElement("AXButton", "Find previous match", true)
            click (my findElement("AXButton", "Find next match", true))
            my findElement("AXStaticText", "1 match", true)
            click (my findElement("AXButton", "Find previous match", true))
        end tell
    end tell

    my closeViewer()
    repeat with attempt from 1 to 20
        if not my hasViewer() then exit repeat
        if attempt is 20 then error "Viewer did not close"
        delay 0.2
    end repeat
    if (do shell script "/usr/bin/xxd -p " & quoted form of selectedPath) is not "616c7068610a" then error "Search changed the source file"
    set resultJson to "{\"passed\":true,\"platform\":\"macOS\",\"mode\":\"Text\",\"query\":\"alpha\",\"visibleMatchStatus\":\"1 match\",\"previousAndNextExposed\":true,\"sourceUnchanged\":true}"
    do shell script "/bin/echo " & quoted form of resultJson & " > " & quoted form of reportPath
    return resultJson
end run
