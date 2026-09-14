# Obsidian Taskbar Icons

Give each [Obsidian](https://obsidian.md) vault its own button on the Windows taskbar, with an icon
you choose. Click the button and that vault opens. Its window groups under that button instead of
under the generic Obsidian one, so two or three open vaults show up on the taskbar as separate,
recognisable icons.

Windows 11 only. Windows 10 should work but is untested. No admin rights are needed, and Obsidian
itself is not modified.

## The problem

Windows groups taskbar buttons by an identifier called the *AppUserModelID*. Obsidian gives the
same identifier (`md.obsidian`) to every window it opens. As a result, every vault lands under the
same button, and a pinned shortcut can only open "Obsidian", never a specific vault.

The obvious workarounds do not help:

- Renaming a copy of `Obsidian.exe` changes nothing. The identifier comes from the application,
  not from the file name.
- A shortcut to an `obsidian://` link opens the correct vault, but the window still groups under
  the generic button, and the shortcut cannot reliably keep a custom icon.

## How the tool solves it

1. It writes a Start menu shortcut for each vault. The shortcut carries a unique identifier
   (`Obsidian.Vault.<name>`) and the icon you selected. When you pin that shortcut, the taskbar
   button owns that identifier.
2. The shortcut runs the tool in launcher mode. The launcher opens the vault through the
   `obsidian://open?vault=` link, waits for the vault window to appear, and then writes the same
   identifier, display name and icon into the window property store
   (`SHGetPropertyStoreForWindow`). An identifier set on a window takes priority over the one set
   by the application.
3. The window and the pinned button now carry the same identifier, so Windows groups them together.
4. A small resident **watcher** (`ObsidianTaskbarIcons.exe watch`) also sets the icon as the window
   icon (`WM_SETICON`). That is what makes Alt-Tab, Task View and the taskbar thumbnails show the
   vault icon instead of Obsidian's. Windows discards a process's icons when that process exits, so
   the watcher has to stay running. The launcher starts it. Once a second it checks every open
   Obsidian window, including windows Obsidian opened on its own and pop-out windows. It exits five
   minutes after the last Obsidian window closes.

Everything happens from outside Obsidian. Nothing is patched or injected. You can leave Obsidian's
*Custom app icon* setting as it is; the watcher's icon simply sits on top of it.

## Installation

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) to build the tool and the
.NET 10 Desktop Runtime to run it. The SDK includes the runtime.

```powershell
git clone https://github.com/slappycat2/obsidian-taskbar-icons.git
cd obsidian-taskbar-icons
.\install.ps1
```

The `install.ps1` script does three things:

- Builds a single-file executable and copies it to `%LOCALAPPDATA%\ObsidianTaskbarIcons\bin`.
- Copies the sample icons to `%LOCALAPPDATA%\ObsidianTaskbarIcons\samples`.
- Adds **Obsidian Taskbar Icons** to the Start menu.

Shortcuts created by the tool always point at the installed copy, so rebuilding later never breaks
an existing pin.

## Usage

1. Open **Obsidian Taskbar Icons** from the Start menu.
2. Select a vault. The list comes from Obsidian's own vault registry. To use any other folder,
   click **Browse**.
3. Change the taskbar name if you want to.
4. Select an icon if you want to. Accepted file types are `.ico`, `.png`, `.jpg`, `.bmp`, `.exe`
   and `.dll`. Images are converted automatically into a multi-size icon (16 to 256 pixels). If you
   do not select an icon, the tool uses Obsidian's icon. **Browse** opens in the
   [sample icons](samples/icons) folder, which contains eight Obsidian-style gems in different
   colours: blue, cyan, green, magenta, purple, red, sky and yellow.
5. Click **Create shortcut & launch**. The vault opens and appears as its own taskbar button.
6. Right-click that taskbar button and choose **Pin to taskbar**. That is the last step.

The shortcut also appears in the Start menu as "Obsidian - *name*", so you can pin it from there
as well.

