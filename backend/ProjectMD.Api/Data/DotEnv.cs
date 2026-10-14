namespace ProjectMD.Api.Data;

// Reads a local ".env" file (KEY=VALUE per line) so private settings such as the database
// connection string can live in backend/.env instead of the code. The file is ignored by git.
// Keys use the environment-variable style, e.g. ConnectionStrings__Default or
// Auth__AdminEmails__0; "__" becomes ":" for .NET configuration.
public static class DotEnv
{
    // Looks for a ".env" file in each given folder (first one found wins) and returns its
    // settings. Real environment variables always win, so keys that are already set in the
    // environment are left out. Returns an empty set if there is no file.
    public static Dictionary<string, string?> Read(params string[] searchFolders)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in searchFolders.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            var path = Path.Combine(folder, ".env");
            if (!File.Exists(path)) continue;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value[1..^1];
                }

                // A real environment variable beats the file.
                if (Environment.GetEnvironmentVariable(key) is not null) continue;

                result[key.Replace("__", ":")] = value;
            }
            break;
        }

        return result;
    }

    // The usual places for the file: the project folder, the folder above it (backend/) and
    // the folder the server was started from.
    public static string[] DefaultFolders(string contentRoot) =>
    [
        contentRoot,
        Path.GetFullPath(Path.Combine(contentRoot, "..")),
        Directory.GetCurrentDirectory(),
    ];
}
