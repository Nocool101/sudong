using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace QuickLauncher.Storage;

/// <summary>
/// SQLite 数据库访问。表结构仿 Quicker：
/// Actions / Scenes / Pages / TextCommands / HotkeyCommands / MouseTriggerRules / ActionHistory
/// </summary>
public sealed class Database : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly string _dbPath;

    public string DbPath => _dbPath;

    public Database(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _dbPath = Path.Combine(dataDir, "launcher.db");
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        _conn.Open();
        Initialize();
    }

    private SqliteCommand Cmd(string sql)
    {
        var c = _conn.CreateCommand();
        c.CommandText = sql;
        return c;
    }

    private void Initialize()
    {
        using var c = Cmd(@"
CREATE TABLE IF NOT EXISTS Actions (
    Id TEXT PRIMARY KEY,
    Data TEXT NOT NULL,
    ModifiedUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Scenes (
    Id TEXT PRIMARY KEY,
    Data TEXT NOT NULL,
    ModifiedUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS Pages (
    Id TEXT PRIMARY KEY,
    Data TEXT NOT NULL,
    ModifiedUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS TextCommands (
    Id TEXT PRIMARY KEY,
    Data TEXT NOT NULL,
    ModifiedUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS HotkeyCommands (
    Id TEXT PRIMARY KEY,
    Data TEXT NOT NULL,
    ModifiedUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS MouseTriggerRules (
    Id TEXT PRIMARY KEY,
    Data TEXT NOT NULL,
    ModifiedUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS ActionHistory (
    ActionId TEXT PRIMARY KEY,
    ClickCount INTEGER NOT NULL DEFAULT 0,
    LastClickTimeUtc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS CommonData (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_actions_modified ON Actions(ModifiedUtc);
");
        c.ExecuteNonQuery();
    }

    public SqliteConnection Connection => _conn;

    /// <summary>用 VACUUM INTO 生成一致快照到目标文件（导出配置用，避免直接读取运行中的文件被占用）。</summary>
    public void BackupTo(string destPath)
    {
        if (string.IsNullOrEmpty(destPath)) throw new ArgumentNullException(nameof(destPath));
        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(destPath)) File.Delete(destPath);
        // VACUUM INTO 由当前连接执行，语句结束时 SQLite 自动关闭目标文件，
        // 不残留第二个连接的文件句柄（BackupDatabase 的 destination 句柄释放不稳定，会导致打包时文件被占用）。
        using var c = _conn.CreateCommand();
        c.CommandText = $"VACUUM INTO '{destPath.Replace("'", "''")}'";
        c.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn.Dispose();
    }
}
