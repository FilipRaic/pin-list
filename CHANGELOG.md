# Changelog

All notable changes to PinList are listed here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [semantic versioning](https://semver.org/).

## [2.5.1] - 2026-11-04

### Fixed
- **macOS:** PinList 2.5.0 quit right after starting ("PinList quit unexpectedly"). Its menu bar menu was replaced in a way the macOS menu code rejects. The menu now stays the same and only its entries change, and a menu that can't be updated no longer stops PinList

## [2.5.0] - 2026-11-04

A clean start for new users, and PinList is easier to find on a Mac.

### Added
- **macOS:** a Dock icon, a Cmd+Tab entry and the PinList app menu (**Settings** ⌘,, **All lists**, **Archive**) while the PinList window is open. With the window closed, PinList lives only in the menu bar as before

### Changed
- New installs start with no lists. All lists (desktop) and Lists (phone) say *No lists yet, create one to get started*, with **New list** ready to press. The desktop opens the PinList window on first start
- A new device that signs in to sync no longer gets an extra empty list next to the synced ones

### Fixed
- **macOS:** opening PinList again from Finder, Launchpad or Spotlight while it runs now shows its widgets, or the PinList window when no list is a widget. Before, nothing happened
- **macOS:** the menu bar icon is a template image, so macOS colors it for light and dark menu bars
- PinList starts even when the system can't create its tray / menu bar icon (it notes this in the log)
- **macOS:** the menu bar icon now shows. It was never handed to the system, so it didn't appear and wasn't listed in System Settings → Menu Bar
- **Sign out** signs out only this device. Before, it removed PinList's access at Google, which signed out every device. **Sign out and delete cloud data** still signs out everywhere
- **Android:** signing in works again after PinList's access was removed on another device. Before, Google Play services kept handing out the old token and every sign-in failed with "Google userinfo answered HTTP 401"

## [2.4.0] - 2026-11-03

One look on Windows, macOS and Android: the Inter font, the same colors and icons, menus without submenus, one PinList window on the desktop and a bottom bar on the phone.

### Added
- **Settings** page with Appearance (System, Light or Dark), Google Drive sync, start at login, check for updates, export, import and the data folder. On the desktop it is a section of the PinList window (**⋯ → Settings** or the tray / menu bar menu), on the phone the **Settings** tab. Each group is a box of rows: the Google account with a sync status dot, and buttons on the right of each row on the desktop or rows you tap on the phone
- One **PinList window** on Windows and macOS with a sidebar: **All lists**, **Archive** and **Settings**. It replaces the separate All lists and Archive windows
- An **Undo** pill at the bottom of a widget for a few seconds after completing an item (Ctrl+Z still works)
- A **bottom bar** on the phone with **Lists**, **Archive** and **Settings**, and a labeled **New list** button. Lists, menus and screens slide and fade in

### Changed
- The **Inter** font is bundled and used on all three platforms
- A new look for widgets and lists: a header with a line under it, each category with a dot, its name and an item count in its own color, a guide line down the items, outlined check circles and pill-shaped due date and reminder chips. Category colors get a readable shade on every list color (the colors in `data.json` don't change)
- A new complete animation: the circle fills and pops, the tick draws itself, the text is crossed out, then the row slides away and the rows below glide up
- Collapsing or expanding a category opens and closes its items smoothly, and the arrow turns
- **+ Add item** on the phone has a plus under the check circles, like on the desktop. The archive search on the phone has a magnifier and rounder corners
- Switches and accent buttons use the PinList accent color instead of the system color, and the reminder bell icon is cleaner
- Menus have no submenus. **⋯** has a row of list colors, **Transparent** and **Sync this list** switches, and every entry has an icon. The category menu has a row of colors too. The tray / menu bar menu is shorter: **All lists**, **New list**, **Archive**, **Show all widgets**, **Settings** and exit
- "Window" is now "list" everywhere you see it (**New list**, **Delete list**, **Sync this list**). **Show all windows** is **Show all widgets**
- The **Widget** switch on cards is now **On desktop**, and All lists has a **+ New list** card
- The archive is grouped by day, shows the list and the added date under each item, and has restore and delete buttons on the row under the mouse (desktop) or in a sheet (phone). On the desktop there are also **Restore** and **Delete** buttons under the list for the selected items, showing their shortcuts (Enter and Del). The shortcuts work wherever the keyboard is in the archive, except in the search box
- On a Mac, shortcuts are shown with the Mac keys (⌘Z, ⌘C, ↩, ⌫, ⌥↑) in menus, the archive and messages. ⌘ works where Windows uses Ctrl (undo, search, moving and resizing a widget), and the delete key deletes a selected item, category or archived item
- On the phone, the list menu has a row of colors and a **Sync this list** switch, and sheets have a grab handle, icons and close when swiped down. The ⋮ menu of the first screen is gone, its entries are in **Settings**

### Fixed
- The font on Android looked wrong because no font was set

## [2.3.0] - 2026-11-02

Choose which lists are widgets on each computer, copy items as text, and fixes for shutting down Windows and for the Mac app.

### Added
- **All lists** on Windows and macOS: every list as a card, like on the phone, in one window (tray / menu bar menu or **⋯ → All lists**). Click a card to open and edit the list, right-click it for its menu
- A **Widget** switch on every card in **All lists**. A list can be a widget on one computer and not on another, and lists still sync either way. The choice stays on each computer (`widget` in `data.json`, see the [data format](https://filipraic.github.io/pin-list/data-format))
- **⋯ → Remove from desktop** hides a widget without deleting the list (Ctrl+Z brings it back). When the last widget goes, **All lists** opens. Deleting a synced list also offers **Remove from desktop instead**
- **Copy items** as text, one item per line: **Copy** in an item's menu, **Copy items** in a category's menu and **Copy all items** in a list's menu, on the desktop and the phone. On the desktop also **Ctrl+C** (the selected item or category) and **Ctrl+Shift+C** (the selected item's category), **Cmd+C** on a Mac
- Every widget's **⋯** menu has a **PinList** submenu with the app settings: Google sync, start at login, check for updates, the data folder and exit
- On Android, notifications show the PinList check mark in the status bar and the app icon in the notification

### Changed
- A reminder is removed once it rang, on every device, also when the device that rang it syncs with one still on 2.2.0. Due dates stay, so overdue items still turn red
- On Android, list names, category names and items start with a capital letter (the keyboard can still switch to lowercase)
- On a Mac the exit entry is called **Quit PinList**
- A release now waits for the Build of its commit and publishes nothing if it failed (tests, coverage and all three apps). The release no longer runs the tests a second time
- Build runs skip commits that only change the website, Markdown files or the license, the Windows build no longer installs the Android tools, and Dependabot puts all GitHub Actions updates in one pull request

### Fixed
- Windows no longer says that PinList is preventing shutdown or restart. PinList saves and closes right away
- On a Mac, **Cmd+Q** and Quit in the Dock quit PinList (before, only deleting every window let it quit)
- On a Mac, PinList no longer shows in the Dock and Cmd+Tab, and the menu bar shows **PinList** instead of "Avalonia Application"
- After a reminder rang, the item's menu no longer offers **Remove reminder** for a reminder that isn't shown anywhere
- The Build of 2.2.0 failed on test coverage, which the release didn't check. Coverage is back above the limit

## [2.2.0] - 2026-11-02

PinList can now tell you when a new version is out, and dragging on Android is smooth.

### Added
- **Update check**, on Windows, macOS and Android. On first start PinList asks whether it may look for new versions. If you say yes, it asks GitHub about once a day for the number of the latest release. Nothing about you or your lists is sent ([privacy policy](https://filipraic.github.io/pin-list/privacy)). PinList never installs anything by itself
- When a new version is out, the desktop shows a notification once and the tray / menu bar menu starts with **Get PinList x.y.z**, which opens the release page. On Android a bar with **Get it** appears once and the ⋮ menu gets **Get PinList x.y.z**, which downloads the new APK in the browser
- **Check for updates** in the tray / menu bar menu and **⋮ → Updates** on Android change the answer at any time
- `data.json` gets two optional settings for this, `checkForUpdates` and `updateAnnounced`. The data format stays at version 1 ([data format](https://filipraic.github.io/pin-list/data-format))

### Changed
- The privacy policy has a new section about the update check (effective 2 November 2026)

### Fixed
- Smoother drag and drop on Android. The card, item or category you drag now stays under your finger instead of jumping from slot to slot, and slides into its place when you let go
- On Android, dragging a card or category over a taller one no longer makes the two swap back and forth. They swap once your finger is past the middle of the other one
- On Android, a dragged item is drawn above the rows below it instead of sliding under them, and the round check button no longer jumps ahead of its row while rows slide
- On Android, scrolling while you hold a dragged row near the top or bottom edge runs at an even speed, and the row stays under your finger while the list scrolls

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
- Every build and release job, and every test step, has a time limit, so a hanging job fails within minutes
- A new app icon on every platform (Windows, macOS, Android, the tray and the menu bar): the PinList logo from the website, a blue outlined check on a dark tile. The Mac app icon is now sharp on Retina screens too
- Menu items and buttons no longer end with "…" (e.g. *Export*, *Color*, *Add reminder*)

### Fixed
- Starting PinList while it's already running now always brings the widgets to the front (the signal to the running app could get lost)
- On Android, opening PinList again after leaving it with Back now shows the lists reliably (the closing screen could still hold them)
- On Android phones set to US English (or another language that writes the month first), opening the archive could crash the app or change when an item was completed. It happened for items completed on a day from the 1st to the 12th of a month

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
