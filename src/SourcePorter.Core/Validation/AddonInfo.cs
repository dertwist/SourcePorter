using ValveKeyValue;

namespace SourcePorter.Core.Validation;

/// <summary>Reads an addon's <c>addoninfo.txt</c> (KeyValues1) with <c>ValveKeyValue</c>.</summary>
public static class AddonInfo
{
    /// <summary>Returns the <c>addontitle</c>, or null if the file is missing/unreadable.</summary>
    public static string? ReadTitle(string addonInfoPath)
    {
        try
        {
            if (!File.Exists(addonInfoPath))
                return null;

            using var stream = File.OpenRead(addonInfoPath);
            var serializer = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);
            var root = serializer.Deserialize(stream);
            return root["addontitle"]?.ToString();
        }
        catch
        {
            return null;
        }
    }
}