The lower half of the window lists the icons you have created. Each entry has four buttons:
**Launch**, **Open shortcut folder**, **Re-tag open windows** and **Remove**.

## Command line

The same executable is also a command line tool. This is what the shortcuts call.

```text
ObsidianTaskbarIcons.exe                                          open the window
ObsidianTaskbarIcons.exe create --vault <folder> [--name <text>] [--icon <file>] [--no-launch]
ObsidianTaskbarIcons.exe launch --id <slug>                       open one vault and tag its window
ObsidianTaskbarIcons.exe tag                                      re-tag every open vault window that has a profile
ObsidianTaskbarIcons.exe watch                                    stay resident and keep window icons and identifiers in sync
ObsidianTaskbarIcons.exe inspect                                  list Obsidian windows with their current identifiers
```

`<slug>` is the vault folder name. Any character outside `A-Z a-z 0-9 . _ -` is replaced with `_`.

## Good to know

- **Vaults that Obsidian opens by itself.** This includes vaults restored at login and vaults
  opened through Obsidian's own vault switcher. The watcher tags them as long as it is running.
  If Obsidian starts with Windows, add `ObsidianTaskbarIcons.exe watch` to your Startup folder as
  well. Otherwise, click **Re-tag open windows** or launch any vault from its pinned button. Both
  start the watcher.
- **The window icon lives only while the watcher runs.** If you stop the watcher, Alt-Tab falls
  back to Obsidian's icon until the next launch or re-tag. The `inspect` command reports whether
  the watcher is running.
- **Pinning is manual.** Windows 11 does not allow a program to pin anything to the taskbar, so
  the final step is one right-click. Unpinning is manual too. **Remove** deletes only the shortcut,
  the icon and the profile.
- **The vault must be known to Obsidian.** The `obsidian://` link only opens vaults that appear in
  Obsidian's vault switcher. Open a new vault once from inside Obsidian first.
- **Pop-out windows** ("Open in new window") are picked up by the watcher within a second.
- **Tested versions.** Obsidian 1.14 on Windows 11 build 26200. The launcher matches on Obsidian's
  window title, which looks like `Note - Vault - Obsidian 1.14.1`. If a future Obsidian changes
  that format, the `inspect` command shows what the titles look like now.

## Where things live

| Item | Location |
| --- | --- |
| Installed executable | `%LOCALAPPDATA%\ObsidianTaskbarIcons\bin\` |
| Profiles | `%LOCALAPPDATA%\ObsidianTaskbarIcons\profiles.json` |
| Generated icons | `%LOCALAPPDATA%\ObsidianTaskbarIcons\icons\` |
| Sample icons | `%LOCALAPPDATA%\ObsidianTaskbarIcons\samples\` (copied from `samples\icons`) |
| Shortcuts | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Obsidian - *.lnk` |

## Uninstall

1. Unpin the buttons from the taskbar.
2. End any running `ObsidianTaskbarIcons.exe` process in Task Manager.
3. Delete the `Obsidian - *.lnk` shortcuts and the "Obsidian Taskbar Icons" shortcut from the
   Start menu folder listed above.
4. If you added `watch` to your Startup folder, delete that too.
5. Delete the `%LOCALAPPDATA%\ObsidianTaskbarIcons` folder.

## Building

```powershell
dotnet build src\ObsidianTaskbarIcons\ObsidianTaskbarIcons.csproj -c Release
```

The project is a single C# WinForms application targeting `net10.0-windows`, with no NuGet
dependencies. The interesting parts are in `src\ObsidianTaskbarIcons\Interop\`:

- `ShellLink.cs` writes shortcuts that carry an AppUserModelID.
- `WindowIdentity.cs` finds Obsidian windows and stamps the identity onto them.
- `WindowIcon.cs` pushes an icon onto a window that belongs to another process.

`Watcher.cs` is the resident loop that keeps both the identity and the icon applied.

## License

MIT. See [LICENSE](LICENSE).
