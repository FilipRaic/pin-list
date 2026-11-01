<p align="center">
  <img src="docs/social-preview.png" width="100%" alt="PinList – to-do lists on your desktop (Windows and macOS) and on your Android phone">
</p>

# PinList

To-do lists pinned to your desktop – on **Windows** and **macOS** – and in your pocket on **Android**. Categories, drag-and-drop, due dates, reminders and an archive of completed items. No account needed. Optional sync through your own Google Drive.

**Website:** [filipraic.github.io/pin-list](https://filipraic.github.io/pin-list/)

---

## Features

- **Desktop widgets** (Windows and macOS). Each list is a borderless widget that stays on the desktop. On Windows it survives **Win+D / Show desktop** and stays out of the taskbar and Alt+Tab; on macOS it's on every Space and out of the Dock and Cmd+Tab.
- **Phone app** (Android). The first screen shows a card for every list with its color, item count, overdue items and a short preview; tap one to open it full screen.
- **As many lists as you want.** Every widget remembers its position and size, and can be locked in place.
- **Categories.** Colored, collapsible and reorderable by drag-and-drop, also between widgets.
- **Fast editing.** Type and press Enter to add, double-click (tap on the phone) to edit, Shift+Enter for multi-line items.
- **Due dates and reminders.** Reminders show up as system notifications (on the phone also when the app is closed).
- **Archive.** Completed items are kept with the date and time they were completed; search, restore or delete them.
- **Undo.** On the desktop, Ctrl+Z undoes almost anything: deleting, completing, editing, renaming, moving, restoring. On the phone, the bar that appears after a change has an *Undo* button.
- **Dark, light or pastel lists**, optional transparency, menus that follow the system light/dark setting.
- **Optional Google Drive sync.** Sign in with Google and choose which lists sync between your PCs, Macs and phone. Changes are merged item by item, also after days offline, and nothing you deleted comes back. Your lists go only to a hidden folder in **your own** Google Drive – see [Sync](#sync).
- **Your data is safe.** Every change is saved right away to a local JSON file, with atomic writes and daily backups. Export and import your lists at any time – also to move them between a PC, a Mac and a phone.

## Requirements

| | |
|---|---|
| **Windows** | Windows 10 or 11 (x64) and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (free, from Microsoft). If it's missing, Windows offers a download link the first time you start PinList. |
| **macOS** | macOS 12 Monterey or newer, on a Mac with Apple Silicon (M1 or newer). Nothing else to install. |
| **Android** | Android 8.0 or newer. |

## Download

Get the files from the [website](https://filipraic.github.io/pin-list/#download) or the [Releases](../../releases/latest) page. Each one has a `.sha256` file next to it, to check that the download wasn't changed.

### Windows – `PinList.exe`

PinList is a single exe – put it anywhere (e.g. `%LOCALAPPDATA%\Programs\PinList`) and run it. On first start it creates one widget and adds itself to *Start with Windows* (you can turn that off in the tray menu). Starting it again while it's running brings the widgets to the front.

> The exe isn't code-signed, so Windows SmartScreen may show *"Windows protected your PC"*. Click **More info → Run anyway**, or build it yourself from source.

```powershell
Get-FileHash .\PinList.exe -Algorithm SHA256   # compare with PinList.exe.sha256
```

### macOS – `PinList-mac-arm64.zip`

1. Unzip it and move **PinList.app** to *Applications*.
2. The app isn't notarized by Apple (that needs a paid developer account), so macOS blocks it at first. Open **Terminal** and run:
   ```bash
   xattr -dr com.apple.quarantine /Applications/PinList.app
   ```
   Then open PinList normally. (Alternatively: try to open it once, then **System Settings → Privacy & Security → Open Anyway**.)
3. PinList lives in the **menu bar** (no Dock icon). On first start it creates one widget and adds itself to the login items; you can turn that off in the menu bar menu.

If macOS says the app *"is damaged"*, sign it for your Mac and try again:

```bash
codesign --force --deep --sign - /Applications/PinList.app
```

To check the download: `shasum -a 256 PinList-mac-arm64.zip` and compare with `PinList-mac-arm64.zip.sha256`.

### Android – `PinList-android.apk`

1. Download the APK on the phone (or copy it there) and open it.
2. Allow your browser or file manager to *install unknown apps* when Android asks.
3. Allow notifications when PinList asks – reminders need them.

On **Xiaomi / HyperOS** (and some other brands), the system may stop apps in the background, which can delay reminders. Open **Settings → Apps → PinList** and set **Battery saver → No restrictions** and turn on **Autostart**.

Updates install over the old version and keep your lists.

## Usage

### Desktop (Windows and macOS)

| Action | How |
|---|---|
| Add an item | Type in **Add item** and press **Enter**. Focus stays in the box for the next one. |
| Multi-line item | **Shift+Enter** starts a new line (when adding and when editing). |
| Complete an item | Click the circle. Click again within ~1 s to cancel. |
| Edit | Double-click the text. **Enter** saves, **Esc** cancels. |
| Due date / reminder | Right-click an item → **Add due date** or **Add reminder**. Overdue dates turn red, today's are highlighted. Reminders missed while PinList wasn't running are shown when it starts; clicking a reminder shows the item. |
| Delete | Click an item or category (it gets highlighted) and press **Delete**. PinList asks first (Ctrl+Z still brings it back). Not archived. Emptying an item's text deletes it too, after asking. |
| Undo | **Ctrl+Z** undoes the last change – in any widget or in the archive (one undo history for the whole app, the last 50 changes). |
| Reorder | Drag an item or a category header – within a widget or to another one. The list scrolls when you drag near its edge. |
| New category | The folder button in the widget header. |
| Category options | Right-click a category header: rename, color, collapse, delete. |
| Rename a widget | Double-click its header. The title is optional – leave it empty for no title. |
| Widget color | **⋯ → Window color** and **⋯ → Transparent background**. |
| App theme | **⋯ → App theme** or the tray / menu bar menu: follow the system (default), *Light* or *Dark*. Applies to menus, dialogs and the archive. |
| Move / resize | Drag the header / drag the edges. The 🔒 button locks position and size. |
| Archive | **⋯ → Archive** or the tray / menu bar menu. Enter or double-click restores, Delete removes permanently (after asking), Ctrl+Z undoes, Ctrl+F searches. |
| Sync a widget | **⋯ → Sync this window** (signs in with Google first if needed). See [Sync](#sync). |
| Export / import | **⋯ → Export** saves all lists and the archive to a file. **⋯ → Import** either adds a file's lists next to yours or replaces everything (a copy of your current lists is kept in `backups`). |
| Tray / menu bar icon | Brings all widgets to the front; its menu has new window, archive, theme, export/import, **Google sync**, start at login, data folder and exit. |

Widgets can't be closed with Alt+F4 by accident – use **⋯ → Delete window** to remove one.

### Phone (Android)

| Action | How |
|---|---|
| Open a list | Tap its card. **Back** returns to the cards. |
| New list | The **+** button on the cards screen. |
| Add an item | Type in **Add item** and press Enter on the keyboard; the keyboard stays open for the next one. |
| Complete an item | Tap the circle (tap again quickly to cancel). |
| Edit | Tap the text of an item, or the list's title. |
| Menus | **Press and hold** a card, a category or an item: due date, reminder, move to another list, color, rename, delete. |
| Reorder | Press and hold, then drag – cards on the first screen, items and categories in a list. Hold near the top or bottom edge to scroll. |
| Undo | The *Undo* button in the bar that appears after deleting, completing or moving. |
| Archive, theme, export / import | The **archive** and **⋮** buttons on the cards screen. |
| Google sync | **⋮ → Google sync** to sign in; **press and hold** a card (or the list's ⋮ menu) → **Sync this list**. |

## Sync

Sync is optional and off until you turn it on. It works between any mix of Windows PCs, Macs and Android phones signed in with the same Google account.

1. **Sign in.** Desktop: tray / menu bar icon → **Google sync → Sign in with Google** (your browser opens Google's sign-in page). Phone: **⋮ → Google sync → Sign in with Google**. Signing in doesn't sync anything yet.
2. **Choose lists.** Desktop: **⋯ → Sync this window**. Phone: press and hold a card → **Sync this list**. The list then appears on your other signed-in devices. Turning it off on any device turns it off everywhere; every device keeps its copy as a normal local list.
3. **The icon** in a synced list's header shows how it's doing: a cloud with a check (up to date), turning arrows (syncing), a cloud with an arrow (offline – changes sync later), a red cloud (a problem – click or tap it for what to do). A crossed-out cloud means the list isn't synced. Signed out, there's no icon at all.

How it behaves:

- Changes reach your other devices within about 20 seconds while PinList runs. The **phone also syncs in the background, about every 15 minutes** (only with a network, and only while you're signed in with at least one synced list), so reminders set on the PC ring on the phone even if PinList is closed. On **Xiaomi / HyperOS** and some other brands, set **Settings → Apps → PinList → Battery saver → No restrictions** for that.
- Edits made on different devices, also offline, are merged item by item. If the same item was changed on two devices, the newer change wins. An item deleted on one device is deleted everywhere – unless it was edited on another device after that, then it stays.
- Deleting a synced list deletes it on all devices (PinList asks first and says so).
- **Sign out** keeps all lists on this device and pauses their sync. **Sign out and delete cloud data** also removes PinList's data from your Google Drive; your lists stay on every device as local lists.
- Window positions and sizes, the lock, the app theme and "start with Windows" are per device and never synced.

Privacy: PinList can only see its own hidden folder in your Drive, never your other files, and there is no PinList server. The full policy is on the [privacy page](https://filipraic.github.io/pin-list/privacy). Building your own release with sync needs your own Google project – see [Releasing a new version](#releasing-a-new-version).

## Keyboard and accessibility

Everything on the desktop works without a mouse. On Windows, press **Win+B** to get to the tray, select the PinList icon, press **Enter** to open its menu and choose **Show all windows** – or simply start PinList again. Keyboard focus lands on the first category.

| Key | In a widget |
|---|---|
| **Tab** / **Shift+Tab**, **↑** / **↓** | Move between the header buttons, categories, items and *Add item* boxes |
| **Space** | Complete the focused item (press again within ~1 s to cancel) |
| **Enter** or **F2** | Edit the item · on a category: **Enter** collapses/expands, **F2** renames |
| **Delete** | Delete the item or category, after asking (focus moves to the next one) |
| **Alt+↑** / **Alt+↓** | Move the item or category up or down (items move into the next category at the edges) |
| **Context menu key** or **Shift+F10** | Open the item's or category's menu (due date, reminder, color, …) |
| **Ctrl+Z** | Undo |
| **Ctrl+Arrows** / **Ctrl+Shift+Arrows** | Move / resize the widget (when it isn't locked) |

- **Screen readers** (tested with Narrator, NVDA and JAWS on Windows): every button and field has a name, items are read with their due date and reminder (*"Renew passport, overdue, was due Sat 17 Oct"*), categories with their item count and whether they're collapsed, and dialogs with their title. Completing, deleting, moving, undoing and restoring are announced.
- **Contrast**: small grey text has at least 4.5:1 contrast (WCAG AA) on every list color. With a Windows **contrast theme** turned on, PinList uses its colors everywhere.
- **Text size**: on Windows, PinList follows **Settings → Accessibility → Text size**.
- **Visible focus**: the focused element gets a clear outline when you use the keyboard. Click targets such as the complete circle are at least 24 × 24 px.

## Where your data lives

| | Folder |
|---|---|
| Windows | `%APPDATA%\PinList\` |
| macOS | `~/Library/Application Support/PinList/` |
| Android | The app's private storage (removed when the app is uninstalled – export first) |

| File | Contents |
|---|---|
| `data.json` | All lists, categories, items, positions, colors, settings and the archive |
| `backups/data-YYYY-MM-DD.json` | One backup per day, the last 7 are kept |
| `backups/before-import-….json` | Your lists as they were before an import replaced them |
| `data.vN.json` | A copy of a file from a newer version of PinList, kept before this version saves over it |
| `log.txt` | Errors, if any occur (older entries move to `log.old.txt` after 1 MB) |
| `sync-state.json` | Google sync's state: the signed-in account and which cloud files were merged (only after signing in) |
| `google-token.dat` | Windows: your Google sign-in, encrypted for your Windows user (macOS keeps it in the **Keychain**, Android in Google Play services). Removed when you sign out. |

`data.json` is written 300 ms after every change, using a temp file + replace, so a crash or power cut can't corrupt it. If the file is ever damaged, the latest backup is loaded automatically. The format is the same on every platform and is described on the [data format page](https://filipraic.github.io/pin-list/data-format).

To move your lists to another device, use **Export** and **Import**.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0):

```powershell
winget install Microsoft.DotNet.SDK.10
```

Then, on Windows:

```powershell
git clone https://github.com/FilipRaic/pin-list.git
cd pin-list
scripts\publish.bat
```

| Script | What it does |
|---|---|
| `scripts\run.bat` | Runs the Windows app in Debug mode (`dotnet run`) while you're developing |
| `scripts\publish.bat` | Builds a Release exe into `publish\PinList.exe` and starts it |
| `scripts/package-mac.sh` | On a Mac: builds `PinList.app` and zips it (`bash scripts/package-mac.sh 2.1.0`) |

Both `.bat` scripts close a running PinList first, because only one instance can run at a time. Debug and Release builds share the same data.

**Android:** install the workload once with `dotnet workload install android` (it needs the Android SDK and a JDK – installing [Android Studio](https://developer.android.com/studio) gives you both). Then `dotnet build src/PinList.Android -c Release` makes `src/PinList.Android/bin/Release/net10.0-android/com.pinlist.app-Signed.apk` (signed with a debug key – see *Releasing* for the real one).

To try something without touching your real lists, point the desktop app at another folder. Such a run doesn't change *Start with Windows* and can run next to your normal PinList:

```powershell
$env:PINLIST_DATA_DIR = "C:\Temp\pinlist-test"
dotnet run --project src\PinList\PinList.csproj -f net10.0-windows
```

### Tests

`dotnet test PinList.sln` runs two test projects:

- `tests/PinList.Core.Tests` – the data, undo, reminders and file format logic, and sync: the merge engine with simulated devices (offline for days, stale files, restored backups, three devices), the Drive client and the sign-in against fake HTTP. It runs on Windows, macOS and Linux (CI runs it on all three).
- `tests/PinList.Tests` – the real windows and the phone screens, run with Avalonia's **headless** platform: nothing appears on your screen, and keys, mouse, touch-and-hold and drag-and-drop are simulated. The tests use their own data folder, a fake tray icon and scripted dialogs, and never touch your real lists or "Start with Windows".

To see coverage:

```powershell
dotnet test PinList.sln -p:CollectCoverage=true -p:ExcludeByFile=**/obj/**%2c**/*.axaml
```

CI fails if line coverage drops below 99 % or branch coverage below 92 %. The only code left out on purpose (`[ExcludeFromCodeCoverage]`) acts on the operating system itself: Win32 and macOS window calls, the real tray icon, opening Explorer/Finder, the system file pickers and the on-screen keyboard.

The solution (`PinList.sln`) opens in Rider, Visual Studio or VS Code with the C# Dev Kit.

## Releasing a new version

Releases are built by GitHub Actions (`.github/workflows/release.yml`):

1. Update `<Version>` in [`Version.props`](Version.props) – the one version for every app (Android's version code follows from it) – and add a `## [x.y.z] - date` section to [CHANGELOG.md](CHANGELOG.md) – it becomes the release notes.
2. Commit and push to `master`.
3. Create and push a tag with the same version:
   ```powershell
   git tag v2.1.0
   git push origin v2.1.0
   ```
4. The workflow checks that the tag is on `master`, matches `Version.props` and has a CHANGELOG section, runs the tests, builds `PinList.exe`, `PinList-mac-arm64.zip` and `PinList-android.apk`, and creates a GitHub release with all three, their `.sha256` checksums and the version's CHANGELOG section as release notes.

**The Android signing key.** Phones only install an update that is signed with the same key as the installed app, so every release uses one key, kept in the repository secrets. To create it (once – keep the file and password safe, a lost key means users must uninstall to update):

```powershell
keytool -genkeypair -v -keystore pinlist-release.keystore -alias pinlist -keyalg RSA -keysize 4096 -validity 10000
[Convert]::ToBase64String([IO.File]::ReadAllBytes("pinlist-release.keystore")) | Set-Clipboard
```

(`keytool` comes with Android Studio: `C:\Program Files\Android\Android Studio\jbr\bin\keytool.exe`.) Then add three secrets under **Settings → Secrets and variables → Actions**: `ANDROID_KEYSTORE_BASE64` (the clipboard), `ANDROID_KEYSTORE_PASSWORD` (the password you chose) and `ANDROID_KEY_ALIAS` (`pinlist`).

**The Google sign-in for sync.** The Windows and Mac builds include PinList's Google client ID, taken from two more repository secrets: `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`. The release stops if they're missing. They are the client ID and secret of a *Desktop app* OAuth client in the Google Cloud Console (project with the Google Drive API enabled and the scopes `drive.appdata`, `openid` and `userinfo.email`). The Android app has nothing built in: Google recognizes it by an *Android* OAuth client with the package name `com.pinlist.app` and the **SHA-1 of the release signing key** (`keytool -list -v -keystore pinlist-release.keystore -alias pinlist`). Without it, sign-in fails in the released APK. Add a second Android client with your debug key's SHA-1 for local builds. While the consent screen's publishing status is *Testing*, only listed test users can sign in and sign-ins expire after 7 days. Set it to **In production** (no Google review needed for these non-sensitive scopes) before telling anyone else about sync.

Every push and pull request to `master` is also built (warnings count as errors) and tested by `.github/workflows/build.yml`, which also builds the Windows exe, the Mac app and the Android APK the same way the release does, so a broken build shows up before you tag. Dependabot opens pull requests when the GitHub Actions or test packages have updates.

## Project structure

```
pin-list/
├─ .github/                 CI (build + tests, releases) and Dependabot
├─ docs/                    the website on GitHub Pages: index.html, privacy.html, data-format.html and site.css,
│                           plus the social preview image (social-preview.html is its source)
├─ scripts/                 run.bat, publish.bat, package-mac.sh
├─ Version.props            the version of every app
├─ src/PinList.Core/        no UI, runs everywhere
│  ├─ Models/               Board → Category → TodoItem, ArchivedItem, Settings, AppData
│  ├─ Services/             Actions (every change + undo), Store (data.json, backups, export/import),
│  │                        Reminders, WallClockConverter (times in data.json)
│  ├─ Sync/                 Google Drive sync: SyncService, SyncMerge (merge engine), ChangeTracker (stamps),
│  │                        ListCopy (cloud files), DriveCloudStore, GoogleOAuth, LoopbackSignIn, SyncState
│  └─ Platform/             timers and work areas the apps provide
├─ src/PinList.UI/          shared Avalonia UI: themes, icons, styles, accessibility, row animations
│  ├─ Phone/                the Android screens: PhoneView (cards, list, archive, menus), PhoneDrag
│  │                        (press-and-hold and drag), BoardCard, PhoneApp
│  └─ SyncIcon.cs           a list's sync status as icon and tooltip (desktop and phone)
├─ src/PinList/             the desktop app (Windows and macOS)
│  ├─ App.axaml(.cs)        startup, single instance, tray / menu bar icon, theme, reminders, export/import
│  ├─ Views/                BoardWindow (widget; split into core, keyboard, menus, drag & drop, sync),
│  │                        ArchiveWindow, ConfirmDialog, SignInDialog, DateDialog, ReminderPopup
│  ├─ Services/             Native (Win32) and MacNative (pinning), Displays (monitors), Autostart,
│  │                        tray icons, SingleInstance, Dialogs, WindowsSettings,
│  │                        GoogleClient (the built-in Google sign-in client for sync), DesktopGoogleAuth,
│  │                        TokenStores (DPAPI / Keychain)
│  └─ macOS/                Info.plist (the Mac app bundle's settings) and icon-1024.png (the Mac app icon)
├─ src/PinList.Android/     the Android app: data folder, notifications, reminder alarms, Google sign-in
│                           (Play services) and background sync (WorkManager)
├─ tests/PinList.Core.Tests/
├─ tests/PinList.Tests/     UI tests (headless) for the desktop app and the phone screens
├─ CHANGELOG.md
├─ PinList.sln
└─ LICENSE
```

### How the desktop pinning works

On **Windows**, each widget is a borderless window whose owner is the desktop window (`Progman`). Owned windows aren't hidden by *Show desktop*, and the `WS_EX_TOOLWINDOW` style keeps them out of the taskbar and Alt+Tab. If Explorer restarts, the widgets are recreated automatically.

On **macOS**, each widget's window joins all Spaces and stays put when switching between them (`NSWindowCollectionBehavior`), and the app has no Dock icon (`LSUIElement`).

### Changing the data format

`data.json` has a `version` number (currently 1). New fields are optional, and fields an app doesn't know are kept when it saves, so an older app doesn't lose what a newer one wrote. Raise `AppData.CurrentVersion` only when older files must be converted; do the conversion in `Store.Parse` and update [docs/data-format.html](docs/data-format.html).

## License

[MIT](LICENSE)
