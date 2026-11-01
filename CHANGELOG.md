# Changelog

All notable changes to PinList are listed here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [semantic versioning](https://semver.org/).

## [2.1.0] - 2026-11-01

Optional sync through your own Google Drive, on Windows, macOS and Android. Off until you sign in and turn it on for a list.

### Added
- **Google Drive sync** between any mix of Windows PCs, Macs and Android phones, per list: sign in from the tray / menu bar menu (*Google sync*) or the phone's ⋮ menu, then choose **Sync this window** / **Sync this list**. Changes are merged item by item – also after days offline on several devices – so no edit is lost and nothing you deleted comes back; an edit made after a delete on another device wins over the delete. Data goes only to a hidden app folder in your own Google Drive, never to a PinList server ([privacy policy](https://filipraic.github.io/pin-list/privacy))
- A sync icon in each list's header (and on the phone's cards) while signed in: synced, syncing, waiting while offline, or a problem with a click / tap to fix it
- On Android, sync also runs in the background about every 15 minutes, so a reminder set on the PC rings on the phone even when PinList is closed
- **Delete confirmations on the desktop**: deleting an item, a category or archived items asks first (Ctrl+Z still undoes), and so does emptying an item's text. Deleting a synced list says that it's deleted on your other devices too
- The Windows and Mac release builds include PinList's Google sign-in client, written from the repository secrets at release time (never stored in git). A build without it works as before, with sync unavailable
- A website on GitHub Pages ([filipraic.github.io/pin-list](https://filipraic.github.io/pin-list/)) with a short introduction, direct download buttons for Windows, macOS and Android, the [privacy policy](https://filipraic.github.io/pin-list/privacy) and the [data format](https://filipraic.github.io/pin-list/data-format). The last two were Markdown files before

### Changed
- *Replace everything* on import leaves synced lists alone, and exports never turn sync on for anyone who imports them
- Completed items keep their id in the archive, and every list, category and item gets a small change record in `data.json` (needed for merging). The data format stays at version 1, and 2.0.0 keeps the new fields ([data format](https://filipraic.github.io/pin-list/data-format))
- Releases need the `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET` repository secrets and stop with an error if they're missing
- GitHub release notes are now the version's section of this changelog (the release stops if it's missing)
- A new app icon on every platform (Windows, macOS, Android, the tray and the menu bar): the PinList logo from the website, a blue outlined check on a dark tile. The Mac app icon is now sharp on Retina screens too
- Menu items and buttons no longer end with "…" (e.g. *Export*, *Color*, *Add reminder*)

### Fixed
- Starting PinList while it's already running now always brings the widgets to the front (the signal to the running app could get lost)
- On Android, opening PinList again after leaving it with Back now shows the lists reliably (the closing screen could still hold them)

## [2.0.0] - 2026-10-31

PinList now runs on Windows, macOS and Android, from one codebase.

### Added
- **macOS app** (Apple Silicon): the same desktop widgets, on every Space and out of the Dock and Cmd+Tab, with a menu bar icon instead of the tray icon, starting at login, reminders and the same archive, export and import. Not notarized – see the README for opening it the first time
- **Android app**: a card for every list on the first screen (color, item count, overdue items and a short preview); tap to open a list full screen. Press and hold for menus (due date, reminder, move to another list, color, rename, delete), press and drag to reorder cards, items and categories (the other rows slide out of the way), an *Undo* bar after changes, the archive, app theme, export and import. Reminders are notifications that arrive also when the app is closed
- Lists can be moved between devices with export and import; the data file is the same everywhere ([data format](https://filipraic.github.io/pin-list/data-format))
- Releases include `PinList-mac-arm64.zip` and `PinList-android.apk` (signed), each with a SHA-256 checksum
- Items, categories and lists slide to their new place when they're reordered

### Changed
- The Windows app is rebuilt on Avalonia (instead of WPF), shared with macOS and Android. It looks and works as before, keeps your data and settings, and still needs the .NET 10 Desktop Runtime
- The data format starts over at version 1: times are saved without a time zone offset (a reminder at 9:00 stays at 9:00 on every device), and fields from newer versions are kept when an older version saves. Files from 1.x are no longer read
- One version number for every app, in `Version.props`
- The UI tests run headless (no hidden desktop needed), and the data logic is also tested on macOS and Linux

## [1.1.1] - 2026-10-31

### Fixed
- Widgets no longer move when a monitor goes to sleep, wakes up or another device is plugged into it. Only moves and resizes made by you are saved (mouse, Ctrl+Arrows and Win+Arrows), and widgets go back to their place once the monitors settle (also after waking the PC from sleep)

## [1.1.0] - 2026-10-21

### Added
- Due dates and reminders: right-click an item → *Add due date* / *Add reminder*. Reminders appear as Windows notifications; ones missed while PinList was closed are shown on start
- App theme for menus, dialogs and the archive: follows the Windows light/dark setting by default, or choose *Light* / *Dark* (⋯ menu or tray menu)
- Export and import of all widgets and the archive (add next to your lists, or replace everything with a safety copy)
- Starting PinList while it's already running brings its widgets to the front
- Undo for restoring from the archive, editing, renaming, recoloring, moving and adding items and categories
- The archive tells you when a restored item went to another category because its own was deleted
- Tests for the whole app (about 280), including UI tests that run the real app on a hidden Windows desktop; 100 % of lines covered, and CI fails if coverage drops below 99 % (lines) / 92 % (branches)
- Release checks that the tag matches the csproj version, and publishes a SHA-256 checksum next to the exe
- Dependabot updates for GitHub Actions and test packages
- Accessibility:
  - Full keyboard support in widgets: focusable items and categories, Space completes, Enter/F2 edits, Delete deletes, Alt+Up/Down moves, context menu key opens the menu, Ctrl+Arrows move and Ctrl+Shift+Arrows resize the widget; focus stays in place after deleting, completing and editing
  - Screen reader support: names for every button and field, items read with due date and reminder, categories with item count and collapsed state, expand/collapse pattern, dialog titles, and announcements for completing, deleting, moving, undoing and restoring
  - Windows contrast themes are used everywhere when turned on
  - The Windows text size setting is applied to all windows
  - Visible keyboard focus outline everywhere, including calendar days and archive rows
  - Bringing widgets to the front (tray icon, second start) puts keyboard focus in them

### Changed
- Daily backups are also made while PinList keeps running, not only when it starts
- `log.txt` is rotated to `log.old.txt` after 1 MB
- Errors on background threads are logged too
- Widgets are checked against each monitor separately, so a widget can't get stuck in a spot no monitor shows; new windows open on the monitor of the widget (or of the mouse, from the tray)
- The data file has a format version with upgrade steps; a copy is kept before upgrading
- Log messages are all in English
- Fixed: a new category's name box now starts with "New category" selected and focused (before, it could start empty and unfocused)
- Fixed: clicking a widget's header no longer brings back the highlight of the last clicked item
- The list area of a widget is no longer an invisible Tab stop
- Small grey text and the overdue color in widgets have at least 4.5:1 contrast on every widget color (checked by a test); the complete circle and collapse button have 24 × 24 px click targets
- README: correct branch name (`master`) and clone URL

## [1.0.0] – 2026-10-18

First public release.

### Features
- Desktop widgets that stay visible on Win+D, hidden from the taskbar and Alt+Tab
- Multiple widgets with saved position and size, optional position lock
- Colored, collapsible categories
- Add, edit, complete and delete items; multi-line items with Shift+Enter
- Drag-and-drop reordering of items and categories, also between widgets, with auto-scroll at the edges
- Delete key for the highlighted item or category, Ctrl+Z undo
- Archive of completed items with completion date and time, search, restore and permanent delete
- Dark, light and pastel widget colors with optional transparency
- Dark, rounded menus, tooltips and dialogs
- Tray icon with quick actions and *Start with Windows*
- Local JSON storage with atomic writes and 7 daily backups
