using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;

public class CommaToPipeConverter : EditorWindow
{
    private string csvPath = "";

    // ============ 自定义导出路径 ============
    // 修改这里为你想要的路径（相对于Assets文件夹）
    private const string EXPORT_FOLDER_PATH = "Assets/LocalizationCSV";
    // =====================================


    [MenuItem("Tools/MyTools/Localization/3. Language.CSV “,”  转  “|”")]
    public static void ShowWindow()
    {
        GetWindow<CommaToPipeConverter>("逗号转管道符");
    }

    void OnEnable()
    {
        // 确保导出文件夹存在
        EnsureExportFolderExists();
    }

    void OnGUI()
    {
        GUILayout.Label("逗号转管道符CSV转换器", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "功能：\n" +
            "1. 将标准CSV（逗号分隔）转换为管道符分隔\n" +
            "2. 文本中的逗号不需要引号保护\n" +
            "3. 解决英文逗号导致CSV解析错误的问题\n" +
            $"4. 自动保存到: {EXPORT_FOLDER_PATH}",
            MessageType.Info
        );

        EditorGUILayout.Space();

        // 显示当前导出路径
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("导出文件夹配置", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("路径:", EXPORT_FOLDER_PATH);

        string fullPath = GetExportFolderFullPath();
        if (Directory.Exists(fullPath))
        {
            string[] files = Directory.GetFiles(fullPath, "*.csv");
            EditorGUILayout.LabelField("已有文件:", $"{files.Length} 个CSV文件");
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // 文件选择
        GUILayout.Label("选择标准CSV文件（逗号分隔）:");
        EditorGUILayout.BeginHorizontal();
        csvPath = EditorGUILayout.TextField(csvPath);
        if (GUILayout.Button("浏览", GUILayout.Width(60)))
        {
            csvPath = EditorUtility.OpenFilePanel("选择CSV", "", "csv");
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // 操作按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("转换并自动保存", GUILayout.Height(50)))
        {
            ConvertCommaToPipe(true);
        }

        if (GUILayout.Button("转换并选择位置", GUILayout.Height(50)))
        {
            ConvertCommaToPipe(false);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // 快速操作按钮
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("打开导出文件夹"))
        {
            OpenExportFolder();
        }

        if (GUILayout.Button("清理导出文件"))
        {
            CleanExportFolder();
        }

        if (GUILayout.Button("查看导出配置"))
        {
            ShowExportConfig();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        GUILayout.Label("转换示例：", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "转换前（逗号分隔）:\n" +
            "Key,Chinese,English\n" +
            "greeting,\"你好，世界\",\"Hello, World\"\n\n" +
            "转换后（管道符分隔）:\n" +
            "Key|Chinese|English\n" +
            "greeting|你好，世界|Hello, World\n\n" +
            "✓ 文本中没有引号了！\n" +
            "✓ 可以自由使用英文逗号！\n" +
            $"✓ 自动保存到: {EXPORT_FOLDER_PATH}",
            MessageType.None
        );
    }

    void ConvertCommaToPipe(bool autoSave = true)
    {
        if (!File.Exists(csvPath))
        {
            EditorUtility.DisplayDialog("错误", "请先选择要转换的CSV文件", "确定");
            return;
        }

        try
        {
            // 读取标准CSV文件（逗号分隔）
            string content = File.ReadAllText(csvPath, Encoding.UTF8);

            // 转换：将分隔符从逗号改为管道符
            string converted = ConvertCSVSeparator(content, ',', '|');

            // 生成输出文件名
            string fileName = Path.GetFileNameWithoutExtension(csvPath) + "_pipe.csv";

            string savePath;

            if (autoSave)
            {
                // 自动保存到预设文件夹
                savePath = Path.Combine(EXPORT_FOLDER_PATH, fileName);
                savePath = savePath.Replace('\\', '/'); // 统一使用正斜杠

                // 确保文件夹存在
                EnsureExportFolderExists();

                // 检查文件是否已存在
                if (File.Exists(savePath))
                {
                    bool overwrite = EditorUtility.DisplayDialog("文件已存在",
                        $"文件已存在:\n{fileName}\n\n是否覆盖？", "覆盖", "取消");

                    if (!overwrite)
                    {
                        // 生成带时间戳的文件名
                        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        fileName = Path.GetFileNameWithoutExtension(csvPath) + $"_pipe_{timestamp}.csv";
                        savePath = Path.Combine(EXPORT_FOLDER_PATH, fileName);
                        savePath = savePath.Replace('\\', '/');
                    }
                }

                // 写入文件
                File.WriteAllText(savePath, converted, Encoding.UTF8);
                AssetDatabase.Refresh(); // 刷新Unity资源数据库

                ShowSuccessMessage(savePath, content, converted, true);
            }
            else
            {
                // 手动选择保存位置（原有功能）
                savePath = EditorUtility.SaveFilePanel("保存转换后的CSV", "",
                    fileName, "csv");

                if (!string.IsNullOrEmpty(savePath))
                {
                    File.WriteAllText(savePath, converted, Encoding.UTF8);
                    ShowSuccessMessage(savePath, content, converted, false);
                }
            }
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("错误", $"转换失败: {e.Message}", "确定");
            Debug.LogError($"CSV转换失败: {e}");
        }
    }

    // ============ 文件夹管理方法 ============

    string GetExportFolderFullPath()
    {
        // 将Assets相对路径转换为完整路径
        if (EXPORT_FOLDER_PATH.StartsWith("Assets/") || EXPORT_FOLDER_PATH.StartsWith("Assets\\"))
        {
            string relativeToAssets = EXPORT_FOLDER_PATH.Substring(7); // 移除"Assets/"
            return Path.GetFullPath(Path.Combine(Application.dataPath, relativeToAssets));
        }

        // 如果已经是完整路径
        if (Path.IsPathRooted(EXPORT_FOLDER_PATH))
        {
            return EXPORT_FOLDER_PATH;
        }

        // 默认相对于项目根目录
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.GetFullPath(Path.Combine(projectRoot, EXPORT_FOLDER_PATH));
    }

    void EnsureExportFolderExists()
    {
        string fullPath = GetExportFolderFullPath();
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
            AssetDatabase.Refresh();
            Debug.Log($"✅ 已创建导出文件夹: {EXPORT_FOLDER_PATH}");
        }
    }

    void OpenExportFolder()
    {
        string fullPath = GetExportFolderFullPath();
        if (!Directory.Exists(fullPath))
        {
            EnsureExportFolderExists();
        }

        EditorUtility.RevealInFinder(fullPath);
    }

    void CleanExportFolder()
    {
        string fullPath = GetExportFolderFullPath();
        if (!Directory.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("文件夹不存在",
                $"导出文件夹不存在:\n{EXPORT_FOLDER_PATH}", "确定");
            return;
        }

        string[] csvFiles = Directory.GetFiles(fullPath, "*.csv");
        string[] metaFiles = Directory.GetFiles(fullPath, "*.meta");

        if (csvFiles.Length == 0)
        {
            EditorUtility.DisplayDialog("文件夹为空",
                "导出文件夹中没有CSV文件", "确定");
            return;
        }

        bool confirm = EditorUtility.DisplayDialog("清理导出文件夹",
            $"确定要删除导出文件夹中的所有CSV文件吗？\n\n" +
            $"路径: {EXPORT_FOLDER_PATH}\n" +
            $"文件数量: {csvFiles.Length}",
            "全部删除", "取消");

        if (confirm)
        {
            foreach (string file in csvFiles)
            {
                File.Delete(file);
            }
            foreach (string file in metaFiles)
            {
                File.Delete(file);
            }
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("清理完成",
                $"已删除 {csvFiles.Length} 个CSV文件", "确定");
        }
    }

    void ShowExportConfig()
    {
        string fullPath = GetExportFolderFullPath();
        bool folderExists = Directory.Exists(fullPath);

        string message = $"📁 导出文件夹配置信息\n\n" +
                        $"配置路径: {EXPORT_FOLDER_PATH}\n" +
                        $"完整路径: {fullPath}\n" +
                        $"状态: {(folderExists ? "✅ 存在" : "❌ 不存在")}\n\n";

        if (folderExists)
        {
            string[] csvFiles = Directory.GetFiles(fullPath, "*.csv");
            message += $"CSV文件数量: {csvFiles.Length}\n";

            if (csvFiles.Length > 0)
            {
                // 按修改时间排序
                List<FileInfo> fileInfos = new List<FileInfo>();
                foreach (string file in csvFiles)
                {
                    fileInfos.Add(new FileInfo(file));
                }
                fileInfos.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));

                message += "\n最新文件:\n";
                for (int i = 0; i < Mathf.Min(3, fileInfos.Count); i++)
                {
                    var file = fileInfos[i];
                    message += $"• {Path.GetFileName(file.Name)} ({file.Length / 1024}KB)\n";
                }
            }
        }
        else
        {
            message += "\n点击\"转换并自动保存\"按钮会自动创建文件夹。";
        }

        EditorUtility.DisplayDialog("导出配置", message, "确定");
    }

    // ============ 原有转换方法保持不变 ============

    string ConvertCSVSeparator(string csvContent, char oldSeparator, char newSeparator)
    {
        string[] lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
        List<string> convertedLines = new List<string>();

        foreach (string line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                convertedLines.Add("");
                continue;
            }

            convertedLines.Add(ConvertLine(line, oldSeparator, newSeparator));
        }

        return string.Join("\n", convertedLines);
    }

