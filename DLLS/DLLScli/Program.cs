/*
    DLLScli /?
    Synchronize files and folders between drives even if unmounted

    DLLSCLI SourceDrive DestinationDrive [-S] [-NL] [-NS] [-ON] [-IT] [-IS] [-H Hash]

    COMMANDS:
    ? / HELP            shows helpfile
    D / DRIVES          print all volume-GUIDs including existing mountpoints
    S / SILENT          suppress all output and window
    NL / NO LOG         do not create log-file
    NS / NO SYNC        compare files but do not sync
    ON / ONLY NEW       only copy new files, do not compare existing files
    IT / IGNORE TIME    ignore file modification time
    IS / IGNORE SIZE    ignore file size
    H Hash / HASH Hash  use checksums to compare files, available hashing algorithms: MD5, SHA1, SHA256

    NOTES:
    * empty folders on source drive will not be synchronized
    * folder attributes (hidden etc.) will not be synchronized
    * files which are currently being written or flagged as inaccessible by a 3rd party application can not be synchronized
    * you should not use the drives while synchronization is in progress (possible file exceptions)
    * using hash function is not recommended (slow), only use it if you absolutely have to
    * you obviously can't delete files on destination drive if they were flagged as read-only by an administrator and you are running DLLS as normal user
    * in some rare cases DLLS may fail to delete a folder on destination drive (not existing on source drive). This should be resolved on the next start-up.
*/

using System;
using System.IO;
using System.Linq;
using System.Management;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.InteropServices;


namespace DLLScli
{
    class Program
    {
        internal static List<string> lFilesToDelete;
        internal static List<string> lDirsToDelete;
        internal static WIN32_FILE[] FilesToSync;
        internal static HashAlgorithm haChecksumAlgorithm;
        internal static string sSourceDrive;
        internal static string sDestDrive;
        internal static string sLogPath;
        internal static long lLastProgress;
        internal static long lSyncSize;
        internal static bool bOnlyNew;
        internal static bool bSilent;
        internal static bool bIgnoreTime;
        internal static bool bIgnoreSize;
        internal static bool bNoSync;
        internal static bool bCheckHash;
        internal static bool bNoLog;


        static void Main(string[] args)
        {
            // hide console window if silent option is present
            if (args.Contains("-s") || args.Contains("-S"))
            {
                var handle = GetConsoleWindow();
                ShowWindow(handle, 0);
            }

            Console.Title = "DLLS v1.0.0";
            // ASCII-Art created with http://patorjk.com/software/taag/
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine(@"      ____  __    __   _____ ");
            Console.WriteLine(@"     / __ \/ /   / /  / ___/ ");
            Console.WriteLine(@"    / / / / /   / /   \__ \  ");
            Console.WriteLine(@"   / /_/ / /___/ /______/ /  ");
            Console.WriteLine(@"  /_____/_____/_____/____/   ");
            Console.WriteLine(@"                             ");
            Console.WriteLine("[ DriveLetterLessSync v1.0.0 ]");
            Console.WriteLine("      - by Uberhalit -");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;

            if (args.Length < 1)
            {
                PrintError("No commands specified!");
                Console.ReadLine();
                Environment.Exit(0);
            }

            // skip first two args (drives)
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-?":
                        Console.WriteLine("DLLScli /?");
                        Console.WriteLine("Synchronize files and folders between drives even if unmounted");
                        Console.WriteLine();
                        Console.WriteLine("DLLSCLI SourceDrive DestinationDrive [-S] [-NL] [-NS] [-ON] [-IT] [-IS] [-H Hash]");
                        Console.WriteLine();
                        Console.WriteLine("S / SILENT          suppress all output and window");
                        Console.WriteLine("NL / NO LOG         do not create log-file");
                        Console.WriteLine("NS / NO SYNC        compare files but do not sync");
                        Console.WriteLine("ON / ONLY NEW       only copy new files, do not compare existing files");
                        Console.WriteLine("IT / IGNORE TIME    ignore file modification time");
                        Console.WriteLine("IS / IGNORE SIZE    ignore file size");
                        Console.WriteLine("H Hash / HASH Hash  use checksums to compare files, available hashing algorithms: MD5, SHA1, SHA256");
                        Console.ReadLine();
                        Environment.Exit(0);
                        break;
                    case "-d":
                        Drives();
                        Console.ReadLine();
                        Environment.Exit(0);
                        break;
                    case "-s":
                        bSilent = true;
                        break;
                    case "-nl":
                        bNoLog = true;
                        break;
                    case "-ns":
                        bNoSync = true;
                        break;
                    case "-on":
                        bOnlyNew = true;
                        break;
                    case "-it":
                        bIgnoreTime = true;
                        break;
                    case "-is":
                        bIgnoreSize = true;
                        break;
                    case "-h":
                        if (args.Length > i + 1)
                        {
                            bCheckHash = true;
                            switch (args[i + 1].ToLower())
                            {
                                case "md5":
                                    haChecksumAlgorithm = new MD5CryptoServiceProvider();
                                    break;
                                case "sha1":
                                    haChecksumAlgorithm = new SHA1Managed();
                                    break;
                                case "sha256":
                                    haChecksumAlgorithm = new SHA256Managed();
                                    break;
                                default:
                                    PrintError("'" + args[i + 1] + "' is not a valid option!");
                                    Console.ReadLine();
                                    Environment.Exit(0);
                                    break;
                            }
                        }
                        break;
                }
            }

