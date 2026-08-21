using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MiniMarkdown.Benchmarks
{
    internal static class ProcessTreeMemory
    {
        private const uint SnapshotProcesses = 0x00000002;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        internal static long GetWorkingSetBytes(int rootProcessId)
        {
            HashSet<int> processIds = GetProcessTree(rootProcessId);
            long workingSet = 0;
            foreach (int processId in processIds)
            {
                try
                {
                    using (Process process = Process.GetProcessById(processId))
                    {
                        process.Refresh();
                        workingSet += process.WorkingSet64;
                    }
                }
                catch (ArgumentException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }

            return workingSet;
        }

        private static HashSet<int> GetProcessTree(int rootProcessId)
        {
            HashSet<int> result = new HashSet<int> { rootProcessId };
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return result;
            }

            Dictionary<int, List<int>> children = new Dictionary<int, List<int>>();
            IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == InvalidHandle)
            {
                return result;
            }

            try
            {
                ProcessEntry entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                if (!Process32First(snapshot, ref entry))
                {
                    return result;
                }

                do
                {
                    List<int> childIds;
                    int parentId = unchecked((int)entry.ParentProcessId);
                    if (!children.TryGetValue(parentId, out childIds))
                    {
                        childIds = new List<int>();
                        children[parentId] = childIds;
                    }
                    childIds.Add(unchecked((int)entry.ProcessId));
                    entry.Size = (uint)Marshal.SizeOf(typeof(ProcessEntry));
                }
                while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            Queue<int> pending = new Queue<int>();
            pending.Enqueue(rootProcessId);
            while (pending.Count != 0)
            {
                int parent = pending.Dequeue();
                List<int> childIds;
                if (!children.TryGetValue(parent, out childIds))
                {
                    continue;
                }

                foreach (int childId in childIds)
                {
                    if (result.Add(childId))
                    {
                        pending.Enqueue(childId);
                    }
                }
            }

            return result;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct ProcessEntry
        {
            internal uint Size;
            internal uint Usage;
            internal uint ProcessId;
            internal IntPtr DefaultHeapId;
            internal uint ModuleId;
            internal uint Threads;
            internal uint ParentProcessId;
            internal int BasePriority;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string ExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}