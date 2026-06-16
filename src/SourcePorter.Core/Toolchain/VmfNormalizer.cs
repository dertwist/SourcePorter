namespace SourcePorter.Core.Toolchain;

/// <summary>
/// Minimal, lossless fix-ups that make a Source 1 <c>.vmf</c> importable by
/// <c>source1import</c> without the manual "open and re-save in Hammer" step.
/// <para>
/// A decompiled <c>.vmf</c> (from BSPSource) starts straight at the <c>world</c>
/// block, omitting the <c>versioninfo</c>/<c>visgroups</c>/<c>viewsettings</c>
/// preamble Hammer always writes. <c>source1import</c>'s <c>CVMFtoVMAP</c> then
/// fails with <i>"Missing a required top-level key"</i>. Prepending that preamble
/// is the whole fix — verified end-to-end against <c>source1import</c> (the map
/// and all its dependency refs import once the header is present). The Hammer++
/// <c>palette_plus</c>/<c>light_plus</c>/… extras and Hammer's geometry re-save are
/// <b>not</b> required.
/// </para>
/// <para>
/// BSPSource also drops the <c>offsets</c>/<c>offset_normals</c> subkeys from
/// decompiled <c>dispinfo</c> displacements (they are zero/default in the compiled
/// BSP), but <c>source1import</c> requires them — without them it logs
/// <i>"Found a displacement missing a needed subkey"</i> and discards the
/// displacement, so <b>terrain disappears</b>. <see cref="EnsureDisplacementOffsets"/>
/// injects neutral defaults so the terrain imports intact.
/// </para>
/// </summary>
public static partial class VmfNormalizer
{
    // The preamble Hammer writes ahead of `world`. source1import only needs these
    // three blocks (values mirror a default Hammer save).
    private const string HeaderPreamble =
        "versioninfo\r\n{\r\n\"editorversion\" \"400\"\r\n\"editorbuild\" \"6412\"\r\n" +
        "\"mapversion\" \"1\"\r\n\"formatversion\" \"100\"\r\n\"prefab\" \"0\"\r\n}\r\n" +
        "visgroups\r\n{\r\n}\r\nviewsettings\r\n{\r\n\"bSnapToGrid\" \"1\"\r\n" +
        "\"bShowGrid\" \"1\"\r\n\"bShowLogicalGrid\" \"0\"\r\n\"nGridSpacing\" \"64\"\r\n" +
        "\"bShow3DGrid\" \"0\"\r\n}\r\n";

