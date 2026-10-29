using System;
using System.Collections.Generic;
using System.IO;
using QuickLauncher.Models;

namespace QuickLauncher.Storage;

/// <summary>
/// 中央仓库：动作/场景/页面/指令的读写 + 每动作 state JSON 管理。
/// </summary>
public sealed class ActionStore
{
    private readonly Database _db;
    private readonly string _stateDir;
    private readonly object _lock = new();

    public string StateDir => _stateDir;

    public ActionStore(Database db, string dataDir)
    {
        _db = db;
        _stateDir = Path.Combine(dataDir, "states");
        Directory.CreateDirectory(_stateDir);
    }

    #region 通用 CRUD

    private List<T> LoadAll<T>(string table)
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = $"SELECT Data FROM {table}";
            using var r = c.ExecuteReader();
            var list = new List<T>();
            while (r.Read())
            {
                var json = r.GetString(0);
                var item = Json.Deserialize<T>(json);
                if (item is not null) list.Add(item);
            }
            return list;
        }
    }

    private void Save(string table, Guid id, object data)
    {
        var json = Json.Serialize(data);
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = $"INSERT OR REPLACE INTO {table} (Id, Data, ModifiedUtc) VALUES (@id, @data, @m)";
            c.Parameters.AddWithValue("@id", id.ToString());
            c.Parameters.AddWithValue("@data", json);
            c.Parameters.AddWithValue("@m", DateTime.UtcNow.ToString("o"));
            c.ExecuteNonQuery();
        }
    }

    private void Delete(string table, Guid id)
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = $"DELETE FROM {table} WHERE Id=@id";
            c.Parameters.AddWithValue("@id", id.ToString());
            c.ExecuteNonQuery();
        }
    }

    #endregion

    #region 动作

    public List<ActionItem> LoadActions() => LoadAll<ActionItem>("Actions");

    public ActionItem? LoadAction(Guid id)
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = "SELECT Data FROM Actions WHERE Id=@id";
            c.Parameters.AddWithValue("@id", id.ToString());
            using var r = c.ExecuteReader();
            return r.Read() ? Json.Deserialize<ActionItem>(r.GetString(0)) : null;
        }
    }

    public void SaveAction(ActionItem action)
    {
        action.ModifiedUtc = DateTime.UtcNow;
        Save("Actions", action.Id, action);
    }

    public void DeleteAction(Guid id) => Delete("Actions", id);

    #endregion

    #region 场景

    public List<Scene> LoadScenes() => LoadAll<Scene>("Scenes");

    public Scene? LoadScene(Guid id)
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = "SELECT Data FROM Scenes WHERE Id=@id";
            c.Parameters.AddWithValue("@id", id.ToString());
            using var r = c.ExecuteReader();
            return r.Read() ? Json.Deserialize<Scene>(r.GetString(0)) : null;
        }
    }

    public void SaveScene(Scene scene) => Save("Scenes", scene.Id, scene);

    public void DeleteScene(Guid id) => Delete("Scenes", id);

    /// <summary>
    /// 清理“坏场景”（启动自愈，幂等）。这类场景会让专用磁贴漏到所有程序的下区，
    /// 破坏“专用磁贴只在该程序出现”的隔离性：
    ///  1) 匹配所有程序的场景：ProcessMatch 为 * 且无窗口类名/标题约束（三条件均无有效值）；
    ///  2) 用占位符“当前程序”当进程匹配的死场景（历史遗留，永远匹配不到真实进程）。
    /// 全局场景（Global）与带具体进程/类名/标题约束的场景一律保留。返回删除数量。
    /// </summary>
    public int SanitizeScenes()
    {
        static bool Real(string v) => !string.IsNullOrWhiteSpace(v) && v.Trim() != "*";
        var bad = LoadScenes().FindAll(s =>
            s.Type != SceneType.Global &&
            ( (!Real(s.ProcessMatch) && !Real(s.WindowClassMatch) && !Real(s.WindowTitleMatch))
              || string.Equals(s.ProcessMatch?.Trim(), "当前程序", StringComparison.Ordinal) ));
        foreach (var s in bad) Delete("Scenes", s.Id);
        return bad.Count;
    }

    #endregion

    #region 动作页

    public List<ActionPage> LoadPages() => LoadAll<ActionPage>("Pages");

    public ActionPage? LoadPage(Guid id)
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = "SELECT Data FROM Pages WHERE Id=@id";
            c.Parameters.AddWithValue("@id", id.ToString());
            using var r = c.ExecuteReader();
            return r.Read() ? Json.Deserialize<ActionPage>(r.GetString(0)) : null;
        }
    }

    public void SavePage(ActionPage page)
    {
        page.ModifiedUtc = DateTime.UtcNow;
        Save("Pages", page.Id, page);
    }

    public void DeletePage(Guid id) => Delete("Pages", id);

    #endregion

    #region 文本指令 / 扩展热键 / 高级鼠标触发

    public List<TextCommand> LoadTextCommands() => LoadAll<TextCommand>("TextCommands");

    public void SaveTextCommand(TextCommand t) => Save("TextCommands", t.Id, t);

    public void DeleteTextCommand(Guid id) => Delete("TextCommands", id);

    public List<HotkeyCommand> LoadHotkeyCommands() => LoadAll<HotkeyCommand>("HotkeyCommands");

    public void SaveHotkeyCommand(HotkeyCommand h) => Save("HotkeyCommands", h.Id, h);

    public void DeleteHotkeyCommand(Guid id) => Delete("HotkeyCommands", id);

    public List<MouseTriggerRule> LoadMouseTriggerRules() => LoadAll<MouseTriggerRule>("MouseTriggerRules");

    public void SaveMouseTriggerRule(MouseTriggerRule r) => Save("MouseTriggerRules", r.Id, r);

    public void DeleteMouseTriggerRule(Guid id) => Delete("MouseTriggerRules", id);

    #endregion

    #region 每动作状态（仿 Quicker states/state_&lt;ActionId&gt;.json）

    private string StatePath(Guid actionId) => Path.Combine(_stateDir, $"state_{actionId}.json");

    /// <summary>读取动作的全部持久变量</summary>
    public Dictionary<string, string> LoadState(Guid actionId)
    {
        var path = StatePath(actionId);
        if (!File.Exists(path)) return new Dictionary<string, string>();
        try
        {
            var json = File.ReadAllText(path);
            return Json.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>保存动作的全部持久变量</summary>
    public void SaveState(Guid actionId, Dictionary<string, string> states)
    {
        var path = StatePath(actionId);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Json.Serialize(states));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>读取单个变量（键带前缀，如 $var:名称）</summary>
    public string? GetStateValue(Guid actionId, string key)
    {
        var states = LoadState(actionId);
        return states.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>写入单个变量</summary>
    public void SetStateValue(Guid actionId, string key, string? value)
    {
        var states = LoadState(actionId);
        if (value is null) states.Remove(key);
        else states[key] = value;
        SaveState(actionId, states);
    }

    #endregion

    #region 使用统计

    public void RecordActionRun(Guid actionId)
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = @"
INSERT INTO ActionHistory (ActionId, ClickCount, LastClickTimeUtc) VALUES (@id, 1, @t)
ON CONFLICT(ActionId) DO UPDATE SET ClickCount = ClickCount + 1, LastClickTimeUtc = @t";
            c.Parameters.AddWithValue("@id", actionId.ToString());
            c.Parameters.AddWithValue("@t", DateTime.UtcNow.ToString("o"));
            c.ExecuteNonQuery();
        }
    }

    public (int count, DateTime? last) GetActionUsage(Guid actionId)
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = "SELECT ClickCount, LastClickTimeUtc FROM ActionHistory WHERE ActionId=@id";
            c.Parameters.AddWithValue("@id", actionId.ToString());
            using var r = c.ExecuteReader();
            if (!r.Read()) return (0, null);
            return (r.GetInt32(0), DateTime.TryParse(r.GetString(1), out var t) ? t : null);
        }
    }

    #endregion

    #region 通用键值

    public string? GetCommon(string key)
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = "SELECT Value FROM CommonData WHERE Key=@k";
            c.Parameters.AddWithValue("@k", key);
            return c.ExecuteScalar() as string;
        }
    }

    public void SetCommon(string key, string value)
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = "INSERT OR REPLACE INTO CommonData (Key, Value) VALUES (@k, @v)";
            c.Parameters.AddWithValue("@k", key);
            c.Parameters.AddWithValue("@v", value);
            c.ExecuteNonQuery();
        }
    }

    #endregion

    /// <summary>首次运行时写入默认数据（全局场景 + 示例动作）</summary>
    public void EnsureSeedData()
    {
        lock (_lock)
        {
            using var c = _db.Connection.CreateCommand();
            c.CommandText = "SELECT COUNT(1) FROM Scenes";
            var sceneCount = Convert.ToInt64(c.ExecuteScalar());
            if (sceneCount > 0) return;

            // 全局场景
            var global = new Scene { Name = "全局", Type = SceneType.Global };
            SaveScene(global);

            // 首页
            var page = new ActionPage { Name = "主页", Columns = 4 };
            SavePage(page);
            global.PanelPageId = page.Id;
            SaveScene(global);

            // 示例动作
            var openBaidu = new ActionItem
            {
                Name = "百度一下",
                Icon = "🔍",
                Description = "打开百度搜索",
                SimpleType = "OpenUrl",
                SimpleParam = "https://www.baidu.com"
            };
            var openNotepad = new ActionItem
            {
                Name = "记事本",
                Icon = "📝",
                SimpleType = "RunProgram",
                SimpleParam = "notepad.exe"
            };
            var sendText = new ActionItem
            {
                Name = "发送问候",
                Icon = "💬",
                Description = "向当前窗口发送问候语",
                SimpleType = "SendText",
                SimpleParam = "你好，这是 QuickLauncher 发送的文本！"
            };
            var openDownloads = new ActionItem
            {
                Name = "下载文件夹",
                Icon = "📁",
                SimpleType = "OpenFolder",
                SimpleParam = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
            };
            var combo = new ActionItem
            {
                Name = "组合示例",
                Icon = "⚡",
                Description = "获取时间并复制到剪贴板",
                Kind = ActionKind.Composite,
                Steps =
                {
                    new ActionStep
                    {
                        StepType = "GetDateTime",
                        Inputs = { ["format"] = "yyyy-MM-dd HH:mm:ss", ["var"] = "now" }
                    },
                    new ActionStep
                    {
                        StepType = "SetClipboardText",
                        Inputs = { ["text"] = "当前时间：{now}" }
                    },
                    new ActionStep
                    {
                        StepType = "ShowMessage",
                        Inputs = { ["text"] = "已复制当前时间到剪贴板" }
                    }
                }
            };

            foreach (var a in new[] { openBaidu, openNotepad, sendText, openDownloads, combo })
                SaveAction(a);

            page.Cells.Add(new ActionCell { Index = 0, ActionId = openBaidu.Id });
            page.Cells.Add(new ActionCell { Index = 1, ActionId = openNotepad.Id });
            page.Cells.Add(new ActionCell { Index = 2, ActionId = sendText.Id });
            page.Cells.Add(new ActionCell { Index = 3, ActionId = openDownloads.Id });
            page.Cells.Add(new ActionCell { Index = 4, ActionId = combo.Id });
            SavePage(page);

            // 文本指令示例：bd + Tab → 百度一下
            SaveTextCommand(new TextCommand
            {
                Abbreviation = "bd",
                ActionId = openBaidu.Id,
                TriggerChar = ""
            });
        }
    }
}
