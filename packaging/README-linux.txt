MQMesh Updater for Linux
========================

Keeps your MQ2Nav navigation meshes in step with https://mqmesh.com, for
MacroQuest running under Wine or Proton.

The program is a single file, mqmesh-updater, with everything it needs built
in. No .NET install is required. It runs on 64-bit x86 Linux with X11 or
XWayland.

Install
-------
1. Copy mqmesh-updater into your MacroQuest folder inside the Wine prefix,
   for example ~/.wine/drive_c/MacroQuest, and make sure it is executable:
     chmod +x mqmesh-updater
2. Run it, then click Update.

It treats its own folder as the MacroQuest folder. Use Change... (or --mq) to
point it somewhere else. Meshes go to <MacroQuest>/resources/MQ2Nav, or
<MacroQuest>/MQ2Nav on older layouts. Folder and file names are matched
ignoring case, the way Wine sees them.

Command line (for cron or systemd timers)
-----------------------------------------
  mqmesh-updater --update [--mq <folder>] [--quiet] [--force]

  --update        Apply your saved choices without opening a window.
                  No display is needed.
  --mq <folder>   MacroQuest folder for this run.
  --quiet         Only print errors.
  --force         Also replace local files that match no known version.

Exit codes: 0 success, 1 some files failed, 2 could not run.

Settings are kept in $XDG_DATA_HOME/MQMeshUpdater, which is usually
~/.local/share/MQMeshUpdater.

More: https://github.com/wired420/MQMeshUpdaterv2
Bugs: https://github.com/wired420/MQMeshUpdaterv2/issues
