using System.Reflection;

namespace Clinic.Persistence.Migrations;

/// <summary>Reads hand-written SQL (seeds, views, grants) embedded next to the migrations.</summary>
internal static class MigrationSql
{
    public static string Read(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
