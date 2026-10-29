using System;
using System.IO;
using System.IO.Compression;

namespace QuickLauncher.App.Services;

/// <summary>
/// 配置导出：把数据目录（launcher.db + states\ + icons\ 等）打包为 zip。
/// 数据库用 VACUUM INTO 生成一致快照，避免直接读取运行中的文件被占用。
/// 导入改由 App.ImportConfig 借助独立恢复进程在应用退出后完成（避免运行中操作 SQLite）。
/// </summary>
public static class ConfigBackupService
{
    private const string DbFileName = "launcher.db";

    /// <summary>导出：把数据目录打包为 zip（只读数据目录，不影响运行）。</summary>
    public static void Export(string dataDir, string zipPath, QuickLauncher.Storage.Database? db)
    {
        if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
            throw new DirectoryNotFoundException("数据目录不存在：" + dataDir);

        var tmp = Path.Combine(Path.GetTempPath(), "ql_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var dbFile = Path.Combine(dataDir, DbFileName);

            // 1) 数据库：用 VACUUM INTO 生成一致快照，避免直接读取运行中的文件被占用
            if (db != null)
                db.BackupTo(Path.Combine(tmp, DbFileName));
            else if (File.Exists(dbFile))
                File.Copy(dbFile, Path.Combine(tmp, DbFileName), overwrite: true);

            // 2) 其余内容（states\、icons\ 等）复制进临时目录
            foreach (var f in Directory.EnumerateFiles(dataDir))
            {
                var name = Path.GetFileName(f);
                if (name.Equals(DbFileName, StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(f, Path.Combine(tmp, name), overwrite: true);
            }
            foreach (var d in Directory.EnumerateDirectories(dataDir))
                CopyDirectory(d, Path.Combine(tmp, Path.GetFileName(d)));

            // 3) 打包
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(tmp, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            try { Directory.Delete(tmp, recursive: true); } catch { }
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.EnumerateDirectories(src))
            CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