    /// <summary>
    /// True if <paramref name="vmfPath"/> already opens with the <c>versioninfo</c>
    /// block — i.e. it does not need the preamble fix-up. Reads only the file head.
    /// </summary>
    public static bool HasImportableHeader(string vmfPath)
    {
        using var reader = new StreamReader(vmfPath);
        var buffer = new char[256];
        var read = reader.Read(buffer, 0, buffer.Length);
        return new string(buffer, 0, read)
            .TrimStart()
            .StartsWith("versioninfo", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Prepends the required preamble to <paramref name="vmfPath"/> if it is absent,
    /// so <c>source1import</c> accepts the map. Idempotent and lossless: a file that
    /// already has the header is left byte-for-byte unchanged. Returns true if the
    /// file was modified.
    /// </summary>
    public static bool EnsureImportableHeader(string vmfPath, Action<string>? log = null)
    {
        if (HasImportableHeader(vmfPath))
            return false;

        File.WriteAllText(vmfPath, HeaderPreamble + File.ReadAllText(vmfPath));
        log?.Invoke("Added the versioninfo/visgroups/viewsettings preamble a decompiled VMF omits " +
                    "(this replaces the manual open-and-save in Hammer).");
        return true;
    }

    /// <summary>
    /// Injects the <c>offsets</c> and <c>offset_normals</c> subkeys into any
    /// <c>dispinfo</c> block missing them (BSPSource omits them), so
    /// <c>source1import</c> keeps the displacement instead of dropping it. The injected
    /// data is neutral — zero offsets and <c>0 0 1</c> offset normals, exactly what
    /// Hammer writes for an unmodified displacement — so the terrain shape (which comes
    /// from the present <c>normals</c>+<c>distances</c>) is unchanged. Idempotent: a
    /// displacement that already has both subkeys is left untouched. Returns the number
    /// of displacements repaired.
    /// </summary>
    public static int EnsureDisplacementOffsets(string vmfPath, Action<string>? log = null)
    {
        var lines = File.ReadAllLines(vmfPath);
        var output = new List<string>(lines.Length + 4096);
        var repaired = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "dispinfo")
            {
                output.Add(lines[i]);
                continue;
            }

            var end = FindBlockEnd(lines, i, out var open);
            if (end < 0)
            {
                output.Add(lines[i]); // malformed/truncated block — pass through untouched
                continue;
            }

            var block = lines[i..(end + 1)];
            var hasOffsets = block.Any(l => l.Trim() == "offsets");
            var hasOffsetNormals = block.Any(l => l.Trim() == "offset_normals");
            var power = ParsePower(block);

            if ((hasOffsets && hasOffsetNormals) || power <= 0)
            {
                output.AddRange(block);
            }
            else
            {
                var keyIndent = LeadingWhitespace(lines[open]); // "{" sits at the key indent level
                var rowIndent = keyIndent + "\t";

                for (var k = 0; k < block.Length - 1; k++) // everything up to the closing "}"
                    output.Add(block[k]);
                if (!hasOffsets)
                    output.AddRange(BuildOffsetSubkey("offsets", power, keyIndent, rowIndent, "0 0 0"));
                if (!hasOffsetNormals)
                    output.AddRange(BuildOffsetSubkey("offset_normals", power, keyIndent, rowIndent, "0 0 1"));
                output.Add(block[^1]); // the closing "}"
                repaired++;
            }

            i = end;
        }

        if (repaired > 0)
        {
            File.WriteAllLines(vmfPath, output);
            log?.Invoke($"Repaired {repaired} decompiled displacement(s) missing offsets/offset_normals " +
                        "(restores terrain source1import would otherwise drop).");
        }
        return repaired;
    }

    // Index of the "}" that closes the block whose header is at lines[headerIndex],
    // or -1 if not found. Sets `open` to the index of the block's "{".
    private static int FindBlockEnd(string[] lines, int headerIndex, out int open)
    {
        open = headerIndex + 1;
        while (open < lines.Length && lines[open].Trim().Length == 0)
            open++;
        if (open >= lines.Length || lines[open].Trim() != "{")
            return -1;

        var depth = 0;
        for (var i = open; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t == "{")
                depth++;
            else if (t == "}" && --depth == 0)
                return i;
        }
        return -1;
    }

    // A displacement of power P has N = 2^P + 1 rows of N vertices; each vertex carries
    // a 3-float `triple`, so a row is N copies of it.
    private static IEnumerable<string> BuildOffsetSubkey(string name, int power, string keyIndent, string rowIndent, string triple)
    {
        var n = (1 << power) + 1;
        var row = string.Join(' ', Enumerable.Repeat(triple, n));

        yield return keyIndent + name;
        yield return keyIndent + "{";
        for (var r = 0; r < n; r++)
            yield return $"{rowIndent}\"row{r}\" \"{row}\"";
        yield return keyIndent + "}";
    }

    private static int ParsePower(IEnumerable<string> block)
    {
        foreach (var line in block)
        {
            var m = PowerRegex().Match(line);
            if (m.Success)
                return int.Parse(m.Groups[1].Value);
        }
        return 0;
    }

    private static string LeadingWhitespace(string line)
    {
        var i = 0;
        while (i < line.Length && (line[i] == '\t' || line[i] == ' '))
            i++;
        return line[..i];
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*""power""\s+""(\d+)""")]
    private static partial System.Text.RegularExpressions.Regex PowerRegex();
}
