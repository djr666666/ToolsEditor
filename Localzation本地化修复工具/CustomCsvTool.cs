using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using UnityEditor.Localization;

public class CustomCsvTool : EditorWindow
{
    private static string selectedTableName = "";
    private static List<string> allTableNames = new List<string>();

    // 使用制表符作为分隔符
    private const char DELIMITER = '\t';

    [MenuItem("Tools/My Localization/CSV 导入导出工具")]
    public static void ShowWindow()
    {
        var window = GetWindow<CustomCsvTool>("CSV 导入导出");
        window.minSize = new Vector2(400, 300);
        RefreshTableList();
    }

    private static void RefreshTableList()
    {
        allTableNames = LocalizationEditorSettings.GetStringTableCollections()
            .Select(c => c.TableCollectionName)
            .OrderBy(n => n)
            .ToList();

        if (allTableNames.Count > 0 && string.IsNullOrEmpty(selectedTableName))
            selectedTableName = allTableNames[0];
    }

    private void OnGUI()
    {
        GUILayout.Label("CSV 导入导出工具 (制表符分隔)", EditorStyles.boldLabel);
        EditorGUILayout.Space(10);

        if (GUILayout.Button("刷新表列表"))
            RefreshTableList();

        EditorGUILayout.Space(5);

        if (allTableNames.Count == 0)
        {
            EditorGUILayout.HelpBox("当前项目没有任何 String Table Collection", MessageType.Warning);
            return;
        }

        int currentIndex = allTableNames.IndexOf(selectedTableName);
        if (currentIndex < 0) currentIndex = 0;

        currentIndex = EditorGUILayout.Popup("选择要操作的表", currentIndex, allTableNames.ToArray());
        selectedTableName = allTableNames[currentIndex];

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("当前表:", selectedTableName, EditorStyles.boldLabel);

        var collection = GetCollection();
        if (collection != null)
        {
            var tables = collection.StringTables;
            EditorGUILayout.LabelField("语言数量:", tables.Count.ToString());
            EditorGUILayout.LabelField("条目数量:", collection.SharedData.Entries.Count.ToString());
        }

        EditorGUILayout.Space(20);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("导出 TSV", GUILayout.Height(40)))
        {
            Export();
        }
        if (GUILayout.Button("导入 TSV", GUILayout.Height(40)))
        {
            Import();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "使用说明:\n" +
            "• 分隔符为「制表符(Tab)」，彻底避开逗号和引号冲突。\n" +
            "• 文本中可随意使用英文逗号、双引号、管道符等任何符号。\n" +
            "• 策划用 Excel/WPS 打开时，选择「制表符」分隔即可。",
            MessageType.Info
        );
    }

    private static StringTableCollection GetCollection()
    {
        if (string.IsNullOrEmpty(selectedTableName))
            RefreshTableList();

        var collection = LocalizationEditorSettings.GetStringTableCollection(selectedTableName);
        if (collection == null)
            Debug.LogError($"找不到名为 '{selectedTableName}' 的表！");
        return collection;
    }

    private static void Export()
    {
        var collection = GetCollection();
        if (collection == null) return;

        string defaultName = $"{collection.TableCollectionName}.tsv";
        string path = EditorUtility.SaveFilePanel("导出TSV", "", defaultName, "tsv");
        if (string.IsNullOrEmpty(path)) return;

        var tables = collection.StringTables;
        StringBuilder sb = new StringBuilder();

        // 表头
        List<string> headers = new List<string> { "Key", "Id" };
        headers.AddRange(tables.Select(t => t.LocaleIdentifier.Code));
        sb.AppendLine(string.Join(DELIMITER.ToString(), headers));

        // 数据行
        foreach (var row in collection.SharedData.Entries)
        {
            List<string> fields = new List<string>
            {
                EscapeField(row.Key),
                row.Id.ToString()
            };
            foreach (var table in tables)
            {
                var entry = table.GetEntry(row.Id);
                fields.Add(EscapeField(entry?.LocalizedValue ?? ""));
            }
            sb.AppendLine(string.Join(DELIMITER.ToString(), fields));
        }

        // 强制使用 UTF-8 with BOM，Excel/WPS 才能正确识别中文
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        Debug.Log($"✅ 导出成功: {path}");
        AssetDatabase.Refresh();
    }

    private static void Import()
    {
        var collection = GetCollection();
        if (collection == null) return;

        string path = EditorUtility.OpenFilePanel("导入TSV", "", "tsv,csv,txt");
        if (string.IsNullOrEmpty(path)) return;

        // 强制以 UTF-8 读取文件
        string content = File.ReadAllText(path, Encoding.UTF8);

        // 如果读取后还有乱码，尝试用系统默认编码（GB2312）再读一次
        if (content.Contains("?"))
        {
            content = File.ReadAllText(path, Encoding.GetEncoding("GB2312"));
        }

        string[] lines = content.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0) { Debug.LogError("文件为空！"); return; }

        ProcessImportLines(lines, collection);
    }

    private static void ProcessImportLines(string[] lines, StringTableCollection collection)
    {
        string[] headers = lines[0].Split(DELIMITER);
        var localeIndices = new Dictionary<string, int>();
        for (int i = 2; i < headers.Length; i++)
            if (!string.IsNullOrEmpty(headers[i].Trim()))
                localeIndices[headers[i].Trim()] = i;

        var tables = collection.StringTables;

        Undo.RecordObject(collection.SharedData, "Import Custom CSV");
        foreach (var table in tables)
            Undo.RecordObject(table, "Import Custom CSV");

        int count = 0;
        List<string> newKeys = new List<string>();   // ★ 记录本次新增的 Key
        List<string> changedKeys = new List<string>();  // ★ 记录本次内容改变的 Key

        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] fields = lines[i].Split(DELIMITER);
            if (fields.Length < 2) continue;

            string key = fields[0].Trim();
            if (string.IsNullOrEmpty(key)) continue;

            var entry = collection.SharedData.GetEntry(key);
            bool isNew = entry == null;
            if (isNew) entry = collection.SharedData.AddKey(key);

            var newContentParts = isNew ? new List<string>() : null;
            var changedParts = isNew ? null : new List<string>();   // ★ 只有更新才收集"改变"

            foreach (var kv in localeIndices)
            {
                if (kv.Value >= fields.Length) continue;
                var table = tables.FirstOrDefault(t => t.LocaleIdentifier.Code == kv.Key);
                if (table == null) continue;

                string rawText = fields[kv.Value];

                if (rawText.Length >= 2 && rawText.StartsWith("\"") && rawText.EndsWith("\""))
                    rawText = rawText.Substring(1, rawText.Length - 2);

                string finalText = rawText.Replace("\"\"", "\"");

                var tableEntry = table.GetEntry(entry.Id);
                if (tableEntry == null)
                {
                    table.AddEntry(entry.Id, finalText);
                    // ★ 已有 Key 但该语言原先没翻译：从「空」变为有内容，也算改变
                    if (!isNew && !string.IsNullOrEmpty(finalText))
                        changedParts.Add($"[{kv.Key}] (空) => {finalText}");
                }
                else
                {
                    string oldText = tableEntry.Value ?? "";
                    // ★ 旧值 != 新值，记录这条改变
                    if (!isNew && oldText != finalText)
                        changedParts.Add($"[{kv.Key}] {oldText} => {finalText}");
                    tableEntry.Value = finalText;
                }

                EditorUtility.SetDirty(table);

                if (isNew) newContentParts.Add($"{kv.Key}={finalText}");
            }

            if (isNew)
                newKeys.Add($"{key}  ->  {string.Join(" | ", newContentParts)}");
            else if (changedParts.Count > 0)                        // ★ 已有 Key 且内容确实变了
                changedKeys.Add($"{key}\n    {string.Join("\n    ", changedParts)}");

            count++;
        }

        EditorUtility.SetDirty(collection.SharedData);
        AssetDatabase.SaveAssets();

        int newCount = newKeys.Count;
        int changedCount = changedKeys.Count;                   // ★ 内容有改动的
        int unchangedCount = count - newCount - changedCount;   // ★ 已有且无改动的

        Debug.Log($"✅ 导入完成：共处理 {count} 行 ｜ 新增 {newCount} 个 ｜ 修改 {changedCount} 个 ｜ 无变化 {unchangedCount} 个");

        if (newCount > 0)
            Debug.Log($"🆕 新增的 {newCount} 个 Key：\n{string.Join("\n", newKeys)}");

        if (changedCount > 0)
            Debug.Log($"✏️ 修改的 {changedCount} 个 Key：\n{string.Join("\n", changedKeys)}");

        if (newCount == 0 && changedCount == 0)
            Debug.Log("ℹ️ 本次没有任何新增或修改（内容全部一致）。");
    }

    private static string EscapeField(string field)
    {
        if (string.IsNullOrEmpty(field)) return field;

        // 替换换行符和制表符（确保不破坏结构）
        return field.Replace("\r", " ")
                    .Replace("\n", " ")
                    .Replace("\t", " ");
    }
}
