---
title: PinList privacy policy
---

# PinList privacy policy

*Effective 1 November 2026*

PinList is a to-do list app for Windows, macOS and Android, made by one developer as an open-source project ([source code](https://github.com/FilipRaic/pin-list)). This page explains what happens with your data.

**In short:** your lists stay on your devices. If you turn on the optional Google Drive sync, they are also stored in a hidden folder in **your own** Google Drive, so your devices can share them. The developer never receives, sees or stores any of your data, and there is no PinList server.

## Without sync (the default)

- Everything you put into PinList – lists, items, categories, due dates, reminders, the archive and settings – is saved only on your device, in PinList's data folder:
  - Windows: `%APPDATA%\PinList\`
  - macOS: `~/Library/Application Support/PinList/`
  - Android: the app's private storage
- PinList doesn't need an account and doesn't connect to the internet.
- There are no ads, no analytics, no tracking and no crash reporting. Errors are written only to a local file (`log.txt`) in the data folder.
- Export creates a file wherever you choose to save it. What happens with that file is up to you.

## With Google Drive sync (optional, off by default)

Sync is off until you sign in with Google **and** turn it on for a list. Lists you don't turn it on for never leave your device.

**Where your data goes.** Synced lists are stored only in the *app data folder* of your own Google Drive (Google's `appDataFolder`): a hidden folder that only PinList can use and that doesn't appear among your Drive files. Your devices signed in with the same Google account read and write it directly. Nobody else's devices and no PinList server are involved.

**What is stored there** for each synced list: its title, color, categories, items (text, creation time, due date, reminder), its completed items, and technical information PinList needs to merge changes correctly (ids, change times and counters, and the name of each of your devices, such as your computer's name or your phone's model, so messages can say where a change came from).

**What PinList can access in your Google account.** PinList asks only for:

| Permission | Why |
|---|---|
| Its own app data folder in your Google Drive (`drive.appdata`) | To store and read your synced lists. PinList **can't** see, open or change any of your other Drive files. |
| Your email address and Google account id (`openid`, `userinfo.email`) | To show "Signed in as …" and to tell your accounts apart. They're stored only on your device. |

**Your sign-in** stays on your device:
- Windows: encrypted for your Windows user account (Windows DPAPI), in `google-token.dat` in the data folder
- macOS: in your login Keychain
- Android: kept by Google Play services; PinList doesn't store a sign-in token itself

On Android, PinList also syncs in the background (about every 15 minutes, only while you're signed in and at least one list is synced), using Android's standard background job system.

**Signing out** removes your sign-in from the device and tells Google to revoke PinList's access. Your lists stay on your devices.

## Deleting your data

- **On a device:** delete lists in PinList, or uninstall PinList and delete its data folder (on Android, uninstalling removes the data).
- **In Google Drive:** in PinList choose **Sign out and delete cloud data**, or in Google Drive go to **Settings → Manage apps → PinList → Delete hidden app data**.
- **Remove PinList's access** to your Google account at any time at <https://myaccount.google.com/permissions>.

## Google API Services User Data Policy

PinList's use and transfer of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements. Data from your Google account is used only to provide sync to you, is never transferred to anyone else, is never used for advertising, and is never read by a person.

## Children

PinList isn't aimed at children and doesn't knowingly collect anything about anyone; it has nowhere to send data except your own Google Drive.

## Changes to this policy

If this policy changes, the new version is published on this page with a new effective date, and the change is noted in the [changelog](https://github.com/FilipRaic/pin-list/blob/master/CHANGELOG.md).

## Contact

Questions or requests about privacy: open an issue at <https://github.com/FilipRaic/pin-list/issues>.
