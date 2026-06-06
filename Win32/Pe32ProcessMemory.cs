using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZoombiniHelper.Win32;

/// <summary>
/// Win32-API memory reader for the v2 (PE32) Zoombinis game.
///
/// Loose-match attach: any process with "zoom" in the name, excluding ourselves
/// and any process with "helper" or "diagnostic" in the name.
///
/// All Read* methods take static VAs from the binary analysis (assuming
/// ImageBase 0x00400000) and translate via the runtime ASLR slide. The retail
/// v2 build has no ASLR, but the slide computation is still correct.
/// </summary>
public sealed class Pe32ProcessMemory : IDisposable
{
    private const nint StaticImageBase = 0x00400000;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    private nint _handle;
    private nint _moduleBase;
    private readonly byte[] _wordBuf = new byte[2];

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(nint process, nint baseAddr, byte[] buffer, int size, out int bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    public bool IsAttached => _handle != 0;
    public nint ModuleBase => _moduleBase;
    public nint AslrSlide => _moduleBase - StaticImageBase;
    public string? AttachedProcessName { get; private set; }
    public int AttachedProcessId { get; private set; }

    public bool TryAttach()
    {
        int selfPid = Process.GetCurrentProcess().Id;
        foreach (var proc in Process.GetProcesses())
        {
            string name;
            try { name = proc.ProcessName; }
            catch { continue; }

            if (name.IndexOf("zoom", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (proc.Id == selfPid) continue;
            if (name.IndexOf("helper", StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (name.IndexOf("diagnostic", StringComparison.OrdinalIgnoreCase) >= 0) continue;

            if (TryAttachProcess(proc)) return true;
        }
        return false;
    }

    private bool TryAttachProcess(Process proc)
    {
        var handle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, proc.Id);
        if (handle == 0) return false;
        _moduleBase = ResolveModuleBase(proc);
        _handle = handle;
        AttachedProcessName = proc.ProcessName;
        AttachedProcessId = proc.Id;
        return true;
    }

    /// <summary>
    /// Ermittelt die Modul-Base des Spiels. Die v2-PE32 lädt ohne ASLR immer
    /// bei <see cref="StaticImageBase"/> (0x00400000).
    ///
    /// <para><b>WOW64-Falle:</b> Ein 64-bit-Helper, der <c>MainModule</c> eines
    /// 32-bit-Prozesses abfragt, bekommt nicht-deterministisch mal die EXE
    /// (0x00400000), mal eine 64-bit-System-DLL (z.B. ntdll bei 0x7FFB…).
    /// Eine Base &gt; 0x7FFFFFFF kann für einen 32-bit-Prozess gar nicht stimmen
    /// — in dem Fall ignorieren und auf die feste PE32-Base zurückfallen.
    /// Davor las der Helper aus Müll-Speicher und alle Puzzle-Detektoren
    /// feuerten gleichzeitig.</para>
    /// </summary>
    private static nint ResolveModuleBase(Process proc)
    {
        // Bevorzugt: das Modul, dessen Datei auf .exe endet (= Haupt-EXE),
        // mit gültiger 32-bit-Base.
        try
        {
            foreach (ProcessModule mod in proc.Modules)
            {
                if (mod.ModuleName?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) != true)
                    continue;
                nint b = mod.BaseAddress;
                if (b > 0 && b <= 0x7FFFFFFF) return b;
            }
        }
        catch { /* Modul-Enumeration kann unter WOW64 fehlschlagen */ }

        // Fallback: MainModule, aber nur wenn 32-bit-plausibel.
        try
        {
            nint b = proc.MainModule?.BaseAddress ?? StaticImageBase;
            if (b > 0 && b <= 0x7FFFFFFF) return b;
        }
        catch { /* ignore */ }

        return StaticImageBase;
    }

    private nint Resolve(nint staticVa) => staticVa + AslrSlide;

    public byte[]? ReadBytes(nint staticVa, int count)
    {
        var buf = new byte[count];
        if (!ReadProcessMemory(_handle, Resolve(staticVa), buf, count, out int read) || read != count)
            return null;
        return buf;
    }

    public ushort? ReadWord(nint staticVa)
    {
        if (!ReadProcessMemory(_handle, Resolve(staticVa), _wordBuf, 2, out int read) || read != 2)
            return null;
        return BitConverter.ToUInt16(_wordBuf, 0);
    }

    public byte? ReadByteAt(nint staticVa)
    {
        var buf = new byte[1];
        if (!ReadProcessMemory(_handle, Resolve(staticVa), buf, 1, out int read) || read != 1)
            return null;
        return buf[0];
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            CloseHandle(_handle);
            _handle = 0;
        }
    }
}
