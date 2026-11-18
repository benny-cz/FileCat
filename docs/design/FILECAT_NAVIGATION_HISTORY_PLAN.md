# Back and Forward history

## Interaction

Right-click Back or Forward to open the active tab's history for that direction. The adjacent drop-down arrow opens the same menu. A primary click on the main button or the configured shortcut travels one visit. The defaults are Alt+Left and Alt+Right. The mouse's dedicated Back and Forward buttons travel one visit in the panel under the pointer. Both toolbar layouts offer these controls.

Show the nearest visit first, one row per visit. Pair the provider display path with a FileCat icon for the location type, including folders, volumes, network and remote sources, archives, and virtual locations. Fit long paths within the menu, expose the full path in the tooltip and accessibility name, and scroll menus that exceed the available height. Opening a menu reads the tab's cached history and picks icons by type without probing destinations. The two parts of a button stay together when a classic toolbar row wraps. An empty direction disables its button and drop-down and exposes the existing reason.

Click a row or use Up or Down followed by Enter to jump. Shift+F10 or the Menu key on a focused direction button opens its history. Keyboard opening highlights the nearest visit. Escape returns keyboard focus to the opener. Clicking elsewhere dismisses the popup. Activation returns focus to the destination panel while the menu owns arrow and activation keys until it closes.

This follows Open Salamander's directional drop-down and adds right-click on the main button. Salamander keeps a separate list of recently used directories. [Toolbar and direction routing](https://github.com/OpenSalamander/salamander/blob/a201fa3cc708c74dc1f4eea7032e0758840447a1/src/mainwnd3.cpp#L1613-L1634) · [Menu order and cursor movement](https://github.com/OpenSalamander/salamander/blob/a201fa3cc708c74dc1f4eea7032e0758840447a1/src/salamdr3.cpp#L1739-L1864) · [Working directories](https://www.altap.cz/salamander/help/salamand/navigating_dirhist/)

## Visit model and navigation

| Action | Back menu | Current | Forward menu |
| --- | --- | --- | --- |
| Visit A, B, C, D | C, B, A | D | Empty |
| Choose B from Back | A | B | C, D |
| Choose D from Forward | C, B, A | D | Empty |
| Choose B from Back, then open E | B, A | E | Empty |

- Keep both directions in the tab, using the existing bounded stack representation. Identify a row by its position in a snapshot of one tab and direction. A nonconsecutive repeat is a distinct visit, even if its path matches the current location. Reopening the same provider address through normal navigation adds no visit. Preserve distinct addresses exactly, including case when the provider opens distinct locations. Invalidate a snapshot when active tab, panel, current location, or history revision changes.
- Route toolbar, keyboard, mouse and popup choices through a shared jump. Move history in one operation and list only the selected destination. Start a navigation intent before preparing the selected location. A newer request, tab close or Escape cancels ownership of the earlier request and any later result.
- Reuse provider preparation, including archive credentials and leases, and stage the selected listing until usable entries arrive or an empty listing succeeds. A failed initial listing or canceled preparation preserves the original tab and both stacks. After the first usable listing is published, later enumeration errors follow FileCat's partial-list handling. For a missing local folder, try the nearest readable local ancestor provided by FileCat's path rules and explain the fallback when it opens. Use normal provider-specific errors when a fallback is unavailable. Salamander can shorten a disk path that no longer opens. [Salamander fallback and visit restoration](https://github.com/OpenSalamander/salamander/blob/a201fa3cc708c74dc1f4eea7032e0758840447a1/src/fileswn2.cpp#L1803-L1892)
- Capture the focused item and the top visible item for each visit. Resolve them against the new visible ordering by identity. If an item has disappeared or is filtered out, use ordinary focus and scroll defaults for that anchor. Apply pending restoration only to the matching listing while the user has yet to move focus or scroll. Inactive tabs capture their viewport when their view detaches. Existing workspace files continue to supply path-only history.
- Use FileCat's configured history size for the runtime trail, with a default of 200 and a minimum of 20. Session serialization saves up to ten eligible Back visits. A plain locked tab opens the chosen location in a new tab using that tab's normal navigation and error handling. The return-to-root variant travels in its own tab and retains its documented return behavior. [FileCat tab policy](FILECAT_PRODUCT_ARCHITECTURE_AND_IMPLEMENTATION_PLAN.md)

## Validation

- Exercise direct jumps in both directions, duplicates, a new branch, empty sides, restored history and locked tabs. Check click and keyboard operation in both toolbar layouts, dismissal without navigation, long literal paths, and stale rows after switching tabs or advancing history.
- Repeat ordinary disk, portable-device, and network choices in one tab without traversing history. Compare the complete Back list to the actual visits as the configured limit is reached, and verify their order in Forward after a jump to the oldest retained visit.
- Exercise late preparation success or failure, missing and unreadable locations, successful empty folders, and a canceled jump. Verify the destination is listed once, crossed locations are only moved in history, and newer user focus wins over late restoration.
- Mix local folders, removable or cloud-backed storage, remote and virtual locations, and archives in one trail. Make one recorded source unavailable after leaving it. A direct retry reports the problem and preserves the current visit and both directions when no fallback can open. A farther jump in either direction can cross the unavailable visit without probing it, and the visit stays available for a later retry.
- Use native Windows, Ubuntu and macOS sessions to check right-click, arrow, Enter and Escape, popup positioning and keyboard focus. Compare the Windows sequence with Open Salamander, including repeated paths, a saved cursor and scroll position, and a removed destination. Record OS and build identity with the observations.

The FileCat verification passed 28 focused tests each on Windows 11 x64, Ubuntu x64, and macOS arm64. The native smoke runs exercised right-click, keyboard selection, Escape, the Forward drop-down, and pointer selection in a four-folder trail. Ubuntu used an isolated X11 display. The Salamander comparison used the pinned upstream source linked above.

The straight-through regression passed 30 focused tests on Ubuntu x64, including a portable-device fixture and exact address identity. On the isolated X11 display, the Back popup listed and opened the oldest of 57 completed disk and Network visits. The previous build stopped at 50 in the same sequence.
