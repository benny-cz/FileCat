# Shared dialog reachability — FCUX-011

The shared overlay now reserves space for its header and actions and gives the
remaining client area to a scrollable body. This removes incompatible fixed
minimum widths from transfer, delete, conflict, Settings, bulk rename, prompts,
chooser filters and checksum output. The window's actual layout constraint,
including the existing overlay margins and theme padding, determines the space
available to the card.

## Layout and interaction contract

`DialogCard` uses header/body/footer grid rows. Action buttons wrap while retaining
their logical order, original string content, access keys, accessible names and
default/cancel behavior. Checkbox and radio labels wrap inside the modal. The card
keeps a stable width as an error, tab or preview changes, so editing does not cause
the entire form to jump sideways.

The body has one outer scroll owner. Its content receives a finite width matching
the actual scroll viewport, so responsive fields and labels can wrap even though
horizontal overflow is supported. A legacy or custom fixed-width child contributes
its real overflow to the horizontal extent. Only realized, visible, unclipped
visuals participate in that fallback; a text editor or virtualized list retains
ownership of its own internal scrolling. Responsive fields are not expanded to
the width of an unrelated oversized child.

`DialogForm` places labels beside fields when there is room and above them in a
compact modal. It retains the controls, values, logical order and label associations
when the layout changes. Settings tabs wrap; its old nested page scroll viewer
and oversized input minima are removed. The column-profile editor's related
sections stack at a narrow width and keep their lists bounded. Bulk rename groups
each label with its field, wraps the groups, and retains its bounded 280-DIP
virtualized preview.

Focus requests belong to the current modal session and current focus owner. A
focused control is brought into view after pending layout. A text editor first
reveals its current caret inside its own scroll viewer, then reveals that exact
caret rectangle in the modal body. This also handles refocusing an unchanged
caret in an editor taller than the available body. Queued requests cannot scroll
or take focus after the user chooses another control, the modal is replaced, or
the modal closes. Product callbacks do not force synchronous layout.

## Conflict decisions and filename safety

The conflict footer uses the short action label **Keep both**. Complete proposed
incoming/existing filenames are available as selectable, wrapped body text with
distinct accessible labels. The safe default remains **Skip**; changing the layout
does not approve an operation or change its submitted filename.

Names pass through the existing `Formatters.SafeName` display contract. Invisible
control, bidirectional and separator characters appear as explicit escape
sequences, with a visible explanation and accessibility help text when applicable.
The underlying proposed filename remains unchanged. This preserves complete
inspectable identity without letting an invisible character visually disguise a
name in a consequential decision.

## Regression coverage

`DialogViewportTests` contains 21 headless cases using the product's actual dialog
styles and laid-out geometry. The minimum main-window client area is 640 × 420
DIPs. Font sizes 14, 21 and 28 exercise 100%, 150% and 200% of the base text size;
these are font-size checks, not a claim of native monitor-scaling acceptance.

| Cases | Verified behavior |
| --- | --- |
| Copy, delete and bulk rename at three font sizes | Header and all footer actions remain inside the owner; the long body scrolls; the last field is reachable by keyboard focus; fields fit horizontally; bulk preview remains bounded; cancel submits no job. |
| Conflict at three font sizes, plus invisible-name input | Six actions wrap in the same logical/Tab order; Skip owns default focus and Enter; full names remain selectable; display escapes and explanatory help text preserve unambiguous identity. |
| Every Settings page at three font sizes | Visible fields fit the body width; lower fields scroll into view; text caret geometry is visible; footer remains reachable. |
| Direct and nested oversized custom content | A 1,300-DIP minimum-width child has a genuine horizontal scroll range; focusing its far lower edge reveals both axes while actions stay in place. |
| Resizing an open dialog | A live host shrinks to the minimum area without replacing the draft, caret or focus owner. |
| Refocusing a retained tall-editor caret near the start/end | Both nested scroll levels reveal the unchanged caret; moving inner/outer offsets cannot strand it; a newer focus owner wins over queued reveal work. |

Existing dialog fixtures explicitly realize the new scroll-viewer body before
querying visual children. Their operation effects, hidden-mark scope, safe-default,
settings persistence, cancellation and weak-reference lifetime assertions remain
in place. The combined focused gate passed all 173 cases under the verified
ordinary identity, with no failures or skips. The complete execution record is
maintained in the batch validation report; focused counts overlap the complete
App suite.

## Acceptance still requiring a desktop

Run transfer, delete, conflict, bulk rename and every Settings page with long
local/network paths and 200-character names at 100%, 150% and 200% native display
scaling on supported desktops. Check actual font fallback, pointer and keyboard
routing, assistive-technology announcements and focus, and physical-pixel clipping.
The automated geometry tests establish the specified DIP/font behavior and
ownership contract; they do not substitute for those native checks.
