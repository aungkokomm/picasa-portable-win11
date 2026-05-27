# Picasa Portable for Win 11

A portable, fully sandboxed Picasa 3.9 — runs from any folder, sees only
the photos you put in its `Pictures` subfolder, and leaves zero trace on
the host system.

Built with Sandboxie-Plus as the virtualization layer, packaged as a
one-click InnoSetup installer.

## Why this exists

Back in the Windows 7 days, I used **JauntePE** to make Picasa portable.
It worked beautifully — you could have a dozen separate "Picasa Portable"
folders, each holding a different project's photo library, completely
isolated from each other and from the host system.

The workflow looked like this:

- `PicasaPortable_Wedding2014\` — only the wedding shoot
- `PicasaPortable_FamilyArchive\` — only family photos
- `PicasaPortable_ClientJob_Acme\` — only that client's job
- `PicasaPortable_Travel_Japan\` — only the trip
- ...etc

Each folder was a self-contained Picasa with its own album database,
face-tags, edits, and watched folder. No mixing, no leakage, no
contamination of the host's "real" Picasa.

That stopped working on Windows 11 — JauntePE's runtime hooks don't
survive on modern Windows. **This project is a humble attempt to bring
the same workflow back, with a different approach**: instead of API
hooking, it uses **Sandboxie-Plus** to virtualize Picasa's file and
registry writes into a portable folder.

## What it does

- One self-extracting installer drops everything you need into a folder
  of your choice (USB drive, external SSD, or local disk).
- Double-click `Picasa.exe` — UAC once on first run, Sandboxie wizard
  once, then Picasa opens.
- Picasa scans **only** the `Pictures\` subfolder inside the portable
  folder. The host PC's My Pictures, Desktop, Documents, Downloads,
  OneDrive, and other drives are hidden from the sandboxed Picasa.
- All of Picasa's database, face-tags, edits, and settings stay inside
  the portable folder's `data\` subfolder.
- Move the folder to another PC — drive letters auto-detect; everything
  keeps working.

## Multi-library workflow (the original JauntePE idea)

After installing the first copy, just **copy the entire installed
folder** to make a new isolated library:

```
C:\PicasaPortable_Wedding2014\
C:\PicasaPortable_FamilyArchive\
C:\PicasaPortable_ClientJob_Acme\
C:\PicasaPortable_Travel_Japan\
```

Each one is fully independent — its own albums, its own face-tags, its
own `Pictures\` folder. Launch whichever you need; the others stay
untouched.

> **Note:** Sandboxie's service is shared across all copies, so they all
> share the same `Picasa` sandbox name. Don't run two copies at the same
> time — close one before launching another.

## Screenshots

*(coming soon)*

## Installation

1. Download the latest `PicasaPortable-Setup-vX.Y.exe` from
   [Releases](../../releases).
2. Run it. Pick a folder (default is `<drive>\PicasaPortable`).
3. The installer extracts ~250 MB and offers to launch Picasa.

### First run on a new PC

1. **UAC prompt** → Yes. (Registers the Sandboxie kernel service —
   needed once per PC, not per copy.)
2. **Sandboxie Setup Wizard** → "Personally, for private
   non-commercial use" → Next → Finish.
3. Picasa launches. Put photos in the `Pictures\` subfolder and
   refresh.

Subsequent launches: instant, no prompts.

## How the isolation works

| Setting | Value | Effect |
|---|---|---|
| `FileRootPath` | `<portable>\data` | Sandbox stores everything here |
| `ReadFilePath` | `<portable>\Pictures` | Only this folder is readable from outside the sandbox |
| `WriteFilePath` | `%LocalAppData%\Google`, `%AppData%\Google` | Host's Google data hidden, but sandbox can create its own |
| `ClosedFilePath` | Host Pictures/Documents/Desktop/Downloads/Music/Videos/OneDrive/Public | Fully blocked |
| `BlockNetworkFiles` | `y` | No network share access |
| `ConfigLevel` | `9` | Strict mode |

The host PC's Picasa data (if any) is hidden from the sandboxed Picasa
via `WriteFilePath`, not `ClosedFilePath`. That distinction matters:
`ClosedFilePath` would also block Picasa from writing its own database,
causing `CBlockFile::Restore err=11` errors on close. `WriteFilePath`
hides host data while still letting the sandbox create its own files at
that path.

## Folder structure (after install)

```
PicasaPortable\
├── Picasa.exe              ← Double-click this
├── picasa.ico
├── README.md
├── Pictures\               ← Your photos go here
├── data\                   ← Sandboxed Picasa data (auto-managed)
└── Sandboxie-Plus\         ← Sandboxie binaries
```

## Building from source

If you want to rebuild the launcher or repackage the installer:

- `PicasaLauncher.cs` — C# source for `Picasa.exe`. Compile with the
  .NET Framework 4.x `csc.exe` (any modern Windows already has it):
  ```
  csc.exe /target:winexe /reference:System.ServiceProcess.dll ^
    /win32icon:picasa.ico /out:Picasa.exe PicasaLauncher.cs
  ```
- `PicasaPortable.iss` — InnoSetup 6 script. Compile with `ISCC.exe`.

## Known limitations

- **Windows 11 64-bit only.** Tested on Win 11 only; older Windows
  untested.
- **If the host already has Picasa installed**, the sandboxed Picasa
  may occasionally pick up some of the host's existing Picasa database
  through paths Sandboxie's default templates allow. This project is
  designed for clean PCs where Picasa was never installed — which is
  the common case in 2026.
- **Picasa 3.9 is abandonware.** Google retired it in 2016. No updates,
  no online features that depended on Picasa Web Albums. This project
  just makes it usable again, locally, on Windows 11.

## License

[MIT](LICENSE) — © Aung Ko Ko

## Credits

- **Picasa 3.9** — Google (retired 2016). All rights to Picasa belong
  to Google.
- **Sandboxie-Plus** — David Xanatos / sandboxie-plus, GPLv3. The
  virtualization layer that makes this whole thing possible.
- **JauntePE** — the original portable-app virtualizer (Windows 7 era)
  that inspired this. RIP.

---

*Author: Aung Ko Ko*
