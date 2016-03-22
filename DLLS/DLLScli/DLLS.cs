using System;
using System.IO;
using System.Management;
using System.ComponentModel;
using System.Collections.Generic;
using System.Security.Permissions;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Runtime.ConstrainedExecution;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;


namespace DLLScli
{
    internal static class DLLS
    {
        /// <summary>
        /// Enumerates all files and directories from a given directory (PathTooLong safe, volume guid safe).
        /// </summary>
        public static WIN32_FILE[] GetEntries(string path, string searchPattern, SearchOption searchOption)
        {
            IEnumerable<WIN32_FILE> IEn = EnumerateEntries(FixPath(path), searchPattern, searchOption);
            List<WIN32_FILE> lTmp = new List<WIN32_FILE>(IEn);

            WIN32_FILE[] lEntries = new WIN32_FILE[lTmp.Count];
            lTmp.CopyTo(lEntries);

            return lEntries;
        }


        /// <summary>
        /// Enumerates all files and directories from a given directory (PathTooLong safe, volume guid safe).
        /// </summary>
        public static List<WIN32_FILE> GetEntriesList(string path, string searchPattern, SearchOption searchOption)
        {
            IEnumerable<WIN32_FILE> IEn = EnumerateEntries(FixPath(path), searchPattern, searchOption);
            List<WIN32_FILE> lEntries = new List<WIN32_FILE>(IEn);

            return lEntries;
        }


