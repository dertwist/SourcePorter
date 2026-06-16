namespace SourcePorter.BspSource.Io;

/// <summary>
/// Port of the subset of <c>info.ata4.io.buffer.ByteBufferUtils</c> that BSPSource
/// uses. The Java version memory-maps files; here we simply read the whole file
/// into a heap buffer — BSPs are tens of MB at most and we never need mutation of
/// the on-disk file.
/// </summary>
public static class ByteBufferUtils
{
    /// <summary>Reads the entire file into a new buffer (position 0, big-endian until set).</summary>
    public static ByteBuffer Load(string path) => ByteBuffer.Wrap(File.ReadAllBytes(path));

    /// <summary>Read-only view of a file. We always load fully, so this is just <see cref="Load"/>.</summary>
    public static ByteBuffer OpenReadOnly(string path) => Load(path);

    /// <summary>Read-write view of a file. Loaded fully into memory (BSP saving is out of scope).</summary>
    public static ByteBuffer OpenReadWrite(string path) => Load(path);

    /// <summary>Concatenates the remaining bytes of each buffer into one new buffer.</summary>
    public static ByteBuffer Concat(IEnumerable<ByteBuffer> buffers)
    {
        var slices = new List<byte[]>();
        int total = 0;
        foreach (var b in buffers)
        {
            var arr = b.Duplicate().ToArray();
            slices.Add(arr);
            total += arr.Length;
        }

        var result = ByteBuffer.Allocate(total);
        foreach (var s in slices)
            result.Put(s);
        result.Rewind();
        return result;
    }
}