    string ConvertLine(string line, char oldSeparator, char newSeparator)
    {
        List<string> fields = ParseCSVLine(line, oldSeparator);

        for (int i = 0; i < fields.Count; i++)
        {
            fields[i] = RemoveQuotesIfPossible(fields[i]);
        }

        return string.Join(newSeparator.ToString(), fields);
    }

    List<string> ParseCSVLine(string line, char separator)
    {
        List<string> fields = new List<string>();
        StringBuilder current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == separator && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    string RemoveQuotesIfPossible(string field)
    {
        field = field.Trim();

        if (field.StartsWith("\"") && field.EndsWith("\""))
        {
            string inner = field.Substring(1, field.Length - 2);

            bool hasUnescapedQuotes = false;
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '"')
                {
                    if (i + 1 < inner.Length && inner[i + 1] == '"')
                    {
                        i++;
                    }
                    else
                    {
                        hasUnescapedQuotes = true;
                        break;
                    }
                }
            }

            if (!hasUnescapedQuotes)
            {
                return inner.Replace("\"\"", "\"");
            }
        }

        return field;
    }

    void ShowSuccessMessage(string savedPath, string originalContent, string convertedContent, bool isAutoSave)
    {
        string[] originalLines = originalContent.Split('\n');
        string[] convertedLines = convertedContent.Split('\n');

        string example = "✅ 转换成功！\n\n";

        if (isAutoSave)
        {
            example += $"【自动保存到预设文件夹】\n";
            example += $"路径: {EXPORT_FOLDER_PATH}\n\n";
        }
        else
        {
            example += "【手动保存到选择的位置】\n\n";
        }

        example += "转换对比示例：\n";
        example += "【原始文件】（逗号分隔）:\n";
        for (int i = 0; i < Mathf.Min(3, originalLines.Length); i++)
        {
            example += originalLines[i] + "\n";
        }

        example += "\n【转换后】（管道符分隔）:\n";
        for (int i = 0; i < Mathf.Min(3, convertedLines.Length); i++)
        {
            example += convertedLines[i] + "\n";
        }

        string fileName = Path.GetFileName(savedPath);
        example += $"\n文件已保存: {fileName}\n\n";

        if (isAutoSave)
        {
            example += "✅ 自动保存完成！\n";
            example += "✅ 可以直接在Unity中查看文件\n";
        }

        example += "✅ 文本中可以自由使用英文逗号\n";
        example += "✅ 不需要担心CSV解析错误了！";

        EditorUtility.DisplayDialog("转换成功", example, "确定");

        // 用记事本打开查看
        System.Diagnostics.Process.Start("notepad.exe", savedPath);

        // 如果是自动保存，在Unity中高亮显示文件
        if (isAutoSave && savedPath.StartsWith("Assets/"))
        {
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(savedPath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }
    }
}