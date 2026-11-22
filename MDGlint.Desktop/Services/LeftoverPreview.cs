using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using MDGlint.Desktop.Helpers;
using Microsoft.Win32.SafeHandles;

namespace MDGlint.Desktop.Services;

/// <summary>
/// Finds and ends a WebView2 browser process that a closed MDGlint window left running. Every
/// MDGlint window uses the same WebView2 data folder and so shares one browser process; while a
/// leftover one holds the folder without answering, a new window waits for it and times out. Only
/// the browser process for MDGlint's own data folder whose MDGlint window is gone
/// (<see cref="PreviewProcess"/>, which has unit tests) is ever ended, with its children.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Reads other processes and ends them; the rules it applies are in "
    + "PreviewProcess and PreviewUnavailableNotice, which have unit tests.")]
public static class LeftoverPreview
{
    private const int ProcessBasicInformation = 0;
    private const int ProcessCommandLineInformation = 60;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

    /// <summary>
    /// The browser processes for <paramref name="webView2Folder"/> whose MDGlint window is gone.
    /// The caller disposes them.
    /// </summary>
    public static IReadOnlyList<Process> Find(string webView2Folder)
    {
        using var current = Process.GetCurrentProcess();
        var leftovers = new List<Process>();
        foreach (var process in Process.GetProcessesByName("msedgewebview2"))
        {
            try
            {
                if (TryRead(process.Id, out var commandLine, out var parentId)
                    && PreviewProcess.IsBrowserFor(commandLine, webView2Folder)
                    && PreviewProcess.IsOwnerGone(NameOf(parentId, out var parentStart), parentStart, current.ProcessName, process.StartTime))
                {
                    leftovers.Add(process);
                    continue;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Exited while being read, or not readable: not ours.
            }

            process.Dispose();
        }

        return leftovers;
    }

    /// <summary>Whether a leftover browser process holds <paramref name="webView2Folder"/>.</summary>
    public static bool Exists(string webView2Folder)
    {
        var found = Find(webView2Folder);
        foreach (var process in found)
        {
            process.Dispose();
        }

        return found.Count > 0;
    }

    /// <summary>How many other MDGlint windows (processes) are running.</summary>
    public static int OtherWindows()
    {
        using var current = Process.GetCurrentProcess();
        var all = Process.GetProcessesByName(current.ProcessName);
        try
        {
            return all.Count(p => p.Id != current.Id);
        }
        finally
        {
            foreach (var process in all)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Ends the leftover browser processes for <paramref name="webView2Folder"/>, with their children.</summary>
    public static void End(string webView2Folder)
    {
        foreach (var process in Find(webView2Folder))
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Already gone, or not ours to end; starting the preview again will say so.
                }
            }
        }
    }

    private static string? NameOf(int processId, out DateTime? start)
    {
        start = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            start = process.StartTime;
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static bool TryRead(int processId, out string? commandLine, out int parentId)
    {
        commandLine = null;
        parentId = 0;
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return false;
        }

        var basic = new ProcessBasicInfo();
        if (NtQueryInformationProcess(handle, ProcessBasicInformation, ref basic, Marshal.SizeOf<ProcessBasicInfo>(), out _) != 0)
        {
            return false;
        }

        parentId = (int)basic.InheritedFromUniqueProcessId;

        var status = NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out var length);
        if (status != StatusInfoLengthMismatch || length <= 0)
        {
            return false;
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, length, out _) != 0)
            {
                return false;
            }

            // A UNICODE_STRING (length in bytes, then a pointer into this same buffer).
            var bytes = Marshal.ReadInt16(buffer);
            var text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
            commandLine = Marshal.PtrToStringUni(text, (ushort)bytes / 2);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInfo
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        SafeProcessHandle process, int informationClass, ref ProcessBasicInfo information, int length, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(
        SafeProcessHandle process, int informationClass, IntPtr information, int length, out int returnLength);
}