        /// <summary>
        /// Returns a file (PathTooLong safe, volume guid safe).
        /// </summary>
        public static WIN32_FILE GetFile(string path)
        {
            WIN32_FIND_DATA data = new WIN32_FIND_DATA();
            SafeFindHandle sfh = FindFirstFile(FixPathBackwards(path), data);
            if (sfh == null || sfh.IsInvalid || sfh.IsClosed)
                throw new Exception(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            if (!sfh.IsInvalid && !sfh.IsClosed)
                sfh.Close();
            sfh = null;

            return new WIN32_FILE(path, data);
        }


        /// <summary>
        /// Returns path with "\\.\" prefix.
        /// Path can be specified with "\\?\" or without prefix.
        /// </summary>
        public static string FixPath(string path)
        {
            if (!path.StartsWith(@"\\"))                                                                               // always add prefix for long path
                path =  @"\\.\" + path;
            if (path.StartsWith(@"\\?\"))                                                                              // fix .NET limitation for '?'
                return @"\\.\" + path.Substring(4);
            return path;
        }


        /// <summary>
        /// Returns volume guid path.
        /// Drive can be specified as normal drive letter, with "\\?\" or with "\\.\" prefix.
        /// </summary>
        public static string FixDrive(string drive)
        {
            if (!drive.StartsWith(@"\\"))
                return FixPath(GetVolumeID(drive));
            return FixPath(drive);
        }


        /// <summary>
        /// Returns path without drive (PathTooLong safe).
        /// </summary>
        public static string GetPathDirs(string path)
        {
            if ((path.StartsWith(@"\\.\Volume") || path.StartsWith(@"\\?\Volume")) && path.Length < 50)
                return string.Empty;
            if (path.StartsWith(@"\\.\Volume") || path.StartsWith(@"\\?\Volume"))
                return path.Substring(49);
            if (path.StartsWith(@"\\"))
                return path.Substring(7);
            if (path.Contains(@":\"))
                return path.Substring(3);
            return path;
        }


        /// <summary>
        /// Returns drive from path (PathTooLong safe).
        /// </summary>
        public static string GetPathDrive(string path)
        {
            if (path.StartsWith(@"\\.\Volume") || path.StartsWith(@"\\?\Volume"))
                return path.Substring(0, 49);
            if (path.StartsWith(@"\\"))
                return path.Substring(0, 7);
            if (path.Contains(@":\"))
                return path.Substring(0, 3);
            return path;
        }


        /// <summary>
        /// Checks if a file is existing (PathTooLong safe).
        /// Path must be specified with volume guid.
        /// </summary>
        public static bool FileExists(string path)
        {
            FileAttributes attributes = GetFileAttributes(FixPathBackwards(path));
            if ((int)attributes == -1)
                return false;

            return !attributes.HasFlag(FileAttributes.Normal);
        }


        /// <summary>
        /// Checks if a directory is existing (PathTooLong safe).
        /// Path must be specified with volume guid.
        /// </summary>
        public static bool DirExists(string path)
        {
            FileAttributes attributes = GetFileAttributes(FixPathBackwards(path));
            if ((int)attributes == -1)
                return false;

            return attributes.HasFlag(FileAttributes.Directory);
        }


        /// <summary>
        /// Creates a directory recursively (PathTooLong safe).
        /// Path must be specified with volume guid.
        /// </summary>
        public static bool CreateDirRecursive(string path)
        {
            string fixedpath = FixPathBackwards(path);

            string drive = GetPathDrive(fixedpath);
            string rootDir = GetPathDirs(fixedpath);

            string[] pathParts = rootDir.Split('\\');
            for (int i = 0; i < pathParts.Length; i++)
            {
                if (string.IsNullOrEmpty(pathParts[i]))
                    continue;

                if (i > 0)
                    pathParts[i] = pathParts[i - 1] + @"\" + pathParts[i];

                string sDir = drive + pathParts[i];

                if (!DirExists(sDir))
                {
                    if (!CreateDirectory(sDir, IntPtr.Zero))
                        throw new Exception(new Win32Exception(Marshal.GetLastWin32Error()).Message);
                }
            }
            return true;
        }


        /// <summary>
        /// Copies a file (PathTooLong safe, volume guid safe).
        /// Path must be specified with volume guid.
        /// </summary>
        public static bool FileCopy(string source, string destination)
        {
            string sSourcePath = FixPathBackwards(source);
            string sDestPath = FixPathBackwards(destination);

            if (!CopyFile(sSourcePath, sDestPath, false))
            {
                // if copying fails (overwriting fails due to destination file is read only or similar) try to directly delete destination file
                if (FileExists(sDestPath))
                    FileDelete(sDestPath);

                if (!CopyFile(sSourcePath, sDestPath, false))
                    throw new Exception(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }

            return true;
        }


        /// <summary>
        /// Deletes a file (PathTooLong safe, volume guid safe).
        /// Path must be specified with volume guid.
        /// </summary>
        public static bool FileDelete(string path)
        {
            string sFilepath = FixPathBackwards(path);
            if (!DeleteFile(FixPathBackwards(sFilepath)))
            {
                // if deleting fails (due to read only or similar) try to set fileattributes to normal
                if (FileExists(sFilepath))
                    SetFileAttributes(sFilepath, FILE_ATTRIBUTE_NORMAL);

                if (!DeleteFile(FixPathBackwards(sFilepath)))
                    throw new Exception(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }
            return true;
        }


        /// <summary>
        /// Deletes an empty directory (PathTooLong safe, volume guid safe).
        /// Path must be specified with volume guid.
        /// Can fail if trying to delete a root directory when its subdirs were deleted in rapid session.
        /// </summary>
        public static bool DirDelete(string path)
        {
            if (!RemoveDirectory(FixPathBackwards(path)))
                throw new Exception(new Win32Exception(Marshal.GetLastWin32Error()).Message);

            return true;
        }


        /// <summary>
        /// Returns the calculated hash from a file (PathTooLong safe, voulme guid safe).
        /// Path must be specified with volume guid.
        /// </summary>
        public static string GetHashFromFile(string path, HashAlgorithm algorithm)
        {
            // to make this async use FILE_FLAG_OVERLAPPED instead of FILE_ATTRIBUTE_NORMAL and set isAsync on Filestream
            SafeFileHandle sfh = CreateFile(FixPathBackwards(path), GENERIC_READ, FILE_SHARE_NONE, IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
            string hash;
            if (sfh == null || sfh.IsInvalid || sfh.IsClosed)
                throw new Exception(new Win32Exception(Marshal.GetLastWin32Error()).Message);

            using (var stream = new FileStream(sfh, FileAccess.Read, 204800))
            {
                hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", string.Empty);
            }
            if (!sfh.IsInvalid && !sfh.IsClosed)
                sfh.Close();
            sfh = null;

            return hash;
        }


        // returns volume guid from drive letter
        private static string GetVolumeID(string drive)
        {
            ManagementObjectSearcher mos = new ManagementObjectSearcher("Select * from Win32_Volume");
            foreach (ManagementObject mo in mos.Get())
            {
                var sGuid = mo["DeviceID"].ToString();
                var sMountpoint = "*** NO MOUNTPOINT FOUND ***";
                if (mo["DriveLetter"] != null && !string.IsNullOrEmpty(mo["DriveLetter"].ToString()))
                    sMountpoint = mo["DriveLetter"].ToString();
                if (sMountpoint == drive.Substring(0, 2))
                    return sGuid;
            }
            throw new DriveNotFoundException("Could not find drive '" + drive + "'!");
        }


        // revert volume guid path back to its original state
        private static string FixPathBackwards(string path)
        {
            if (path.StartsWith(@"\\.\"))                                                                           // as we use a kernel-function (import) we have to revert the guid-path back to its original state
                return @"\\?\" + path.Substring(4);
            return path;
        }


        #region IMPORTS

        internal const uint GENERIC_READ = 0x80000000;
        internal const uint FILE_SHARE_NONE = 0x0;
        internal const uint OPEN_EXISTING = 3;
        internal const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        //internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern FileAttributes GetFileAttributes(string lpFileName);

        [DllImport("kernel32.dll")]
        private static extern bool SetFileAttributes(string lpFileName, uint dwFileAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFindHandle FindFirstFile(string lpFileName, [In, Out] WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,              // file name
            uint dwDesiredAccess,           // access mode
            uint dwShareMode,               // share mode
            IntPtr lpSecurityAttributes,    // Security Attributes
            uint dwCreationDisposition,     // how to create
            uint dwFlagsAndAttributes,      // file attributes
            IntPtr hTemplateFile            // handle to template file
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
        private static extern bool CopyFile(string lpExistingFileName, string lpNewFileName, bool bFailIfExists);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool DeleteFile(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateDirectory(string lpPathName, IntPtr lpSecurityAttributes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool RemoveDirectory(string lpPathName);

        #endregion


        #region INTERNAL

        // code from "A Faster Directory Enumerator" by wilsone8
        // modified to make PathToLong-safe and to handle directories
        public static IEnumerable<WIN32_FILE> EnumerateEntries(string path, string searchPattern, SearchOption searchOption)
        {
            return new EntryEnumerable(path, searchPattern, searchOption);
        }

        private class EntryEnumerable : IEnumerable<WIN32_FILE>
        {
            private readonly string m_path;
            private readonly string m_filter;
            private readonly SearchOption m_searchOption;

            public EntryEnumerable(string path, string filter, SearchOption searchOption)
            {
                m_path = path;
                m_filter = filter;
                m_searchOption = searchOption;
            }

            public IEnumerator<WIN32_FILE> GetEnumerator()
            {
                return new FileEnumerator(m_path, m_filter, m_searchOption);
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return new FileEnumerator(m_path, m_filter, m_searchOption);
            }
        }

        [System.Security.SuppressUnmanagedCodeSecurity]
        private class FileEnumerator : IEnumerator<WIN32_FILE>
        {
            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern SafeFindHandle FindFirstFile(string fileName, [In, Out] WIN32_FIND_DATA data);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
            private static extern bool FindNextFile(SafeFindHandle hndFindFile, [In, Out, MarshalAs(UnmanagedType.LPStruct)] WIN32_FIND_DATA lpFindFileData);

            private class SearchContext
            {
                public readonly string Path;
                public Stack<string> SubdirectoriesToProcess;

                public SearchContext(string path)
                {
                    this.Path = path;
                }
            }

            private string m_path;
            private string m_filter;
            private SearchOption m_searchOption;
            private Stack<SearchContext> m_contextStack;
            private SearchContext m_currentContext;

            private SafeFindHandle m_hndFindFile;
            private WIN32_FIND_DATA m_win_find_data = new WIN32_FIND_DATA();

            public FileEnumerator(string path, string filter, SearchOption searchOption)
            {
                m_path = path;
                m_filter = filter;
                m_searchOption = searchOption;
                m_currentContext = new SearchContext(path);

                if (m_searchOption == SearchOption.AllDirectories)
                {
                    m_contextStack = new Stack<SearchContext>();
                }
            }

            public WIN32_FILE Current
            {
                get
                {
                    return new WIN32_FILE(m_path, m_win_find_data);
                }
            }

            public void Dispose()
            {
                if (m_hndFindFile != null)
                {
                    m_hndFindFile.Dispose();
                }
            }

            object System.Collections.IEnumerator.Current
            {
                get
                {
                    return new WIN32_FILE(m_path, m_win_find_data);
                }
            }

            public bool MoveNext()
            {
                bool retval = false;

                // If the handle is null, this is first call to MoveNext in the current 
                // directory. In that case, start a new search.
                if (m_currentContext.SubdirectoriesToProcess == null)
                {
                    if (m_hndFindFile == null)
                    {
                        new FileIOPermission(FileIOPermissionAccess.PathDiscovery, m_path).Demand();

                        string searchPath = Path.Combine(m_path, m_filter);
                        m_hndFindFile = FindFirstFile(searchPath, m_win_find_data);
                        retval = !m_hndFindFile.IsInvalid;
                    }
                    else
                    {
                        // Otherwise, find the next item.
                        retval = FindNextFile(m_hndFindFile, m_win_find_data);
                    }
                }

                // If the call to FindNextFile or FindFirstFile succeeded...
                if (retval)
                {
                    if (((FileAttributes)m_win_find_data.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
                    {
                        // don't ignore directories but skip system internal dirs like '.' and '..'
                        if (m_win_find_data.cFileName == "." || m_win_find_data.cFileName == "..")
                            return MoveNext();
                    }
                }
                else if (m_searchOption == SearchOption.AllDirectories)
                {
                    // SearchContext context = new SearchContext(m_hndFindFile, m_path);
                    // m_contextStack.Push(context);
                    // m_path = Path.Combine(m_path, m_win_find_data.cFileName);
                    // m_hndFindFile = null;

                    if (m_currentContext.SubdirectoriesToProcess == null)
                    {
                        string[] subDirectories = Directory.GetDirectories(m_path);
                        m_currentContext.SubdirectoriesToProcess = new Stack<string>(subDirectories);
                    }

                    if (m_currentContext.SubdirectoriesToProcess.Count > 0)
                    {
                        string subDir = m_currentContext.SubdirectoriesToProcess.Pop();

                        m_contextStack.Push(m_currentContext);
                        m_path = subDir;
                        m_hndFindFile = null;
                        m_currentContext = new SearchContext(m_path);
                        return MoveNext();
                    }

                    // If there are no more files in this directory and we are 
                    // in a sub directory, pop back up to the parent directory and
                    // continue the search from there.
                    if (m_contextStack.Count > 0)
                    {
                        m_currentContext = m_contextStack.Pop();
                        m_path = m_currentContext.Path;
                        if (m_hndFindFile != null)
                        {
                            m_hndFindFile.Close();
                            m_hndFindFile = null;
                        }
                        return MoveNext();
                    }
                }
                return retval;
            }

            public void Reset()
            {
                m_hndFindFile = null;
            }
        }

        private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            [DllImport("kernel32.dll")]
            private static extern bool FindClose(IntPtr handle);

            [SecurityPermission(SecurityAction.LinkDemand, UnmanagedCode = true)]
            internal SafeFindHandle()
                : base(true)
            {
            }

            protected override bool ReleaseHandle()
            {
                return FindClose(base.handle);
            }
        }

        #endregion
    }


    [Serializable]
    public class WIN32_FILE
    {
        public readonly string Name;
        public readonly string FullPath;
        public readonly bool isDirectory;
        public readonly string Path;
        public readonly long Size;
        public readonly FileAttributes Attributes;
        public readonly DateTime CreationTime;
        public readonly DateTime LastAccessTime;
        public readonly DateTime LastWriteTime;

        internal WIN32_FILE(string dir, WIN32_FIND_DATA fdData)
        {
            this.Attributes = fdData.dwFileAttributes;
            this.CreationTime = GetDateTime(fdData.ftCreationTime);
            this.LastAccessTime = GetDateTime(fdData.ftLastAccessTime);
            this.LastWriteTime = GetDateTime(fdData.ftLastWriteTime);
            this.Name = fdData.cFileName;
            this.isDirectory = fdData.dwFileAttributes.HasFlag(FileAttributes.Directory);
            this.FullPath = GetFullPath(dir, fdData.cFileName);
            this.Path = GetPath(FullPath, fdData.cFileName);
            if (!isDirectory)
                this.Size = GetSize(fdData.nFileSizeLow, fdData.nFileSizeHigh);
        }


        private static string GetFullPath(string sDir, string sName)
        {
            if (sDir.EndsWith("\\"))
                return sDir + sName;
            return sDir + "\\" + sName;
        }


        private static string GetPath(string sDir, string sName)
        {
            return sDir.Substring(0, sDir.Length - sName.Length);
        }


        // converts super crappy Low and High values from FileSize of WIN32_FIND_DATA to normal Bytes format
        private static long GetSize(uint uiLow, uint uiHigh)
        {
            //store nFileSizeLow
            long fDataFSize = (long)uiLow;

            //store individual file size for later accounting usage
            long fileSize;

            if (fDataFSize < 0 && (long)uiHigh > 0)
                fileSize = fDataFSize + 4294967296 + ((long)uiHigh * 4294967296);
            else
            {
                if ((long)uiHigh > 0)
                    fileSize = fDataFSize + ((long)uiHigh * 4294967296);
                else if (fDataFSize < 0)
                    fileSize = (fDataFSize + 4294967296);
                else
                    fileSize = fDataFSize;
            }
            return fileSize;
        }


        // converts super crappy FILETIME format from FileSize of WIN32_FIND_DATA to normal DateTime format
        private static DateTime GetDateTime(FILETIME ftTime)
        {
            ulong high = (ulong)ftTime.dwHighDateTime;
            uint low = (uint)ftTime.dwLowDateTime;
            long fileTime = (long)((high << 32) + low);
            return DateTime.FromFileTime(fileTime);
        }
    }


    [Serializable, StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode), BestFitMapping(false)]
    public class WIN32_FIND_DATA
    {
        public FileAttributes dwFileAttributes;
        public FILETIME ftCreationTime;
        public FILETIME ftLastAccessTime;
        public FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public int dwReserved0;
        public int dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }
}
