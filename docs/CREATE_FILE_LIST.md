# Create a file list

Choose **Commands → Create file list…** in a folder, archive, or connected file panel. Choose **All items in this folder** or **Selected or focused items**. Marked items take priority over the focused item. The all-items choice includes files hidden by the current panel filter.

Turn on **Include subfolders** to list their contents. FileCat does not follow directory links while recursing. Choose whether to include file lines, folder lines, or both.

Each item produces one line using your format. Choose a built-in rule—**Names and folders**, **All info**, **Files only**, or **Full paths**—then edit the format if needed. **Save rule as…** stores your own format, recursion choice, and file/folder choices for later use. The example updates as you edit.

| Field | Value |
|---|---|
| `{Name}` | Full file or folder name |
| `{Filename}` | Name without the extension (full name for folders) |
| `{Ext}` | Extension without the dot; empty for folders |
| `{Size}` | File size in bytes; empty for folders or unknown sizes |
| `{Modified}`, `{Created}` | Available UTC timestamps |
| `{Path}` | Full displayed path |
| `{RelativePath}` | Path beneath the folder where the list started |
| `{Type}` | `File` or `Folder` |

Use `{{` and `}}` for literal braces. For example, `{Filename}.{Ext} - to remove` creates `photo.jpg - to remove` for a file named `photo.jpg`.

Choose **Open in viewer**, **Copy to clipboard**, or **Save as text file…**. The text file uses UTF-8. FileCat reads directory metadata, not file contents. An unreadable folder, changed selection, or exceeded limit stops the export without copying or saving a partial list. A list is limited to 100,000 entries, 64 folder levels, and 8 MiB of UTF-8 text.
