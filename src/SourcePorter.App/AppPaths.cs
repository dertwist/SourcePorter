namespace SourcePorter.App;

/// <summary>Well-known locations for the running app.</summary>
internal static class AppPaths
{
    /// <summary>
    /// The bundled <c>import_scripts</c> folder shipped next to the exe — the
    /// importer's working directory and the source1import config location.
    /// </summary>
    public static string ImportScriptsDir =>
        Path.Combine(AppContext.BaseDirectory, "import_scripts");
}