            // check if provided drive-pathes exist
            try
            {
                // bug: due to a bug or limitation in .NET volume-GUID-pathes won't work in most .NET-functions except we replace '?' with '.'
                // https://msdn.microsoft.com/en-us/library/aa365247.aspx
                if (Directory.Exists(args[0].Replace('?', '.')) && Directory.Exists(args[1].Replace('?', '.')))
                {
                    // get full volume guid path for provied drives
                    sSourceDrive = DLLS.FixDrive(args[0]);
                    sDestDrive = DLLS.FixDrive(args[1]);
                }
                else
                {
                    PrintError("Unable to find provided drives!");
                    Logger("ERROR: Unable to find provided drives!");
                    if (!bSilent)
                        Console.ReadLine();
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                PrintError(ex.Message);
                Logger("ERROR: " + ex.Message);
                if (!bSilent)
                    Console.ReadLine();
                Environment.Exit(0);
            }

            PrintInfo("Starting operation...");
            Console.WriteLine();

            if (!bNoLog)
            {
                // get current executable directory and create logs folder if not already present
                string sLogsDir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "logs");
                if (!Directory.Exists(sLogsDir))
                    Directory.CreateDirectory(sLogsDir);

                if (Directory.Exists(sLogsDir))
                    sLogPath = sLogsDir + @"\DLLS_" + DateTime.Today.ToString("d") + ".log";
                else
                    bNoLog = true;

                Logger("DLLScli - Starting operation...");
            }

            Console.CursorVisible = false;
            lFilesToDelete = new List<string>();
            lDirsToDelete = new List<string>();

            if (bCheckHash)
            {
                Print("WARNING: hash option detected. This can take a VERY long time.");
                Logger("WARNING: hash option detected. This can take a VERY long time.");
            }

            bool noerror1 = Find(sSourceDrive, sDestDrive);

            if (bNoSync)
            {
                Console.CursorVisible = true;
                // get user input if an error has occured
                if (!bSilent && !noerror1)
                    Console.ReadLine();
                Environment.Exit(0);
            }

            bool noerror2 = Sync(sDestDrive, FilesToSync, lFilesToDelete, lDirsToDelete);

