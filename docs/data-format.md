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
- `log.txt` / `log.old.txt` – errors and notes, at most about 1 MB each. Sync writes one line for every object a
  merge removes or overrides (ids only, never item text).
- `sync-state.json` – Google sync's own state (from 2.1.0, only after signing in; see [Sync](#sync)). Not exported
  and not backed up.
- `google-token.dat` – Windows only: the Google sign-in (refresh token), encrypted for the Windows user with DPAPI.
  Deleted when signing out. macOS keeps it in the login Keychain (service `PinList`, account
  `google-refresh-token`); Android keeps no token (Google Play services does).

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
| `syncInfo` | object or absent | `DeviceInfo`: this device's sync id and change counter (2.1.0). Not exported. | absent |

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
| `synced` | bool | The list syncs through Google Drive (2.1.0; written only when true) | false |
| `syncedChanged` | object or absent | `SyncMeta` of the last change of `synced` | absent |
| `seen` | object or absent | Seen-list: device id → highest change counter this copy of the list contains | absent |
| `sync` | object or absent | `SyncMeta` of the list's synced fields | absent |
| `orderSync` | object or absent | `SyncMeta` of the order of its categories | absent |

### `Category`

| Field | Type | Meaning | Default |
|---|---|---|---|
| `id` | string | Id | new GUID |
| `name` | string | Name | `"New category"` |
| `color` | string | Dot color, `#RRGGBB` | `"#60CDFF"` |
| `collapsed` | bool | Items hidden | false |
| `items` | array | `TodoItem` objects, in order | `[]` |
| `sync` | object or absent | `SyncMeta` (2.1.0) | absent |
| `orderSync` | object or absent | `SyncMeta` of the order of its items (2.1.0) | absent |

### `TodoItem`

| Field | Type | Meaning | Default |
|---|---|---|---|
| `id` | string | Id | new GUID |
| `text` | string | Text, may have line breaks | `""` |
| `createdAt` | time | When it was added | now |
| `dueDate` | date or null | Due day | null |
| `reminderAt` | time or null | When to remind | null |
| `reminderShown` | bool | The reminder was shown (it is shown only once) | false |
| `sync` | object or absent | `SyncMeta` (2.1.0) | absent |

### `ArchivedItem` (a completed item)

| Field | Type | Meaning |
|---|---|---|
| `id` | string | The completed item's id (2.1.0). Entries from older files get a new id when they're loaded. |
| `text` | string | Text of the item |
| `createdAt` | time | When the item was added |
| `completedAt` | time | When it was completed |
| `dueDate` | date or null | Its due day |
| `categoryId`, `categoryName`, `categoryColor` | string | The category it was in (`categoryId` is empty if unknown) – restoring puts it back there, or into the first category if that one is gone |
| `boardId`, `boardTitle` | string | The widget it was on |
| `sync` | object or absent | `SyncMeta` (2.1.0) |

### `SyncMeta` (2.1.0)

Bookkeeping that lets sync merge item by item. Every list, category, item and archived item gets it the first time
it's saved, also when sync isn't used (it never leaves the device then).

| Field | Type | Meaning |
|---|---|---|
| `born` | `Stamp` | The change that created the object |
| `changed` | `Stamp` | The last change of the object's synced fields |
| `modifiedAt` | time (UTC) | When that change was made. **The only UTC time in the file**, written with `Z`, e.g. `"2026-11-01T08:30:15.2500000Z"` |
| `hash` | string | Fingerprint (SHA-256, first 16 bytes as hex) of the synced fields when they were last stamped or merged |

A `Stamp` is `{ "device": "<device id>", "counter": <number> }`: the device that made the change and the value of
its change counter then. Counters only go up, so a stamp is never used twice.

Synced fields (what the fingerprint covers): list – title, style, transparent; category – name, color, collapsed,
its list; item – text, created, due date, reminder, reminder shown, its
category and list; archived item – all its fields. The order of a list's categories and of a category's items
has its own `SyncMeta` (`orderSync`), so reordering (also adding or removing a child) on one device and renaming on
another are two separate changes and both are kept. Positions, sizes, `locked`, the theme and the undo history are
per device and never synced.

### `DeviceInfo` (2.1.0)

| Field | Type | Meaning |
|---|---|---|
| `deviceId` | string | This device's sync id (a GUID without dashes) |
| `counter` | number | This device's change counter |

## Sync

Optional Google Drive sync (2.1.0). Everything lives in the hidden *app data folder* of the user's own Google Drive.
Each device writes only its own files, so two devices never write the same file.

