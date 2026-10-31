# PinList data format

All PinList apps (Windows, macOS, Android) read and write the same JSON file. Exports use the same format, so a
file exported on one platform can be imported on any other. The code is in `src/PinList.Core`
(`Models/AppData.cs`, `Services/Store.cs`); the test `DataFormatTests` checks the example file
`tests/PinList.Core.Tests/Fixtures/data-v1.json`, so the format can't change by accident.

Current version: **1**

## Where the file is

| Platform | Folder |
|---|---|
| Windows | `%APPDATA%\PinList\` |
| macOS (from 2.0.0) | `~/Library/Application Support/PinList/` |
| Android (from 2.0.0) | the app's private storage |

The folder contains:

- `data.json` – all widgets, categories, items, the archive and settings.
- `backups/data-YYYY-MM-DD.json` – one copy per day, the last 7 are kept.
- `backups/before-import-YYYY-MM-DD-HHMMSS.json` – a copy taken before an import replaced everything.
- `data.vN.json` – a copy of a file from a newer PinList (version N), kept before this version saves over it.
- `data.json.corrupt-…` – a damaged file that couldn't be read (the latest backup was loaded instead).
- `log.txt` / `log.old.txt` – errors and notes, at most about 1 MB each.

Writing is atomic: the new content goes to `data.json.tmp`, is flushed to disk, and then replaces `data.json`.
If the app stops between the two steps, the `.tmp` file is used on the next start.

## Rules

- Property names are camelCase. Text is UTF-8, written with relaxed escaping (`“`, `é`, `€` stay readable).
- **Times are local wall-clock time without a time zone**, e.g. `"2026-11-02T09:00:00"` (fractions of a second
  may follow, e.g. `"…T09:00:00.1234567"`). A reminder for 09:00 fires at 09:00 on whichever device has the
  file. A time with an offset or `Z` (`"…T09:00:00+01:00"`) isn't valid, and the file is rejected.
- **`dueDate` is a date only**; it is written with a time of `00:00:00`, which is ignored.
- **Unknown fields are kept.** A field this version doesn't know (from a newer PinList) is read and written back
  unchanged, on every object.
- **Desktop-only fields are kept by every platform.** Android doesn't use a widget's position, size, lock or
  transparency, but keeps them, so the desktop layout survives a round trip through the phone.
- Ids are 32 lowercase hex characters (a GUID without dashes). Importing with *add next to mine* gives every
  imported widget, category and item a new id.
- Text may contain line breaks (`\n`) for multi-line items.
- A missing `version` means the current version. A file without a `boards` array isn't PinList data and is rejected.

## Objects

### Root (`AppData`)

| Field | Type | Meaning | Default |
|---|---|---|---|
| `version` | number | Format version | 1 |
| `settings` | object | `Settings` | |
| `boards` | array | `Board` objects, in order (the phone's card list uses this order too) | `[]` |
| `archive` | array | `ArchivedItem` objects, newest first | `[]` |

### `Settings`

| Field | Type | Meaning | Default |
|---|---|---|---|
| `theme` | string | Theme for menus, dialogs and the archive: `"System"`, `"Light"` or `"Dark"`. Kept per device: an import doesn't change it. | `"System"` |

### `Board` (a widget on the desktop, a card on the phone)

| Field | Type | Meaning | Default |
|---|---|---|---|
| `id` | string | Id | new GUID |
| `title` | string | Title; may be empty (shown as no title) | `"My list"` |
| `x`, `y` | number | Desktop position, in device-independent pixels (desktop only) | 100, 100 |
| `width`, `height` | number | Desktop size (desktop only) | 320, 480 |
| `locked` | bool | Position and size locked (desktop only) | false |
| `style` | string | Color: `Dark`, `Light`, `Yellow`, `Green`, `Blue`, `Purple`, `Pink`, `Peach` or `Gray` | `"Dark"` |
| `transparent` | bool | Semi-transparent background (desktop only) | true |
| `categories` | array | `Category` objects, in order | `[]` |

### `Category`

| Field | Type | Meaning | Default |
|---|---|---|---|
| `id` | string | Id | new GUID |
| `name` | string | Name | `"New category"` |
| `color` | string | Dot color, `#RRGGBB` | `"#60CDFF"` |
| `collapsed` | bool | Items hidden | false |
| `items` | array | `TodoItem` objects, in order | `[]` |

### `TodoItem`

| Field | Type | Meaning | Default |
|---|---|---|---|
| `id` | string | Id | new GUID |
| `text` | string | Text, may have line breaks | `""` |
| `createdAt` | time | When it was added | now |
| `dueDate` | date or null | Due day | null |
| `reminderAt` | time or null | When to remind | null |
| `reminderShown` | bool | The reminder was shown (it is shown only once) | false |

### `ArchivedItem` (a completed item)

| Field | Type | Meaning |
|---|---|---|
| `text` | string | Text of the item |
| `createdAt` | time | When the item was added |
| `completedAt` | time | When it was completed |
| `dueDate` | date or null | Its due day |
| `categoryId`, `categoryName`, `categoryColor` | string | The category it was in (`categoryId` is empty if unknown) – restoring puts it back there, or into the first category if that one is gone |
| `boardId`, `boardTitle` | string | The widget it was on |

Archived items have no id of their own.

## Version history

| Version | Changes |
|---|---|
| 1 | First format (PinList 2.0.0): widgets, categories, items, due dates, reminders, archive and settings |

New fields are optional and unknown fields are kept, so most changes need no new version. Raise
`AppData.CurrentVersion` only when older files have to be converted: then convert them in `Store.Parse`, add a
fixture next to `data-v1.json`, and update this file. A newer file opened by an older PinList is loaded with a
warning, and a copy of it is kept as `data.vN.json`.
