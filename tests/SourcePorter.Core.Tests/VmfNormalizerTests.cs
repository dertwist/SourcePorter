using SourcePorter.Core.Toolchain;

namespace SourcePorter.Core.Tests;

public class VmfNormalizerTests
{
    [Fact]
    public void Prepends_the_preamble_when_a_decompiled_vmf_starts_at_world()
    {
        var path = Path.Combine(Path.GetTempPath(), "vmfnorm_" + Guid.NewGuid().ToString("N") + ".vmf");
        File.WriteAllText(path, "world\r\n{\r\n\t\"id\" \"1\"\r\n}\r\n");
        try
        {
            Assert.False(VmfNormalizer.HasImportableHeader(path));

            var changed = VmfNormalizer.EnsureImportableHeader(path);

            Assert.True(changed);
            var text = File.ReadAllText(path);
            Assert.StartsWith("versioninfo", text);
            Assert.Contains("viewsettings", text);
            Assert.Contains("world", text); // original content preserved
            Assert.True(VmfNormalizer.HasImportableHeader(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Is_a_noop_when_the_header_is_already_present()
    {
        var path = Path.Combine(Path.GetTempPath(), "vmfnorm_" + Guid.NewGuid().ToString("N") + ".vmf");
        var original = "versioninfo\r\n{\r\n\t\"editorversion\" \"400\"\r\n}\r\nworld\r\n{\r\n}\r\n";
        File.WriteAllText(path, original);
        try
        {
            var changed = VmfNormalizer.EnsureImportableHeader(path);

            Assert.False(changed);
            Assert.Equal(original, File.ReadAllText(path)); // byte-for-byte unchanged
        }
        finally { File.Delete(path); }
    }
}