### Cloud files

- `w-<listId>.<deviceId>.json` – this device's latest copy of one synced list:

  | Field | Meaning |
  |---|---|
  | `format` | Cloud file format, currently 1. A device that finds a higher number stops syncing that list and asks to update PinList; it never overwrites the file. |
  | `boardId`, `device`, `deviceName`, `updatedAt` | The list, the device that wrote the file, its name (computer name, or phone maker and model), when it was written (UTC) |
  | `seen` | Seen-list of this copy (device id → highest counter it contains) |
  | `synced`, `syncedChanged` | The sync switch and its `SyncMeta` |
  | `deleted`, `deletedChanged` | The list was deleted, and when (`SyncMeta`) |
  | `board` | `{ title, style, transparent, categoryOrder, sync, orderSync }`; null in a *marker* |
  | `categories` | `{ id, name, color, collapsed, itemOrder, sync, orderSync }` |
  | `items` | `{ id, text, createdAt, dueDate, reminderAt, reminderShown, categoryId, sync }` |
  | `archive` | The list's `ArchivedItem`s |

  Times follow the same rules as in `data.json` (wall-clock time, except `modifiedAt` and `updatedAt`). A *marker*
  has only the flags and no content: what remains after sync was turned off (`synced: false`) or the list was
  deleted (`deleted: true`).
- `reset.json` – `{ "device": "<deviceId>", "at": "<UTC time>" }`, left after *Sign out and delete cloud data*. Other
  devices that see it turn their synced lists into local lists.

### How lists are merged

For every object, "has this side already seen the other side's version?" is answered with stamps and seen-lists:
a side has seen a version when its seen-list covers that version's `changed` stamp. Changed on one side only → that
side wins. Changed on both without seeing each other → the newer `modifiedAt` wins (the same reminder shown on
either side stays shown). Present on one side only → it was deleted on the other side if that side had seen it,
otherwise it's new (an edit made after a delete wins over the delete). Completed on one side → stays completed. The
order of categories and items comes from the side whose order changed last; new ids keep their place next to their
old neighbour. A list a device doesn't have appears only if all copies of it together say it syncs and none says it
was deleted. A device's counter is also raised to the highest of its own stamps any cloud file has seen. See `src/PinList.Core/Sync/SyncMerge.cs` and the design in `docs/superpowers/specs/2026-10-21-google-sync-design.md`.

### `sync-state.json`

| Field | Meaning |
|---|---|
| `account` | `{ userId, email }` of the signed-in Google account, or null after signing out |
| `lastUserId` | The account of the last sign-in (to tell the same account from another one) |
| `deviceId`, `counter` | Copy of `syncInfo`. On load the higher counter wins, so a `data.json` restored from a backup never reuses a stamp. Data copied from another device (a different id) gets a new device id. |
| `mergedFiles` | Cloud file name → its modified time when it was last merged |
| `uploaded`, `uploadedSeen` | Own cloud file name → fingerprint of what was uploaded last, and this device's counter in its seen-list. If the list here is behind its own cloud copy (data.json restored from a backup), that copy is merged instead of overwritten. |
| `turningOff`, `stripping`, `markers`, `deletedLists` | Pending "sync off" uploads, own copies to shrink to a marker, markers still to upload, and lists deleted on this device (they never come back from other devices' older files) |
| `lastSyncUtc` | The last successful sync |

Losing this file is harmless: every cloud file is merged again, and the stamps make that safe.

### Export and import with sync

An export never contains `syncInfo`, and every list in it has `synced: false`. *Add next to mine* imports lists as
new local lists (new ids, no sync bookkeeping). *Replace everything* replaces only the lists that don't sync: synced
lists and their archived items stay, so an import can't delete lists on the user's other devices.

## Version history

| Version | Changes |
|---|---|
| 1 | First format (PinList 2.0.0): widgets, categories, items, due dates, reminders, archive and settings |
| 1 | PinList 2.1.0 adds optional fields for sync (`syncInfo`, `synced`, `syncedChanged`, `seen`, `sync`, `orderSync`) and `ArchivedItem.id`. Older versions keep them when they save. |

New fields are optional and unknown fields are kept, so most changes need no new version. Raise
`AppData.CurrentVersion` only when older files have to be converted: then convert them in `Store.Parse`, add a
fixture next to `data-v1.json`, and update this file. A newer file opened by an older PinList is loaded with a
warning, and a copy of it is kept as `data.vN.json`.
