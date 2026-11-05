namespace Umbraco.Community.EmailSender;

/// <summary>
/// Names that are permanent once the package is released: the NuGet ID, the App_Plugins folder, the bundle alias and
/// (when added) migration plan, table and column names. Changing one breaks installed sites.
/// </summary>
public static class PackageConstants
{
    public const string PackageName = "Umbraco.Community.EmailSender";

    /// <summary>Folder under /App_Plugins/ that serves the built client from wwwroot.</summary>
    public const string AppPluginsFolder = "EmailSender";

    /// <summary>Prefix for every backoffice extension alias this package registers.</summary>
    public const string AliasPrefix = "EmailSender";
}
