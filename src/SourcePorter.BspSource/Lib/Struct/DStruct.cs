using SourcePorter.BspSource.Io;

namespace SourcePorter.BspSource.Lib.Struct;

/// <summary>
/// Generic interface for classes that emulate C/C++ structures, ported from
/// BSPSource's <c>DStruct</c> (which extends <c>info.ata4.io.Struct</c>). Each
/// struct knows its fixed on-disk byte size and can read/write itself through a
/// byte-order-aware cursor.
/// </summary>
public interface DStruct
{
    /// <summary>The fixed on-disk size of this struct in bytes.</summary>
    int GetSize();

    void Read(DataReader reader);

    void Write(DataWriter writer);
}
