# MQMesh Updater v2

A desktop app for Windows and Linux that keeps your [MQ2Nav](https://github.com/macroquest/macroquest) navigation
meshes in step with [mqmesh.com](https://mqmesh.com). It downloads the meshes and doors files each zone needs and
checks every file before using it. It also lets you pick a different mesh for any zone and remembers what you picked.

This replaces the original MeshUpdater and uses the mqmesh.com v1 API.

## Features

- **Site meshes by default.** Every zone uses the mesh mqmesh.com recommends, called the Site mesh, until you choose otherwise.
- **Choose a mesh per zone.** Each zone's **Mesh** list offers the Site mesh, any community versions (shown with
  version, author and date; hover for notes), or **Don't manage**, which leaves your own file alone. Your choices
  are saved.
- **Grouped by expansion** in release order. Each expansion heading has a checkbox to manage or skip the whole expansion.
- **Instant search** by zone name or short name (Ctrl+F), plus filters: updates and missing, managed, not managed,
  and zones with community meshes.
- **Doors files** follow the zone's Site doors file. Untick **Doors** for any zone to leave its doors file alone.
- **Safe downloads.** Up to 4 files download in parallel. Each one is checked against its size and SHA-256 before it
  replaces the old file, so a broken download never overwrites a good one. Failed files can be retried, and
  **Cancel** stops the run.
- **Backups.** Replaced files are copied to `MQ2Nav/backup/<date-time>/` first. This is on by default.
- **Custom mesh protection.** If a local file matches no version on mqmesh.com, it may be a mesh you made yourself.
  The updater shows it as **Local file differs** and asks before replacing it.
- **Fast restarts.** The zone list is only downloaded again when it has changed, and local file hashes are cached.
  If mqmesh.com can't be reached, the last saved zone list is used.
- **Command-line mode** for scheduled tasks, cron and systemd timers (see below).

## Downloads

Every push to `main` builds both apps. Open the latest run on the **Actions** tab and download the `dist` artifact:

| File | |
| --- | --- |
| `MQMeshUpdater-<version>-win-x64.exe` | Windows app, a single small exe. |
| `MQMeshUpdater-<version>-win-x64.zip` | The same exe with a README. |
| `mqmesh-updater-<version>-linux-x64` | Linux app, a single self-contained file. |
| `mqmesh-updater-<version>-linux-x64.tar.gz` | The same file, already executable, with a README. |
| `SHA256SUMS.txt` | Checksums for the files above. |

## Windows

### Requirements

- Windows 10 or 11, 64-bit.
- The [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64). Newer major versions work
  too. If no suitable runtime is installed, Windows says so when the updater starts and offers a download link.

### Getting started

1. Put `MQMeshUpdater.exe` in your MacroQuest folder (rename it from the versioned download name if you like).
2. Run it. It finds your mesh folder and shows every zone's status.
3. Click **Update** to download everything that is missing or out of date.

The updater treats the folder it's in as the MacroQuest folder. Use **Change...** to point it at a different one.

## Linux

For MacroQuest running under Wine or Proton. The Linux app has the same window and features as the Windows one.

### Requirements

- 64-bit x86 Linux with X11 or XWayland. Nothing else: .NET and the UI toolkit are built into the file.
- `xdg-open` (from `xdg-utils`) for **Open** and the **Help** links.

### Getting started

1. Extract `mqmesh-updater` from the `.tar.gz` into your MacroQuest folder inside the Wine prefix, for example
   `~/.wine/drive_c/MacroQuest`. If you downloaded the bare file, run `chmod +x mqmesh-updater` first.
2. Run it. It finds your mesh folder and shows every zone's status.
3. Click **Update**.

Like the Windows app, it treats its own folder as the MacroQuest folder; use **Change...** or `--mq` otherwise.
Folder and file names are matched ignoring case, the way Wine sees them, so `resources/mq2nav` or a file named
`PoKnowledge.navmesh` is found and updated in place instead of getting a second copy beside it.

## Where files go

Meshes and doors files are saved to:

- `<MacroQuest>/resources/MQ2Nav` if that folder exists, or if `<MacroQuest>/resources` exists (current MacroQuest), otherwise
- `<MacroQuest>/MQ2Nav` (older layouts).

Files are named the way MQ2Nav expects: `<zone>.navmesh` and `<zone>_doors.json`.

### Zone status

| Status | Meaning |
| --- | --- |
| Up to date | The local files match your chosen version. |
| Update available | A newer or different version is chosen, or the doors file is missing. |
| Missing | The zone has no mesh on disk yet. |
| Local file differs | The local mesh matches no known version and may be your own. You're asked before it is replaced. |
| Not managed | You chose **Don't manage**, so the updater leaves this zone alone. |

## Command line

```
MQMeshUpdater.exe --update [--mq <folder>] [--quiet] [--force]
mqmesh-updater --update [--mq <folder>] [--quiet] [--force]
```

This applies your saved choices without opening the window. On Linux it needs no display, so it works from cron
or a systemd timer.

| Option | |
| --- | --- |
| `--update` | Download everything the managed zones need, then exit. |
| `--mq <folder>` | MacroQuest folder for this run (default: the saved one, else the program's folder). |
| `--quiet` | Only print errors. |
| `--force` | Also replace local files that match no known version. Without it they are skipped. |
| `--help` | Show usage. |

Exit codes: `0` success, `1` some files failed or the run was cancelled (Ctrl+C), `2` could not run (for example,
the site was unreachable and no zone list was saved).

Windows doesn't wait for desktop apps by default. To see output and the exit code from `cmd`, use
`start /wait MQMeshUpdater.exe --update`. From PowerShell, use
`Start-Process .\MQMeshUpdater.exe -ArgumentList '--update' -Wait -PassThru`. Task Scheduler waits on its own.
Install the .NET Desktop Runtime before scheduling `--update`. If the runtime is missing, Windows shows its
"install .NET" dialog instead of running the update.

## Settings and caches

Everything is kept in `%LOCALAPPDATA%\MQMeshUpdater` on Windows and in `$XDG_DATA_HOME/MQMeshUpdater` (usually
`~/.local/share/MQMeshUpdater`) on Linux:

| File | |
| --- | --- |
| `settings.json` | MacroQuest folder, backup option, window position, and per-zone choices (only zones you changed). |
| `manifest.json`, `manifest.etag` | The last zone list, so the updater can tell when it changed. |
| `expansions.json` | Expansion names and order. |
| `hashcache.json` | Hashes of local files by path, size and modified time. |
| `installed.json` | Hashes of files the updater wrote, so it can tell them apart from your own meshes. |

Set the `MQMESHUPDATER_DATA` environment variable to keep these somewhere else, for example next to the program for
a portable setup.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). One command runs the tests and builds both apps
into `dist/` with versioned names (the version comes from `Directory.Build.props`):

```
.\build.ps1             # Windows
./build.sh              # Linux or WSL
```

Both take `--skip-tests` and `--out <folder>`. They run `scripts/build.cs`, which publishes:

- `src/MQMeshUpdater` for `win-x64`: a framework-dependent single-file exe of about 600 KB that runs on the .NET 10
  Desktop Runtime or newer. Only Windows can write the icon and manifest into it, so build releases on Windows.
- `src/MQMeshUpdater.Linux` for `linux-x64`: a self-contained, compressed single file of about 46 MB. This works
  from Windows or Linux.

In WSL with the repository on a Windows drive (`/mnt/...`), `build.sh` runs the tests on Linux and then hands the
rest to `build.ps1` on Windows, so both test runs and a proper exe come out of one command.

The plain .NET commands work too, on either OS:

```
dotnet build
dotnet test
```

GitHub Actions runs the tests on Windows and Ubuntu, then builds and uploads `dist/` on every push to `main`.

### Layout

| Path | |
| --- | --- |
| `src/MQMeshUpdater.Core` | API client, local file tracking, planning, downloads and the command-line runner. No UI. |
| `src/MQMeshUpdater.ViewModels` | View models shared by both apps: zone list, grouping, search, filters, choices, downloads. |
| `src/MQMeshUpdater` | The Windows app (WPF). Views only. |
| `src/MQMeshUpdater.Linux` | The Linux app (Avalonia). Views only; also runs `--update` without a window. |
| `tests/MQMeshUpdater.Core.Tests` | xUnit tests for the core and the shared view models. |
| `scripts/build.cs`, `build.ps1`, `build.sh` | The build that produces `dist/`. |
| `packaging/` | The READMEs that go into the zip and tar.gz. |

## Not in this version

- Self-update of the updater itself.
- Signing in or uploading meshes. Use [mqmesh.com](https://mqmesh.com) for that.

## Reporting problems

Open an issue at https://github.com/wired420/MQMeshUpdaterv2/issues or use **Help > Report a bug** in the app.
