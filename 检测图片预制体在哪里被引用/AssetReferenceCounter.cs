using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 资源引用计数器
/// ------------------------------------------------------------------
/// 功能：
///   1. 扫描整个工程，建立「资源 ← 被哪些预制体/场景/资产引用」的反向索引。
///   2. 在 Project 窗口里，被引用的图片 / 代码 / 资源后面用黄色标出引用次数。
///   3. 鼠标点击那个黄色数字，会弹出一个列表，告诉你它现在被谁引用。
///   4. 可以在 Tools 菜单里随时开关显示，并重建索引。
///
/// 入口（Tools 下拉菜单）：
///   Tools/资源引用计数器/打开面板
///   Tools/资源引用计数器/显示引用计数   （带勾选的开关）
///   Tools/资源引用计数器/重建索引
/// </summary>
[InitializeOnLoad]
public static class AssetReferenceCounter
{
    // ---------- 配置 ----------

    // 哪些类型的资源算作「引用者（容器）」。这些文件会被解析依赖，从而构成反向引用。
    private static readonly HashSet<string> ContainerExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".prefab", ".unity", ".asset", ".controller", ".mat", ".anim", ".spriteatlas", ".playable"
    };

    private const string MenuToggle = "Tools/资源引用计数器/显示引用计数";
    private const string MenuRebuild = "Tools/资源引用计数器/重建索引";
    private const string MenuPanel = "Tools/资源引用计数器/打开面板";

    private const string PrefEnabled = "AssetRefCounter.Enabled";
    private static readonly string CacheFile =
        Path.Combine(Application.dataPath, "..", "Library", "AssetReferenceCounter.json");

    // ---------- 运行时状态 ----------

    // 资源 GUID -> 引用它的容器 GUID 集合
    private static Dictionary<string, HashSet<string>> _index;

    private static bool _enabled;
    private static GUIStyle _countStyle;

    public static bool Enabled => _enabled;
    public static bool HasIndex => _index != null && _index.Count > 0;
    public static int IndexedCount => _index != null ? _index.Count : 0;

    // ---------- 初始化 ----------

    static AssetReferenceCounter()
    {
        _enabled = EditorPrefs.GetBool(PrefEnabled, false);
        LoadIndex();
        if (_enabled)
            Subscribe();
    }

    private static void Subscribe()
    {
        EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
        EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
    }

    private static void Unsubscribe()
    {
        EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
    }

    // ---------- 菜单：开关 ----------

    [MenuItem(MenuToggle)]
    private static void ToggleEnabled()
    {
        _enabled = !_enabled;
        EditorPrefs.SetBool(PrefEnabled, _enabled);
        Menu.SetChecked(MenuToggle, _enabled);

        if (_enabled)
        {
            Subscribe();
            // 第一次打开且没有索引时，提示是否立刻扫描
            if (!HasIndex)
            {
                if (EditorUtility.DisplayDialog("资源引用计数器",
                        "当前还没有引用索引，是否现在扫描整个工程？\n（工程较大时可能需要一些时间）",
                        "立即扫描", "稍后"))
                {
                    RebuildIndex();
                }
            }
        }
        else
        {
            Unsubscribe();
        }

        EditorApplication.RepaintProjectWindow();
    }

    [MenuItem(MenuToggle, true)]
    private static bool ToggleEnabledValidate()
    {
        Menu.SetChecked(MenuToggle, _enabled);
        return true;
    }

    // ---------- 菜单：重建索引 ----------

    [MenuItem(MenuRebuild)]
    public static void RebuildIndex()
    {
        var newIndex = new Dictionary<string, HashSet<string>>();

        try
        {
            string[] allPaths = AssetDatabase.GetAllAssetPaths();

            // 只处理 Assets/ 下、且属于「容器」类型的资源
            var containers = allPaths
                .Where(p => p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .Where(p => ContainerExtensions.Contains(Path.GetExtension(p)))
                .ToArray();

            for (int i = 0; i < containers.Length; i++)
            {
                string containerPath = containers[i];

                if (i % 20 == 0 &&
                    EditorUtility.DisplayCancelableProgressBar(
                        "资源引用计数器 - 重建索引",
                        $"{i}/{containers.Length}  {containerPath}",
                        (float)i / containers.Length))
                {
                    Debug.LogWarning("资源引用计数器：索引重建被取消。");
                    return;
                }

                string containerGuid = AssetDatabase.AssetPathToGUID(containerPath);

                // 直接依赖（recursive = false）。嵌套预制体内部引用由嵌套预制体自身负责，
                // 这样得到的「引用次数」就等于「直接引用它的容器数量」。
                string[] deps = AssetDatabase.GetDependencies(containerPath, false);
                foreach (string dep in deps)
                {
                    if (dep == containerPath) continue;
                    if (!dep.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;

                    string depGuid = AssetDatabase.AssetPathToGUID(dep);
                    if (string.IsNullOrEmpty(depGuid)) continue;

                    if (!newIndex.TryGetValue(depGuid, out var set))
                    {
                        set = new HashSet<string>();
                        newIndex[depGuid] = set;
                    }
                    set.Add(containerGuid);
                }
            }

            _index = newIndex;
            SaveIndex();

            Debug.Log($"<color=cyan>资源引用计数器：索引重建完成，共 {_index.Count} 个被引用资源。</color>");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            EditorApplication.RepaintProjectWindow();
        }
    }

    // ---------- 菜单：打开面板 ----------

    [MenuItem(MenuPanel)]
    private static void OpenPanel()
    {
        AssetReferenceCounterWindow.Open();
    }

    // ---------- 查询 ----------

    /// <summary>取得某个 GUID 当前有效的引用者路径列表。</summary>
    public static List<string> GetReferencerPaths(string guid)
    {
        var result = new List<string>();
        if (_index == null || !_index.TryGetValue(guid, out var set)) return result;

        foreach (string refGuid in set)
        {
            string path = AssetDatabase.GUIDToAssetPath(refGuid);
            if (!string.IsNullOrEmpty(path))
                result.Add(path);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    // ---------- Project 窗口绘制 ----------

    private static void OnProjectWindowItemGUI(string guid, Rect rect)
    {
        if (!_enabled || _index == null) return;
        if (!_index.TryGetValue(guid, out var refs) || refs.Count == 0) return;

        if (_countStyle == null)
        {
            _countStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = Color.black }
            };
        }

        string text = refs.Count.ToString();
        Vector2 size = _countStyle.CalcSize(new GUIContent(text));
        float w = Mathf.Max(size.x + 6f, 16f);
        float h = 14f;

        // 列表视图（行高约 16）右侧；图标视图放右下角
        bool isIconView = rect.height > 20f;
        Rect badge = isIconView
            ? new Rect(rect.xMax - w, rect.yMax - h - 14f, w, h)
            : new Rect(rect.xMax - w - 2f, rect.y + (rect.height - h) * 0.5f, w, h);

        // 黄色底 + 黑字，保证在深浅皮肤下都醒目
        EditorGUI.DrawRect(badge, new Color(1f, 0.85f, 0.1f, 0.95f));
        GUI.Label(badge, text, _countStyle);

        // 点击黄色数字 -> 弹出引用者列表
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && badge.Contains(e.mousePosition))
        {
            ReferenceListWindow.ShowFor(guid);
            e.Use();
        }
    }

    // ---------- 持久化（跨域重载 / 重启 Editor 后仍可用） ----------

    [Serializable]
    private class Entry
    {
        public string guid;
        public string[] refs;
    }

    [Serializable]
    private class IndexData
    {
        public Entry[] entries;
    }

    private static void SaveIndex()
    {
        try
        {
            var data = new IndexData
            {
                entries = _index.Select(kv => new Entry
                {
                    guid = kv.Key,
                    refs = kv.Value.ToArray()
                }).ToArray()
            };
            File.WriteAllText(Path.GetFullPath(CacheFile), EditorJsonUtility.ToJson(data));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("资源引用计数器：保存索引失败 - " + ex.Message);
        }
    }

    private static void LoadIndex()
    {
        try
        {
            string full = Path.GetFullPath(CacheFile);
            if (!File.Exists(full)) { _index = new Dictionary<string, HashSet<string>>(); return; }

            var data = new IndexData();
            EditorJsonUtility.FromJsonOverwrite(File.ReadAllText(full), data);

            _index = new Dictionary<string, HashSet<string>>();
            if (data.entries != null)
            {
                foreach (var entry in data.entries)
                {
                    if (string.IsNullOrEmpty(entry.guid)) continue;
                    _index[entry.guid] = new HashSet<string>(entry.refs ?? Array.Empty<string>());
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("资源引用计数器：读取索引失败 - " + ex.Message);
            _index = new Dictionary<string, HashSet<string>>();
        }
    }
}

/// <summary>点击黄色数字后弹出的引用者列表窗口。</summary>
public class ReferenceListWindow : EditorWindow
{
    private string _targetGuid;
    private string _targetPath;
    private List<string> _referencers = new List<string>();
    private Vector2 _scroll;

    public static void ShowFor(string guid)
    {
        var win = GetWindow<ReferenceListWindow>(true, "引用者列表");
        win.minSize = new Vector2(420, 200);
        win._targetGuid = guid;
        win._targetPath = AssetDatabase.GUIDToAssetPath(guid);
        win._referencers = AssetReferenceCounter.GetReferencerPaths(guid);
        win.Show();
        win.Focus();
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(_targetPath))
        {
            EditorGUILayout.HelpBox("目标资源已不存在。", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("被引用的资源：", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope("box"))
        {
            var icon = AssetDatabase.GetCachedIcon(_targetPath);
            if (icon != null) GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            EditorGUILayout.LabelField(_targetPath);
            if (GUILayout.Button("定位", GUILayout.Width(50)))
                PingPath(_targetPath);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"被以下 {_referencers.Count} 个资源引用：", EditorStyles.boldLabel);

        if (_referencers.Count == 0)
        {
            EditorGUILayout.HelpBox("没有找到引用者（或索引尚未重建）。", MessageType.Info);
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (string path in _referencers)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var icon = AssetDatabase.GetCachedIcon(path);
                if (icon != null) GUILayout.Label(icon, GUILayout.Width(18), GUILayout.Height(18));

                if (GUILayout.Button(path, EditorStyles.linkLabel))
                    PingPath(path);
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        if (GUILayout.Button("刷新"))
            _referencers = AssetReferenceCounter.GetReferencerPaths(_targetGuid);
    }

    private static void PingPath(string path)
    {
        var obj = AssetDatabase.LoadMainAssetAtPath(path);
        if (obj != null)
        {
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }
    }
}

/// <summary>Tools 菜单里打开的总面板。</summary>
public class AssetReferenceCounterWindow : EditorWindow
{
    public static void Open()
    {
        var win = GetWindow<AssetReferenceCounterWindow>("资源引用计数器");
        win.minSize = new Vector2(360, 220);
        win.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("资源引用计数器", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "扫描整个工程，统计每个图片 / 代码 / 资源被哪些预制体、场景、资产引用。\n" +
            "开启后，引用次数会以黄色数字显示在 Project 窗口资源的右侧，点击数字可查看引用者列表。",
            MessageType.Info);

        EditorGUILayout.Space();

        bool enabled = AssetReferenceCounter.Enabled;
        EditorGUILayout.LabelField("显示开关", enabled ? "● 已开启" : "○ 已关闭");
        if (GUILayout.Button(enabled ? "关闭显示引用计数" : "开启显示引用计数", GUILayout.Height(28)))
        {
            EditorApplication.ExecuteMenuItem("Tools/资源引用计数器/显示引用计数");
            Repaint();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("当前索引",
            AssetReferenceCounter.HasIndex
                ? $"已建立，{AssetReferenceCounter.IndexedCount} 个被引用资源"
                : "尚未建立");

        if (GUILayout.Button("重建索引（扫描整个工程）", GUILayout.Height(28)))
        {
            AssetReferenceCounter.RebuildIndex();
            Repaint();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "提示：新增 / 删除 / 修改资源引用关系后，需要重新「重建索引」才能刷新计数。",
            MessageType.None);
    }
}
