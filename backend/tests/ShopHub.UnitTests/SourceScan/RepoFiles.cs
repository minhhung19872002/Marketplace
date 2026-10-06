namespace ShopHub.UnitTests.SourceScan;

// Locates repository folders/files for source-scanning tests
internal static class RepoFiles
{
    public static readonly string BackendRoot = FindBackendRoot();
    public static readonly string RepoRoot = Path.GetFullPath(Path.Combine(BackendRoot, ".."));

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ShopHub.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy ShopHub.sln từ thư mục chạy test.");
    }

    /// <summary>All C# source files under backend/src/{project}, excluding generated bin/obj output.</summary>
    public static IEnumerable<string> SourceFiles(params string[] projects) =>
        projects.SelectMany(p => Directory.EnumerateFiles(Path.Combine(BackendRoot, "src", p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    public static IEnumerable<string> AllSourceFiles() =>
        SourceFiles("ShopHub.Domain", "ShopHub.Application", "ShopHub.Infrastructure", "ShopHub.Reporting", "ShopHub.Api");

    public static string Relative(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    // Strip // and /* */ comments so documentation never trips a rule
    public static string WithoutComments(string source)
    {
        var noBlock = System.Text.RegularExpressions.Regex.Replace(source, @"/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return System.Text.RegularExpressions.Regex.Replace(noBlock, @"(?<![:""])//.*?$", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
    }
}
