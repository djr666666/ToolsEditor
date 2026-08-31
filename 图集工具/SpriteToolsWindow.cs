// ★ 通用化开关：目标项目【没装 TextMeshPro】就注释掉下面这行，
//   「图集转换 → TMP_SpriteAsset」会自动隐藏，其余功能照常编译使用。
#define SPRITETOOLS_TMP

// ============================================================================
//  Sprite 工具箱 —— 一站式 2D 图集可视化编辑器
//  菜单：Tools/Sprite 工具箱
//
//  说明：本工具完全独立、自包含，不依赖也不修改项目里原有的
//        RightClickMenuExtension.*（ProjectPanelRightClickExtension 右键菜单版）。
//        核心逻辑为独立移植版，放在 namespace SpriteTools 下，与全局符号零冲突。
//
//  环境要求：Unity 2021.3+（需装 2D Sprite 包 com.unity.2d.sprite，切图/合并/拆分依赖它的
//           SpriteDataProviderFactories；该包在 2021.3 一般已内置）。
//           无 TextMeshPro 时注释顶部 #define SPRITETOOLS_TMP 即可。
//
//  作者：onzero
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
#if SPRITETOOLS_TMP
using TMPro;
#endif
using UnityEditor;
using UnityEditor.U2D;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpriteTools
{
    public class SpriteToolsWindow : EditorWindow
    {
        // ------------------------------------------------------------------ Tab
        enum Tab { ToSprite, Slice, Combine, Split, Convert, Table, Lookup, Prefab }

        static readonly string[] kTabNames =
        {
            "转 Sprite", "JSON 切图", "合并图集", "拆分图集", "图集转换", "注册图集表", "子图速查", "预制体换图"
        };

        static readonly string[] kTabDesc =
        {
            "把图片（或整个父文件夹递归）的贴图类型批量改成 Sprite (2D and UI) + Sprite Mode: Single；已经是这个设置的自动跳过，不重复导入。",
            "把 TexturePacker 导出的 .json 直接切成 Sprite(Multiple)，无需手动切图。\n（TexturePacker 里 Data Format 选「JSON (Array)」，并关闭 Allow rotation）",
            "把多张散图打包合并成一张 Sprite(Multiple) 图集（自动排版并写好切片）。",
            "把 Sprite(Multiple) 图集拆成一张张单图 PNG，输出到「原名_sliced」文件夹。",
            "把 SpriteAtlas 转成 TMP_SpriteAsset / Sprite(Multiple) / TextureSheet。",
            "把图集登记进 AtlasName 枚举表；可查看/搜索/清理已注册项，或一键扫描整个 Atlas 目录全部注册。",
            "列出图集里所有子图名字，一键复制 GameTools.SetAtlasSprite(...) 调用代码。",
            "遍历预制体里所有 Image，把它替换成「图集里同名的子图」。"
        };

        Tab _tab;

        // 各 tab 独立的资源列表（可拖入资源或文件夹，执行时再按需要过滤/展开）
        readonly List<Object> _spriteItems = new List<Object>();
        readonly List<Object> _sliceItems = new List<Object>();
        readonly List<Object> _combineItems = new List<Object>();
        readonly List<Object> _splitItems = new List<Object>();
        readonly List<Object> _convertItems = new List<Object>();
        readonly List<Object> _tableItems = new List<Object>();
        readonly List<Object> _prefabItems = new List<Object>();

        // 参数
        bool _sliceAutoRegister = true;
        int _combinePadding = 2;
        int _combineMaxSize = 8192;
        int _sheetRow = 1;
        string _atlasRoot = SpriteToolsCore.DefaultAtlasRoot;

        // 「转 Sprite」批量导入设置
        readonly SpriteToolsCore.ImportPreset _preset = new SpriteToolsCore.ImportPreset();
        static readonly string[] kMaxSizeLabels = { "256", "512", "1024", "2048", "4096", "8192" };
        static readonly int[] kMaxSizeValues = { 256, 512, 1024, 2048, 4096, 8192 };

        // 「注册图集表」列表 / 搜索 / 扫描
        string _tableScanRoot = SpriteToolsCore.DefaultAtlasRoot;
        string _tableSearch = "";
        Vector2 _tableScroll;
        Dictionary<string, string> _tableCache;

        // 子图速查页
        readonly List<Object> _lookupItems = new List<Object>();
        string _lookupSearch = "";
        string _copyTemplate = SpriteToolsCore.DefaultCopyTemplate;
        List<(string key, string name)> _lookupRows;
        Vector2 _lookupScroll;

        // 预制体换图预览状态
        string _scannedPrefabPath;
        int _prefabImageTotal;
        SpriteToolsCore.AtlasIndex _prefabIndex;
        List<SpriteToolsCore.PrefabEntry> _prefabEntries;

        // 状态栏
        string _status = "就绪";
        MessageType _statusKind = MessageType.None;

        Vector2 _scrollList;
        Vector2 _scrollPrefab;
        Vector2 _mainScroll;

        // 输出目录（合并 / 拆分的产物放这里；留空 = 合并弹框、拆分放源图旁；支持拖文件夹进配置区设置）
        string _outputDir = "";

        // 图集预览（点列表项设为预览目标）
        Object _previewObj;
        bool _showPreview = true;

        // 操作日志（逐条记录：跳过 / 修改 / 失败，类似 Localization 工具）
        struct LogItem { public string time; public SpriteToolsCore.LogKind kind; public string msg; }
        readonly List<LogItem> _logs = new List<LogItem>();
        Vector2 _logScroll;
        bool _showLog = true;

        // 样式（懒加载）
        bool _stylesReady;
        GUIStyle _titleStyle, _subStyle, _dropStyle, _dropHoverStyle, _tipStyle, _logStyle, _linkStyle, _previewHintStyle;

        // EditorPrefs 持久化
        const string kPrefAtlasTablePath = "SpriteTools_AtlasTablePath";
        const string kPrefAtlasEnumName = "SpriteTools_AtlasEnumName";
        const string kPrefSliceAutoRegister = "SpriteTools_SliceAutoRegister";

        void OnEnable()
        {
            string saved = EditorPrefs.GetString(kPrefAtlasTablePath, "");
            if (!string.IsNullOrEmpty(saved)) SpriteToolsCore.AtlasTablePath = saved;
            string savedEnum = EditorPrefs.GetString(kPrefAtlasEnumName, "");
            if (!string.IsNullOrEmpty(savedEnum)) SpriteToolsCore.AtlasEnumName = savedEnum;
            _sliceAutoRegister = EditorPrefs.GetBool(kPrefSliceAutoRegister, true);
        }

        [MenuItem("Tools/Sprite 工具箱", priority = 2000)]
        static void Open()
        {
            var w = GetWindow<SpriteToolsWindow>("Sprite 工具箱");
            w.minSize = new Vector2(560, 640);
            w.Show();
        }

        // ============================================================== OnGUI
        void OnGUI()
        {
            InitStyles();
            DrawHeader();

            GUILayout.Space(4);
            _tab = (Tab)GUILayout.Toolbar((int)_tab, kTabNames, GUILayout.Height(26));
            GUILayout.Space(2);
            EditorGUILayout.HelpBox(kTabDesc[(int)_tab], MessageType.Info);
            GUILayout.Space(4);

            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);
            switch (_tab)
            {
                case Tab.ToSprite: DrawToSpriteTab(); break;
                case Tab.Slice: DrawSliceTab(); break;
                case Tab.Combine: DrawCombineTab(); break;
                case Tab.Split: DrawSplitTab(); break;
                case Tab.Convert: DrawConvertTab(); break;
                case Tab.Table: DrawTableTab(); break;
                case Tab.Lookup: DrawLookupTab(); break;
                case Tab.Prefab: DrawPrefabTab(); break;
            }
            EditorGUILayout.EndScrollView();

            // 预览 / 日志 / 状态栏固定在底部，不随内容多少被挤压
            DrawPreviewPanel();
            DrawLogPanel();
            DrawStatusBar();
        }

        void InitStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };
            _subStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 1f, 1f, 0.7f) },
                alignment = TextAnchor.MiddleLeft
            };
            _dropStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };
            _dropHoverStyle = new GUIStyle(_dropStyle)
            {
                normal = { textColor = new Color(0.3f, 0.7f, 1f) }
            };
            _tipStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            _logStyle = new GUIStyle(EditorStyles.label) { fontSize = 11, richText = false };
            // 自建“链接”样式，替代 EditorStyles.linkLabel（后者在部分 2021.x 不可用）
            _linkStyle = new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.3f, 0.6f, 1f) } };
            _previewHintStyle = new GUIStyle(EditorStyles.miniLabel)
            { alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(0.55f, 0.55f, 0.55f) } };
        }

        void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.22f, 0.30f)
                : new Color(0.24f, 0.34f, 0.50f));

            var titleRect = new Rect(rect.x + 12, rect.y + 4, rect.width - 24, 22);
            var subRect = new Rect(rect.x + 12, rect.y + 24, rect.width - 24, 16);
            GUI.Label(titleRect, "◆  Sprite 工具箱", _titleStyle);
            GUI.Label(subRect, "TexturePacker 切图 · 合并 / 拆分 · 图集转换 · 图集表 · 预制体换图", _subStyle);
        }

        void DrawStatusBar()
        {
            var rect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(0.15f, 0.15f, 0.15f)
                : new Color(0.82f, 0.82f, 0.82f));

            Color c;
            switch (_statusKind)
            {
                case MessageType.Error: c = new Color(0.9f, 0.35f, 0.35f); break;
                case MessageType.Warning: c = new Color(0.95f, 0.75f, 0.2f); break;
                case MessageType.Info: c = new Color(0.35f, 0.75f, 0.4f); break;
                default: c = EditorGUIUtility.isProSkin ? Color.gray : Color.black; break;
            }
            var style = new GUIStyle(EditorStyles.label) { normal = { textColor = c } };
            GUI.Label(new Rect(rect.x + 8, rect.y + 2, rect.width - 16, 18), _status, style);
        }

        // ============================================================== 各 Tab
        void DrawToSpriteTab()
        {
            DropAndList(_spriteItems, "把图片（或父文件夹）拖到这里，会递归处理下面所有图片");

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("统一导入设置（勾选的项才强制统一，未勾选保持原样）", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("· 贴图类型固定设为 Sprite (2D and UI)", _tipStyle);
                _preset.setSpriteMode = PresetRow(_preset.setSpriteMode, "Sprite Mode",
                    () => _preset.spriteMode = (SpriteImportMode)EditorGUILayout.EnumPopup(_preset.spriteMode));
                _preset.setPPU = PresetRow(_preset.setPPU, "Pixels Per Unit",
                    () => _preset.ppu = EditorGUILayout.FloatField(_preset.ppu));
                _preset.setFilter = PresetRow(_preset.setFilter, "Filter Mode",
                    () => _preset.filter = (FilterMode)EditorGUILayout.EnumPopup(_preset.filter));
                _preset.setMaxSize = PresetRow(_preset.setMaxSize, "Max Size",
                    () => _preset.maxSize = EditorGUILayout.IntPopup(_preset.maxSize, kMaxSizeLabels, kMaxSizeValues));
                _preset.setMesh = PresetRow(_preset.setMesh, "Mesh Type",
                    () => _preset.meshType = (SpriteMeshType)EditorGUILayout.EnumPopup(_preset.meshType));
                _preset.setCompression = PresetRow(_preset.setCompression, "Compression",
                    () => _preset.compression = (TextureImporterCompression)EditorGUILayout.EnumPopup(_preset.compression));
                _preset.setReadWrite = PresetRow(_preset.setReadWrite, "Read/Write",
                    () => _preset.readWrite = EditorGUILayout.Toggle(_preset.readWrite));
            }

            if (RunButton("批量应用导入设置", new Color(0.30f, 0.62f, 0.95f)))
            {
                var imgs = SpriteToolsCore.Collect(_spriteItems, SpriteToolsCore.IsCombineImage);
                if (imgs.Count == 0) { SetStatus("没有可处理的图片（png/tga/jpg/jpeg/psd）", MessageType.Warning); return; }
                AddLog(SpriteToolsCore.LogKind.Ok, $"▶ 应用导入设置：{imgs.Count} 张");
                var r = SpriteToolsCore.ApplyImportPreset(imgs, _preset, AddLog);
                SetStatus(r.msg, MessageType.Info);
            }
        }

        // 一行“勾选 + 值控件”；未勾选时值控件禁用
        bool PresetRow(bool enabled, string label, System.Action drawValue)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                enabled = EditorGUILayout.ToggleLeft(label, enabled, GUILayout.Width(150));
                using (new EditorGUI.DisabledScope(!enabled))
                    drawValue();
            }
            return enabled;
        }

        void DrawSliceTab()
        {
            DropAndList(_sliceItems, "把 TexturePacker 的 .json 文件（或其所在文件夹）拖到这里");

            bool prevAuto = _sliceAutoRegister;
            _sliceAutoRegister = EditorGUILayout.ToggleLeft("切图完成后自动登记进图集表（AtlasName）", _sliceAutoRegister);
            if (_sliceAutoRegister != prevAuto) EditorPrefs.SetBool(kPrefSliceAutoRegister, _sliceAutoRegister);

            if (_sliceAutoRegister)
            {
                bool tableExists = File.Exists(SpriteToolsCore.AtlasTablePath);
                string curScriptName = Path.GetFileNameWithoutExtension(SpriteToolsCore.AtlasTablePath);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("脚本名称", GUILayout.Width(56));
                    string newName = EditorGUILayout.TextField(curScriptName);
                    EditorGUILayout.LabelField(".cs", GUILayout.Width(24));
                    if (newName != curScriptName)
                    {
                        string dir = Path.GetDirectoryName(SpriteToolsCore.AtlasTablePath);
                        SpriteToolsCore.AtlasTablePath = Path.Combine(string.IsNullOrEmpty(dir) ? "Assets" : dir, newName + ".cs").Replace('\\', '/');
                        EditorPrefs.SetString(kPrefAtlasTablePath, SpriteToolsCore.AtlasTablePath);
                    }
                }
                if (!IsValidScriptName(curScriptName))
                    EditorGUILayout.LabelField("⚠ 名称须为合法标识符（字母或下划线开头，仅含字母/数字/下划线）", _tipStyle);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("生成路径", GUILayout.Width(56));
                    string newPath = EditorGUILayout.TextField(SpriteToolsCore.AtlasTablePath);
                    if (newPath != SpriteToolsCore.AtlasTablePath) { SpriteToolsCore.AtlasTablePath = newPath; EditorPrefs.SetString(kPrefAtlasTablePath, newPath); }
                    if (GUILayout.Button("选择…", GUILayout.Width(52))) PicAtlasTablePath();
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("枚举名称", GUILayout.Width(56));
                    string newEnum = EditorGUILayout.TextField(SpriteToolsCore.AtlasEnumName);
                    if (newEnum != SpriteToolsCore.AtlasEnumName) { SpriteToolsCore.AtlasEnumName = newEnum; EditorPrefs.SetString(kPrefAtlasEnumName, newEnum); }
                }
                string enumHint = string.IsNullOrEmpty(SpriteToolsCore.AtlasEnumName) ? "AtlasName" : SpriteToolsCore.AtlasEnumName;
                EditorGUILayout.LabelField(
                    tableExists
                        ? $"✓ 已就绪（枚举 {enumHint}），切图后自动更新"
                        : $"尚未创建，切图时自动生成枚举 {enumHint} 到上方路径",
                    _tipStyle);
                EditorGUILayout.LabelField("首次使用会自动生成脚本；配置完成后请勿随意修改，避免已有代码引用失效。", _tipStyle);
            }

            if (RunButton("开始切图", new Color(0.30f, 0.62f, 0.95f)))
            {
                var jsons = SpriteToolsCore.Collect(_sliceItems, p => p.ToLower().EndsWith(".json"));
                if (jsons.Count == 0) { SetStatus("没有可切的 .json（请拖入 TexturePacker 导出的 JSON）", MessageType.Warning); return; }

                int okCount = 0, spr = 0;
                var errs = new List<string>();
                foreach (var j in jsons)
                {
                    var r = SpriteToolsCore.SliceJson(j, _sliceAutoRegister);
                    if (r.ok) { okCount++; spr += r.count; } else errs.Add(r.msg);
                }
                AssetDatabase.Refresh();
                if (jsons.Count > 0) _previewObj = AssetDatabase.LoadAssetAtPath<Object>(jsons[0]); // 切完自动预览第一张，看切片框
                if (errs.Count == 0) SetStatus($"切图完成：{okCount} 张图集，共 {spr} 个精灵", MessageType.Info);
                else SetStatus($"完成 {okCount} 个，{errs.Count} 个失败（详见 Console）", MessageType.Warning);
            }
        }

        void DrawCombineTab()
        {
            DrawOutputDirRow();
            DropAndList(_combineItems, "把要合并的多张图片（或图片文件夹）拖到这里");
            using (new EditorGUILayout.HorizontalScope())
            {
                _combinePadding = EditorGUILayout.IntField("切片间距(px)", _combinePadding);
                _combineMaxSize = EditorGUILayout.IntField("图集最大边长", _combineMaxSize);
            }

            if (RunButton("合并成 Sprite(Multiple)", new Color(0.30f, 0.62f, 0.95f)))
            {
                var imgs = SpriteToolsCore.Collect(_combineItems, SpriteToolsCore.IsCombineImage);
                if (imgs.Count < 2) { SetStatus("请至少准备 2 张图片再合并", MessageType.Warning); return; }

                string firstDir = Path.GetDirectoryName(imgs[0]);
                string defaultName = SpriteToolsCore.SanitizeKey(new DirectoryInfo(firstDir).Name) + "_atlas";

                string outputFile;
                if (!string.IsNullOrEmpty(_outputDir))
                    outputFile = _outputDir.TrimEnd('/') + "/" + defaultName + ".png";
                else
                {
                    outputFile = EditorUtility.SaveFilePanelInProject(
                        "保存合并图集", defaultName, "png", "选择合并后的图集保存位置", firstDir);
                    if (string.IsNullOrEmpty(outputFile)) { SetStatus("已取消", MessageType.None); return; }
                }

                AddLog(SpriteToolsCore.LogKind.Ok, $"▶ 合并 {imgs.Count} 张 → {outputFile}");
                var r = SpriteToolsCore.CombineImages(imgs, outputFile, _combinePadding, _combineMaxSize, AddLog);
                SetStatus(r.msg, r.ok ? MessageType.Info : MessageType.Warning);
            }
        }

        void DrawSplitTab()
        {
            DrawOutputDirRow();
            DropAndList(_splitItems, "把要拆分的 Sprite(Multiple) 图集拖到这里");
            if (RunButton("拆分成单图", new Color(0.30f, 0.62f, 0.95f)))
            {
                var texs = SpriteToolsCore.Collect(_splitItems, SpriteToolsCore.IsCombineImage);
                if (texs.Count == 0) { SetStatus("没有可拆分的图集", MessageType.Warning); return; }

                int total = 0, okCount = 0;
                string outRoot = string.IsNullOrEmpty(_outputDir) ? null : _outputDir;
                AddLog(SpriteToolsCore.LogKind.Ok, $"▶ 拆分 {texs.Count} 个图集" + (outRoot != null ? $" → {outRoot}" : ""));
                foreach (var t in texs)
                {
                    var r = SpriteToolsCore.SplitAtlas(t, outRoot, AddLog);
                    if (r.ok) { okCount++; total += r.count; }
                }
                AssetDatabase.Refresh();
                SetStatus(okCount > 0 ? $"拆分完成：{okCount} 个图集，共导出 {total} 张单图" : "没有可拆分的 Sprite(Multiple)（图集需为 Multiple 模式）", okCount > 0 ? MessageType.Info : MessageType.Warning);
            }
        }

        void DrawConvertTab()
        {
            DropAndList(_convertItems, "把 SpriteAtlas 资源拖到这里");
            _sheetRow = Mathf.Max(1, EditorGUILayout.IntField("TextureSheet 行数", _sheetRow));

            var atlases = _convertItems.OfType<SpriteAtlas>().ToList();
            using (new EditorGUI.DisabledScope(atlases.Count == 0))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
#if SPRITETOOLS_TMP
                    if (GUILayout.Button("→ TMP_SpriteAsset", GUILayout.Height(30)))
                        RunConvert(atlases, a => SpriteToolsCore.AtlasToTmp(a), "TMP_SpriteAsset");
#endif
                    if (GUILayout.Button("→ Sprite(Multiple)", GUILayout.Height(30)))
                        RunConvert(atlases, a => SpriteToolsCore.AtlasToSpriteSheet(a), "Sprite(Multiple)");
                    if (GUILayout.Button("→ TextureSheet", GUILayout.Height(30)))
                        RunConvert(atlases, a => SpriteToolsCore.AtlasToTextureSheet(a, _sheetRow), "TextureSheet");
                }
            }
            if (atlases.Count == 0)
                EditorGUILayout.LabelField("（拖入 SpriteAtlas 后上方按钮可用）", _tipStyle);
        }

        void RunConvert(List<SpriteAtlas> atlases, System.Func<SpriteAtlas, SpriteToolsCore.Result> op, string what)
        {
            int okCount = 0;
            foreach (var a in atlases) if (op(a).ok) okCount++;
            AssetDatabase.Refresh();
            SetStatus($"{what} 转换完成：{okCount}/{atlases.Count}", okCount > 0 ? MessageType.Info : MessageType.Warning);
        }

        void DrawTableTab()
        {
            DropAndList(_tableItems, "把图集 PNG（或它的 .json）拖到这里");
            string curScriptName = Path.GetFileNameWithoutExtension(SpriteToolsCore.AtlasTablePath);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("脚本名称", GUILayout.Width(56));
                string newName = EditorGUILayout.TextField(curScriptName);
                EditorGUILayout.LabelField(".cs", GUILayout.Width(24));
                if (newName != curScriptName)
                {
                    string dir = Path.GetDirectoryName(SpriteToolsCore.AtlasTablePath);
                    SpriteToolsCore.AtlasTablePath = Path.Combine(string.IsNullOrEmpty(dir) ? "Assets" : dir, newName + ".cs").Replace('\\', '/');
                    EditorPrefs.SetString(kPrefAtlasTablePath, SpriteToolsCore.AtlasTablePath);
                }
            }
            if (!IsValidScriptName(curScriptName))
                EditorGUILayout.LabelField("⚠ 名称须为合法标识符（字母或下划线开头，仅含字母/数字/下划线）", _tipStyle);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("生成路径", GUILayout.Width(56));
                string newPath = EditorGUILayout.TextField(SpriteToolsCore.AtlasTablePath);
                if (newPath != SpriteToolsCore.AtlasTablePath) { SpriteToolsCore.AtlasTablePath = newPath; EditorPrefs.SetString(kPrefAtlasTablePath, newPath); }
                if (GUILayout.Button("选择…", GUILayout.Width(52))) PicAtlasTablePath();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("枚举名称", GUILayout.Width(56));
                string newEnum = EditorGUILayout.TextField(SpriteToolsCore.AtlasEnumName);
                if (newEnum != SpriteToolsCore.AtlasEnumName) { SpriteToolsCore.AtlasEnumName = newEnum; EditorPrefs.SetString(kPrefAtlasEnumName, newEnum); }
            }
            string curEnum = string.IsNullOrEmpty(SpriteToolsCore.AtlasEnumName) ? "AtlasName" : SpriteToolsCore.AtlasEnumName;
            EditorGUILayout.LabelField($"图集表枚举（{curEnum}）生成到此 .cs；留空默认 AtlasName。", _tipStyle);

            if (RunButton("注册选中的图集", new Color(0.30f, 0.62f, 0.95f)))
            {
                var pngs = new List<string>();
                foreach (var o in _tableItems)
                {
                    string png = SpriteToolsCore.ResolveAtlasPng(AssetDatabase.GetAssetPath(o));
                    if (!string.IsNullOrEmpty(png) && !pngs.Contains(png)) pngs.Add(png);
                }
                if (pngs.Count == 0) SetStatus("没有可注册的图集（选图集 PNG 或它的 .json）", MessageType.Warning);
                else
                {
                    var r = SpriteToolsCore.RegisterAtlases(pngs);
                    AddLog(r.ok ? SpriteToolsCore.LogKind.Ok : SpriteToolsCore.LogKind.Skip, r.msg);
                    _tableCache = SpriteToolsCore.GetRegistered();
                    SetStatus(r.msg, r.ok ? MessageType.Info : MessageType.Warning);
                }
            }

            // 一键扫描整个目录注册
            using (new EditorGUILayout.HorizontalScope())
            {
                _tableScanRoot = EditorGUILayout.TextField("扫描目录", _tableScanRoot);
                if (GUILayout.Button("扫描并全部注册", GUILayout.Width(120)))
                {
                    var r = SpriteToolsCore.ScanAndRegisterFolder(_tableScanRoot);
                    AddLog(r.ok ? SpriteToolsCore.LogKind.Ok : SpriteToolsCore.LogKind.Warn, r.msg);
                    _tableCache = SpriteToolsCore.GetRegistered();
                    SetStatus(r.msg, r.ok ? MessageType.Info : MessageType.Warning);
                }
            }

            // 已注册列表 + 搜索 + 清理
            EditorGUILayout.Space(2);
            if (_tableCache == null) _tableCache = SpriteToolsCore.GetRegistered();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"已注册（{_tableCache.Count}）", EditorStyles.boldLabel, GUILayout.Width(96));
                _tableSearch = EditorGUILayout.TextField(_tableSearch);
                if (GUILayout.Button("刷新", GUILayout.Width(46))) _tableCache = SpriteToolsCore.GetRegistered();
                if (GUILayout.Button("清理失效", GUILayout.Width(68)))
                {
                    var r = SpriteToolsCore.CleanInvalid();
                    AddLog(SpriteToolsCore.LogKind.Skip, r.msg);
                    _tableCache = SpriteToolsCore.GetRegistered();
                    SetStatus(r.msg, MessageType.Info);
                }
            }

            _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll, "box", GUILayout.Height(150));
            string removeKey = null;
            foreach (var kv in _tableCache)
            {
                if (!string.IsNullOrEmpty(_tableSearch) && kv.Key.IndexOf(_tableSearch, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(File.Exists(kv.Value) ? " " : "⚠", GUILayout.Width(14));
                    if (GUILayout.Button(kv.Key, _linkStyle, GUILayout.Width(150)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<Object>(kv.Value);
                        if (asset != null) { EditorGUIUtility.PingObject(asset); Selection.activeObject = asset; }
                    }
                    EditorGUILayout.LabelField(kv.Value, _tipStyle);
                    if (GUILayout.Button("×", GUILayout.Width(22))) removeKey = kv.Key;
                }
            }
            EditorGUILayout.EndScrollView();
            if (removeKey != null)
            {
                var r = SpriteToolsCore.RemoveAtlasKeys(new List<string> { removeKey });
                AddLog(SpriteToolsCore.LogKind.Skip, r.msg);
                _tableCache = SpriteToolsCore.GetRegistered();
            }
        }

        void DrawLookupTab()
        {
            DropAndList(_lookupItems, "把 Sprite(Multiple) 图集 或 SpriteAtlas 拖到这里");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("读取子图名", GUILayout.Height(24)))
                {
                    _lookupRows = new List<(string, string)>();
                    foreach (var o in _lookupItems)
                    {
                        string path = AssetDatabase.GetAssetPath(o);
                        string key = SpriteToolsCore.SanitizeKey(Path.GetFileNameWithoutExtension(path));
                        foreach (var n in SpriteToolsCore.GetSpriteNames(path)) _lookupRows.Add((key, n));
                    }
                    SetStatus($"读取到 {_lookupRows.Count} 个子图", MessageType.Info);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("搜索", GUILayout.Width(32));
                _lookupSearch = EditorGUILayout.TextField(_lookupSearch, GUILayout.Width(160));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("复制模板", GUILayout.Width(56));
                _copyTemplate = EditorGUILayout.TextField(_copyTemplate);
            }
            EditorGUILayout.LabelField("占位符：{atlas}=图集名(AtlasName)，{sprite}=子图名。换项目改成你自己的取图 API。", _tipStyle);

            if (_lookupRows == null) { EditorGUILayout.LabelField("（拖入图集后点「读取子图名」，会列出所有子图）", _tipStyle); return; }

            _lookupScroll = EditorGUILayout.BeginScrollView(_lookupScroll, "box", GUILayout.Height(240));
            foreach (var row in _lookupRows)
            {
                if (!string.IsNullOrEmpty(_lookupSearch) && row.name.IndexOf(_lookupSearch, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(row.name, GUILayout.Width(180));
                    EditorGUILayout.LabelField($"AtlasName.{row.key}", _tipStyle);
                    if (GUILayout.Button("复制代码", GUILayout.Width(72)))
                    {
                        EditorGUIUtility.systemCopyBuffer = _copyTemplate.Replace("{atlas}", row.key).Replace("{sprite}", row.name);
                        SetStatus($"已复制：{row.key} / {row.name}", MessageType.Info);
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawPrefabTab()
        {
            _atlasRoot = EditorGUILayout.TextField("图集根目录", _atlasRoot);
            DropAndList(_prefabItems, "把预制体（可多个）拖到这里");

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_prefabItems.Count == 0))
                {
                    if (GUILayout.Button("扫描预览（第 1 个）", GUILayout.Height(26))) ScanFirstPrefab();
                    if (GUILayout.Button("批量自动替换全部", GUILayout.Height(26))) BatchReplacePrefabs();
                }
            }

            if (_prefabEntries != null)
            {
                int willReplace = _prefabEntries.Count(e => e.choice >= 0);
                EditorGUILayout.LabelField(
                    $"预览：{Path.GetFileName(_scannedPrefabPath)}　Image 总数 {_prefabImageTotal}，可替换 {_prefabEntries.Count}，将替换 {willReplace}",
                    EditorStyles.boldLabel);

                _scrollPrefab = EditorGUILayout.BeginScrollView(_scrollPrefab, GUILayout.MaxHeight(220));
                foreach (var e in _prefabEntries)
                {
                    using (new EditorGUILayout.VerticalScope("box"))
                    {
                        EditorGUILayout.LabelField(e.spriteName, EditorStyles.boldLabel);
                        EditorGUILayout.LabelField(e.hierarchyPath, EditorStyles.miniLabel);
                        var options = e.candidates.Select(Path.GetFileNameWithoutExtension)
                            .Concat(new[] { "（跳过）" }).ToArray();
                        int cur = e.choice < 0 ? options.Length - 1 : e.choice;
                        int next = EditorGUILayout.Popup("替换到图集", cur, options);
                        e.choice = (next == options.Length - 1) ? -1 : next;
                        if (!string.IsNullOrEmpty(e.note))
                            EditorGUILayout.LabelField(e.note, EditorStyles.miniLabel);
                    }
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUI.DisabledScope(willReplace == 0))
                    if (RunButton($"替换并保存（{willReplace}）", new Color(0.35f, 0.72f, 0.4f)))
                        ApplyPrefabPreview();
            }
        }

        void ScanFirstPrefab()
        {
            var prefab = _prefabItems.FirstOrDefault(o => AssetDatabase.GetAssetPath(o).EndsWith(".prefab"));
            if (prefab == null) { SetStatus("列表里没有预制体", MessageType.Warning); return; }

            _prefabIndex = SpriteToolsCore.AtlasIndex.Build(_atlasRoot);
            if (_prefabIndex.AtlasPaths.Count == 0)
            {
                SetStatus($"在 {_atlasRoot} 下没扫到任何 Sprite(Multiple) 图集", MessageType.Warning);
                _prefabEntries = null; return;
            }
            _scannedPrefabPath = AssetDatabase.GetAssetPath(prefab);
            _prefabEntries = SpriteToolsCore.PrefabScan(_scannedPrefabPath, _prefabIndex, out _prefabImageTotal);
            SetStatus($"扫描完成：{_prefabEntries.Count} 个 Image 可替换", MessageType.Info);
        }

        void ApplyPrefabPreview()
        {
            int n = SpriteToolsCore.PrefabApply(_scannedPrefabPath, _prefabEntries);
            AssetDatabase.SaveAssets();
            ScanFirstPrefab(); // 刷新（已替换的会消失）
            SetStatus($"已替换 {n} 个 Image 引用为图集子图", MessageType.Info);
        }

        void BatchReplacePrefabs()
        {
            var prefabs = _prefabItems.Select(o => AssetDatabase.GetAssetPath(o))
                .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab")).Distinct().ToList();
            if (prefabs.Count == 0) { SetStatus("列表里没有预制体", MessageType.Warning); return; }

            var index = SpriteToolsCore.AtlasIndex.Build(_atlasRoot);
            if (index.AtlasPaths.Count == 0) { SetStatus($"在 {_atlasRoot} 下没扫到任何图集", MessageType.Warning); return; }

            int total = 0;
            try
            {
                for (int i = 0; i < prefabs.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("替换为图集", prefabs[i], (i + 1f) / prefabs.Count);
                    total += SpriteToolsCore.PrefabReplaceAuto(prefabs[i], index);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            AssetDatabase.SaveAssets();
            SetStatus($"批量完成：{prefabs.Count} 个预制体，共替换 {total} 个 Image 引用", MessageType.Info);
        }

        // ============================================================== 通用控件
        /// <summary>拖拽区 + 操作行 + 资源列表 三件套。</summary>
        void DropAndList(List<Object> list, string hint)
        {
            // 1. 拖拽区
            var rect = GUILayoutUtility.GetRect(0, 52, GUILayout.ExpandWidth(true));
            var evt = Event.current;
            bool hover = rect.Contains(evt.mousePosition);
            GUI.Box(rect, hint, hover && DragAndDrop.objectReferences.Length > 0 ? _dropHoverStyle : _dropStyle);

            if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && hover)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var o in DragAndDrop.objectReferences)
                        if (o != null && !list.Contains(o)) list.Add(o);
                    evt.Use();
                }
            }

            // 2. 操作行
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 加入当前选中", GUILayout.Height(20)))
                    foreach (var o in Selection.objects) if (o != null && !list.Contains(o)) list.Add(o);
                using (new EditorGUI.DisabledScope(list.Count == 0))
                    if (GUILayout.Button($"清空（{list.Count}）", GUILayout.Height(20))) list.Clear();
            }

            // 3. 列表
            if (list.Count == 0)
            {
                EditorGUILayout.LabelField("（列表为空，拖入资源或点「加入当前选中」）", _tipStyle);
                return;
            }
            _scrollList = EditorGUILayout.BeginScrollView(_scrollList, GUILayout.MaxHeight(140));
            int removeAt = -1;
            for (int i = 0; i < list.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var content = EditorGUIUtility.ObjectContent(list[i], list[i].GetType());
                    GUILayout.Label(content.image, GUILayout.Width(18), GUILayout.Height(18));
                    if (GUILayout.Button(list[i].name, EditorStyles.label, GUILayout.ExpandWidth(true))) _previewObj = list[i];
                    if (GUILayout.Button("×", GUILayout.Width(22))) removeAt = i;
                }
            }
            if (removeAt >= 0) list.RemoveAt(removeAt);
            EditorGUILayout.EndScrollView();
        }

        bool RunButton(string label, Color color)
        {
            GUILayout.Space(2);
            var old = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool clicked = GUILayout.Button(label, GUILayout.Height(32));
            GUI.backgroundColor = old;
            return clicked;
        }

        static bool IsValidScriptName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!char.IsLetter(name[0]) && name[0] != '_') return false;
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        void SetStatus(string msg, MessageType kind)
        {
            _status = msg;
            _statusKind = kind;
            Repaint();
        }

        // ============================================================== 输出目录栏（仅合并 / 拆分页内使用）
        void DrawOutputDirRow()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("输出目录", GUILayout.Width(56));
                    _outputDir = EditorGUILayout.TextField(_outputDir);
                    if (GUILayout.Button("选择…", GUILayout.Width(52))) PickOutputDir();
                    using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_outputDir)))
                        if (GUILayout.Button("×", GUILayout.Width(22))) _outputDir = "";
                }
                EditorGUILayout.LabelField(
                    string.IsNullOrEmpty(_outputDir)
                        ? "合并 / 拆分 的产物位置。留空＝合并时弹框选、拆分放到源图旁。可把文件夹拖到本栏设置。"
                        : "合并 / 拆分 将输出到此目录。",
                    _tipStyle);
            }
            HandleFolderDrop(GUILayoutUtility.GetLastRect());
        }

        void PickOutputDir()
        {
            string start = string.IsNullOrEmpty(_outputDir) ? Application.dataPath : _outputDir;
            string abs = EditorUtility.OpenFolderPanel("选择输出目录（须在工程 Assets 内）", start, "");
            if (string.IsNullOrEmpty(abs)) return;
            abs = abs.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');
            if (abs == dataPath) _outputDir = "Assets";
            else if (abs.StartsWith(dataPath + "/")) _outputDir = "Assets" + abs.Substring(dataPath.Length);
            else SetStatus("输出目录必须在工程 Assets 内", MessageType.Warning);
        }

        void PicAtlasTablePath()
        {
            string dir = Path.GetDirectoryName(SpriteToolsCore.AtlasTablePath);
            string curName = Path.GetFileNameWithoutExtension(SpriteToolsCore.AtlasTablePath);
            string file = EditorUtility.SaveFilePanelInProject(
                "选择图集表生成位置", string.IsNullOrEmpty(curName) ? "AtlasTable" : curName, "cs",
                "图集枚举表将生成到此 .cs 文件", string.IsNullOrEmpty(dir) ? "Assets" : dir);
            if (!string.IsNullOrEmpty(file)) { SpriteToolsCore.AtlasTablePath = file; EditorPrefs.SetString(kPrefAtlasTablePath, file); }
        }

        void HandleFolderDrop(Rect rect)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            string folder = null;
            foreach (var o in DragAndDrop.objectReferences)
            {
                string p = AssetDatabase.GetAssetPath(o);
                if (!string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p)) { folder = p; break; }
            }
            DragAndDrop.visualMode = folder != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (evt.type == EventType.DragPerform && folder != null)
            {
                DragAndDrop.AcceptDrag();
                _outputDir = folder;
                evt.Use();
            }
        }

        // ============================================================== 图集预览面板
        void DrawPreviewPanel()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("预览", EditorStyles.boldLabel, GUILayout.Width(40));
                if (_previewObj != null) EditorGUILayout.LabelField(_previewObj.name, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                _showPreview = GUILayout.Toggle(_showPreview, _showPreview ? "折叠" : "展开", EditorStyles.miniButton, GUILayout.Width(46));
            }
            if (!_showPreview) return;

            // 固定高度的独立预览区：无论有没有选中都在，跟操作日志一样是个明确的盒子
            var rect = GUILayoutUtility.GetRect(0, 160, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
            DrawOutline(rect, new Color(1f, 1f, 1f, 0.12f)); // 淡边框界定区域

            string info = null;
            if (_previewObj == null)
            {
                GUI.Label(rect, "点上面列表里某一项的名字预览它的图\n切完 JSON 会在这里叠加绿色切片框", _previewHintStyle);
            }
            else
            {
                string texPath = ResolvePreviewTexPath(AssetDatabase.GetAssetPath(_previewObj));
                var tex = string.IsNullOrEmpty(texPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex != null)
                {
                    var drawRect = FitRect(Inset(rect, 4), tex.width, tex.height);
                    GUI.DrawTexture(drawRect, tex, ScaleMode.StretchToFill, true);
                    int slices = DrawSliceOutlines(drawRect, texPath, tex);
                    info = $"{Path.GetFileName(texPath)}　{tex.width}×{tex.height}" + (slices > 0 ? $"　{slices} 切片" : "　(未切片)");
                }
                else
                {
                    var thumb = AssetPreview.GetAssetPreview(_previewObj);
                    if (thumb != null) { GUI.DrawTexture(FitRect(Inset(rect, 4), thumb.width, thumb.height), thumb, ScaleMode.StretchToFill, true); info = _previewObj.name; }
                    else { GUI.Label(rect, "生成预览中…", _previewHintStyle); Repaint(); }
                }
            }

            if (!string.IsNullOrEmpty(info)) EditorGUILayout.LabelField(info, _tipStyle);
        }

        string ResolvePreviewTexPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Path.GetExtension(path).ToLower() == ".json") return SpriteToolsCore.ResolveAtlasPng(path);
            return SpriteToolsCore.IsCombineImage(path) ? path : null;
        }

        static Rect FitRect(Rect box, float texW, float texH)
        {
            if (texW <= 0 || texH <= 0) return box;
            float ta = texW / texH, ca = box.width / box.height;
            if (ta > ca) { float h = box.width / ta; return new Rect(box.x, box.y + (box.height - h) / 2, box.width, h); }
            float w = box.height * ta; return new Rect(box.x + (box.width - w) / 2, box.y, w, box.height);
        }

        static int DrawSliceOutlines(Rect drawRect, string texPath, Texture2D tex)
        {
            int n = 0;
            var col = new Color(0.3f, 0.9f, 0.4f, 0.9f);
            foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(texPath))
            {
                if (!(rep is Sprite sp)) continue;
                var sr = sp.rect; // 贴图像素坐标，原点左下
                float sx = drawRect.x + sr.x / tex.width * drawRect.width;
                float sy = drawRect.y + (1f - (sr.y + sr.height) / tex.height) * drawRect.height;
                float sw = sr.width / tex.width * drawRect.width;
                float sh = sr.height / tex.height * drawRect.height;
                DrawOutline(new Rect(sx, sy, sw, sh), col);
                n++;
            }
            return n;
        }

        static void DrawOutline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
        }

        static Rect Inset(Rect r, float m) => new Rect(r.x + m, r.y + m, r.width - 2 * m, r.height - 2 * m);

        // ============================================================== 操作日志面板
        void DrawLogPanel()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"操作日志（{_logs.Count}）", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                _showLog = GUILayout.Toggle(_showLog, _showLog ? "折叠" : "展开", EditorStyles.miniButton, GUILayout.Width(46));
                using (new EditorGUI.DisabledScope(_logs.Count == 0))
                    if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(46))) _logs.Clear();
            }
            if (!_showLog) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.Height(120));
                if (_logs.Count == 0)
                    EditorGUILayout.LabelField("（暂无日志，执行操作后这里会逐条记录：跳过 / 修改 / 失败）", _tipStyle);
                else
                    foreach (var it in _logs)
                    {
                        _logStyle.normal.textColor = LogColor(it.kind);
                        EditorGUILayout.LabelField($"[{it.time}] {LogIcon(it.kind)} {it.msg}", _logStyle);
                    }
                EditorGUILayout.EndScrollView();
            }
        }

        void AddLog(SpriteToolsCore.LogKind kind, string msg)
        {
            _logs.Add(new LogItem { time = System.DateTime.Now.ToString("HH:mm:ss"), kind = kind, msg = msg });
            const int cap = 800;
            if (_logs.Count > cap) _logs.RemoveRange(0, _logs.Count - cap);
            _logScroll.y = float.MaxValue; // 滚到底
            Repaint();
        }

        static Color LogColor(SpriteToolsCore.LogKind k)
        {
            switch (k)
            {
                case SpriteToolsCore.LogKind.Ok: return new Color(0.35f, 0.75f, 0.4f);
                case SpriteToolsCore.LogKind.Skip: return new Color(0.6f, 0.6f, 0.6f);
                case SpriteToolsCore.LogKind.Warn: return new Color(0.92f, 0.72f, 0.2f);
                default: return new Color(0.9f, 0.4f, 0.4f);
            }
        }

        static string LogIcon(SpriteToolsCore.LogKind k)
        {
            switch (k)
            {
                case SpriteToolsCore.LogKind.Ok: return "✓";
                case SpriteToolsCore.LogKind.Skip: return "•";
                case SpriteToolsCore.LogKind.Warn: return "!";
                default: return "✗";
            }
        }
    }

    // ========================================================================
    //  核心逻辑（独立移植版，与项目原有 RightClickMenuExtension 无引用关系）
    // ========================================================================
    static class SpriteToolsCore
    {
        public struct Result
        {
            public bool ok;
            public int count;
            public string msg;
            public static Result Ok(int c, string m) => new Result { ok = true, count = c, msg = m };
            public static Result Fail(string m) => new Result { ok = false, msg = m };
        }

        /// <summary>操作日志级别。</summary>
        public enum LogKind { Ok, Skip, Warn, Err }
        /// <summary>逐条日志回调：核心逻辑把每张图的处理结果回传给 UI 日志面板。</summary>
        public delegate void Logger(LogKind kind, string msg);

        // ================= 可配置默认值（换项目时改这三个即可）=================
        public const string DefaultAtlasRoot = "Assets/Projects/Art/Atlas";
        public const string DefaultAtlasTablePath = "Assets/Projects/Scripts/GameTools/AtlasTable.cs";
        // 子图速查「复制代码」模板：{atlas}=图集名(AtlasName)，{sprite}=子图名
        public const string DefaultCopyTemplate = "GameTools.SetAtlasSprite(image, AtlasName.{atlas}, \"{sprite}\");";

        // 图集表生成文件；可在「注册图集表」页手动改位置
        public static string AtlasTablePath = DefaultAtlasTablePath;
        public static string AtlasEnumName = "AtlasName";

        static readonly string[] kImageExts = { ".png", ".tga", ".jpg", ".jpeg", ".psd" };
        static readonly string[] kAtlasImageExts = { ".png", ".tga", ".jpg", ".jpeg", ".psd", ".exr" };

        // -------------------------------------------------------------- 收集/过滤
        /// <summary>把选中的资源列表（可含文件夹）展开成满足条件的资源路径，去重排序。</summary>
        public static List<string> Collect(List<Object> objs, System.Func<string, bool> accept)
        {
            var set = new HashSet<string>();
            foreach (var o in objs)
            {
                string path = AssetDatabase.GetAssetPath(o);
                if (string.IsNullOrEmpty(path)) continue;
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (var f in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
                    {
                        string p = f.Replace('\\', '/');
                        if (accept(p)) set.Add(p);
                    }
                }
                else if (accept(path)) set.Add(path);
            }
            var list = new List<string>(set);
            list.Sort(System.StringComparer.Ordinal);
            return list;
        }

        public static bool IsCombineImage(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path).ToLower();
            foreach (var e in kImageExts) if (e == ext) return true;
            return false;
        }

        // ============================================ 功能：批量导入设置（转 Sprite + 统一项）
        /// <summary>「转 Sprite」页的批量导入设置：贴图类型固定 Sprite；其余勾选项才统一。</summary>
        public class ImportPreset
        {
            public bool setSpriteMode = true; public SpriteImportMode spriteMode = SpriteImportMode.Single;
            public bool setPPU; public float ppu = 100;
            public bool setFilter; public FilterMode filter = FilterMode.Bilinear;
            public bool setMaxSize; public int maxSize = 2048;
            public bool setMesh; public SpriteMeshType meshType = SpriteMeshType.Tight;
            public bool setCompression; public TextureImporterCompression compression = TextureImporterCompression.Compressed;
            public bool setReadWrite; public bool readWrite = false;
        }

        /// <summary>按预设批量应用导入设置；贴图类型固定 Sprite，其余按勾选统一；已完全符合的跳过，逐条回传日志。</summary>
        public static Result ApplyImportPreset(List<string> paths, ImportPreset p, Logger log = null)
        {
            int changed = 0, skipped = 0, failed = 0;
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string shortName = Path.GetFileName(paths[i]);
                    EditorUtility.DisplayProgressBar($"应用导入设置 ({i + 1}/{paths.Count})", paths[i], (i + 1f) / paths.Count);
                    var ti = AssetImporter.GetAtPath(paths[i]) as TextureImporter;
                    if (ti == null) { failed++; log?.Invoke(LogKind.Err, $"读取失败：{shortName}"); continue; }

                    var tis = new TextureImporterSettings();
                    ti.ReadTextureSettings(tis);

                    bool need = ti.textureType != TextureImporterType.Sprite
                        || (p.setSpriteMode && ti.spriteImportMode != p.spriteMode)
                        || (p.setPPU && !Mathf.Approximately(ti.spritePixelsPerUnit, p.ppu))
                        || (p.setFilter && ti.filterMode != p.filter)
                        || (p.setMaxSize && ti.maxTextureSize != p.maxSize)
                        || (p.setMesh && tis.spriteMeshType != p.meshType)
                        || (p.setCompression && ti.textureCompression != p.compression)
                        || (p.setReadWrite && ti.isReadable != p.readWrite);

                    if (!need) { skipped++; log?.Invoke(LogKind.Skip, $"跳过：{shortName}（已符合）"); continue; }

                    ti.textureType = TextureImporterType.Sprite;
                    if (p.setSpriteMode) ti.spriteImportMode = p.spriteMode;
                    if (p.setPPU) ti.spritePixelsPerUnit = p.ppu;
                    if (p.setFilter) ti.filterMode = p.filter;
                    if (p.setMaxSize) ti.maxTextureSize = p.maxSize;
                    if (p.setCompression) ti.textureCompression = p.compression;
                    if (p.setReadWrite) ti.isReadable = p.readWrite;
                    if (p.setMesh) { ti.ReadTextureSettings(tis); tis.spriteMeshType = p.meshType; ti.SetTextureSettings(tis); }
                    ti.SaveAndReimport();
                    changed++;
                    log?.Invoke(LogKind.Ok, $"已改：{shortName}");
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
            AssetDatabase.Refresh();
            string msg = $"完成：改 {changed} 张，跳过 {skipped} 张" + (failed > 0 ? $"，{failed} 张失败" : "");
            Debug.Log("✅ 批量导入设置 " + msg);
            log?.Invoke(failed > 0 ? LogKind.Warn : LogKind.Ok, "— " + msg);
            return Result.Ok(changed, msg);
        }

        // ==================================================== 功能 1：JSON 切图
        [System.Serializable] class TPRect { public float x, y, w, h; }
        [System.Serializable] class TPSize { public float w, h; }
        [System.Serializable] class TPBorder { public float l, b, r, t; }
        [System.Serializable] class TPFrame
        {
            public string filename;
            public TPRect frame;
            public bool rotated;
            public bool trimmed;
            public TPRect spriteSourceSize;
            public TPSize sourceSize;
            public TPBorder border;
        }
        [System.Serializable] class TPMeta { public string image; public TPSize size; }
        [System.Serializable] class TPData { public TPFrame[] frames; public TPMeta meta; }

        public static Result SliceJson(string jsonPath, bool autoRegister)
        {
            TPData data;
            try { data = JsonUtility.FromJson<TPData>(File.ReadAllText(jsonPath)); }
            catch (System.Exception e) { Debug.LogError($"❌ 解析 JSON 失败: {jsonPath}\n{e.Message}"); return Result.Fail($"解析失败: {Path.GetFileName(jsonPath)}"); }

            if (data == null || data.frames == null || data.frames.Length == 0)
            { Debug.LogError($"❌ {jsonPath} 里没有 frames（请确认导出的是「JSON (Array)」而非 JSON Hash）。"); return Result.Fail("无 frames 数据（需 JSON Array 格式）"); }
            if (data.meta == null || data.meta.size == null || data.meta.size.h <= 0)
            { Debug.LogError($"❌ {jsonPath} 缺少 meta.size。"); return Result.Fail("缺少 meta.size"); }

            string jsonDir = Path.GetDirectoryName(jsonPath);
            string imageName = string.IsNullOrEmpty(data.meta.image)
                ? Path.GetFileNameWithoutExtension(jsonPath) + ".png" : data.meta.image;
            string texPath = Path.Combine(jsonDir, imageName).Replace('\\', '/');
            if (!File.Exists(texPath))
            { Debug.LogError($"❌ 找不到对应贴图: {texPath}"); return Result.Fail($"找不到贴图: {imageName}"); }

            var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (importer == null) { Debug.LogError($"❌ {texPath} 不是贴图。"); return Result.Fail("目标不是可识别贴图"); }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.SaveAndReimport();

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
            dp.InitSpriteEditorDataProvider();

            float atlasH = data.meta.size.h;
            var rects = new List<SpriteRect>(data.frames.Length);
            int skipped = 0;
            foreach (var f in data.frames)
            {
                if (f == null || f.frame == null) continue;
                if (f.border == null) f.border = new TPBorder();
                if (f.rotated) { Debug.LogWarning($"⚠️ 跳过旋转帧 \"{f.filename}\"（请关闭 Allow rotation）"); skipped++; continue; }

                var rect = new Rect(f.frame.x, atlasH - f.frame.y - f.frame.h, f.frame.w, f.frame.h);
                Vector2 pivot = new Vector2(0.5f, 0.5f);
                if (f.sourceSize != null && f.sourceSize.w > 0 && f.sourceSize.h > 0
                    && f.spriteSourceSize != null && f.frame.w > 0 && f.frame.h > 0)
                {
                    pivot.x = (f.sourceSize.w * 0.5f - f.spriteSourceSize.x) / f.frame.w;
                    pivot.y = (f.spriteSourceSize.y + f.frame.h - f.sourceSize.h * 0.5f) / f.frame.h;
                }
                string spName = Path.GetFileNameWithoutExtension(f.filename);
                if (string.IsNullOrEmpty(spName)) spName = $"sprite_{rects.Count}";
                rects.Add(new SpriteRect
                {
                    name = spName, rect = rect, pivot = pivot, alignment = SpriteAlignment.Custom,
                    border = new Vector4(f.border.l, f.border.b, f.border.r, f.border.t),
                    spriteID = GUID.Generate(),
                });
            }
            if (rects.Count == 0) { Debug.LogError($"❌ {jsonPath} 没有可用切片。"); return Result.Fail("无可用切片（可能全是旋转帧）"); }

            dp.SetSpriteRects(rects.ToArray());
            dp.Apply();
            importer.SaveAndReimport();

            if (autoRegister) RegisterAtlasPath(texPath, out _);
            Debug.Log($"✅ 切图完成: {texPath}\n共 {rects.Count} 个精灵" + (skipped > 0 ? $"，跳过 {skipped} 个旋转帧" : ""));
            return Result.Ok(rects.Count, "ok");
        }

        // ==================================================== 功能 2：合并图集
        public static Result CombineImages(List<string> imagePaths, string outputFile, int padding, int maxSize, Logger log = null)
        {
            string outputFull = Path.GetFullPath(outputFile);
            imagePaths = imagePaths.Where(p => Path.GetFullPath(p) != outputFull).ToList();
            if (imagePaths.Count < 2) return Result.Fail("去掉与输出同名的图片后不足 2 张");

            var textures = new List<Texture2D>();
            var rawNames = new List<string>();
            try
            {
                for (int i = 0; i < imagePaths.Count; i++)
                {
                    EditorUtility.DisplayProgressBar($"合并图集({i + 1}/{imagePaths.Count})", imagePaths[i], (i + 1f) / imagePaths.Count);
                    var src = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePaths[i]);
                    if (src == null) { Debug.LogWarning($"⚠️ 跳过无法读取的资源: {imagePaths[i]}"); log?.Invoke(LogKind.Warn, $"跳过：{Path.GetFileName(imagePaths[i])}（无法读取）"); continue; }
                    textures.Add(GetReadableCopy(src));
                    rawNames.Add(Path.GetFileNameWithoutExtension(imagePaths[i]));
                }
                if (textures.Count < 2) { CleanupTextures(textures); return Result.Fail("有效贴图不足 2 张"); }

                var names = MakeUniqueNames(rawNames);
                var atlasTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                Rect[] uvRects = atlasTex.PackTextures(textures.ToArray(), padding, maxSize);
                atlasTex.Apply();
                int aw = atlasTex.width, ah = atlasTex.height;

                File.WriteAllBytes(outputFile, atlasTex.EncodeToPNG());
                Object.DestroyImmediate(atlasTex);
                CleanupTextures(textures);
                AssetDatabase.ImportAsset(outputFile, ImportAssetOptions.ForceUpdate);

                var spriteRects = new SpriteRect[uvRects.Length];
                for (int i = 0; i < uvRects.Length; i++)
                {
                    var uv = uvRects[i];
                    spriteRects[i] = new SpriteRect
                    {
                        name = names[i],
                        rect = new Rect(Mathf.Round(uv.x * aw), Mathf.Round(uv.y * ah), Mathf.Round(uv.width * aw), Mathf.Round(uv.height * ah)),
                        pivot = new Vector2(0.5f, 0.5f), alignment = SpriteAlignment.Center,
                        border = Vector4.zero, spriteID = GUID.Generate(),
                    };
                }

                var importer = AssetImporter.GetAtPath(outputFile) as TextureImporter;
                if (importer == null) return Result.Fail("生成的图集不是可识别贴图");
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.alphaIsTransparency = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();

                var factory = new SpriteDataProviderFactories();
                factory.Init();
                var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
                dp.InitSpriteEditorDataProvider();
                dp.SetSpriteRects(spriteRects);
                dp.Apply();
                importer.SaveAndReimport();

                RegisterAtlasPath(outputFile, out _);
                AssetDatabase.Refresh();
                Debug.Log($"✅ 合并完成: {outputFile}\n共 {spriteRects.Length} 张图，图集尺寸 {aw}x{ah}");
                log?.Invoke(LogKind.Ok, $"已合并：{spriteRects.Length} 张 → {Path.GetFileName(outputFile)}（{aw}x{ah}）");
                return Result.Ok(spriteRects.Length, $"合并完成：{spriteRects.Length} 张图 → {Path.GetFileName(outputFile)}（{aw}x{ah}）");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static Texture2D GetReadableCopy(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
        static void CleanupTextures(List<Texture2D> ts) { foreach (var t in ts) if (t != null) Object.DestroyImmediate(t); ts.Clear(); }
        static string[] MakeUniqueNames(List<string> names)
        {
            var seen = new HashSet<string>();
            var res = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                string b = string.IsNullOrEmpty(names[i]) ? $"sprite_{i}" : names[i];
                string n = b; int k = 1;
                while (!seen.Add(n)) n = $"{b}_{k++}";
                res[i] = n;
            }
            return res;
        }

        // ==================================================== 功能 3：拆分图集
        public static Result SplitAtlas(string spFileName, string outputRoot = null, Logger log = null)
        {
            string shortName = Path.GetFileName(spFileName);
            var spTex = AssetDatabase.LoadAssetAtPath<Texture2D>(spFileName);
            if (spTex == null) { log?.Invoke(LogKind.Err, $"跳过：{shortName}（非贴图）"); return Result.Fail("非贴图"); }
            var importer = AssetImporter.GetAtPath(spFileName) as TextureImporter;
            if (importer == null || importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple)
            { Debug.LogWarning($"拆分跳过（非 Sprite/Multiple）: {spFileName}"); log?.Invoke(LogKind.Skip, $"跳过：{shortName}（非 Sprite/Multiple）"); return Result.Fail("非 Multiple 图集"); }

            bool wasReadable = importer.isReadable;
            if (!wasReadable) { importer.isReadable = true; importer.SaveAndReimport(); }

            string baseDir = string.IsNullOrEmpty(outputRoot) ? Path.GetDirectoryName(spFileName) : outputRoot;
            var outputDir = Path.Combine(baseDir, $"{Path.GetFileNameWithoutExtension(spFileName)}_sliced").Replace('\\', '/');
            if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dp = factory.GetSpriteEditorDataProviderFromObject(spTex);
            dp.InitSpriteEditorDataProvider();
            var spRects = dp.GetSpriteRects();

            var sliced = new List<string>();
            try
            {
                for (int i = 0; i < spRects.Length; i++)
                {
                    var sd = spRects[i];
                    EditorUtility.DisplayProgressBar($"拆分 {Path.GetFileName(spFileName)}", sd.name, (i + 1f) / spRects.Length);
                    var tex = new Texture2D((int)sd.rect.width, (int)sd.rect.height);
                    tex.SetPixels(spTex.GetPixels((int)sd.rect.x, (int)sd.rect.y, tex.width, tex.height));
                    tex.Apply();
                    string fn = Path.Combine(outputDir, $"{sd.name}.png").Replace('\\', '/');
                    if (File.Exists(fn)) File.Delete(fn);
                    File.WriteAllBytes(fn, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    sliced.Add(fn);
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            importer.isReadable = wasReadable;
            importer.SaveAndReimport();
            AssetDatabase.Refresh();

            foreach (var item in sliced)
            {
                var ti = AssetImporter.GetAtPath(item) as TextureImporter;
                if (ti == null) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
            Debug.Log($"✅ 拆分完成: {spFileName}\n共导出 {sliced.Count} 张 → {outputDir}");
            log?.Invoke(LogKind.Ok, $"已拆分：{shortName} → {sliced.Count} 张（{outputDir}）");
            return Result.Ok(sliced.Count, "ok");
        }

        // ==================================================== 功能 4：图集转换
        public static Result AtlasToSpriteSheet(SpriteAtlas atlas)
        {
            string src = AssetDatabase.GetAssetPath(atlas);
            string dir = Path.GetDirectoryName(src);
            string name = Path.GetFileNameWithoutExtension(src);
            string texFile = Path.Combine(dir, name + "_sheet.png").Replace('\\', '/');
            if (!AtlasToTexture(atlas, texFile, TextureImporterType.Sprite)) return Result.Fail("导出贴图失败");

            var ti = AssetImporter.GetAtPath(texFile) as TextureImporter;
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dp = factory.GetSpriteEditorDataProviderFromObject(ti);
            dp.InitSpriteEditorDataProvider();
            dp.SetSpriteRects(GetSpriteRects(atlas));
            dp.Apply();
            ti.SaveAndReimport();
            Debug.Log($"✅ SpriteAtlas → Sprite(Multiple): {texFile}");
            return Result.Ok(0, "ok");
        }

#if SPRITETOOLS_TMP
        public static Result AtlasToTmp(SpriteAtlas atlas)
        {
            string src = AssetDatabase.GetAssetPath(atlas);
            string dir = Path.GetDirectoryName(src);
            string name = Path.GetFileNameWithoutExtension(src);
            string assetFile = Path.Combine(dir, name + ".asset").Replace('\\', '/');
            string texFile = Path.Combine(dir, name + ".png").Replace('\\', '/');
            if (!AtlasToTexture(atlas, texFile, TextureImporterType.Default)) return Result.Fail("导出贴图失败");

            Sprite[] sprites = GetPackedSprites(atlas);
            if (sprites == null) return Result.Fail("取不到子图");
            System.Array.Sort(sprites, (a, b) => a.name.CompareTo(b.name));

            TMP_SpriteAsset spriteAsset = File.Exists(assetFile)
                ? AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(assetFile)
                : ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            if (!File.Exists(assetFile)) AssetDatabase.CreateAsset(spriteAsset, assetFile);

            spriteAsset.spriteSheet = AssetDatabase.LoadAssetAtPath<Texture2D>(texFile);
            spriteAsset.spriteCharacterTable.Clear();
            spriteAsset.spriteGlyphTable.Clear();
            if (spriteAsset.material == null)
            {
                var mat = new Material(Shader.Find("TextMeshPro/Sprite")) { mainTexture = spriteAsset.spriteSheet };
                AssetDatabase.AddObjectToAsset(mat, spriteAsset);
                AssetDatabase.SaveAssetIfDirty(spriteAsset);
                spriteAsset.material = mat;
            }
            var trim = "(Clone)".ToCharArray();
            for (int i = 0; i < sprites.Length; i++)
            {
                var sp = sprites[i];
                var r = sp.textureRect;
                var glyph = new TMP_SpriteGlyph((uint)i,
                    new UnityEngine.TextCore.GlyphMetrics(r.width, r.height, 0, r.height, r.width),
                    new UnityEngine.TextCore.GlyphRect(r), 1, 0);
                spriteAsset.spriteGlyphTable.Add(glyph);
                var ch = new TMP_SpriteCharacter(ToUnicode(i.ToString()), glyph) { name = sp.name.TrimEnd(trim) };
                spriteAsset.spriteCharacterTable.Add(ch);
            }
            AssetDatabase.SaveAssetIfDirty(spriteAsset);
            Debug.Log($"✅ SpriteAtlas → TMP_SpriteAsset: {assetFile}");
            return Result.Ok(sprites.Length, "ok");
        }
#endif

        public static Result AtlasToTextureSheet(SpriteAtlas atlas, int row)
        {
            if (atlas == null || atlas.spriteCount == 0) return Result.Fail("空图集");
            var sprites = new Sprite[atlas.spriteCount];
            atlas.GetSprites(sprites);
            System.Array.Sort(sprites, (a, b) => a.name.CompareTo(b.name));
            string src = AssetDatabase.GetAssetPath(atlas);
            string dir = Path.GetDirectoryName(src);
            string name = Path.GetFileNameWithoutExtension(src);
            string texFile = Path.Combine(dir, name + "_girdsheet.png").Replace('\\', '/');
            Sprites2Sheet(sprites, texFile, Mathf.Max(1, row));
            Debug.Log($"✅ SpriteAtlas → TextureSheet: {texFile}");
            return Result.Ok(sprites.Length, "ok");
        }

        static void Sprites2Sheet(Sprite[] sprites, string outFile, int row)
        {
            if (sprites == null || sprites.Length == 0 || row < 1) return;
            int cellW = 0, cellH = 0;
            foreach (var s in sprites)
            {
                cellW = Mathf.Max(cellW, (int)s.rect.width);
                cellH = Mathf.Max(cellH, (int)s.rect.height);
            }
            int cols = Mathf.CeilToInt(sprites.Length / (float)row);
            var atlasTex = new Texture2D(cols * cellW, row * cellH, TextureFormat.ARGB32, false);
            var clear = new Color[atlasTex.width * atlasTex.height];
            for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
            atlasTex.SetPixels(clear);

            var temps = new List<Texture2D>();
            for (int i = 0; i < sprites.Length; i++)
            {
                var sp = sprites[i];
                var srcTex = sp.texture;
                var srcRect = sp.textureRect;
                if (!srcTex.isReadable)
                {
                    var rt = RenderTexture.GetTemporary(srcTex.width, srcTex.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(srcTex, rt);
                    var tmp = new Texture2D(srcTex.width, srcTex.height, TextureFormat.ARGB32, false);
                    RenderTexture.active = rt;
                    tmp.ReadPixels(new Rect(0, 0, srcTex.width, srcTex.height), 0, 0);
                    tmp.Apply();
                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);
                    srcTex = tmp; temps.Add(tmp);
                }
                var px = srcTex.GetPixels((int)srcRect.x, (int)srcRect.y, (int)srcRect.width, (int)srcRect.height);
                int ri = i / cols, ci = i % cols;
                int dx = ci * cellW + (cellW - (int)srcRect.width) / 2;
                int dy = (row - 1 - ri) * cellH + (cellH - (int)srcRect.height) / 2;
                atlasTex.SetPixels(dx, dy, (int)srcRect.width, (int)srcRect.height, px);
            }
            atlasTex.Apply();
            File.WriteAllBytes(outFile, atlasTex.EncodeToPNG());
            foreach (var t in temps) Object.DestroyImmediate(t);
            Object.DestroyImmediate(atlasTex);
            AssetDatabase.Refresh();
        }

        static bool AtlasToTexture(SpriteAtlas atlas, string outFile, TextureImporterType texType)
        {
            if (atlas == null || atlas.spriteCount == 0) return false;
            var m = typeof(SpriteAtlasExtensions).GetMethod("GetPreviewTextures", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) return false;
            var previews = m.Invoke(null, new object[] { atlas }) as Texture2D[];
            if (previews == null || previews.Length != 1)
            { Debug.LogError($"图集存在 {previews?.Length ?? 0} 个子图集，请调大 MaxTextureSize 保证单图集。"); return false; }

            var rt = new RenderTexture(previews[0].width, previews[0].height, 0);
            Graphics.Blit(previews[0], rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(rt.width, rt.height) { alphaIsTransparency = true };
            readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            readable.Apply();
            RenderTexture.active = null;
            rt.Release();

            try { File.WriteAllBytes(outFile, readable.EncodeToPNG()); }
            catch (System.Exception e) { Debug.LogException(e); return false; }

            AssetDatabase.Refresh();
            var ti = AssetImporter.GetAtPath(outFile) as TextureImporter;
            ti.textureType = texType;
            if (texType == TextureImporterType.Sprite) { ti.spriteImportMode = SpriteImportMode.Multiple; ti.isReadable = true; }
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
            return true;
        }

        static SpriteRect[] GetSpriteRects(SpriteAtlas atlas)
        {
            var sprites = GetPackedSprites(atlas);
            if (sprites == null) return null;
            var trim = "(Clone)".ToCharArray();
            var rects = new SpriteRect[sprites.Length];
            for (int i = 0; i < sprites.Length; i++)
                rects[i] = new SpriteRect { name = sprites[i].name.Trim(trim), rect = sprites[i].textureRect };
            return rects;
        }

        static Sprite[] GetPackedSprites(SpriteAtlas atlas)
        {
            if (atlas == null || atlas.spriteCount == 0) return null;
            var m = typeof(SpriteAtlasExtensions).GetMethod("GetPackedSprites", BindingFlags.NonPublic | BindingFlags.Static);
            return m?.Invoke(null, new object[] { atlas }) as Sprite[];
        }

        static uint ToUnicode(string chars)
        {
            if (char.IsHighSurrogate(chars, 0) && 1 < chars.Length && char.IsLowSurrogate(chars, 1))
                return (uint)char.ConvertToUtf32(chars[0], chars[1]);
            return chars[0];
        }

        // ==================================================== 功能 5：图集表
        public static string ResolveAtlasPng(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            string ext = Path.GetExtension(assetPath).ToLower();
            if (ext == ".json")
            {
                try
                {
                    var data = JsonUtility.FromJson<TPData>(File.ReadAllText(assetPath));
                    if (data != null && data.meta != null && !string.IsNullOrEmpty(data.meta.image))
                        return Path.Combine(Path.GetDirectoryName(assetPath), data.meta.image).Replace('\\', '/');
                }
                catch { }
                return null;
            }
            foreach (var e in kAtlasImageExts) if (e == ext) return assetPath;
            return null;
        }

        public static Result RegisterAtlases(List<string> pngPaths)
        {
            var table = ReadAtlasTable();
            int added = 0, updated = 0;
            foreach (var png in pngPaths)
            {
                string key = SanitizeKey(Path.GetFileNameWithoutExtension(png));
                if (table.TryGetValue(key, out var old)) { if (old != png) { table[key] = png; updated++; } }
                else { table[key] = png; added++; }
            }
            if (added == 0 && updated == 0) return Result.Fail("图集表已是最新，无改动");

            WriteAtlasTable(table);
            AssetDatabase.ImportAsset(AtlasTablePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            Debug.Log($"✅ 图集表已更新：新增 {added}，更新 {updated}。\n{AtlasTablePath}");
            return Result.Ok(added + updated, $"图集表已更新：新增 {added}，更新 {updated}（编译后用 AtlasName.<名字> 调用）");
        }

        /// <summary>读取当前已注册的图集表（key -> png 路径）。</summary>
        public static Dictionary<string, string> GetRegistered() => ReadAtlasTable();

        /// <summary>扫描目录下所有 Sprite(Multiple) 图集并全部注册。</summary>
        public static Result ScanAndRegisterFolder(string root)
        {
            if (string.IsNullOrEmpty(root) || !AssetDatabase.IsValidFolder(root)) return Result.Fail($"目录无效：{root}");
            var pngs = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                if (ti != null && ti.spriteImportMode == SpriteImportMode.Multiple) pngs.Add(path);
            }
            if (pngs.Count == 0) return Result.Fail($"{root} 下没有 Sprite(Multiple) 图集");
            var r = RegisterAtlases(pngs);
            return r.ok ? r : Result.Ok(0, $"扫描 {pngs.Count} 个图集，图集表已是最新");
        }

        /// <summary>删除指定 key 的登记项。</summary>
        public static Result RemoveAtlasKeys(List<string> keys)
        {
            var table = ReadAtlasTable();
            int n = 0;
            foreach (var k in keys) if (table.Remove(k)) n++;
            if (n == 0) return Result.Fail("没有可删除的条目");
            WriteAtlasTable(table);
            AssetDatabase.ImportAsset(AtlasTablePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            return Result.Ok(n, $"已从图集表移除 {n} 项");
        }

        /// <summary>清理 png 已不存在的失效登记项。</summary>
        public static Result CleanInvalid()
        {
            var table = ReadAtlasTable();
            var dead = table.Where(kv => !File.Exists(kv.Value)).Select(kv => kv.Key).ToList();
            if (dead.Count == 0) return Result.Ok(0, "没有失效条目");
            foreach (var k in dead) table.Remove(k);
            WriteAtlasTable(table);
            AssetDatabase.ImportAsset(AtlasTablePath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            return Result.Ok(dead.Count, $"已清理 {dead.Count} 个失效条目");
        }

        static bool RegisterAtlasPath(string pngPath, out string key)
        {
            key = null;
            if (string.IsNullOrEmpty(pngPath)) return false;
            var table = ReadAtlasTable();
            key = SanitizeKey(Path.GetFileNameWithoutExtension(pngPath));
            if (table.TryGetValue(key, out var old) && old == pngPath) return false;
            table[key] = pngPath;
            WriteAtlasTable(table);
            return true;
        }

        static Dictionary<string, string> ReadAtlasTable()
        {
            var dict = new Dictionary<string, string>();
            if (!File.Exists(AtlasTablePath)) return dict;
            string text = File.ReadAllText(AtlasTablePath);
            string enumName = string.IsNullOrEmpty(AtlasEnumName) ? "AtlasName" : AtlasEnumName;
            string pattern = "\\{\\s*" + Regex.Escape(enumName) + "\\.(\\w+)\\s*,\\s*\"([^\"]+)\"\\s*\\}";
            foreach (Match m in Regex.Matches(text, pattern))
                dict[m.Groups[1].Value] = m.Groups[2].Value;
            return dict;
        }

        static void WriteAtlasTable(Dictionary<string, string> table)
        {
            var keys = new List<string>(table.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//   由「Sprite 工具箱 / 注册到图集表」自动生成，请勿手动修改。");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();
            string enumName = string.IsNullOrEmpty(AtlasEnumName) ? "AtlasName" : AtlasEnumName;
            sb.AppendLine("/// <summary>图集名枚举（自动生成）</summary>");
            sb.AppendLine($"public enum {enumName}");
            sb.AppendLine("{");
            foreach (var k in keys) sb.AppendLine($"    {k},");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("public static partial class GameTools");
            sb.AppendLine("{");
            sb.AppendLine($"    static readonly Dictionary<{enumName}, string> _atlasPaths = new Dictionary<{enumName}, string>");
            sb.AppendLine("    {");
            foreach (var k in keys) sb.AppendLine($"        {{ {enumName}.{k}, \"{table[k]}\" }},");
            sb.AppendLine("    };");
            sb.AppendLine();
            sb.AppendLine($"    public static string GetAtlasPath({enumName} atlas)");
            sb.AppendLine("        => _atlasPaths.TryGetValue(atlas, out var p) ? p : null;");
            sb.AppendLine("}");
            string tableDir = Path.GetDirectoryName(AtlasTablePath);
            if (!string.IsNullOrEmpty(tableDir) && !Directory.Exists(tableDir))
                Directory.CreateDirectory(tableDir);
            File.WriteAllText(AtlasTablePath, sb.ToString(), new UTF8Encoding(false));
        }

        public static string SanitizeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "_";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw) sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            string s = sb.ToString();
            if (char.IsDigit(s[0])) s = "_" + s;
            return s;
        }

        // ==================================================== 功能：子图速查
        /// <summary>取一个图集（Sprite(Multiple) 贴图 或 SpriteAtlas）里所有子图名，排序。</summary>
        public static List<string> GetSpriteNames(string assetPath)
        {
            var names = new List<string>();
            if (string.IsNullOrEmpty(assetPath)) return names;
            var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (main is SpriteAtlas atlas)
            {
                var sprites = GetPackedSprites(atlas);
                if (sprites != null)
                {
                    var trim = "(Clone)".ToCharArray();
                    foreach (var sp in sprites) names.Add(sp.name.TrimEnd(trim));
                }
            }
            else
            {
                foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                    if (rep is Sprite sp) names.Add(sp.name);
            }
            names.Sort(System.StringComparer.Ordinal);
            return names;
        }

        // ==================================================== 功能 6：预制体换图
        public class AtlasIndex
        {
            public readonly Dictionary<string, List<string>> NameToAtlases = new Dictionary<string, List<string>>();
            public readonly Dictionary<string, List<string>> NameToAtlasesCI = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);
            public readonly List<string> AtlasPaths = new List<string>();

            public static AtlasIndex Build(string atlasRoot)
            {
                var index = new AtlasIndex();
                if (string.IsNullOrEmpty(atlasRoot) || !AssetDatabase.IsValidFolder(atlasRoot))
                { Debug.LogError($"[图集替换] 图集根目录无效：{atlasRoot}"); return index; }

                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { atlasRoot }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var ti = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (ti == null || ti.spriteImportMode != SpriteImportMode.Multiple) continue;
                    index.AtlasPaths.Add(path);
                    foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                    {
                        if (!(rep is Sprite sp)) continue;
                        Add(index.NameToAtlases, sp.name, path);
                        Add(index.NameToAtlasesCI, sp.name, path);
                    }
                }
                return index;
            }
            static void Add(Dictionary<string, List<string>> d, string k, string v)
            {
                if (!d.TryGetValue(k, out var l)) { l = new List<string>(); d[k] = l; }
                if (!l.Contains(v)) l.Add(v);
            }
            public List<string> FindCandidates(string name)
            {
                if (string.IsNullOrEmpty(name)) return null;
                if (NameToAtlases.TryGetValue(name, out var l) && l.Count > 0) return l;
                if (NameToAtlasesCI.TryGetValue(name, out var l2) && l2.Count > 0) return l2;
                return null;
            }
            public static Sprite LoadSprite(string atlasPath, string name)
            {
                Sprite ci = null;
                foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(atlasPath))
                {
                    if (!(rep is Sprite s)) continue;
                    if (s.name == name) return s;
                    if (ci == null && string.Equals(s.name, name, System.StringComparison.OrdinalIgnoreCase)) ci = s;
                }
                return ci;
            }
        }

        public class PrefabEntry
        {
            public int imageIndex;
            public string hierarchyPath;
            public string spriteName;
            public string fromAssetPath;
            public List<string> candidates;
            public int choice;
            public string note;
        }

        public static List<PrefabEntry> PrefabScan(string prefabPath, AtlasIndex index, out int imageTotal)
        {
            var result = new List<PrefabEntry>();
            imageTotal = 0;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var images = root.GetComponentsInChildren<Image>(true);
                imageTotal = images.Length;
                string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
                for (int i = 0; i < images.Length; i++)
                {
                    var sprite = images[i].sprite;
                    if (sprite == null) continue;
                    string curPath = AssetDatabase.GetAssetPath(sprite);
                    if (index.AtlasPaths.Contains(curPath)) continue;
                    var candidates = index.FindCandidates(sprite.name);
                    if (candidates == null || candidates.Count == 0) continue;
                    result.Add(new PrefabEntry
                    {
                        imageIndex = i,
                        hierarchyPath = GetHierarchyPath(images[i].transform, root.transform),
                        spriteName = sprite.name,
                        fromAssetPath = curPath,
                        candidates = candidates,
                        choice = PreferredChoice(candidates, prefabName),
                        note = candidates.Count > 1 ? $"{candidates.Count} 个候选图集" : "",
                    });
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return result;
        }

        public static int PrefabApply(string prefabPath, List<PrefabEntry> entries)
        {
            int replaced = 0;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var images = root.GetComponentsInChildren<Image>(true);
                foreach (var e in entries)
                {
                    if (e.choice < 0 || e.choice >= e.candidates.Count) continue;
                    if (e.imageIndex < 0 || e.imageIndex >= images.Length) continue;
                    var img = images[e.imageIndex];
                    if (img == null || img.sprite == null || img.sprite.name != e.spriteName) continue;
                    var atlasSprite = AtlasIndex.LoadSprite(e.candidates[e.choice], e.spriteName);
                    if (atlasSprite == null || atlasSprite == img.sprite) continue;
                    img.sprite = atlasSprite;
                    replaced++;
                    Debug.Log($"[图集替换] {Path.GetFileName(prefabPath)} :: {e.hierarchyPath}\n  {e.spriteName} -> {e.candidates[e.choice]}");
                }
                if (replaced > 0) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return replaced;
        }

        public static int PrefabReplaceAuto(string prefabPath, AtlasIndex index)
        {
            var entries = PrefabScan(prefabPath, index, out _);
            string prefabName = Path.GetFileNameWithoutExtension(prefabPath);
            foreach (var e in entries)
                if (e.candidates.Count > 1 && PickByName(e.candidates, prefabName) < 0)
                {
                    e.choice = -1;
                    Debug.LogWarning($"[图集替换] {Path.GetFileName(prefabPath)} :: \"{e.spriteName}\" 同名出现在多个图集，已跳过。");
                }
            return PrefabApply(prefabPath, entries);
        }

        static int PreferredChoice(List<string> candidates, string prefabName)
        {
            if (candidates.Count == 1) return 0;
            int byName = PickByName(candidates, prefabName);
            return byName >= 0 ? byName : 0;
        }
        static int PickByName(List<string> candidates, string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                string a = Path.GetFileNameWithoutExtension(candidates[i]);
                if (prefabName.IndexOf(a, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    a.IndexOf(prefabName, System.StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }
            return -1;
        }
        static string GetHierarchyPath(Transform t, Transform root)
        {
            var stack = new Stack<string>();
            while (t != null && t != root) { stack.Push(t.name); t = t.parent; }
            return string.Join("/", stack);
        }
    }
}
