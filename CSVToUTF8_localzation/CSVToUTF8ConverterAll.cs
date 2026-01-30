#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class CSVToUTF8ConverterAll : EditorWindow
{
    private string targetDirectory = $"{Application.dataPath}/../Tools/Luban/Application/Datas/Language/";
    private string fileName = "";
    private string path = $"";
    private bool includeSubdirectories = true;
    private bool createBackup = true;
    private bool removeBOM = true;
    private bool fixContent = true;
    private Vector2 scrollPosition;

    private List<ConversionResult> results = new List<ConversionResult>();

    [MenuItem("Tools/MyTools/Localization/2. ***(先执行 3. 如发生乱码执行 2.) -- 批量转换UTF-8")]
    static void ShowWindow()
    {
        GetWindow<CSVToUTF8ConverterAll>("CSV批量转换");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField(" 📊 当前选择的CSV文件:", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        // 在这里添加需要水平排列的GUI元素
        // 目录选择
        EditorGUILayout.LabelField("路径:", GUILayout.Width(40));
        EditorGUILayout.Space(10);
        if (string.IsNullOrEmpty(path))
        {
            EditorGUILayout.LabelField("未选择文件", EditorStyles.helpBox);
        }
        else
        {
            // 可点击的路径（带超链接样式）
            if (GUILayout.Button(path, EditorStyles.linkLabel))
            {
                // 在资源管理器中显示文件
                EditorUtility.RevealInFinder(path);
            }
        }

        if (GUILayout.Button("浏览", GUILayout.Width(60)))
        {
            // 在资源管理器中显示文件
            EditorUtility.RevealInFinder(path);
        }

        EditorGUILayout.EndHorizontal();
        // 显示文件名和大小
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("文件名:", GUILayout.Width(50));
            EditorGUILayout.LabelField(fileName, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("大小:", GUILayout.Width(50));
            FileInfo fileInfo = new FileInfo(path);
            EditorGUILayout.LabelField(FormatFileSize(fileInfo.Length));
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(20);

        // 2. 文件选择按钮
        EditorGUILayout.BeginHorizontal();

        // 方法1：使用 OpenFilePanel
        if (GUILayout.Button("选择 CSV 文件", GUILayout.Height(40)))
        {
            // 打开文件对话框
            string path = EditorUtility.OpenFilePanel("选择CSV文件", "", "csv");

            if (!string.IsNullOrEmpty(path))
            {
                fileName = path;
                fileName = Path.GetFileName(path);
                this.path = path;
                // 显示成功对话框
                EditorUtility.DisplayDialog("选择成功",$"已选择文件: {fileName}\n路径: {path}","确定");
            }
        }
        if (GUILayout.Button("转换单独的CSV文件", GUILayout.Height(40)))
        {
            // 使用之前的转换方法
            if (this.path != null && this.path != "")
            {
                CSVToUTF8Converter.ConvertFileToUTF8(this.path, createBackup, removeBOM);
            }
            else
            {
                // 显示成功对话框
                EditorUtility.DisplayDialog("文件是空", $"已选择文件: {this.path}", "确定");
            }
        }

            EditorGUILayout.EndHorizontal();





        EditorGUILayout.Space(60);
        GUILayout.Label("📁 CSV批量转换为UTF-8", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        // 目录选择
        targetDirectory = EditorGUILayout.TextField("目标目录", targetDirectory);
        if (GUILayout.Button("浏览", GUILayout.Width(60)))
        {
            string selectedPath = EditorUtility.OpenFolderPanel("选择目录", Application.dataPath, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                targetDirectory = selectedPath;
            }
        }
        EditorGUILayout.EndHorizontal();

        // 选项
        includeSubdirectories = EditorGUILayout.Toggle("包含子目录", includeSubdirectories);
        createBackup = EditorGUILayout.Toggle("创建备份", createBackup);
        removeBOM = EditorGUILayout.Toggle("移除BOM", removeBOM);
        fixContent = EditorGUILayout.Toggle("修复内容", fixContent);

        EditorGUILayout.Space();

        // 按钮
        if (GUILayout.Button("扫描CSV文件", GUILayout.Height(30)))
        {
            ScanCSVFiles();
        }

        if (GUILayout.Button("开始批量转换", GUILayout.Height(40)))
        {
            ConvertAllFiles();
        }

        if (GUILayout.Button("打开结果目录", GUILayout.Height(30)))
        {
            if (Directory.Exists(targetDirectory))
            {
                EditorUtility.RevealInFinder(targetDirectory);
            }
        }

        // 结果显示
        if (results.Count > 0)
        {
            EditorGUILayout.Space();
            GUILayout.Label($"📊 转换结果 ({results.Count}个文件)", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            int successCount = 0;
            int failCount = 0;
            long totalSizeBefore = 0;
            long totalSizeAfter = 0;

            foreach (var result in results)
            {
                EditorGUILayout.BeginHorizontal("box");

                // 状态图标
                GUIStyle statusStyle = new GUIStyle(EditorStyles.label);
                if (result.success)
                {
                    statusStyle.normal.textColor = Color.green;
                    GUILayout.Label("✅", statusStyle, GUILayout.Width(20));
                    successCount++;
                }
                else
                {
                    statusStyle.normal.textColor = Color.red;
                    GUILayout.Label("❌", statusStyle, GUILayout.Width(20));
                    failCount++;
                }

                // 文件信息
                EditorGUILayout.BeginVertical();
                GUILayout.Label(Path.GetFileName(result.filePath), EditorStyles.miniBoldLabel);

                if (result.success)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label($"编码: {result.encodingBefore} → UTF-8", GUILayout.Width(150));
                    GUILayout.Label($"大小: {FormatBytes(result.sizeBefore)} → {FormatBytes(result.sizeAfter)}", GUILayout.Width(150));
                    EditorGUILayout.EndHorizontal();

                    totalSizeBefore += result.sizeBefore;
                    totalSizeAfter += result.sizeAfter;
                }
                else
                {
                    GUILayout.Label($"错误: {result.errorMessage}", EditorStyles.miniLabel);
                }

                EditorGUILayout.EndVertical();

                // 操作按钮
                if (GUILayout.Button("定位", GUILayout.Width(50)))
                {
                    SelectFile(result.filePath);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // 统计信息
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal("box");
            GUILayout.Label($"成功: {successCount}", GUILayout.Width(80));
            GUILayout.Label($"失败: {failCount}", GUILayout.Width(80));
            GUILayout.Label($"总大小变化: {FormatBytes(totalSizeBefore)} → {FormatBytes(totalSizeAfter)}", GUILayout.Width(200));

            if (totalSizeBefore > 0)
            {
                float compressionRate = (1 - (float)totalSizeAfter / totalSizeBefore) * 100;
                GUILayout.Label($"压缩率: {compressionRate:F1}%", GUILayout.Width(100));
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    void ScanCSVFiles()
    {
        if (!Directory.Exists(targetDirectory))
        {
            Debug.LogError($"目录不存在: {targetDirectory}");
            return;
        }

        results.Clear();

        SearchOption searchOption = includeSubdirectories ?
            SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        string[] csvFiles = Directory.GetFiles(targetDirectory, "*.csv", searchOption);

        foreach (string file in csvFiles)
        {
            var result = new ConversionResult
            {
                filePath = file,
                success = true
            };

            try
            {
                FileInfo fileInfo = new FileInfo(file);
                result.sizeBefore = fileInfo.Length;

                // 检测编码
                result.encodingBefore = CSVToUTF8Converter.DetectFileEncoding(file);

                results.Add(result);
            }
            catch (System.Exception ex)
            {
                result.success = false;
                result.errorMessage = ex.Message;
                results.Add(result);
            }
        }

        Debug.Log($"扫描到 {csvFiles.Length} 个CSV文件");
        Repaint();
    }

    void ConvertAllFiles()
    {
        if (results.Count == 0)
        {
            Debug.LogWarning("请先扫描文件");
            return;
        }

        int successCount = 0;
        int failCount = 0;

        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];

            EditorUtility.DisplayProgressBar("批量转换",
                $"正在转换: {Path.GetFileName(result.filePath)} ({i + 1}/{results.Count})",
                (float)i / results.Count);

            try
            {
                // 使用之前的转换方法
                CSVToUTF8Converter.ConvertFileToUTF8(result.filePath, createBackup, removeBOM);

                // 更新结果
                FileInfo fileInfo = new FileInfo(result.filePath);
                result.sizeAfter = fileInfo.Length;
                result.success = true;
                successCount++;
            }
            catch (System.Exception ex)
            {
                result.success = false;
                result.errorMessage = ex.Message;
                failCount++;
            }

            results[i] = result;
        }

        EditorUtility.ClearProgressBar();

        Debug.Log($"批量转换完成! 成功: {successCount}, 失败: {failCount}");
        Repaint();
    }

    void SelectFile(string filePath)
    {
        // 尝试在项目中选择
        string relativePath = GetRelativePath(filePath);
        if (!string.IsNullOrEmpty(relativePath))
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(relativePath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                return;
            }
        }

        // 否则在文件系统中打开
        EditorUtility.RevealInFinder(filePath);
    }

    string GetRelativePath(string absolutePath)
    {
        string dataPath = Application.dataPath;
        if (absolutePath.StartsWith(dataPath))
        {
            return "Assets" + absolutePath.Substring(dataPath.Length);
        }
        return "";
    }

    string FormatBytes(long bytes)
    {
        const long MB = 1024 * 1024;
        const long KB = 1024;

        if (bytes >= MB)
            return $"{(bytes / (float)MB):F2} MB";
        else if (bytes >= KB)
            return $"{(bytes / (float)KB):F2} KB";
        else
            return $"{bytes} B";
    }

    class ConversionResult
    {
        public string filePath;
        public bool success;
        public Encoding encodingBefore;
        public long sizeBefore;
        public long sizeAfter;
        public string errorMessage;
    }
    /// <summary>
    /// 格式化文件大小
    /// </summary>
    string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double len = bytes;

        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return string.Format("{0:0.##} {1}", len, sizes[order]);
    }

}
#endif