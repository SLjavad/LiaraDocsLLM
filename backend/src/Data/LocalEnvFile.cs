using DotNetEnv;

namespace LiaraDocsAssistant.Data;

public static class LocalEnvFile
{
    private const string FileName = ".env";
    private const int MaxUpwardLevels = 8;

    public static void Load()
    {
        var path = Find();
        if (path is null) return;

        Env.Load(path);
    }

    public static string? Find()
    {
        var roots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory,
        };

        foreach (var root in roots)
        {
            var dir = new DirectoryInfo(root);
            for (var i = 0; dir is not null && i < MaxUpwardLevels; i++)
            {
                var candidate = Path.Combine(dir.FullName, FileName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }
}
