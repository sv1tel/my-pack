using System.Runtime.InteropServices;
using System.Text;

namespace PackLauncher.Services;

public static class RamPlanner
{
    public static readonly int[] Profiles = [4, 8, 16, 32];

    public static int HeapForProfile(int pcRamGb) => pcRamGb switch
    {
        <= 4 => 2048,
        <= 8 => 4096,
        <= 16 => 8192,
        _ => 12288
    };

    public static int DetectTotalRamMb()
    {
        var status = new MemoryStatusEx();
        if (GlobalMemoryStatusEx(status))
            return (int)(status.ullTotalPhys / 1024 / 1024);
        return 8192;
    }

    public static int NormalizeProfile(int gb) => gb switch
    {
        >= 24 => 32,
        >= 12 => 16,
        >= 6 => 8,
        _ => 4
    };

    public static int DetectedProfile() => NormalizeProfile((int)Math.Round(DetectTotalRamMb() / 1024.0));

    public static int EffectiveProfile(LauncherSettings s) => s.PcRamGb > 0 ? NormalizeProfile(s.PcRamGb) : DetectedProfile();

    public static int GetMaxHeapMb(LauncherSettings s)
    {
        var wanted = HeapForProfile(EffectiveProfile(s));
        var safe = Math.Max(1536, DetectTotalRamMb() - 2560);
        return Math.Max(1024, Math.Min(wanted, safe / 512 * 512));
    }

    public static int GetMinHeapMb(int maxHeapMb) => Math.Min(maxHeapMb, Math.Max(1024, maxHeapMb / 2));

    public static List<string> DefaultJvmFlags(int maxHeapMb)
    {
        var big = maxHeapMb >= 12288;
        var threads = Environment.ProcessorCount;
        var parallel = Math.Clamp(threads, 2, 8);
        var concurrent = Math.Max(1, (int)Math.Round(parallel / 4.0));

        var flags = new List<string>
        {
            "-XX:+UseG1GC",
            "-XX:+ParallelRefProcEnabled",
            "-XX:MaxGCPauseMillis=200",
            "-XX:+UnlockExperimentalVMOptions",
            "-XX:+DisableExplicitGC",
            $"-XX:G1NewSizePercent={(big ? 40 : 30)}",
            $"-XX:G1MaxNewSizePercent={(big ? 50 : 40)}",
            $"-XX:G1HeapRegionSize={(big ? 16 : 8)}M",
            $"-XX:G1ReservePercent={(big ? 15 : 20)}",
            "-XX:G1HeapWastePercent=5",
            "-XX:G1MixedGCCountTarget=4",
            $"-XX:InitiatingHeapOccupancyPercent={(big ? 20 : 15)}",
            "-XX:G1MixedGCLiveThresholdPercent=90",
            "-XX:G1RSetUpdatingPauseTimePercent=5",
            "-XX:SurvivorRatio=32",
            "-XX:+PerfDisableSharedMem",
            "-XX:MaxTenuringThreshold=1",
            $"-XX:ParallelGCThreads={parallel}",
            $"-XX:ConcGCThreads={concurrent}"
        };

        if (maxHeapMb >= 4096 && threads >= 4)
            flags.Add("-XX:+UseStringDeduplication");

        flags.Add("-Dfile.encoding=UTF-8");
        flags.Add("-Dstdout.encoding=UTF-8");
        flags.Add("-Dlog4j2.formatMsgNoLookups=true");
        return flags;
    }

    public static List<string> BuildJvmFlags(LauncherSettings s)
    {
        if (!s.AutoJvmFlags && !string.IsNullOrWhiteSpace(s.CustomJvmFlags))
            return SplitArgs(s.CustomJvmFlags)
                .Where(a => !a.StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase) &&
                            !a.StartsWith("-Xms", StringComparison.OrdinalIgnoreCase) &&
                            !a.StartsWith("-XX:MaxHeapSize", StringComparison.OrdinalIgnoreCase) &&
                            !a.StartsWith("-XX:InitialHeapSize", StringComparison.OrdinalIgnoreCase))
                .ToList();
        return DefaultJvmFlags(GetMaxHeapMb(s));
    }

    public static string FlagsAsText(IEnumerable<string> flags) => string.Join(Environment.NewLine, flags);

    public static string MemoryArgs(LauncherSettings s)
    {
        var max = GetMaxHeapMb(s);
        return $"-Xmx{max}M -Xms{GetMinHeapMb(max)}M";
    }

    public static string FormatGb(int mb) =>
        (mb / 1024.0).ToString(mb % 1024 == 0 ? "0" : "0.#") + " " + Loc.T("gb");

    public static IEnumerable<string> SplitArgs(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            yield break;

        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var c in input)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);
}
