namespace SourcePorter.BspSource.Lib.Lump;

/// <summary>
/// Enumeration of BSP lump types, ported from BSPSource's <c>LumpType</c> enum.
/// Carries the lump-table index and the BSP version a type first appeared in.
///
/// Modelled as a type-safe enum class (not a C# enum) because each value carries
/// two ints and the lookups iterate values in <b>declaration order</b>, returning
/// the first match — indices are reused across versions (e.g. index 22 is
/// PROPCOLLISION@v21, UNUSED0@v20, and PORTALS@v19), so order is significant.
/// </summary>
public sealed class LumpType
{
    private static readonly List<LumpType> All = [];

    public string Name { get; }
    public int Index { get; }
    public int BspVersion { get; }

    private LumpType(string name, int index, int bspVersion = -1)
    {
        Name = name;
        Index = index;
        BspVersion = bspVersion;
        All.Add(this);
    }

    public static readonly LumpType LUMP_UNKNOWN = new("LUMP_UNKNOWN", -1);
    // v21
    public static readonly LumpType LUMP_PROPCOLLISION = new("LUMP_PROPCOLLISION", 22, 21);
    public static readonly LumpType LUMP_PROPHULLS = new("LUMP_PROPHULLS", 23, 21);
    public static readonly LumpType LUMP_PROPHULLVERTS = new("LUMP_PROPHULLVERTS", 24, 21);
    public static readonly LumpType LUMP_PROPTRIS = new("LUMP_PROPTRIS", 25, 21);
    public static readonly LumpType LUMP_PROP_BLOB = new("LUMP_PROP_BLOB", 49, 21);
    public static readonly LumpType LUMP_PHYSLEVEL = new("LUMP_PHYSLEVEL", 62, 21);
    public static readonly LumpType LUMP_DISP_MULTIBLEND = new("LUMP_DISP_MULTIBLEND", 63, 21);
    // v20
    public static readonly LumpType LUMP_FACEIDS = new("LUMP_FACEIDS", 11, 20);
    public static readonly LumpType LUMP_UNUSED0 = new("LUMP_UNUSED0", 22, 20);
    public static readonly LumpType LUMP_UNUSED1 = new("LUMP_UNUSED1", 23, 20);
    public static readonly LumpType LUMP_UNUSED2 = new("LUMP_UNUSED2", 24, 20);
    public static readonly LumpType LUMP_UNUSED3 = new("LUMP_UNUSED3", 25, 20);
    public static readonly LumpType LUMP_PHYSDISP = new("LUMP_PHYSDISP", 28, 20);
    public static readonly LumpType LUMP_WATEROVERLAYS = new("LUMP_WATEROVERLAYS", 50, 20);
    public static readonly LumpType LUMP_LEAF_AMBIENT_INDEX_HDR = new("LUMP_LEAF_AMBIENT_INDEX_HDR", 51, 20);
    public static readonly LumpType LUMP_LEAF_AMBIENT_INDEX = new("LUMP_LEAF_AMBIENT_INDEX", 52, 20);
    public static readonly LumpType LUMP_LIGHTING_HDR = new("LUMP_LIGHTING_HDR", 53, 20);
    public static readonly LumpType LUMP_WORLDLIGHTS_HDR = new("LUMP_WORLDLIGHTS_HDR", 54, 20);
    public static readonly LumpType LUMP_LEAF_AMBIENT_LIGHTING_HDR = new("LUMP_LEAF_AMBIENT_LIGHTING_HDR", 55, 20);
    public static readonly LumpType LUMP_LEAF_AMBIENT_LIGHTING = new("LUMP_LEAF_AMBIENT_LIGHTING", 56, 20);
    public static readonly LumpType LUMP_XZIPPAKFILE = new("LUMP_XZIPPAKFILE", 57, 20);
    public static readonly LumpType LUMP_FACES_HDR = new("LUMP_FACES_HDR", 58, 20);
    public static readonly LumpType LUMP_MAP_FLAGS = new("LUMP_MAP_FLAGS", 59, 20);
    public static readonly LumpType LUMP_OVERLAY_FADES = new("LUMP_OVERLAY_FADES", 60, 20);
    public static readonly LumpType LUMP_OVERLAY_SYSTEM_LEVELS = new("LUMP_OVERLAY_SYSTEM_LEVELS", 61, 20);
    // v19 and previous
    public static readonly LumpType LUMP_ENTITIES = new("LUMP_ENTITIES", 0);
    public static readonly LumpType LUMP_PLANES = new("LUMP_PLANES", 1);
    public static readonly LumpType LUMP_TEXDATA = new("LUMP_TEXDATA", 2);
    public static readonly LumpType LUMP_VERTEXES = new("LUMP_VERTEXES", 3);
    public static readonly LumpType LUMP_VISIBILITY = new("LUMP_VISIBILITY", 4);
    public static readonly LumpType LUMP_NODES = new("LUMP_NODES", 5);
    public static readonly LumpType LUMP_TEXINFO = new("LUMP_TEXINFO", 6);
    public static readonly LumpType LUMP_FACES = new("LUMP_FACES", 7);
    public static readonly LumpType LUMP_LIGHTING = new("LUMP_LIGHTING", 8);
    public static readonly LumpType LUMP_OCCLUSION = new("LUMP_OCCLUSION", 9);
    public static readonly LumpType LUMP_LEAFS = new("LUMP_LEAFS", 10);
    public static readonly LumpType LUMP_UNDEFINED = new("LUMP_UNDEFINED", 11);
    public static readonly LumpType LUMP_EDGES = new("LUMP_EDGES", 12);
    public static readonly LumpType LUMP_SURFEDGES = new("LUMP_SURFEDGES", 13);
    public static readonly LumpType LUMP_MODELS = new("LUMP_MODELS", 14);
    public static readonly LumpType LUMP_WORLDLIGHTS = new("LUMP_WORLDLIGHTS", 15);
    public static readonly LumpType LUMP_LEAFFACES = new("LUMP_LEAFFACES", 16);
    public static readonly LumpType LUMP_LEAFBRUSHES = new("LUMP_LEAFBRUSHES", 17);
    public static readonly LumpType LUMP_BRUSHES = new("LUMP_BRUSHES", 18);
    public static readonly LumpType LUMP_BRUSHSIDES = new("LUMP_BRUSHSIDES", 19);
    public static readonly LumpType LUMP_AREAS = new("LUMP_AREAS", 20);
    public static readonly LumpType LUMP_AREAPORTALS = new("LUMP_AREAPORTALS", 21);
    public static readonly LumpType LUMP_PORTALS = new("LUMP_PORTALS", 22);
    public static readonly LumpType LUMP_CLUSTERS = new("LUMP_CLUSTERS", 23);
    public static readonly LumpType LUMP_PORTALVERTS = new("LUMP_PORTALVERTS", 24);
    public static readonly LumpType LUMP_CLUSTERPORTALS = new("LUMP_CLUSTERPORTALS", 25);
    public static readonly LumpType LUMP_DISPINFO = new("LUMP_DISPINFO", 26);
    public static readonly LumpType LUMP_ORIGINALFACES = new("LUMP_ORIGINALFACES", 27);
    public static readonly LumpType LUMP_UNUSED = new("LUMP_UNUSED", 28);
    public static readonly LumpType LUMP_PHYSCOLLIDE = new("LUMP_PHYSCOLLIDE", 29);
    public static readonly LumpType LUMP_VERTNORMALS = new("LUMP_VERTNORMALS", 30);
    public static readonly LumpType LUMP_VERTNORMALINDICES = new("LUMP_VERTNORMALINDICES", 31);
    public static readonly LumpType LUMP_DISP_LIGHTMAP_ALPHAS = new("LUMP_DISP_LIGHTMAP_ALPHAS", 32);
    public static readonly LumpType LUMP_DISP_VERTS = new("LUMP_DISP_VERTS", 33);
    public static readonly LumpType LUMP_DISP_LIGHTMAP_SAMPLE_POSITIONS = new("LUMP_DISP_LIGHTMAP_SAMPLE_POSITIONS", 34);
    public static readonly LumpType LUMP_GAME_LUMP = new("LUMP_GAME_LUMP", 35);
    public static readonly LumpType LUMP_LEAFWATERDATA = new("LUMP_LEAFWATERDATA", 36);
    public static readonly LumpType LUMP_PRIMITIVES = new("LUMP_PRIMITIVES", 37);
    public static readonly LumpType LUMP_PRIMVERTS = new("LUMP_PRIMVERTS", 38);
    public static readonly LumpType LUMP_PRIMINDICES = new("LUMP_PRIMINDICES", 39);
    public static readonly LumpType LUMP_PAKFILE = new("LUMP_PAKFILE", 40);
    public static readonly LumpType LUMP_CLIPPORTALVERTS = new("LUMP_CLIPPORTALVERTS", 41);
    public static readonly LumpType LUMP_CUBEMAPS = new("LUMP_CUBEMAPS", 42);
    public static readonly LumpType LUMP_TEXDATA_STRING_DATA = new("LUMP_TEXDATA_STRING_DATA", 43);
    public static readonly LumpType LUMP_TEXDATA_STRING_TABLE = new("LUMP_TEXDATA_STRING_TABLE", 44);
    public static readonly LumpType LUMP_OVERLAYS = new("LUMP_OVERLAYS", 45);
    public static readonly LumpType LUMP_LEAFMINDISTTOWATER = new("LUMP_LEAFMINDISTTOWATER", 46);
    public static readonly LumpType LUMP_FACE_MACRO_TEXTURE_INFO = new("LUMP_FACE_MACRO_TEXTURE_INFO", 47);
    public static readonly LumpType LUMP_DISP_TRIS = new("LUMP_DISP_TRIS", 48);
    public static readonly LumpType LUMP_PHYSCOLLIDESURFACE = new("LUMP_PHYSCOLLIDESURFACE", 49);

    /// <summary>All declared lump types in declaration order.</summary>
    public static IReadOnlyList<LumpType> Values => All;

    public static LumpType Get(string name, int bspVersion)
    {
        foreach (var type in All)
            if (type.Name == name && type.BspVersion <= bspVersion)
                return type;
        return LUMP_UNKNOWN;
    }

    public static LumpType Get(string name) => Get(name, -1);

    public static LumpType Get(int index, int bspVersion)
    {
        foreach (var type in All)
            if (type.Index == index && type.BspVersion <= bspVersion)
                return type;
        return LUMP_UNKNOWN;
    }

    public static LumpType Get(int index) => Get(index, -1);

    public override string ToString() => Name;
}
