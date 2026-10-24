namespace PackLauncher;

public static class AppConfig
{
    public const string LauncherName = "Launcher";

    public const string GitHubOwner = "sv1tel";

    public const string GitHubRepo = "my-pack";

    public const string GitHubBranch = "main";

    public const string PackFolderInRepo = "pack";

    public const string DataFolderName = "MyPackLauncher";

    public static string ApiBase => $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}";

    public static string CommitApiUrl => $"{ApiBase}/commits/{Uri.EscapeDataString(GitHubBranch)}";

    public static string TreeApiUrl(string commit) => $"{ApiBase}/git/trees/{commit}?recursive=1";

    public static string ReleasesApiUrl => $"{ApiBase}/releases?per_page=30";

    public static string RawFileUrl(string commit, string relativePath) =>
        $"https://raw.githubusercontent.com/{GitHubOwner}/{GitHubRepo}/{commit}/{PackFolderInRepo}/" +
        string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString));

    public static string RepositoryUrl => $"https://github.com/{GitHubOwner}/{GitHubRepo}";
}
