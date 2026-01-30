using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

public class ModifyCsvDelimiter : EditorWindow
{
    [MenuItem("Tools/MyTools/Localization/1. 开源localzationCSV分隔符为管道符")]
    public static void ModifyCsvFile()
    {
        bool proceed = EditorUtility.DisplayDialog("修改CSV分隔符",
                  "此操作将修改Localization插件的CSV分隔符为管道符(|)。\n\n" +
                  "注意：\n" +
                  "1. 如果导出后报错，请检查是否缺少 using CsvHelper.Configuration;\n" +
                  "2. 解决方法：确保 using CsvHelper.Configuration; 在文件顶部\n\n" +
                  "是否继续？",
                  "继续", "取消");
        if (!proceed) return;

        // 先备份
        string targetFile = FindCsvFile();
        if (targetFile == null)
        {
            EditorUtility.DisplayDialog("错误", "未找到Csv.cs文件", "确定");
            return;
        }

        string backupFile = targetFile + ".backup";
        File.Copy(targetFile, backupFile, true);

        // 读取文件
        string content = File.ReadAllText(targetFile, Encoding.UTF8);
        string originalContent = content;

        // 2. 修改Export方法中的CsvWriter
        content = ModifyExportMethod(content);

        // 3. 修改ImportInto方法中的CsvReader
        content = ModifyImportMethod(content);


        // 1. 添加命名空间（如果需要）
        if (!content.Contains("using CsvHelper.Configuration;"))
        {
            int lastUsingIndex = content.LastIndexOf("using ");
            int lineEndIndex = content.IndexOf(';', lastUsingIndex) + 1;
            content = content.Insert(lineEndIndex, "\nusing CsvHelper.Configuration;");
        }



        // 写入修改
        File.WriteAllText(targetFile, content, Encoding.UTF8);

        // 显示修改结果
        ShowModificationResult(targetFile, originalContent, content);
    }

    static string FindCsvFile()
    {
        string packageCachePath = Path.Combine(Application.dataPath, "..", "Library", "PackageCache");
        string[] allFiles = Directory.GetFiles(packageCachePath, "Csv.cs", SearchOption.AllDirectories);

        foreach (string file in allFiles)
        {
            if (file.Contains("localization", System.StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    static string ModifyExportMethod(string content)
    {
        // 查找Export方法中的CsvWriter创建
        int exportStart = content.IndexOf("public static void Export(TextWriter writer, StringTableCollection collection, IList<CsvColumns> columnMappings");
        if (exportStart == -1)
        {
            return content;
        }


        // 找到using (var csvWriter = new CsvWriter(
        int csvWriterStart = content.IndexOf("using (var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture))", exportStart);
        if (csvWriterStart == -1)
        {
            return content;
        }

        // 获取前面的缩进
        int lineStart = content.LastIndexOf('\n', csvWriterStart) + 1;
        string indent = content.Substring(lineStart, csvWriterStart - lineStart);

        // 构建新的代码
        string newCode = indent + "// === 修改这里：添加CSV配置 ===\n" +
                        indent + "var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)\n" +
                        indent + "{\n" +
                        indent + "    Delimiter = \"|\", // 管道符分隔符\n" +
                        indent + "    HasHeaderRecord = true\n" +
                        indent + "};\n" +
                        indent + "\n" +
                        indent + "using (var csvWriter = new CsvWriter(writer, csvConfig)) // 使用配置";

        // 替换
        int blockEnd = content.IndexOf("\n" + indent + "{", csvWriterStart);
        string toReplace = content.Substring(csvWriterStart, blockEnd - csvWriterStart);
        return content.Replace(toReplace, newCode);
    }

    static string ModifyImportMethod(string content)
    {
        // 查找ImportInto方法中的CsvReader创建
        int importStart = content.IndexOf("public static void ImportInto(TextReader reader, StringTableCollection collection, IList<CsvColumns> columnMappings");
        if (importStart == -1) return content;

        // 找到using (var csvReader = new CsvReader(
        int csvReaderStart = content.IndexOf("using (var csvReader = new CsvReader(reader, CultureInfo.InvariantCulture))", importStart);
        if (csvReaderStart == -1) return content;

        // 获取前面的缩进
        int lineStart = content.LastIndexOf('\n', csvReaderStart) + 1;
        string indent = content.Substring(lineStart, csvReaderStart - lineStart);

        // 构建新的代码
        string newCode = indent + "// === 修改这里：添加CSV配置 ===\n" +
                        indent + "var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)\n" +
                        indent + "{\n" +
                        indent + "    Delimiter = \"|\", // 管道符分隔符\n" +
                        indent + "    HasHeaderRecord = true,\n" +
                        indent + "    MissingFieldFound = null, // 忽略缺失字段\n" +
                        indent + "    BadDataFound = null // 忽略坏数据\n" +
                        indent + "};\n" +
                        indent + "\n" +
                        indent + "using (var csvReader = new CsvReader(reader, csvConfig)) // 使用配置";

        // 替换
        int blockEnd = content.IndexOf("\n" + indent + "{", csvReaderStart);
        string toReplace = content.Substring(csvReaderStart, blockEnd - csvReaderStart);

        return content.Replace(toReplace, newCode);
    }

    static void ShowModificationResult(string filePath, string original, string modified)
    {
        string fileName = Path.GetFileName(filePath);

        // 检查修改内容
        bool exportModified = original.IndexOf("using (var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture))") !=
                            modified.IndexOf("using (var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture))");

        bool importModified = original.IndexOf("using (var csvReader = new CsvReader(reader, CultureInfo.InvariantCulture))") !=
                            modified.IndexOf("using (var csvReader = new CsvReader(reader, CultureInfo.InvariantCulture))");

        string message = $"✅ 修改完成！\n\n" +
                        $"文件: {fileName}\n\n" +
                        $"修改内容：\n";

        if (exportModified) message += "• Export方法：CsvWriter使用管道符分隔符\n";
        if (importModified) message += "• ImportInto方法：CsvReader使用管道符分隔符\n";

        message += "\n现在Localization插件将使用管道符(|)作为CSV分隔符。\n\n" +
                  "原文件已备份为 .backup 文件。";

        EditorUtility.DisplayDialog("修改成功", message, "确定");

        // 用记事本打开查看
        System.Diagnostics.Process.Start("notepad.exe", filePath);
    }
}