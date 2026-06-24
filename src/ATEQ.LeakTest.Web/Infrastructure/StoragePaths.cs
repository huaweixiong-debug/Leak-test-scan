using Microsoft.Data.Sqlite;

namespace ATEQ.LeakTest.Web.Infrastructure;

public sealed class StoragePaths
{
    public string RepositoryRootPath { get; }
    public string ContentRootPath { get; }
    public string AppBaseDirectoryPath { get; }
    public string DatabasePath { get; }
    public string ConnectionString { get; }
    public string DataDirectoryPath { get; }
    public IReadOnlyList<string> RuntimeStoreCandidates { get; }
    public IReadOnlyList<string> LegacyDatabaseCandidates { get; }

    private StoragePaths(
        string repositoryRootPath,
        string contentRootPath,
        string appBaseDirectoryPath,
        string databasePath,
        string connectionString,
        IReadOnlyList<string> runtimeStoreCandidates,
        IReadOnlyList<string> legacyDatabaseCandidates)
    {
        RepositoryRootPath = repositoryRootPath;
        ContentRootPath = contentRootPath;
        AppBaseDirectoryPath = appBaseDirectoryPath;
        DatabasePath = databasePath;
        ConnectionString = connectionString;
        DataDirectoryPath = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Database path must include a directory");
        RuntimeStoreCandidates = runtimeStoreCandidates;
        LegacyDatabaseCandidates = legacyDatabaseCandidates;
    }

    public static StoragePaths Resolve(string contentRootPath, string appBaseDirectoryPath, string configuredConnectionString)
    {
        var normalizedContentRoot = Path.GetFullPath(contentRootPath);
        var normalizedAppBaseDirectory = Path.GetFullPath(appBaseDirectoryPath);
        var repositoryRootPath = ResolveRepositoryRootPath(normalizedContentRoot);

        var builder = new SqliteConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configuredConnectionString)
                ? "Data Source=data/ateq.db"
                : configuredConnectionString);

        var configuredDataSource = string.IsNullOrWhiteSpace(builder.DataSource)
            ? Path.Combine("data", "ateq.db")
            : builder.DataSource;

        var databasePath = Path.IsPathRooted(configuredDataSource)
            ? configuredDataSource
            : Path.GetFullPath(Path.Combine(repositoryRootPath, configuredDataSource));

        builder.DataSource = databasePath;

        var runtimeStoreCandidates = new[]
        {
            Path.Combine(repositoryRootPath, "data", "runtime-store.json"),
            Path.Combine(normalizedContentRoot, "data", "runtime-store.json"),
            Path.Combine(normalizedAppBaseDirectory, "data", "runtime-store.json")
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var legacyDatabaseCandidates = new[]
        {
            Path.Combine(normalizedContentRoot, "data", "ateq.db"),
            Path.Combine(normalizedAppBaseDirectory, "data", "ateq.db")
        }
        .Where(path => !string.Equals(Path.GetFullPath(path), databasePath, StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        return new StoragePaths(
            repositoryRootPath,
            normalizedContentRoot,
            normalizedAppBaseDirectory,
            databasePath,
            builder.ConnectionString,
            runtimeStoreCandidates,
            legacyDatabaseCandidates);
    }

    public void EnsurePrimaryStorageReady()
    {
        Directory.CreateDirectory(DataDirectoryPath);

        if (File.Exists(DatabasePath))
        {
            return;
        }

        foreach (var candidate in LegacyDatabaseCandidates)
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            CopySqliteTriplet(candidate, DatabasePath);
            Console.WriteLine($"[storage] copied legacy database from {candidate} to {DatabasePath}");
            return;
        }
    }

    private static string ResolveRepositoryRootPath(string contentRootPath)
    {
        var projectDirectory = new DirectoryInfo(contentRootPath);
        var srcDirectory = projectDirectory.Parent;

        if (srcDirectory?.Name.Equals("src", StringComparison.OrdinalIgnoreCase) == true &&
            srcDirectory.Parent != null)
        {
            return srcDirectory.Parent.FullName;
        }

        return contentRootPath;
    }

    private static void CopySqliteTriplet(string sourceDbPath, string targetDbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetDbPath)!);
        File.Copy(sourceDbPath, targetDbPath, overwrite: false);

        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sourceSidecar = sourceDbPath + suffix;
            var targetSidecar = targetDbPath + suffix;

            if (File.Exists(sourceSidecar) && !File.Exists(targetSidecar))
            {
                File.Copy(sourceSidecar, targetSidecar, overwrite: false);
            }
        }
    }
}
