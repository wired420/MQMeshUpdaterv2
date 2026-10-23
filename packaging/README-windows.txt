MQMesh Updater for Windows
==========================

Keeps your MQ2Nav navigation meshes in step with https://mqmesh.com.

Install
-------
1. Install the .NET 10 Desktop Runtime (x64) if you don't have it:
   https://dotnet.microsoft.com/download/dotnet/10.0
   Newer major versions work too. Windows offers the download if it's missing.
2. Put MQMeshUpdater.exe in your MacroQuest folder.
3. Run it, then click Update.

Meshes go to <MacroQuest>\resources\MQ2Nav, or <MacroQuest>\MQ2Nav on older
layouts. Use Change... to pick a different MacroQuest folder.

Command line (for scheduled tasks)
----------------------------------
  MQMeshUpdater.exe --update [--mq <folder>] [--quiet] [--force]

  --update        Apply your saved choices without opening the window.
  --mq <folder>   MacroQuest folder for this run.
  --quiet         Only print errors.
  --force         Also replace local files that match no known version.

Exit codes: 0 success, 1 some files failed, 2 could not run.
From cmd, use "start /wait MQMeshUpdater.exe --update" to see the output.

Settings are kept in %LOCALAPPDATA%\MQMeshUpdater.

More: https://github.com/wired420/MQMeshUpdaterv2
Bugs: https://github.com/wired420/MQMeshUpdaterv2/issues