            // get user input if an error has occured
            if (!bSilent && (!noerror1 || !noerror2))
            {
                Console.CursorVisible = true;
                Console.ReadLine();
            }
        }


        #region FUNCTIONS

        // DRIVES - list all volume GUIDs and mountpoints
        internal static void Drives()
        {
            PrintInfo("VOLUMES:");
            Console.WriteLine("");
            ManagementObjectSearcher mos = new ManagementObjectSearcher("Select * from Win32_Volume");
            foreach (ManagementObject mo in mos.Get())
            {
                var sGuid = mo["DeviceID"].ToString();
                var sMountpoint = "*** NO MOUNTPOINT FOUND ***";
                if (mo["DriveLetter"] != null && !string.IsNullOrEmpty(mo["DriveLetter"].ToString()))
                    sMountpoint = mo["DriveLetter"].ToString();
                Console.WriteLine("\t" + sGuid);
                Console.WriteLine("\t\t" + sMountpoint);
                Console.WriteLine("");
            }
        }


        // find all differences
        internal static bool Find(string sSourceRoot, string sDestRoot)
        {
            long lTotalSize = 0;                                                                                        // total size of all processed files in bytes
            List<string> lErrorList = new List<string>();                                                               // list of all errors which occured during process
            List<string> lFilesSource = new List<string>();                                                             // all relative file pathes from source drive (without drive)
            List<string> lDirsSource = new List<string>();                                                              // all relative folder pathes from source drive (without drive)

            // get all entries on drive root (TopDirectoryOnly)
            // we have to skip volume specific folders here ($RECYCLE.BIN / System Volume Information)
            List<string> lRootDirs = Directory.GetDirectories(sSourceRoot).Where(s => !s.EndsWith("$RECYCLE.BIN") && !s.EndsWith("System Volume Information")).ToList();
            List<WIN32_FILE> lEntries = DLLS.GetEntriesList(sSourceRoot, "*", SearchOption.TopDirectoryOnly).Where(s => !s.Name.EndsWith("$RECYCLE.BIN") && !s.Name.EndsWith("System Volume Information")).ToList();

            // get all entries from root directories and append to entries list
            foreach (var sDir in lRootDirs)
            {
                lEntries.AddRange(DLLS.GetEntriesList(sDir, "*", SearchOption.AllDirectories));
            }

            // remove any duplicates?
            //var xEntries = new HashSet<WIN32_FILE>(lEntries);                                                         // HashSet is a lot faster than Distinct().ToList() when > 1*10^6 entries

            // copy entry list to WIN32_FILE array
            WIN32_FILE[] aSoureFiles = new WIN32_FILE[lEntries.Count];
            lEntries.CopyTo(aSoureFiles);
            lRootDirs.Clear();
            lEntries.Clear();                                                                                           // IMPORTANT: we will reuse lEntries as a temporary list

            Print("Found " + aSoureFiles.Length + " entries on source drive.");
            int iDirs = 0;                                                                                              // count all directories
            int iFiles = 0;                                                                                             // count all files

            foreach (var SourceFile in aSoureFiles)
            {
                // handle directories
                if (SourceFile.isDirectory)
                {
                    iDirs++;
                    if (!lDirsSource.Contains(DLLS.GetPathDirs(SourceFile.FullPath)))
                        lDirsSource.Add(DLLS.GetPathDirs(SourceFile.FullPath));
                    continue;
                }

                iFiles++;
                lTotalSize += SourceFile.Size;                                                                          // add filesize to total size
                lFilesSource.Add(DLLS.GetPathDirs(SourceFile.FullPath));                                                // add full relative path from file

                string sDestPath = sDestRoot + DLLS.GetPathDirs(SourceFile.FullPath);                                   // create a hypothetically destination path for current file
                bool bAdd = false;                                                                                      // should the file be synchronized?

                try
                {
                    // if file does not exist on destination drive (new file)
                    if (!DLLS.FileExists(sDestPath))
                        bAdd = true;

                    else if (!bOnlyNew)
                    {
                        WIN32_FILE DestFile = DLLS.GetFile(sDestPath);                                                  // get file infos

                        if (!bIgnoreSize)                                                                               // compare filesizes
                        {
                            if (SourceFile.Size != DestFile.Size)
                                bAdd = true;
                        }
                        if (!bIgnoreTime && !bAdd)                                                                      // compare last modification time
                        {
                            if (SourceFile.LastWriteTime != DestFile.LastWriteTime)
                                bAdd = true;
                        }
                        if (bCheckHash && !bAdd)                                                                        // calculate and compare checksums
                        {
                            string sSourceHash = DLLS.GetHashFromFile(SourceFile.FullPath, haChecksumAlgorithm);
                            string sDestHash = DLLS.GetHashFromFile(DestFile.FullPath, haChecksumAlgorithm);

                            if (sSourceHash != sDestHash)
                                bAdd = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lErrorList.Add(ex.Message);                                                                         // add any error to error list for now
                }
                
                // add file which will be synchronized to entries list (temporary)
                if (bAdd)
                {
                    lEntries.Add(SourceFile);
                    lSyncSize += SourceFile.Size;
                }
                UpdateProgress(iFiles + iDirs, aSoureFiles.Length);                                                     // show progress
            }

            Print("");
            Print("");

            // copy all file information which shall be synchronized to array
            FilesToSync = new WIN32_FILE[lEntries.Count];
            lEntries.CopyTo(FilesToSync);
            lEntries.Clear();

            // get all entries from destination drive
            lRootDirs = Directory.GetDirectories(sDestRoot).Where(s => !s.EndsWith("$RECYCLE.BIN") && !s.EndsWith("System Volume Information")).ToList();
            lEntries = DLLS.GetEntriesList(sDestRoot, "*", SearchOption.TopDirectoryOnly).Where(s => !s.Name.EndsWith("$RECYCLE.BIN") && !s.Name.EndsWith("System Volume Information")).ToList();
            foreach (var sDir in lRootDirs)
            {
                lEntries.AddRange(DLLS.GetEntriesList(sDir, "*", SearchOption.AllDirectories));
            }

            WIN32_FILE[] aDestFiles = new WIN32_FILE[lEntries.Count];
            lEntries.CopyTo(aDestFiles);
            lEntries.Clear();
            lRootDirs.Clear();

            Print("Found " + aDestFiles.Length + " entries on destination drive.");

            foreach (var DestFile in aDestFiles)
            {
                // handle directories
                if (DestFile.isDirectory)
                {
                    // mark folder for deletation if relative path of destination dir is not present on source drive
                    if (!lDirsSource.Contains(DLLS.GetPathDirs(DestFile.FullPath)))
                    {
                        if (!lDirsToDelete.Contains(DestFile.FullPath))
                            lDirsToDelete.Add(DestFile.FullPath);                                                       // fill list with full path from destination drive
                    }
                    else
                        lDirsSource.Remove(DLLS.GetPathDirs(DestFile.FullPath));                                        // remove entry from source dir list so we can speed up future look up
                    continue;
                }

                // handle files
                if (!lFilesSource.Contains(DLLS.GetPathDirs(DestFile.FullPath)))
                {
                    lFilesToDelete.Add(DestFile.FullPath);                                                              // fill list with full path from destination drive
                }
                else
                    lFilesSource.Remove(DLLS.GetPathDirs(DestFile.FullPath));                                           // remove entry from source file list so we can speed up future look up
            }

            // print all errors
            if (lErrorList.Count > 0)
            {
                PrintError("ERROR: ");
                Logger("ERROR: ");
                foreach (var sErrorMsg in lErrorList)
                {
                    Print(sErrorMsg);
                    Logger(sErrorMsg);
                }
                Print("");
                Print("");
                Logger("");
            }
            Print("Processed " + iFiles + " files on source drive (" + String.Format("{0:0.00}", lTotalSize / 1073741824f) + " GiB)");
            Logger("Processed " + iFiles + " files on source drive (" + String.Format("{0:0.00}", lTotalSize / 1073741824f) + " GiB)");
            Print(FilesToSync.Length + " files to sync (" + String.Format("{0:0.00}", lSyncSize / 1073741824f) + " GiB)");
            Logger(FilesToSync.Length + " files to sync (" + String.Format("{0:0.00}", lSyncSize / 1073741824f) + " GiB)");
            Print(lFilesToDelete.Count + " files to delete on destination drive");
            Logger(lFilesToDelete.Count + " files to delete on destination drive");
            Print(lDirsToDelete.Count + " folders to delete on destination drive");
            Logger(lDirsToDelete.Count + " folders to delete on destination drive");

            // return result
            return lErrorList.Count < 1;
        }


        // sync files
        internal static bool Sync(string sSourceRoot, WIN32_FILE[] Files, List<string> lFiles_Delete, List<string> lDirs_Delete)
        {
            List<string> lErrorList = new List<string>();

            // copy files
            int iFilesCopied = 0;
            long lCopiedSize = 0;
            foreach (var SourceFile in Files)
            {
                string DestPath = sSourceRoot + DLLS.GetPathDirs(SourceFile.FullPath);
                string DestDir = sSourceRoot + DLLS.GetPathDirs(SourceFile.Path);

                try
                {
                    // create directory recursivle if not existing
                    if (!DLLS.DirExists(DestDir))
                        DLLS.CreateDirRecursive(DestDir);
                    DLLS.FileCopy(SourceFile.FullPath, DestPath);
                }
                catch (Exception ex)
                {
                    lErrorList.Add(ex.Message + " - " + SourceFile.FullPath);
                    continue;
                }
                lCopiedSize += SourceFile.Size;
                UpdateProgress(lCopiedSize, lSyncSize);
                iFilesCopied++;
            }

            // delete files
            int iFilesDeleted = 0;
            foreach (var FilePath in lFiles_Delete)
            {
                try
                {
                    DLLS.FileDelete(FilePath);
                }
                catch (Exception ex)
                {
                    lErrorList.Add(ex.Message + " - " + FilePath);
                    continue;
                }
                iFilesDeleted++;
            }


            // sort and reverse dictionary list so dirs will get removed from upper to lower (otherwise DirDelete will fail if directory is not empty - which should not happen but safe is safe)
            lDirs_Delete.Sort();
            lDirs_Delete.Reverse();

            // delete folders
            int iDirsDeleted = 0;
            foreach (var DirPath in lDirs_Delete)
            {
                try
                {
                    DLLS.DirDelete(DirPath);
                }
                catch (Exception)
                {
                    // UGLY CODE
                    // sometimes DirDelete will fail with DirectoryNotEmpty because the system can't keep up with already deleted folders
                    // so we have to wait a few ms and then try again
                    System.Threading.Thread.Sleep(50);
                    try
                    {
                        DLLS.DirDelete(DirPath);
                    }
                    catch (Exception ex)
                    {
                        lErrorList.Add(ex.Message + " - " + DirPath);
                        continue;
                    }
                    
                }
                iDirsDeleted++;
            }

            Print("");
            Print("");

            if (lErrorList.Count > 0)
            {
                PrintError("Error while tring to sync files: ");
                Logger("ERROR: Error while tring to sync files: ");
                foreach (var sErrorMsg in lErrorList)
                {
                    Print(sErrorMsg);
                    Logger(sErrorMsg);
                }
                Print("");
                Print("");
                Logger("");
            }

            Logger("Copied " + iFilesCopied + "/" + Files.Length + " files (" + String.Format("{0:0.00}", lCopiedSize / 1073741824f) + " GiB)");
            Print("Copied " + iFilesCopied + "/" + Files.Length + " files (" + String.Format("{0:0.00}", lCopiedSize / 1073741824f) + " GiB)");
            Logger("Deleted " + iFilesDeleted + "/" + lFilesToDelete.Count + " files");
            Print("Deleted " + iFilesDeleted + "/" + lFilesToDelete.Count + " files");
            Logger("Deleted " + iDirsDeleted + "/" + lDirsToDelete.Count + " folders");
            Print("Deleted " + iDirsDeleted + "/" + lDirsToDelete.Count + " folders");

            return lErrorList.Count < 1;
        }


        #region PRINTING

        // show progress
        internal static void UpdateProgress(long lCurrent, long lTotal)
        {
            if (bSilent)
                return;

            int iProgress = (int)(lCurrent / (lTotal / 100));                                                           // curent progress in percent

            // IMPORTANT: do not remove this check here
            // otherwise Find() will take about 10 times longer because UpdateProgress will draw on every single file
            if (iProgress < (lLastProgress / (lTotal / 100) + 2))
                return;

            lLastProgress = lCurrent;                                                                                   // set current progress

            Console.CursorLeft = 0;
            Console.Write("                             ");                                                             // clear line
            Console.CursorLeft = 0;
            Console.Write(String.Format("{0:0}", iProgress) + "%");

            Console.CursorTop = Console.CursorTop + 1;
            Console.CursorLeft = 0;
            Console.Write("[");
            Console.CursorLeft = 50;
            Console.Write("]");
            Console.CursorLeft = 1;

            int iBar = 2;                                                                                              // one bar represents 2.0%
            int i = 1;

            while (iProgress > iBar)
            {
                Console.CursorLeft = i;
                Console.Write("█");
                iBar += 2;
                i++;
            }

            Console.CursorTop = Console.CursorTop - 1;
        }


        // prints error messages
        internal static void PrintError(string msg)
        {
            if (bSilent)
                return;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[!] ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("ERROR: " + msg);
        }


        // prints info messages
        internal static void PrintInfo(string msg)
        {
            if (bSilent)
                return;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("[+] ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine(msg);
        }


        // prints messages
        internal static void Print(string msg)
        {
            if (!bSilent)
                Console.WriteLine(msg);
        }


        // log messages to file
        internal static void Logger(string msg)
        {
            if (bNoLog)
                return;
            try
            {
                using (StreamWriter writer = new StreamWriter(sLogPath, true))
                {
                    writer.WriteLine("[" + DateTime.Now + "] " + msg);
                }
            }
            catch (Exception)
            {
                // nothing
            }
        }

        #endregion

        #endregion


        #region IMPORTS

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        #endregion
    }
}
