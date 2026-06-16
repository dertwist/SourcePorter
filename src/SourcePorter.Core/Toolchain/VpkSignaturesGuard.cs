namespace SourcePorter.Core.Toolchain;

/// <summary>
/// Applies the guide §-1.1 workaround: since a Dec-2023 CS2 update,
/// <c>source1import</c> fails to read the CS:GO <c>pak01.vpk</c>
/// (<c>FATAL ERROR … Failed to load file (invalid)!</c>) while
/// <c>game/bin/win64/vpk.signatures</c> is present. This temporarily renames that
/// file for the duration of an import and restores it on <see cref="Dispose"/> —
/// so the user's install is left pristine (and VAC-safe) between imports.
/// </summary>
public sealed class VpkSignaturesGuard : IDisposable
{
    private readonly string? _active;
    private readonly string? _disabled;

    public VpkSignaturesGuard(string vpkSignaturesPath)
    {
        if (!File.Exists(vpkSignaturesPath))
            return;

        var disabled = vpkSignaturesPath + ".disabled";
        if (File.Exists(disabled))
            File.Delete(disabled); // remove any stale leftover

        File.Move(vpkSignaturesPath, disabled);
        _active = vpkSignaturesPath;
        _disabled = disabled;
    }

    /// <summary>True if this guard actually disabled the signatures file.</summary>
    public bool Applied => _active is not null;

    public void Dispose()
    {
        if (_active is not null && _disabled is not null &&
            File.Exists(_disabled) && !File.Exists(_active))
        {
            File.Move(_disabled, _active);
        }
    }
}
