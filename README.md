<p align="center">
  <img src="docs/social-preview.png" width="100%" alt="PinList – to-do lists that live on your Windows desktop">
</p>

# PinList

To-do list widgets pinned to your Windows desktop. Categories, drag-and-drop and an archive of completed items – no account, no cloud, everything stays on your PC.

---

## Features

- **Desktop widgets.** Each list is a borderless widget that stays on the desktop, survives **Win+D / Show desktop**, and isn't cluttering the taskbar or Alt+Tab.
- **As many widgets as you want.** Every widget remembers its position and size, and can be locked in place.
- **Categories.** Colored, collapsible and reorderable by drag-and-drop, also between widgets.
- **Fast editing.** Type and press Enter to add, double-click to edit, Shift+Enter for multi-line items.
- **Archive.** Completed items are kept with the date and time they were completed; search, restore or delete them.
- **Undo.** Ctrl+Z brings back the last deleted or completed item.
- **Looks native on Windows 11.** Dark, light or pastel widgets, optional transparency, rounded dark menus.
- **Your data is safe.** Every change is saved instantly to a local JSON file, with atomic writes and daily backups.

## Requirements

- Windows 10 or 11 (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (free, from Microsoft). If it's missing, Windows offers a download link the first time you start PinList.

## Download

Download `PinList.exe` from the [Releases](../../releases/latest) page.

PinList is a single exe – put it anywhere (e.g. `%LOCALAPPDATA%\Programs\PinList`) and run it. On first start it creates one widget and adds itself to *Start with Windows* (you can turn that off in the tray menu).

> The exe isn't code-signed, so Windows SmartScreen may show *"Windows protected your PC"*. Click **More info → Run anyway**, or build it yourself from source.

## Usage

| Action | How |
|---|---|
| Add an item | Type in **Add item…** and press **Enter**. Focus stays in the box for the next one. |
| Multi-line item | **Shift+Enter** starts a new line (when adding and when editing). |
| Complete an item | Click the circle. Click again within ~1 s to cancel. |
| Edit | Double-click the text. **Enter** saves, **Esc** cancels. |
| Delete | Click an item or category (it gets highlighted) and press **Delete**. Not archived. |
| Undo | **Ctrl+Z** restores the last deleted or completed item. |
| Reorder | Drag an item or a category header – within a widget or to another one. The list scrolls when you drag near its edge. |
| New category | The folder button in the widget header. |
| Category options | Right-click a category header: rename, color, collapse, delete. |
| Rename a widget | Double-click its header. The title is optional – leave it empty for no title. |
| Widget color | **⋯ → Window color** and **⋯ → Transparent background**. |
| Move / resize | Drag the header / drag the edges. The 🔒 button locks position and size. |
| Archive | **⋯ → Archive** or the tray menu. Enter or double-click restores, Delete removes permanently, Ctrl+Z undoes, Ctrl+F searches. |
| Tray icon | Left-click brings all widgets to the front; right-click opens the menu (new window, archive, start with Windows, data folder, exit). |

Widgets can't be closed with Alt+F4 by accident – use **⋯ → Delete window** to remove one.

## Where your data lives

Everything is stored in `%APPDATA%\PinList\`:

| File | Contents |
|---|---|
| `data.json` | All widgets, categories, items, positions, colors and the archive |
| `backups\data-YYYY-MM-DD.json` | One backup per day, the last 7 are kept |
| `log.txt` | Errors, if any occur |

`data.json` is written 300 ms after every change, using a temp file + replace, so a crash or power cut can't corrupt it. If the file is ever damaged, the latest backup is loaded automatically.

Tip: to move your lists to another PC, copy `data.json` while PinList is closed.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
winget install Microsoft.DotNet.SDK.10
```

Then:

```powershell
git clone https://github.com/<your-user>/PinList.git
cd PinList
scripts\publish.bat
```

| Script | What it does |
|---|---|
| `scripts\run.bat` | Runs the app in Debug mode (`dotnet run`) while you're developing |
| `scripts\publish.bat` | Builds a Release exe into `publish\PinList.exe` and starts it |

Both scripts close a running PinList first, because only one instance can run at a time. Debug and Release builds share the same data in `%APPDATA%\PinList`.

Or use the .NET CLI directly:

```powershell
dotnet build src\PinList\PinList.csproj
dotnet run --project src\PinList\PinList.csproj
```

The solution (`PinList.sln`) opens in Rider, Visual Studio or VS Code with the C# Dev Kit.

## Releasing a new version

Releases are built by GitHub Actions (`.github/workflows/release.yml`):

1. Update the version in `src/PinList/PinList.csproj` and add an entry to [CHANGELOG.md](CHANGELOG.md).
2. Commit and push to `main`.
3. Create and push a tag:
   ```powershell
   git tag v1.1.0
   git push origin v1.1.0
   ```
4. The workflow builds `PinList.exe` and creates a GitHub release with it attached. Edit the release notes on GitHub if needed.

Every push and pull request to `main` is also compiled by `.github/workflows/build.yml`, so a broken build shows up right away.

## Project structure

```
PinList/
├─ .github/workflows/      CI: build check and release builds
├─ docs/                   README and social preview image
├─ scripts/                run.bat, publish.bat
├─ src/PinList/
│  ├─ App.xaml(.cs)        startup, single instance, tray icon, shared styles
│  ├─ Assets/              app icon
│  ├─ Converters/          XAML value converters
│  ├─ Models/              Board → Category → TodoItem, ArchivedItem, AppData
│  ├─ Services/
│  │  ├─ Actions.cs        every data change (add, complete, delete, move, undo)
│  │  ├─ Store.cs          loading/saving data.json, backups
│  │  ├─ Themes.cs         widget, menu and archive colors
│  │  ├─ Native.cs         Win32: pin to desktop, tool window, title bar color
│  │  └─ Autostart.cs      start with Windows (HKCU\…\Run)
│  └─ Views/               BoardWindow (widget), ArchiveWindow, ConfirmDialog
├─ CHANGELOG.md
├─ PinList.sln
└─ LICENSE
```

### How the desktop pinning works

Each widget is a borderless, transparent WPF window whose owner is the desktop window (`Progman`). Owned windows aren't hidden by *Show desktop*, and the `WS_EX_TOOLWINDOW` style keeps them out of the taskbar and Alt+Tab. If Explorer restarts, the widgets are recreated automatically.

## License

[MIT](LICENSE)
