# Obsidian Taskbar Icons

Give each [Obsidian](https://obsidian.md) vault its own Windows taskbar button with its own icon.
Click the button and exactly that vault opens. Its window groups under that button instead of
under the generic Obsidian one, so two or three open vaults sit side by side on the taskbar as
distinct, recognisable icons.

Windows 11 only (Windows 10 should work but is untested). No admin rights, no changes to Obsidian.

## The problem

Windows groups taskbar buttons by *AppUserModelID*. Obsidian registers one ID (`md.obsidian`) for
every window it opens, so every vault lands under the same pinned button and a pinned shortcut can
only ever open "Obsidian", not a specific vault. Renaming copies of `Obsidian.exe` does not help,
because the ID comes from the app, not the file name. Shortcuts to `obsidian://` links open the
right vault but still group under the generic button and cannot carry a custom icon reliably.

## How this tool solves it

1. It writes a Start-menu shortcut per vault that carries a unique ID (`Obsidian.Vault.<name>`) and
   your chosen icon. A pinned copy of that shortcut is a taskbar button that owns that ID.
2. The shortcut runs the tool in launcher mode. The launcher opens the vault through the
   `obsidian://open?vault=` URI, waits for the vault's window to appear, and stamps the same ID,
   display name and icon onto that window using the Windows property store
   (`SHGetPropertyStoreForWindow`). An explicit per-window ID overrides the app-wide one.
3. Windows now sees a window whose ID matches the pin and groups them together.
4. A small resident **watcher** (`ObsidianTaskbarIcons.exe watch`) sets the same icon as the window's
   own icon (`WM_SETICON`), so Alt-Tab, Task View and the taskbar hover thumbnails show the vault's
   icon instead of Obsidian's one custom app icon for every vault. Windows destroys a process's icons
   when it exits, which is why this has to be a process that stays around: the launcher starts it,
   it re-checks every open Obsidian window once a second (including windows Obsidian opened on its
   own, and pop-out windows), and it quits five minutes after the last Obsidian window closes.

Everything happens from outside Obsidian; nothing is patched or injected. Obsidian's own
*Custom app icon* setting can stay as it is; the watcher's icon simply sits on top of it.

## Install

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build, and the .NET 10
Desktop Runtime (included with the SDK) to run.

```powershell
git clone https://github.com/slappycat2/obsidian-taskbar-icons.git
cd obsidian-taskbar-icons
.\install.ps1
```

`install.ps1` publishes a single-file exe to `%LOCALAPPDATA%\ObsidianTaskbarIcons\bin` and adds
**Obsidian Taskbar Icons** to the Start menu. Shortcuts created by the tool always target that
installed copy, so rebuilding later never breaks an existing pin.

## Use

1. Open **Obsidian Taskbar Icons** from the Start menu.
2. Pick a vault. The list comes from Obsidian's own vault registry, or use **Browse** for any folder.
3. Optionally change the taskbar name and pick an icon: `.ico`, `.png`, `.jpg`, `.bmp`, `.exe` or
   `.dll`. Images are converted to a multi-size icon (16 to 256 px) automatically. With no icon
   chosen, Obsidian's own icon is used. **Browse** opens in the [sample icons](samples/icons)
   folder, which ships ten Obsidian-style gems: five flat colours (purple, teal, orange, green, rose)
   and five styles (glass, low-poly, line art, neon, papercut).
4. Click **Create shortcut & launch**. The vault opens and shows up as its own taskbar button.
5. Right-click that taskbar button and choose **Pin to taskbar**. Done.

The shortcut also lives in the Start menu as "Obsidian - *name*", so you can pin from there as well.

The lower half of the window lists the icons you have created, with **Launch**, **Open shortcut
folder**, **Re-tag open windows** and **Remove**.

## Command line

The same exe doubles as a CLI, which is what the shortcuts call.

```text
ObsidianTaskbarIcons.exe                                          open the window
ObsidianTaskbarIcons.exe create --vault <folder> [--name <text>] [--icon <file>] [--no-launch]
ObsidianTaskbarIcons.exe launch --id <slug>                       open one vault and tag its window
ObsidianTaskbarIcons.exe tag                                      re-tag every open vault window that has a profile
ObsidianTaskbarIcons.exe watch                                    stay resident and keep window icons and IDs in sync
ObsidianTaskbarIcons.exe inspect                                  list Obsidian windows with their current IDs
```

`<slug>` is the vault folder name with anything outside `A-Z a-z 0-9 . _ -` replaced by `_`.

## Good to know

- **Vaults opened by Obsidian itself** (for example at login, or through Obsidian's own vault
  switcher) are tagged by the watcher while it runs. If Obsidian starts with Windows, add
  `ObsidianTaskbarIcons.exe watch` to your Startup folder too; otherwise click **Re-tag open
  windows** or launch any vault from its pinned button, both of which start the watcher.
- **The window icon lives only while the watcher runs.** If you kill the watcher, Alt-Tab falls back
  to Obsidian's own icon until the next launch or re-tag. `inspect` reports whether it is running.
- **Pinning is manual.** Windows 11 does not let programs pin to the taskbar, so the final step is
  one right-click. Unpinning is manual as well; **Remove** deletes the shortcut, icon and profile only.
- **The vault must be known to Obsidian.** The `obsidian://` link only opens vaults listed in
  Obsidian's vault switcher. Open a new vault once from inside Obsidian first.
- **Pop-out windows** ("Open in new window") are picked up by the watcher within a second.
- Tested with Obsidian 1.14 on Windows 11 build 26200. Obsidian's window title format
  (`Note - Vault - Obsidian 1.14.1`) is what the launcher matches on; if a future Obsidian changes
  it, `inspect` shows what the titles look like.

## Where things live

| What | Where |
| --- | --- |
| Installed exe | `%LOCALAPPDATA%\ObsidianTaskbarIcons\bin\` |
| Profiles | `%LOCALAPPDATA%\ObsidianTaskbarIcons\profiles.json` |
| Generated icons | `%LOCALAPPDATA%\ObsidianTaskbarIcons\icons\` |
| Sample icons | `%LOCALAPPDATA%\ObsidianTaskbarIcons\samples\` (copied from `samples\icons`) |
| Shortcuts | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Obsidian - *.lnk` |

## Uninstall

Unpin the buttons, end any running `ObsidianTaskbarIcons.exe` in Task Manager, delete the
`Obsidian - *.lnk` shortcuts and the "Obsidian Taskbar Icons" shortcut from the Start menu folder
above (and `watch` from Startup if you added it), and delete `%LOCALAPPDATA%\ObsidianTaskbarIcons`.

## Building

```powershell
dotnet build src\ObsidianTaskbarIcons\ObsidianTaskbarIcons.csproj -c Release
```

The project is a single C# WinForms app targeting `net10.0-windows` with no NuGet dependencies.
The interesting parts are in `src\ObsidianTaskbarIcons\Interop\`: `ShellLink.cs` writes shortcuts
with an AppUserModelID, `WindowIdentity.cs` finds Obsidian windows and stamps the identity onto
them, and `WindowIcon.cs` pushes an icon onto a foreign window. `Watcher.cs` is the resident loop
that keeps both applied.

## License

MIT. See [LICENSE](LICENSE).
