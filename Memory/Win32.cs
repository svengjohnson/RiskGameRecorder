
using System;
using System.Runtime.InteropServices;

namespace RiskGameRecorder.Memory;

static class Win32
{
    public const int ACCESS_READ = 0x0010;

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(int access, bool inherit, int pid);

    [DllImport("kernel32.dll")]
    public static extern bool ReadProcessMemory(
        IntPtr hProcess,
        IntPtr lpBaseAddress,
        byte[] lpBuffer,
        int size,
        out int lpNumberOfBytesRead
    );
}
