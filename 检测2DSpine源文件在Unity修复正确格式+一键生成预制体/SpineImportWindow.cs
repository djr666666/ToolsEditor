using Spine.Unity;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SpineImportWindow : EditorWindow
{
    private DefaultAsset sourceFolder;
    private DefaultAsset prefabFolder;

 
    private Vector2 scroll;
    private StringBuilder log = new StringBuilder();

    private DefaultAsset batchRootFolder;

    // ==================== 分类系统 ====================

    private enum SpineSystemType
    {
        UI,
        ANI3D,
        ME
    }

    private enum SpineViewType
    {
        Q,
        PT
    }

    private enum SpineBusinessType
    {
        Role,
        Monster
    }

    private SpineSystemType systemType = SpineSystemType.UI;
    private SpineViewType viewType = SpineViewType.Q;
    private SpineBusinessType businessType = SpineBusinessType.Role;

    private string customName = "";

    [MenuItem("Tools/Spine/Import Tool")]
    public static void Open()
    {
        GetWindow<SpineImportWindow>("Spine Pipeline");
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("Spine Pipeline Tool", EditorStyles.boldLabel);

        //==================== 单个模式 ====================

        GUILayout.Label("单个模式", EditorStyles.boldLabel);

        sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Spine源文件夹",
            sourceFolder,
            typeof(DefaultAsset),
            false);

        prefabFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Prefab输出路径",
            prefabFolder,
            typeof(DefaultAsset),
            false);

        GUILayout.Space(10);

        systemType = (SpineSystemType)EditorGUILayout.EnumPopup("系统类型", systemType);
        viewType = (SpineViewType)EditorGUILayout.EnumPopup("表现类型(Q/PT)", viewType);
        businessType = (SpineBusinessType)EditorGUILayout.EnumPopup("业务类型", businessType);

        customName = EditorGUILayout.TextField("手动命名(可选)", customName);

        GUILayout.Space(15);

        if (GUILayout.Button("① 处理Spine资源", GUILayout.Height(30)))
        {
            ProcessSpine();
        }

        if (GUILayout.Button("② 单个生成Prefab", GUILayout.Height(30)))
        {
            CreatePrefab();
        }

        GUILayout.Space(20);

        //==================== 批量模式 ====================

        GUILayout.Label("批量模式（自动递归扫描）", EditorStyles.boldLabel);

        batchRootFolder = (DefaultAsset)EditorGUILayout.ObjectField(
            "Spine根目录",
            batchRootFolder,
            typeof(DefaultAsset),
            false);

        GUILayout.Space(10);

        if (GUILayout.Button("③ 批量处理Spine资源", GUILayout.Height(30)))
        {
            BatchProcessSpine();
        }

        if (GUILayout.Button("④ 批量生成Prefab", GUILayout.Height(30)))
        {
            BatchCreatePrefab();
        }

        GUILayout.Space(15);

        GUILayout.Label("快捷操作", EditorStyles.boldLabel);

        if (GUILayout.Button("⑤ 单个一键导入", GUILayout.Height(35)))
        {
            OneClickImport();
        }

        if (GUILayout.Button("⑥ 批量一键导入", GUILayout.Height(35)))
        {
            OneClickBatchImport();
        }

        GUILayout.Space(10);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        GUILayout.TextArea(
            log.ToString(),
            GUILayout.ExpandHeight(true));

        EditorGUILayout.EndScrollView();
    }


    private List<string> FindSpineFolders(string rootPath)
    {
        List<string> result = new List<string>();

        string fullRoot = Path.Combine(
            Directory.GetCurrentDirectory(),
            rootPath);

        if (!Directory.Exists(fullRoot))
        {
            log.AppendLine($"❌ 目录不存在 : {rootPath}");
            return result;
        }

        // 获取所有子目录
        string[] dirs = Directory.GetDirectories(
            fullRoot,
            "*",
            SearchOption.AllDirectories);

        foreach (string dir in dirs)
        {
            bool hasSkel =
                Directory.GetFiles(dir, "*.skel", SearchOption.TopDirectoryOnly).Length > 0 ||
                Directory.GetFiles(dir, "*.skel.bytes", SearchOption.TopDirectoryOnly).Length > 0;

            bool hasAtlas =
                Directory.GetFiles(dir, "*.atlas", SearchOption.TopDirectoryOnly).Length > 0 ||
                Directory.GetFiles(dir, "*.atlas.txt", SearchOption.TopDirectoryOnly).Length > 0;

            if (!hasSkel || !hasAtlas)
                continue;

            string assetPath = dir.Replace("\\", "/");

            string projectPath = Directory
                .GetCurrentDirectory()
                .Replace("\\", "/");

            if (assetPath.StartsWith(projectPath))
            {
                assetPath = assetPath.Substring(projectPath.Length + 1);
            }

            result.Add(assetPath);
        }

        return result;
    }


    // =========================================================
    // ① Spine资源处理（不改）
    // =========================================================

    private void ProcessSpine()
    {
        log.Clear();

        if (sourceFolder == null)
        {
            Debug.LogWarning("未选择源文件夹");
            return;
        }

        string path = AssetDatabase.GetAssetPath(sourceFolder);
        string full = Path.Combine(Directory.GetCurrentDirectory(), path);

        // 修改skel/atlas后缀
        foreach (var file in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".skel"))
                File.Move(file, file + ".bytes");

            if (file.EndsWith(".atlas"))
                File.Move(file, file + ".txt");
        }

        AssetDatabase.Refresh();

        // 修改所有图片为2D and UI（Sprite）
        string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { path });

        foreach (var guid in texGuids)
        {
            string texPath = AssetDatabase.GUIDToAssetPath(guid);

            TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;

            if (importer == null)
                continue;

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

            if (dirty)
                importer.SaveAndReimport();
        }

        AssetDatabase.Refresh();

        log.AppendLine("✔ Spine资源处理完成");
        log.AppendLine("✔ 图片已设置为 Sprite (2D and UI)");
    }
    // =========================================================
    // ② 单个生成（保留你原流程，不动）
    // =========================================================

    private void CreatePrefab()
    {
        if (sourceFolder == null || prefabFolder == null)
        {
            Debug.LogWarning("请选择源文件夹和Prefab输出路径");
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(sourceFolder);
        string outputPath = AssetDatabase.GetAssetPath(prefabFolder);

        string[] guids = AssetDatabase.FindAssets("t:SkeletonDataAsset", new[] { sourcePath });

        if (guids.Length == 0)
        {
            Debug.LogWarning("未找到 SkeletonDataAsset");
            return;
        }

        var data = AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(
            AssetDatabase.GUIDToAssetPath(guids[0])
        );

        string folderName = Path.GetFileName(sourcePath);
        string prefabName = GetPrefabName(folderName);

        CreateOnePrefab(data, prefabName, outputPath);
    }

    // =========================================================
    // ③ 3.0 批处理核心（新增）
    // =========================================================

    private void BatchCreatePrefab()
    {
        log.Clear();

        if (batchRootFolder == null)
        {
            Debug.LogWarning("请选择Spine根目录");
            return;
        }

        if (prefabFolder == null)
        {
            Debug.LogWarning("请选择Prefab输出路径");
            return;
        }

        string rootPath = AssetDatabase.GetAssetPath(batchRootFolder);
        string outputPath = AssetDatabase.GetAssetPath(prefabFolder);

        List<string> folders = FindSpineFolders(rootPath);

        if (folders == null || folders.Count == 0)
        {
            Debug.LogWarning("未找到任何Spine资源目录");
            return;
        }

        int success = 0;
        int fail = 0;
        int skip = 0;

        int total = folders.Count;
        int index = 0;

        foreach (var sourcePath in folders)
        {
            index++;

            // ===== 进度条 + 可取消 =====
            if (EditorUtility.DisplayCancelableProgressBar(
                "Spine 批量生成Prefab",
                $"正在处理：{Path.GetFileName(sourcePath)} ({index}/{total})",
                (float)index / total))
            {
                EditorUtility.ClearProgressBar();
                log.AppendLine("⚠ 用户取消批量生成");
                return;
            }

            try
            {
                string[] guids = AssetDatabase.FindAssets(
                    "t:SkeletonDataAsset",
                    new[] { sourcePath });

                if (guids.Length == 0)
                {
                    log.AppendLine($"❌ 未找到SkeletonDataAsset : {sourcePath}");
                    fail++;
                    continue;
                }

                SkeletonDataAsset data =
                    AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(
                        AssetDatabase.GUIDToAssetPath(guids[0]));

                if (data == null)
                {
                    log.AppendLine($"❌ SkeletonDataAsset加载失败 : {sourcePath}");
                    fail++;
                    continue;
                }

                string folderName = Path.GetFileName(sourcePath);
                string prefabName = GetPrefabName(folderName);

                string savePath = $"{outputPath}/{prefabName}.prefab";

                GameObject exist =
                    AssetDatabase.LoadAssetAtPath<GameObject>(savePath);

                if (exist != null)
                {
                    log.AppendLine($"⚪ 已存在Prefab，跳过 : {prefabName}");
                    skip++;
                    continue;
                }

                CreateOnePrefab(data, prefabName, outputPath);

                log.AppendLine($"✔ 生成成功 : {prefabName}");

                success++;
            }
            catch (System.Exception e)
            {
                log.AppendLine($"❌ 生成失败 : {sourcePath}");
                log.AppendLine(e.Message);

                fail++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.ClearProgressBar();

        log.AppendLine("");
        log.AppendLine("==========================");
        log.AppendLine($"扫描目录 : {folders.Count}");
        log.AppendLine($"成功生成 : {success}");
        log.AppendLine($"已跳过   : {skip}");
        log.AppendLine($"失败数量 : {fail}");
        log.AppendLine("==========================");

        EditorUtility.DisplayDialog(
            "Spine 批量生成完成",
            $"扫描目录：{folders.Count}\n成功：{success}\n跳过：{skip}\n失败：{fail}",
            "确定");
    }

    // =========================================================
    // prefab生成核心（统一复用）
    // =========================================================

    private void CreateOnePrefab(SkeletonDataAsset data, string prefabName, string outputPath)
    {
        GameObject root = new GameObject(prefabName);

        GameObject parent;
        GameObject local;

        if (systemType == SpineSystemType.UI)
        {
            var rootRT = root.AddComponent<RectTransform>();
            EnsureUILayer(root);

            parent = new GameObject("Parent");
            parent.transform.SetParent(root.transform, false);

            RectTransform parentRT = parent.AddComponent<RectTransform>();
            parentRT.anchorMin = Vector2.zero;
            parentRT.anchorMax = Vector2.one;
            parentRT.offsetMin = Vector2.zero;
            parentRT.offsetMax = Vector2.zero;

            local = new GameObject("Local");
            local.transform.SetParent(parent.transform, false);

            RectTransform localRT = local.AddComponent<RectTransform>();
            localRT.anchorMin = new Vector2(0.5f, 0.5f);
            localRT.anchorMax = new Vector2(0.5f, 0.5f);
            localRT.anchoredPosition = Vector2.zero;
            localRT.sizeDelta = Vector2.zero;

            var sg = local.AddComponent<SkeletonGraphic>();
            sg.skeletonDataAsset = data;
            sg.Initialize(true);
        }
        else
        {
            parent = new GameObject("Parent");
            parent.transform.SetParent(root.transform, false);

            local = new GameObject("Local");
            local.transform.SetParent(parent.transform, false);

            var sa = local.AddComponent<SkeletonAnimation>();
            sa.skeletonDataAsset = data;
            sa.Initialize(true);
        }

        string savePath = $"{outputPath}/{prefabName}.prefab";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(savePath);
        if (existing != null)
        {
            log.AppendLine($"⚠ 跳过（Prefab已存在）: {prefabName}");
            Object.DestroyImmediate(root);
            return;
        }

        PrefabUtility.SaveAsPrefabAsset(root, savePath);

        Object.DestroyImmediate(root);
    }

    // =========================================================
    // UI Layer
    // =========================================================

    private void EnsureUILayer(GameObject go)
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer == -1) return;
        go.layer = uiLayer;
    }

    // =========================================================
    // 命名
    // =========================================================

    private string GetPrefabName(string folderName)
    {
        return $"{systemType}_{viewType}_{businessType}_{folderName}";
    }

    private void BatchProcessSpine()
    {
        log.Clear();

        if (batchRootFolder == null)
        {
            Debug.LogWarning("请选择Spine根目录");
            return;
        }

        string rootPath = AssetDatabase.GetAssetPath(batchRootFolder);

        string fullRoot = Path.Combine(
            Directory.GetCurrentDirectory(),
            rootPath);

        if (!Directory.Exists(fullRoot))
        {
            Debug.LogWarning("目录不存在");
            return;
        }

        int convertCount = 0;
        int skipCount = 0;

        // =========================
        // Step 1: 收集文件
        // =========================
        string[] files = Directory.GetFiles(fullRoot, "*", SearchOption.AllDirectories);

        int total = files.Length;
        int index = 0;

        // =========================
        // Step 2: 处理 skel / atlas
        // =========================
        foreach (string file in files)
        {
            index++;

            if (EditorUtility.DisplayCancelableProgressBar(
                "Spine 批量处理资源",
                $"处理文件：{Path.GetFileName(file)} ({index}/{total})",
                (float)index / total))
            {
                EditorUtility.ClearProgressBar();
                log.AppendLine("⚠ 用户取消处理");
                return;
            }

            try
            {
                // ===== skel → bytes =====
                if (file.EndsWith(".skel"))
                {
                    string dst = file + ".bytes";

                    if (!File.Exists(dst))
                    {
                        File.Move(file, dst);
                        convertCount++;
                        log.AppendLine($"✔ skel : {Path.GetFileName(file)}");
                    }
                    else
                    {
                        skipCount++;
                    }
                }

                // ===== atlas → txt =====
                if (file.EndsWith(".atlas"))
                {
                    string dst = file + ".txt";

                    if (!File.Exists(dst))
                    {
                        File.Move(file, dst);
                        convertCount++;
                        log.AppendLine($"✔ atlas : {Path.GetFileName(file)}");
                    }
                    else
                    {
                        skipCount++;
                    }
                }
            }
            catch (System.Exception e)
            {
                log.AppendLine($"❌ 处理失败 : {file}");
                log.AppendLine(e.Message);
            }
        }

        AssetDatabase.Refresh();

        // =========================
        // Step 3: 设置 Sprite
        // =========================
        string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { rootPath });

        int texTotal = texGuids.Length;
        int texIndex = 0;

        foreach (string guid in texGuids)
        {
            texIndex++;

            if (EditorUtility.DisplayCancelableProgressBar(
                "Spine 设置Sprite (2D and UI)",
                $"处理贴图：{texIndex}/{texTotal}",
                texTotal == 0 ? 1 : (float)texIndex / texTotal))
            {
                EditorUtility.ClearProgressBar();
                log.AppendLine("⚠ 用户取消贴图设置");
                return;
            }

            string texPath = AssetDatabase.GUIDToAssetPath(guid);

            TextureImporter importer =
                AssetImporter.GetAtPath(texPath) as TextureImporter;

            if (importer == null)
                continue;

            bool dirty = false;

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                dirty = true;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
                log.AppendLine($"✔ Sprite : {Path.GetFileName(texPath)}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        FixSpineAtlasStraightAlpha(rootPath);
        AssetDatabase.Refresh();

        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { rootPath });

        int matCount = 0;

        foreach (string guid in matGuids)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (mat == null) continue;

            bool changed = false;

            // =========================
            // 1. float 参数方式（部分 shader）
            // =========================
            if (mat.HasProperty("_StraightAlphaTexture"))
            {
                if (mat.GetFloat("_StraightAlphaTexture") < 0.5f)
                {
                    mat.SetFloat("_StraightAlphaTexture", 1f);
                    changed = true;
                }
            }

            if (mat.HasProperty("_StraightAlpha"))
            {
                if (mat.GetFloat("_StraightAlpha") < 0.5f)
                {
                    mat.SetFloat("_StraightAlpha", 1f);
                    changed = true;
                }
            }

            // =========================
            // 2. Keyword 方式（Spine 官方常见）
            // =========================
            if (!mat.IsKeywordEnabled("STRAIGHT_ALPHA_TEXTURE"))
            {
                mat.EnableKeyword("STRAIGHT_ALPHA_TEXTURE");
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(mat);
                matCount++;
                log.AppendLine($"✔ StraightAlpha开启 : {mat.name}");
            }
        }

        AssetDatabase.SaveAssets();

        log.AppendLine($"材质StraightAlpha修复数量 : {matCount}");

        EditorUtility.ClearProgressBar();

        log.AppendLine("");
        log.AppendLine("======================");
        log.AppendLine($"处理完成");
        log.AppendLine($"文件总数 : {files.Length}");
        log.AppendLine($"转换数量 : {convertCount}");
        log.AppendLine($"跳过数量 : {skipCount}");
        log.AppendLine("======================");

        EditorUtility.DisplayDialog(
            "Spine 处理完成",
            $"文件总数：{files.Length}\n转换：{convertCount}\n跳过：{skipCount}",
            "确定");
    }

    private void OneClickImport()
    {
        if (sourceFolder == null)
        {
            EditorUtility.DisplayDialog(
                "提示",
                "请选择Spine源文件夹！",
                "确定");
            return;
        }

        if (prefabFolder == null)
        {
            EditorUtility.DisplayDialog(
                "提示",
                "请选择Prefab输出路径！",
                "确定");
            return;
        }

        bool ok = EditorUtility.DisplayDialog(
            "确认导入",
            "即将执行【单个一键导入】\n\n将会：\n1、处理Spine资源\n2、生成Prefab\n\n是否继续？",
            "开始导入",
            "取消");

        if (!ok)
            return;

        ProcessSpine();

        AssetDatabase.Refresh();

        CreatePrefab();

        log.AppendLine("");
        log.AppendLine("==========");
        log.AppendLine("单个一键导入完成");
        log.AppendLine("==========");
    }
    private void OneClickBatchImport()
    {
        if (batchRootFolder == null)
        {
            EditorUtility.DisplayDialog(
                "提示",
                "请选择Spine根目录！",
                "确定");
            return;
        }

        if (prefabFolder == null)
        {
            EditorUtility.DisplayDialog(
                "提示",
                "请选择Prefab输出路径！",
                "确定");
            return;
        }

        string rootPath = AssetDatabase.GetAssetPath(batchRootFolder);

        bool ok = EditorUtility.DisplayDialog(
            "确认批量导入",
            $"即将扫描目录：\n\n{rootPath}\n\n将执行：\n1、批量处理Spine资源\n2、批量生成Prefab\n\n是否继续？",
            "开始导入",
            "取消");

        if (!ok)
            return;

        BatchProcessSpine();

        AssetDatabase.Refresh();

        BatchCreatePrefab();

        log.AppendLine("");
        log.AppendLine("==========");
        log.AppendLine("批量一键导入完成");
        log.AppendLine("==========");
    }

    private void FixSpineAtlasStraightAlpha(string rootPath)
    {
        string[] atlasGuids = AssetDatabase.FindAssets("t:SpineAtlasAsset", new[] { rootPath });

        foreach (var guid in atlasGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var atlas = AssetDatabase.LoadAssetAtPath<SpineAtlasAsset>(path);

            if (atlas == null) continue;

            var materials = atlas.materials;
            if (materials == null) continue;

            foreach (var mat in materials)
            {
                if (mat == null) continue;

                bool changed = false;

                // 关键1：float字段
                if (mat.HasProperty("_StraightAlphaTexture"))
                {
                    mat.SetFloat("_StraightAlphaTexture", 1f);
                    changed = true;
                }

                // 关键2：keyword（非常重要）
                if (!mat.IsKeywordEnabled("STRAIGHT_ALPHA_TEXTURE"))
                {
                    mat.EnableKeyword("STRAIGHT_ALPHA_TEXTURE");
                    changed = true;
                }

                if (changed)
                {
                    EditorUtility.SetDirty(mat);
                }
            }
        }

        AssetDatabase.SaveAssets();
    }
}