# DLLS - DriveLetterLessSync #

## Description ##
DLLS offers synchronization for files and folders between normal and unmounted (drive letter less) volumes. The whole application is PathTooLong safe and will accept normal drive letters (`C:\` or similar) and volume IDs (guid like `\\?\Volume{1b3b1146-4076-11e1-84aa-806e6f6e6963}\`). This way it provides a snapshot-like RAID-1 experience without any of the downsides as both drives can be completely individually controlled and the second drive can be unmounted (hidden in Windows) while still being accessible.
____
## Usage ##

```
#!
DLLSCLI SourceDrive DestinationDrive [-S] [-NL] [-NS] [-ON] [-IT] [-IS] [-H Hash]
```
```
#!
? / HELP            shows helpfile
D / DRIVES          print all volume-GUIDs including existing mountpoints
S / SILENT          suppress all output and window
NL / NO LOG         do not create log-file
NS / NO SYNC        compare files but do not sync
ON / ONLY NEW       only copy new files, do not compare existing files
IT / IGNORE TIME    ignore file modification time
IS / IGNORE SIZE    ignore file size
H Hash / HASH Hash  use checksums to compare files, available hashing algorithms: MD5, SHA1, SHA256
```
Examples:
```
#!
DLLSCLI D:\ \\?\Volume{1b3b1146-4076-11e1-84aa-806e6f6e6963}\ -NL
```
![dlls.png]

## Finding volume ID ##
You can pass `-D` to get a list of all hard drives including their ID and mountpoint.
Alternatively call `mountvol` via commandline for the same result.

![-D.png]
![mountvol.png]

These IDs can be used to open an explorer window, independent from the drive letter:

![run.png]

## Schedule DLLS to run automatically ##
Use Windows Task Scheduler and create a Basic Task. Enter a name and a description, "When I log on" as Trigger, "Start a program" as Action. Select DLLScli.exe and add your drives as arguments´. Save your Task. DLLS will sync automatically after logging on now.

![scheduler.png]

## Limitations ##
* empty folders on source drive will not be synchronized
* folder attributes (hidden etc.) will not be synchronized
* files which are currently being written or flagged as inaccessible by a 3rd party application can not be synchronized
* you should not use the drives while synchronization is in progress (possible file exceptions)
* using hash function is not recommended (slow), only use it if you absolutely have to
* you obviously can't delete files on destination drive if they were flagged as read-only by an administrator and you are running DLLS as normal user
* in some rare cases DLLS may fail to delete a folder on destination drive (not existing on source drive). This should be resolved on the next start-up.