# Changelog

All notable changes to PinList are listed here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

### Added
- Due dates and reminders: right-click an item → *Add due date…* / *Add reminder…*. Reminders appear as Windows notifications; ones missed while PinList was closed are shown on start
- App theme for menus, dialogs and the archive: follows the Windows light/dark setting by default, or choose *Light* / *Dark* (⋯ menu or tray menu)
- Export and import of all widgets and the archive (add next to your lists, or replace everything with a safety copy)
- Starting PinList while it's already running brings its widgets to the front
- Undo for restoring from the archive, editing, renaming, recoloring, moving and adding items and categories
- The archive tells you when a restored item went to another category because its own was deleted
- Tests for the whole app (about 270), including UI tests that run the real app on a hidden Windows desktop; 100 % of lines covered, and CI fails if coverage drops below 99 % (lines) / 92 % (branches)
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
