using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.Localization;
using UnityEngine.Localization.Tables;

public class CustomCsvTool : EditorWindow
{
    private static string selectedTableName = "";
    private static List<string> allTableNames = new List<string>();

    private const char DELIMITER = '\t';

    // 记住路径(EditorPrefs，按表名分)
    private const string PREF_IMPORT_PATH = "CustomCsvTool.ImportPath.";
    private const string PREF_EXPORT_PATH = "CustomCsvTool.ExportPath.";
    private const string PREF_KEY_PREFIX = "CustomCsvTool.KeyPrefix";
    // 导入文件列表(EditorPrefs 存，按表名；多个路径用换行分隔)
    private static List<string> GetImportPaths()
    {
        string raw = EditorPrefs.GetString(PREF_IMPORT_PATH + selectedTableName, "");
        return string.IsNullOrEmpty(raw)
            ? new List<string>()
            : raw.Split('\n').Where(s => !string.IsNullOrEmpty(s)).ToList();
    }
    private static void SetImportPaths(List<string> paths)
        => EditorPrefs.SetString(PREF_IMPORT_PATH + selectedTableName, string.Join("\n", paths));
    private static void AddImportPath(string p)
    {
        if (string.IsNullOrEmpty(p)) return;
        var list = GetImportPaths();
        if (!list.Contains(p)) { list.Add(p); SetImportPaths(list); }
    }
    private static void RemoveImportPath(string p)
    {
        var list = GetImportPaths();
        if (list.Remove(p)) SetImportPaths(list);
    }
    private static string GetExportPath() => EditorPrefs.GetString(PREF_EXPORT_PATH + selectedTableName, "");
    private static void SetExportPath(string p) => EditorPrefs.SetString(PREF_EXPORT_PATH + selectedTableName, p);

    // ===== 日志 =====
    private enum LogKind { Info, Success, New, Changed, Deleted, Warn, Error, Title }
    private struct LogLine { public LogKind kind; public string text; }
    private static readonly List<LogLine> _log = new List<LogLine>();
    private Vector2 _logScroll;

    // ===== 查询/预览 =====
    private enum FilterMode { 全部, 任意语言漏翻, 中文漏翻, 英文漏翻 }
    private bool _showSearch;   // 下半区显示：false=操作日志  true=查询/预览
    private string _search = "";
    private FilterMode _filter = FilterMode.全部;
    private string _lastSearchKey = "\0";
    private readonly List<(string key, string zh, string en)> _results = new List<(string, string, string)>();
    private Vector2 _resultScroll;
    private const int MAX_RESULTS = 500;

    // ===== 导入预演(pending) =====
    private class PendingImport
    {
        public string path;
        public string[] lines;
        public int totalRows;
        public int missingCount;
        public List<string> newKeys = new List<string>();
        public List<string> changedKeys = new List<string>();
        public List<string> deletedKeys = new List<string>();
        public List<string> warnings = new List<string>();
        public List<string> skippedByPrefix = new List<string>();
    }
    private PendingImport _pending;
    private bool _deleteMissingOnCommit;

    // 导出选项
    private bool _exportOnlyMissing;
    // 导入 Key 前缀限制（空=不限制）
    private string _keyPrefix = "";

    // 占位符正则
    private static readonly Regex PlaceholderRe = new Regex(@"\{[^}]*\}");

    // ===== GUIStyle =====
    private bool _stylesReady;
    private GUIStyle _titleStyle, _sectionStyle, _cardStyle, _logStyle, _tipStyle, _tipTitleStyle, _dropStyle, _keyStyle;

    [MenuItem("Tools/My Localization/CSV 导入导出工具")]
    public static void ShowWindow()
    {
        var window = GetWindow<CustomCsvTool>("CSV 导入导出");
        window.minSize = new Vector2(540, 720);
        RefreshTableList();
    }

    private void OnEnable() => _keyPrefix = EditorPrefs.GetString(PREF_KEY_PREFIX, "");
    private void OnFocus() => RefreshTableList();

    private static void RefreshTableList()
    {
        allTableNames = LocalizationEditorSettings.GetStringTableCollections()
            .Select(c => c.TableCollectionName).OrderBy(n => n).ToList();
        if (allTableNames.Count > 0 && string.IsNullOrEmpty(selectedTableName))
            selectedTableName = allTableNames[0];
    }

