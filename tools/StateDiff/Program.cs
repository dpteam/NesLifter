// State differ for lifted games.
//
//   dotnet run --project tools/StateDiff -- --a dirA --b dirB
//
// Compares two directories of state_NNNNNN.txt snapshots written by a lifted
// game with `--shots ... --dump-state`, and reports the first frame where the
// two runs disagree, field by field, down to individual RAM addresses.
//
// The point is to stop debugging by eye. A lifted game and a reference emulator
// both produce the same snapshot format; the first divergent frame is the frame
// where the bug happens, and the field that differs says which subsystem to look
// at. Comparing two builds of the lifter works the same way, which is how a
// regression gets attributed to the change that caused it instead of guessed at.

using System.Globalization;

namespace NesLifter.StateDiff;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? dirA = null;
        string? dirB = null;
        int maxDiffs = 40;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--a" or "-a":
                    if (i + 1 < args.Length) dirA = args[++i];
                    break;

                case "--b" or "-b":
                    if (i + 1 < args.Length) dirB = args[++i];
                    break;

                case "--max" or "-m":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int m)) maxDiffs = m;
                    i++;
                    break;
            }
        }

        if (dirA == null || dirB == null || !Directory.Exists(dirA) || !Directory.Exists(dirB))
        {
            Console.Error.WriteLine("Usage: StateDiff --a <dirA> --b <dirB> [--max 40]");
            return 1;
        }

        List<string> a = Files(dirA);
        List<string> b = Files(dirB);

        if (a.Count == 0 || b.Count == 0)
        {
            Console.Error.WriteLine("No state_*.txt found. Run the game with --shots ... --dump-state.");
            return 1;
        }

        // Match by frame number, not by position: the two runs may have dumped
        // different frames or one may have trapped early.
        Dictionary<int, string> byFrameA = Index(a);
        Dictionary<int, string> byFrameB = Index(b);

        List<int> frames = byFrameA.Keys.Intersect(byFrameB.Keys).OrderBy(f => f).ToList();

        if (frames.Count == 0)
        {
            Console.Error.WriteLine("No frames in common. A has " + byFrameA.Count + ", B has " + byFrameB.Count + ".");
            return 1;
        }

        Console.WriteLine("comparing " + frames.Count + " frames: " + string.Join(", ", frames));
        Console.WriteLine();

        int reported = 0;
        int divergentFrames = 0;

        foreach (int frame in frames)
        {
            List<string> diffs = Compare(byFrameA[frame], byFrameB[frame]);

            if (diffs.Count == 0)
            {
                Console.WriteLine("frame " + frame + ": identical");
                continue;
            }

            divergentFrames++;

            Console.WriteLine("frame " + frame + ": " + diffs.Count + " difference(s)");

            foreach (string line in diffs)
            {
                if (reported++ >= maxDiffs)
                    break;

                Console.WriteLine("  " + line);
            }

            if (reported >= maxDiffs)
            {
                Console.WriteLine("  ... stopping after " + maxDiffs + " differences (--max to raise)");
                break;
            }
        }

        Console.WriteLine();
        Console.WriteLine(divergentFrames == 0
            ? "Runs are identical on every compared frame."
            : divergentFrames + " of " + frames.Count + " compared frames differ.");

        return divergentFrames == 0 ? 0 : 2;
    }

    private static List<string> Files(string dir)
    {
        return Directory.GetFiles(dir, "state_*.txt").OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    private static Dictionary<int, string> Index(List<string> paths)
    {
        Dictionary<int, string> result = new Dictionary<int, string>();

        foreach (string path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string digits = name.Substring(name.LastIndexOf('_') + 1);

            if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame))
                result[frame] = path;
        }

        return result;
    }

    /// <summary>
    /// Compares two snapshots and returns human-readable differences.
    ///
    /// RAM lines are compared per byte so the report names the address, not just
    /// "line 34 differs". That address is usually enough to identify which
    /// subsystem went wrong: $0000-$07FF is the game's own state, and a value
    /// that never advances is a stalled state machine.
    /// </summary>
    private static List<string> Compare(string pathA, string pathB)
    {
        List<string> diffs = new List<string>();

        string[] linesA = File.ReadAllLines(pathA);
        string[] linesB = File.ReadAllLines(pathB);

        Dictionary<string, string> scalarsA = Scalars(linesA);
        Dictionary<string, string> scalarsB = Scalars(linesB);

        foreach (string key in scalarsA.Keys.Union(scalarsB.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            scalarsA.TryGetValue(key, out string? va);
            scalarsB.TryGetValue(key, out string? vb);

            if (va != vb)
                diffs.Add(key + ": " + (va ?? "<missing>") + " vs " + (vb ?? "<missing>"));
        }

        Dictionary<int, byte> ramA = Ram(linesA);
        Dictionary<int, byte> ramB = Ram(linesB);

        foreach (int address in ramA.Keys.Union(ramB.Keys).OrderBy(a => a))
        {
            ramA.TryGetValue(address, out byte a);
            ramB.TryGetValue(address, out byte b);

            if (a != b)
                diffs.Add("RAM $" + address.ToString("X4") + ": " + a.ToString("X2") + " vs " + b.ToString("X2"));
        }

        return diffs;
    }

    private static Dictionary<string, string> Scalars(string[] lines)
    {
        Dictionary<string, string> result = new Dictionary<string, string>();

        foreach (string line in lines)
        {
            if (line.StartsWith("ram", StringComparison.Ordinal))
                continue;

            int space = line.IndexOf(' ');

            if (space <= 0)
                continue;

            result[line.Substring(0, space)] = line.Substring(space + 1).Trim();
        }

        return result;
    }

    private static Dictionary<int, byte> Ram(string[] lines)
    {
        Dictionary<int, byte> result = new Dictionary<int, byte>();

        foreach (string line in lines)
        {
            if (!line.StartsWith("ram ", StringComparison.Ordinal))
                continue;

            // "ram XXXX XX XX XX ..."
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
                continue;

            if (!int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int baseAddress))
                continue;

            for (int i = 2; i < parts.Length; i++)
            {
                if (byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                    result[baseAddress + (i - 2)] = value;
            }
        }

        return result;
    }
}
