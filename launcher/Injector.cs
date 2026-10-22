using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Lanppart3Launcher
{
    // Starts the game with the mod loaded in it, without touching the game's folder: the process is
    // created suspended, UE4SS.dll (from the launcher's own runtime folder) is loaded into it through a
    // remote LoadLibraryW, then the game resumes. A game started any other way (Steam...) never sees it.
    public static class Injector
    {
        const uint CREATE_SUSPENDED = 0x4, CREATE_UNICODE_ENVIRONMENT = 0x400, CREATE_NO_WINDOW = 0x08000000;
        const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000, PAGE_READWRITE = 0x4;
        const uint WAIT_OBJECT_0 = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public int cb;
            public string lpReserved, lpDesktop, lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessW(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
                                          IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attr, UIntPtr stack, IntPtr start, IntPtr param, uint flags, out uint id);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint WaitForSingleObject(IntPtr handle, uint ms);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetExitCodeThread(IntPtr thread, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);

        // Starts exe (arguments, working folder, extra environment variables) with dll loaded in it.
        public static Process Start(string exe, string arguments, string workingDir, IDictionary<string, string> environment,
                                    string dll, bool hidden)
        {
            var env = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry e in Environment.GetEnvironmentVariables()) env[(string)e.Key] = (string)e.Value;
            foreach (var e in environment) env[e.Key] = e.Value;
            var block = new StringBuilder();
            foreach (var e in env) block.Append(e.Key).Append('=').Append(e.Value).Append('\0');
            block.Append('\0');
            var envPtr = Marshal.StringToHGlobalUni(block.ToString());

            var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)) };
            var cmd = new StringBuilder("\"" + exe + "\" " + arguments);
            uint flags = CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | (hidden ? CREATE_NO_WINDOW : 0);
            PROCESS_INFORMATION pi;
            try
            {
                if (!CreateProcessW(exe, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, envPtr, workingDir, ref si, out pi))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Impossible de démarrer le jeu");
            }
            finally { Marshal.FreeHGlobal(envPtr); }

            try
            {
                LoadLibraryIn(pi.hProcess, dll);
                ResumeThread(pi.hThread);
                return Process.GetProcessById(pi.dwProcessId);
            }
            catch
            {
                TerminateProcess(pi.hProcess, 1);
                throw;
            }
            finally
            {
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
            }
        }

        static void LoadLibraryIn(IntPtr process, string dll)
        {
            var path = Encoding.Unicode.GetBytes(dll + "\0");
            var size = (UIntPtr)path.Length;
            var remote = VirtualAllocEx(process, IntPtr.Zero, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (remote == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Injection du mod (mémoire)");
            try
            {
                UIntPtr written;
                if (!WriteProcessMemory(process, remote, path, size, out written))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Injection du mod (écriture)");
                // kernel32 sits at the same address in every process of the session.
                var loadLibrary = GetProcAddress(GetModuleHandleW("kernel32.dll"), "LoadLibraryW");
                uint threadId;
                var thread = CreateRemoteThread(process, IntPtr.Zero, UIntPtr.Zero, loadLibrary, remote, 0, out threadId);
                if (thread == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Injection du mod (thread)");
                try
                {
                    if (WaitForSingleObject(thread, 60000) != WAIT_OBJECT_0)
                        throw new Exception("Injection du mod : le jeu ne répond pas.");
                    uint module;
                    GetExitCodeThread(thread, out module);
                    if (module == 0) throw new Exception("Injection du mod : UE4SS.dll n'a pas pu être chargé.");
                }
                finally { CloseHandle(thread); }
            }
            finally { VirtualFreeEx(process, remote, UIntPtr.Zero, MEM_RELEASE); }
        }
    }
}