    private void InitStyles()
    {
        if (_stylesReady) return;
        _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
        _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        _cardStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(12, 12, 10, 10) };
        _logStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 12 };
        _tipStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 13 };
        _tipTitleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
        _dropStyle = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 12, richText = true };
        _keyStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 12 };
        _stylesReady = true;
    }

    private void OnGUI()
    {
        InitStyles();

        EditorGUILayout.Space(6);
        GUILayout.Label("CSV 导入导出工具", _titleStyle);
        GUILayout.Label("本地化 String Table ⇄ TSV（制表符分隔，规避逗号/引号冲突）", EditorStyles.miniLabel);
        Separator();

        if (allTableNames.Count == 0)
        {
            EditorGUILayout.HelpBox("当前项目没有任何 String Table Collection", MessageType.Warning);
            return;
        }

        int currentIndex = allTableNames.IndexOf(selectedTableName);
        if (currentIndex < 0) currentIndex = 0;
        int newIndex = EditorGUILayout.Popup("选择要操作的表", currentIndex, allTableNames.ToArray());
        if (newIndex != currentIndex) { _lastSearchKey = "\0"; _pending = null; }
        selectedTableName = allTableNames[newIndex];

        var collection = GetCollection();

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginVertical(_cardStyle);
        InfoRow("当前表", selectedTableName, true);
        if (collection != null)
        {
            InfoRow("语言数量", collection.StringTables.Count.ToString());
            InfoRow("条目数量", collection.SharedData.Entries.Count.ToString());
        }
        EditorGUILayout.EndVertical();

        Separator();

        DrawExportSection(collection);
        Separator();
        DrawImportSection(collection);

        Separator();

        // 下半区：操作日志 / 查询预览 —— 用一个按钮切换（不横排常驻），占满剩余高度、尽量大
        EditorGUILayout.BeginVertical(_cardStyle, GUILayout.ExpandHeight(true), GUILayout.MinHeight(280));
        if (_showSearch) DrawSearch(collection);
        else DrawLog();
        EditorGUILayout.EndVertical();

        Separator();

        EditorGUILayout.BeginVertical(_cardStyle);
        GUILayout.Label("使用说明", _tipTitleStyle);
        EditorGUILayout.Space(2);
        GUILayout.Label(
            "• 导入是 <b>预演 → 确认 → 自动备份 → 写入</b>：点『导入』先只算 diff+校验，核对无误再点『确认写入』。\n" +
            "• 写入前自动备份当前表到 <b>项目根/Backups/Localization/</b>，导错可回滚。\n" +
            "• 校验会报：重复/空 Key、字段不足行、未知语言列、<color=#FFC107>占位符不一致</color>（如中文有 {0} 英文没有）。\n" +
            "• 删除 = 表里有、文件里没有的 Key；确认时可勾选是否同步删除（默认不删）。\n" +
            "• Localization Tables 窗口不会自动刷新，用「查询/预览」看内存最新数据。",
            _tipStyle);
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4);
    }

    // ---------- 导入源（拖拽 + 记住路径） ----------
    private void DrawImportList(StringTableCollection collection)
    {
        GUILayout.Label("已记住的 TSV（点该行『导入』即预演它）", EditorStyles.miniBoldLabel);

        var paths = GetImportPaths();
        if (paths.Count == 0)
        {
            GUILayout.Label("<color=#808080>列表为空。点下面「+ 选择文件」或把 .tsv 拖进来添加。</color>", _logStyle);
        }
        else
        {
            string toRemove = null, toImport = null;
            foreach (var p in paths)
            {
                bool missing = !File.Exists(p);
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label(new GUIContent((missing ? "⚠ " : "") + Path.GetFileName(p), p), _keyStyle, GUILayout.ExpandWidth(true));
                using (new EditorGUI.DisabledScope(missing))
                    if (GUILayout.Button("导入", GUILayout.Width(48), GUILayout.Height(18))) toImport = p;
                if (GUILayout.Button("−", GUILayout.Width(24), GUILayout.Height(18))) toRemove = p;
                EditorGUILayout.EndHorizontal();
            }
            if (toRemove != null) RemoveImportPath(toRemove);
            if (toImport != null && collection != null && File.Exists(toImport))
            {
                _showSearch = false;
                PrepareImport(toImport, collection);
            }
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ 选择文件…", GUILayout.Height(20)))
        {
            string p = EditorUtility.OpenFilePanel("选择要添加的 TSV", "", "tsv,csv,txt");
            if (!string.IsNullOrEmpty(p)) AddImportPath(p);
        }
        using (new EditorGUI.DisabledScope(paths.Count == 0))
            if (GUILayout.Button("清空列表", GUILayout.Height(20))) SetImportPaths(new List<string>());
        EditorGUILayout.EndHorizontal();

        Rect dropRect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "把 <b>.tsv</b> 拖到这里添加到列表", _dropStyle);
        HandleDragAndDrop(dropRect);
    }

    private void HandleDragAndDrop(Rect rect)
    {
        var e = Event.current;
        if (!rect.Contains(e.mousePosition)) return;
        if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
            {
                string p = DragAndDrop.paths[0];
                if (Directory.Exists(p))
                    p = Directory.GetFiles(p, "*.tsv").FirstOrDefault()
                        ?? Directory.GetFiles(p, "*.csv").FirstOrDefault()
                        ?? Directory.GetFiles(p, "*.txt").FirstOrDefault();

                ClearLog();
                _showSearch = false;
                if (!string.IsNullOrEmpty(p) && File.Exists(p))
                {
                    AddImportPath(p);
                    AddLog(LogKind.Info, $"已添加到列表：{Path.GetFileName(p)}");
                    AddLog(LogKind.Info, "在上面列表点该文件的『导入』开始预演。");
                }
                else AddLog(LogKind.Error, "拖入的内容里没找到 .tsv / .csv / .txt 文件。");
                Repaint();
            }
            e.Use();
        }
    }

    // ---------- 导出/导入 或 预演确认 按钮区 ----------
    // ① 导出模板（第一步：从 Unity 定 key → 导出 tsv 给策划规定格式）
    private void DrawExportSection(StringTableCollection collection)
    {
        GUILayout.Label("① 导出模板（给策划定 TSV 格式）", _sectionStyle);
        _exportOnlyMissing = EditorGUILayout.ToggleLeft("只含有空翻译的行（给翻译补漏用）", _exportOnlyMissing);
        var old = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.40f, 0.65f, 1f);
        if (GUILayout.Button("⬆  导出 TSV", GUILayout.Height(38))) { _showSearch = false; Export(collection); }
        GUI.backgroundColor = old;
    }

    // ② 导入（后续：策划按模板填好，导回；前端日常维护）
    private void DrawImportSection(StringTableCollection collection)
    {
        GUILayout.Label("② 导入（策划填好的 TSV 导回）", _sectionStyle);

        EditorGUI.BeginChangeCheck();
        _keyPrefix = EditorGUILayout.TextField(
            new GUIContent("Key 前缀限制", "留空=不限制(配啥导啥)；填了如 l_ 则只导入以此开头的 Key，其余跳过不导"),
            _keyPrefix);
        if (EditorGUI.EndChangeCheck()) EditorPrefs.SetString(PREF_KEY_PREFIX, _keyPrefix ?? "");
        if (!string.IsNullOrEmpty(_keyPrefix))
            GUILayout.Label($"<color=#FFB040>仅导入以 “{_keyPrefix}” 开头的 Key，其余跳过。</color>", _logStyle);
        EditorGUILayout.Space(2);

        DrawImportList(collection);

        if (_pending != null) DrawPendingConfirm(collection);
    }

    // 预演后的确认/取消
    private void DrawPendingConfirm(StringTableCollection collection)
    {
        EditorGUILayout.Space(4);
        int n = _pending.newKeys.Count, m = _pending.changedKeys.Count, k = _pending.deletedKeys.Count,
            w = _pending.warnings.Count, s = _pending.skippedByPrefix.Count;
        GUILayout.Label(
            $"<color=#FFC107><b>预演完成，尚未写入</b></color>  新增 {n} ｜ 修改 {m} ｜ 删除 {k} ｜ 前缀跳过 {s} ｜ 警告 {w}\n" +
            "<size=10><color=#909090>核对下方日志，确认后写入（会先自动备份）</color></size>", _logStyle);

        if (k > 0)
            _deleteMissingOnCommit = EditorGUILayout.ToggleLeft(
                $"同步删除文件里已移除的 {k} 个 Key（默认不删，勾选才删）", _deleteMissingOnCommit);

        EditorGUILayout.BeginHorizontal();
        var old = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.45f, 0.80f, 0.45f);
        if (GUILayout.Button("✅  确认写入", GUILayout.Height(38))) { _showSearch = false; CommitImport(collection); }
        GUI.backgroundColor = new Color(0.85f, 0.5f, 0.5f);
        if (GUILayout.Button("✕  取消", GUILayout.Height(38), GUILayout.Width(120)))
        {
            _pending = null; _deleteMissingOnCommit = false;
            AddLog(LogKind.Info, "已取消，未写入任何改动。");
        }
        GUI.backgroundColor = old;
        EditorGUILayout.EndHorizontal();
    }

    // ---------- 操作日志 ----------
    private void DrawLog()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("操作日志", _sectionStyle);
        GUILayout.FlexibleSpace();
        using (new EditorGUI.DisabledScope(_log.Count == 0))
        {
            if (GUILayout.Button("复制", GUILayout.Width(50), GUILayout.Height(18)))
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", _log.Select(l => l.text));
            if (GUILayout.Button("清空", GUILayout.Width(50), GUILayout.Height(18))) _log.Clear();
        }
        if (GUILayout.Button("查询 / 预览 ▸", GUILayout.Width(108), GUILayout.Height(18))) _showSearch = true;
        EditorGUILayout.EndHorizontal();

        _logScroll = EditorGUILayout.BeginScrollView(_logScroll);
        if (_log.Count == 0)
            GUILayout.Label("<color=#808080>还没有操作。导出/导入后结果会显示在这里。</color>", _logStyle);
        else
            foreach (var line in _log)
            {
                string body = line.kind == LogKind.Title ? $"<b>{line.text}</b>" : line.text;
                GUILayout.Label($"<color={ColorOf(line.kind)}>{body}</color>", _logStyle);
            }
        EditorGUILayout.EndScrollView();
    }

    // ---------- 查询/预览 ----------
    private void DrawSearch(StringTableCollection collection)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("查询 / 预览", _sectionStyle);
        GUILayout.Label("<color=#808080>读内存表，导入后即最新</color>", _logStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("◂ 返回操作日志", GUILayout.Width(120), GUILayout.Height(18))) _showSearch = false;
        EditorGUILayout.EndHorizontal();

        EditorGUI.BeginChangeCheck();
        _search = EditorGUILayout.TextField("搜 Key / 中文 / 英文", _search);
        _filter = (FilterMode)EditorGUILayout.EnumPopup("筛选", _filter);
        bool changed = EditorGUI.EndChangeCheck();

        string cacheKey = $"{selectedTableName}|{_filter}|{_search}";
        if (changed || cacheKey != _lastSearchKey) { RebuildResults(collection); _lastSearchKey = cacheKey; }

        if (_filter == FilterMode.全部 && string.IsNullOrEmpty(_search))
        {
            GUILayout.Label("<color=#808080>输入关键词，或用「筛选」看漏翻的条目。</color>", _logStyle);
            return;
        }

        GUILayout.Label($"<color=#5AC8FA><b>命中 {_results.Count}{(_results.Count >= MAX_RESULTS ? "+" : "")} 条</b></color>", _logStyle);

        _resultScroll = EditorGUILayout.BeginScrollView(_resultScroll);
        foreach (var r in _results)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"<b><color=#5AC8FA>{HL(r.key, _search)}</color></b>", _keyStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("复制Key", GUILayout.Width(66), GUILayout.Height(16)))
                EditorGUIUtility.systemCopyBuffer = r.key;
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(r.zh) ? "<color=#FF6B6B>中  (空)</color>" : $"<color=#909090>中</color>  {HL(r.zh, _search)}", _keyStyle);
            GUILayout.Label(string.IsNullOrEmpty(r.en) ? "<color=#FF6B6B>EN  (空)</color>" : $"<color=#909090>EN</color>  {HL(r.en, _search)}", _keyStyle);
            EditorGUILayout.EndVertical();
        }
        if (_results.Count >= MAX_RESULTS)
            GUILayout.Label($"<color=#808080>结果过多，只显示前 {MAX_RESULTS} 条。</color>", _logStyle);
        EditorGUILayout.EndScrollView();
    }

    private void RebuildResults(StringTableCollection collection)
    {
        _results.Clear();
        if (collection == null) return;
        string s = (_search ?? "").ToLowerInvariant();
        var zhTable = collection.StringTables.FirstOrDefault(t => t.LocaleIdentifier.Code.StartsWith("zh"));
        var enTable = collection.StringTables.FirstOrDefault(t => t.LocaleIdentifier.Code.StartsWith("en"));

        foreach (var row in collection.SharedData.Entries)
        {
            string zh = zhTable?.GetEntry(row.Id)?.Value ?? "";
            string en = enTable?.GetEntry(row.Id)?.Value ?? "";

            switch (_filter)
            {
                case FilterMode.任意语言漏翻: if (!(string.IsNullOrEmpty(zh) || string.IsNullOrEmpty(en))) continue; break;
                case FilterMode.中文漏翻: if (!string.IsNullOrEmpty(zh)) continue; break;
                case FilterMode.英文漏翻: if (!string.IsNullOrEmpty(en)) continue; break;
            }

            bool match = string.IsNullOrEmpty(s)
                || row.Key.ToLowerInvariant().Contains(s)
                || zh.ToLowerInvariant().Contains(s)
                || en.ToLowerInvariant().Contains(s);
            if (!match) continue;

            _results.Add((row.Key, zh, en));
            if (_results.Count >= MAX_RESULTS) break;
        }
    }

    // 高亮 + 转义 <（防止文本里的富文本标签被解析）
    private static string HL(string text, string search)
    {
        if (string.IsNullOrEmpty(text)) return "";
        Func<string, string> esc = x => x.Replace("<", "<");
        if (string.IsNullOrEmpty(search)) return esc(text);
        var sb = new StringBuilder();
        int i = 0;
        while (true)
        {
            int idx = text.IndexOf(search, i, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) { sb.Append(esc(text.Substring(i))); break; }
            sb.Append(esc(text.Substring(i, idx - i)));
            sb.Append("<color=#FFD54F>").Append(esc(text.Substring(idx, search.Length))).Append("</color>");
            i = idx + search.Length;
        }
        return sb.ToString();
    }

    private void InfoRow(string label, string value, bool bold = false)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(80));
        GUILayout.Label(value, bold ? EditorStyles.boldLabel : EditorStyles.label);
        EditorGUILayout.EndHorizontal();
    }

    private void Separator()
    {
        EditorGUILayout.Space(6);
        var r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.08f));
        EditorGUILayout.Space(6);
    }

    private static void ClearLog() => _log.Clear();
    private static void AddLog(LogKind kind, string text) => _log.Add(new LogLine { kind = kind, text = text });

    private static string ColorOf(LogKind k)
    {
        switch (k)
        {
            case LogKind.Success: return "#4CD964";
            case LogKind.New: return "#7CFC00";
            case LogKind.Changed: return "#FFC107";
            case LogKind.Deleted: return "#FF6B6B";
            case LogKind.Warn: return "#FFB040";
            case LogKind.Error: return "#FF453A";
            case LogKind.Title: return "#5AC8FA";
            default: return "#C8C8C8";
        }
    }

    private static StringTableCollection GetCollection()
    {
        if (string.IsNullOrEmpty(selectedTableName)) RefreshTableList();
        var collection = LocalizationEditorSettings.GetStringTableCollection(selectedTableName);
        if (collection == null) Debug.LogError($"找不到名为 '{selectedTableName}' 的表！");
        return collection;
    }

    private static void TryRepaintLocalizationWindow()
    {
        foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if (w.GetType().Name.Contains("Localization")) w.Repaint();
    }

    // ================= 导出 =================
    private void Export(StringTableCollection collection)
    {
        if (collection == null) return;

        string rememberedDir = "";
        string rememberedName = $"{collection.TableCollectionName}.tsv";
        string last = GetExportPath();
        if (!string.IsNullOrEmpty(last)) { rememberedDir = Path.GetDirectoryName(last); rememberedName = Path.GetFileName(last); }

        string path = EditorUtility.SaveFilePanel("导出TSV", rememberedDir, rememberedName, "tsv");
        if (string.IsNullOrEmpty(path)) return;
        SetExportPath(path);

        File.WriteAllText(path, BuildTsv(collection, _exportOnlyMissing), new UTF8Encoding(true));
        AssetDatabase.Refresh();

        ClearLog();
        AddLog(LogKind.Title, "导出结果");
        AddLog(LogKind.Success, $"✅ 导出成功{(_exportOnlyMissing ? "（仅空翻译行）" : "")}");
        AddLog(LogKind.Info, path);
        EditorUtility.RevealInFinder(path);
        Debug.Log($"✅ 导出成功: {path}");
    }

    private static string BuildTsv(StringTableCollection collection, bool onlyMissing)
    {
        var tables = collection.StringTables;
        var sb = new StringBuilder();
        var headers = new List<string> { "Key", "Id" };
        headers.AddRange(tables.Select(t => t.LocaleIdentifier.Code));
        sb.AppendLine(string.Join(DELIMITER.ToString(), headers));

        foreach (var row in collection.SharedData.Entries)
        {
            if (onlyMissing && !tables.Any(t => string.IsNullOrEmpty(t.GetEntry(row.Id)?.LocalizedValue)))
                continue;

            var fields = new List<string> { EscapeField(row.Key), row.Id.ToString() };
            foreach (var table in tables)
                fields.Add(EscapeField(table.GetEntry(row.Id)?.LocalizedValue ?? ""));
            sb.AppendLine(string.Join(DELIMITER.ToString(), fields));
        }
        return sb.ToString();
    }

    // ================= 导入：预演 =================
    private void PrepareImport(string path, StringTableCollection collection)
    {
        if (collection == null) return;

        string content = File.ReadAllText(path, Encoding.UTF8);
        if (content.Contains("?")) content = File.ReadAllText(path, Encoding.GetEncoding("GB2312"));
        string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        ClearLog();
        if (lines.Length == 0) { AddLog(LogKind.Error, "❌ 文件为空！"); return; }

        _pending = new PendingImport { path = path, lines = lines };
        _deleteMissingOnCommit = false;
        RunImport(_pending, collection, apply: false);

        AddLog(LogKind.Title, "🔎 预演结果（尚未写入）");
        AddLog(LogKind.Success,
            $"共 {_pending.totalRows} 行 ｜ 新增 {_pending.newKeys.Count} ｜ 修改 {_pending.changedKeys.Count} ｜ 删除 {_pending.deletedKeys.Count} ｜ 前缀跳过 {_pending.skippedByPrefix.Count} ｜ 警告 {_pending.warnings.Count}");

        if (_pending.warnings.Count > 0)
        {
            AddLog(LogKind.Title, $"⚠️ 校验警告 {_pending.warnings.Count} 条");
            foreach (var w in _pending.warnings) AddLog(LogKind.Warn, w);
        }
        if (_pending.skippedByPrefix.Count > 0)
        {
            AddLog(LogKind.Title, $"⏭️ 前缀不符跳过 {_pending.skippedByPrefix.Count} 个（不导入其内容）");
            foreach (var x in _pending.skippedByPrefix) AddLog(LogKind.Warn, x);
        }
        if (_pending.newKeys.Count > 0)
        {
            AddLog(LogKind.Title, $"🆕 新增 {_pending.newKeys.Count}");
            foreach (var x in _pending.newKeys) AddLog(LogKind.New, x);
        }
        if (_pending.changedKeys.Count > 0)
        {
            AddLog(LogKind.Title, $"✏️ 修改 {_pending.changedKeys.Count}");
            foreach (var x in _pending.changedKeys) AddLog(LogKind.Changed, x);
        }
        if (_pending.deletedKeys.Count > 0)
        {
            AddLog(LogKind.Title, $"🗑️ 文件里已移除 {_pending.deletedKeys.Count}（默认不删）");
            foreach (var x in _pending.deletedKeys) AddLog(LogKind.Deleted, x);
        }
        AddLog(LogKind.Info, "↑ 核对无误后点『确认写入』（写入前会自动备份）。");
    }

    // ================= 导入：确认写入 =================
    private void CommitImport(StringTableCollection collection)
    {
        if (collection == null || _pending == null) return;

        // 1) 自动备份
        string backup = BackupTable(collection);

        // 2) 真正写入
        RunImport(_pending, collection, apply: true);

        // 3) 可选同步删除
        int deleted = 0;
        if (_deleteMissingOnCommit && _pending.deletedKeys.Count > 0)
            deleted = DeleteKeys(collection, _pending.deletedKeys);

        EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssets();
        _lastSearchKey = "\0";
        TryRepaintLocalizationWindow();

        int n = _pending.newKeys.Count, m = _pending.changedKeys.Count, k = _pending.deletedKeys.Count;
        ClearLog();
        AddLog(LogKind.Title, "✅ 导入完成");
        AddLog(LogKind.Success,
            $"新增 {n} ｜ 修改 {m} ｜ 删除 {(_deleteMissingOnCommit ? deleted + "（已执行）" : k + "（未执行）")}");
        AddLog(LogKind.Info, $"已备份到：{backup}");
        Debug.Log($"✅ 导入完成：新增 {n} ｜ 修改 {m} ｜ 删除 {(_deleteMissingOnCommit ? deleted.ToString() : "0")} ｜ 备份 {backup}");

        _pending = null;
        _deleteMissingOnCommit = false;
    }

    // 解析 + (预演: 只算 diff/校验；提交: 真写)
    private void RunImport(PendingImport P, StringTableCollection collection, bool apply)
    {
        var tables = collection.StringTables;
        var codeToTable = new Dictionary<string, StringTable>();
        foreach (var t in tables) codeToTable[t.LocaleIdentifier.Code] = t;

        string[] headers = P.lines[0].Split(DELIMITER);
        var localeIndices = new Dictionary<string, int>();
        for (int i = 2; i < headers.Length; i++)
        {
            string code = headers[i].Trim();
            if (string.IsNullOrEmpty(code)) continue;
            localeIndices[code] = i;
            if (!apply && !codeToTable.ContainsKey(code))
                P.warnings.Add($"表头语言列 “{code}” 在表里不存在（拼错/未加该语言?），该列将被忽略");
        }

        if (apply)
        {
            Undo.RecordObject(collection.SharedData, "Import Custom CSV");
            foreach (var t in tables) Undo.RecordObject(t, "Import Custom CSV");
        }

        var existingKeys = new HashSet<string>(collection.SharedData.Entries.Select(e => e.Key));
        var seenTsvKeys = new HashSet<string>();

        if (!apply) { P.newKeys.Clear(); P.changedKeys.Clear(); P.deletedKeys.Clear(); P.warnings.Clear(); P.skippedByPrefix.Clear(); P.missingCount = 0; }
        int count = 0;

        for (int i = 1; i < P.lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(P.lines[i])) continue;
            string[] fields = P.lines[i].Split(DELIMITER);
            if (fields.Length < 2) { if (!apply) P.warnings.Add($"第 {i + 1} 行字段不足（<2 列），已跳过"); continue; }

            string key = fields[0].Trim();
            if (string.IsNullOrEmpty(key)) { if (!apply) P.warnings.Add($"第 {i + 1} 行 Key 为空，已跳过"); continue; }
            if (!seenTsvKeys.Add(key)) { if (!apply) P.warnings.Add($"重复 Key “{key}”（第 {i + 1} 行），后者覆盖前者"); }

            // Key 前缀限制：设了前缀且不匹配 → 跳过不导入(但已记入 seenTsvKeys，不会被当成删除)
            if (!string.IsNullOrEmpty(_keyPrefix) && !key.StartsWith(_keyPrefix))
            {
                if (!apply) P.skippedByPrefix.Add(key);
                continue;
            }

            var entry = collection.SharedData.GetEntry(key);
            bool isNew = entry == null;
            if (apply && isNew) entry = collection.SharedData.AddKey(key);

            var perLocale = new Dictionary<string, string>();
            var newParts = isNew ? new List<string>() : null;
            var changedParts = isNew ? null : new List<string>();

            foreach (var kv in localeIndices)
            {
                if (kv.Value >= fields.Length) continue;
                if (!codeToTable.TryGetValue(kv.Key, out var table)) continue;

                string raw = fields[kv.Value];
                if (raw.Length >= 2 && raw.StartsWith("\"") && raw.EndsWith("\"")) raw = raw.Substring(1, raw.Length - 2);
                string finalText = raw.Replace("\"\"", "\"");
                perLocale[kv.Key] = finalText;
                if (!apply && string.IsNullOrEmpty(finalText)) P.missingCount++;

                if (isNew)
                {
                    if (!apply) newParts.Add($"{kv.Key}={Trunc(finalText)}");
                }
                else
                {
                    // entry 在预演时对已存在 Key 一定非 null
                    var te = entry != null ? table.GetEntry(entry.Id) : null;
                    string oldText = te?.Value ?? "";
                    if (!apply && oldText != finalText)
                        changedParts.Add($"[{kv.Key}] {Trunc(oldText)} => {Trunc(finalText)}");
                }

                if (apply)
                {
                    var te = table.GetEntry(entry.Id);
                    if (te == null) table.AddEntry(entry.Id, finalText);
                    else te.Value = finalText;
                    EditorUtility.SetDirty(table);
                }
            }

            if (!apply) CheckPlaceholders(key, perLocale, P.warnings);

            if (!apply)
            {
                if (isNew) P.newKeys.Add($"{key}  ->  {string.Join(" | ", newParts)}");
                else if (changedParts.Count > 0) P.changedKeys.Add($"{key}\n    {string.Join("\n    ", changedParts)}");
            }
            count++;
        }

        if (!apply)
        {
            P.totalRows = count;
            P.deletedKeys = existingKeys.Where(x => !seenTsvKeys.Contains(x)).OrderBy(x => x).ToList();
        }
    }

    // 占位符一致性：各有内容的语言的 {..} / \n 应一致
    private static void CheckPlaceholders(string key, Dictionary<string, string> perLocale, List<string> warnings)
    {
        var all = new HashSet<string>();
        var byLocale = new Dictionary<string, HashSet<string>>();
        foreach (var kv in perLocale)
        {
            if (string.IsNullOrEmpty(kv.Value)) continue;
            var ph = ExtractPlaceholders(kv.Value);
            byLocale[kv.Key] = ph;
            foreach (var p in ph) all.Add(p);
        }
        if (all.Count == 0 || byLocale.Count < 2) return;
        foreach (var kv in byLocale)
        {
            var missing = all.Where(p => !kv.Value.Contains(p)).ToList();
            if (missing.Count > 0)
                warnings.Add($"占位符不一致 “{key}”：[{kv.Key}] 缺少 {string.Join(" ", missing)}");
        }
    }

    private static HashSet<string> ExtractPlaceholders(string s)
    {
        var set = new HashSet<string>();
        foreach (Match m in PlaceholderRe.Matches(s)) set.Add(m.Value);
        if (s.Contains("\\n")) set.Add("\\n");
        return set;
    }

    private int DeleteKeys(StringTableCollection c, List<string> keys)
    {
        Undo.RecordObject(c.SharedData, "Delete Keys");
        foreach (var t in c.StringTables) Undo.RecordObject(t, "Delete Keys");

        int n = 0;
        foreach (var key in keys)
        {
            var e = c.SharedData.GetEntry(key);
            if (e == null) continue;
            long id = e.Id;
            foreach (var t in c.StringTables) { t.RemoveEntry(id); EditorUtility.SetDirty(t); }
            c.SharedData.RemoveKey(key);
            n++;
        }
        EditorUtility.SetDirty(c.SharedData);
        return n;
    }

    private static string BackupTable(StringTableCollection c)
    {
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Backups", "Localization");
        Directory.CreateDirectory(dir);
        string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string p = Path.Combine(dir, $"{c.TableCollectionName}_{ts}.tsv");
        File.WriteAllText(p, BuildTsv(c, false), new UTF8Encoding(true));
        return p;
    }

    private static string Trunc(string s, int max = 60)
    {
        if (string.IsNullOrEmpty(s)) return "(空)";
        s = s.Replace("\n", "⏎");
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }

    private static string EscapeField(string field)
    {
        if (string.IsNullOrEmpty(field)) return field;
        return field.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
    }
}
