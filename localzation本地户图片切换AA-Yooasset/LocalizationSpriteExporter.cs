using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Components;
using UnityEngine.UI;

public class LocalizationSpriteExporter
{
    [MenuItem("Tools/Localization/Convert Sprite Localization")]
    public static void Convert()
    {
        string savePath =
            "Assets/Projects/Scripts/GameTools/Images/localzationS.asset";

        // 读取已有配置
        var config =
            AssetDatabase.LoadAssetAtPath<SpriteLocalizationConfig>(savePath);

        // 不存在则创建
        if (config == null)
        {
            config =
                ScriptableObject.CreateInstance<SpriteLocalizationConfig>();

            AssetDatabase.CreateAsset(config, savePath);
        }

        // 清空旧数据
        config.Items.Clear();

        EditorUtility.SetDirty(config);

        // 防止重复 Key
        Dictionary<string, SpriteLocalizationItem> itemMap =
            new();

        // 扫描所有 prefab
        string[] guids =
            AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { "Assets" });

        int convertCount = 0;

        foreach (string guid in guids)
        {
            string prefabPath =
                AssetDatabase.GUIDToAssetPath(guid);

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
                continue;

            bool changed = false;

            // 查找所有 LocalizeSpriteEvent
            var localizeSprites =
                prefab.GetComponentsInChildren<LocalizeSpriteEvent>(true);

            foreach (var localizeSprite in localizeSprites)
            {
                var localizedSprite =
                    localizeSprite.AssetReference;

                var tableRef =
                    localizedSprite.TableReference;

                var entryRef =
                    localizedSprite.TableEntryReference;

                // 获取 Localization Collection
                var collection =
                    LocalizationEditorSettings
                    .GetAssetTableCollection(tableRef);

                if (collection == null)
                {
                    Debug.LogWarning(
                        $"找不到 AssetTableCollection : {tableRef}");

                    continue;
                }

                // 获取 SharedEntry
                var sharedEntry =
                    collection.SharedData
                    .GetEntryFromReference(entryRef);

                if (sharedEntry == null)
                {
                    Debug.LogWarning(
                        $"找不到 SharedEntry : {entryRef.KeyId}");

                    continue;
                }

                string key = sharedEntry.Key;

                // 创建配置项
                if (!itemMap.TryGetValue(key, out var item))
                {
                    item = new SpriteLocalizationItem();

                    item.Key = key;

                    itemMap.Add(key, item);

                    config.Items.Add(item);
                }

                // 遍历语言表
                foreach (var table in collection.AssetTables)
                {
                    var entry =
                        table.GetEntry(sharedEntry.Id);

                    if (entry == null)
                        continue;

                    string guidValue = entry.Guid;

                    if (string.IsNullOrEmpty(guidValue))
                        continue;

                    // GUID -> AssetPath
                    string assetPath =
                        AssetDatabase.GUIDToAssetPath(guidValue);

                    // 转 YooAsset Address
                    string yooAddress =
                        ConvertToYooAddress(assetPath);

                    string localeCode =
                        table.LocaleIdentifier.Code;

                    switch (localeCode)
                    {
                        case "zh-Hans":
                            item.ZhHans = yooAddress;
                            break;

                        case "zh-Hant":
                            item.ZhHant = yooAddress;
                            break;

                        case "en":
                            item.En = yooAddress;
                            break;

                        case "ja":
                            item.Ja = yooAddress;
                            break;

                        case "ko":
                            item.Ko = yooAddress;
                            break;

                        case "fr":
                            item.Fr = yooAddress;
                            break;

                        case "de":
                            item.De = yooAddress;
                            break;

                        case "es":
                            item.Es = yooAddress;
                            break;

                        case "pt":
                            item.Pt = yooAddress;
                            break;

                        case "ru":
                            item.Ru = yooAddress;
                            break;
                    }
                }

                GameObject targetGo =
                    localizeSprite.gameObject;

                // 不删除，只禁用
                localizeSprite.enabled = false;

                // 自动查找 Image
                var image =
                    targetGo.GetComponent<Image>();

                if (image == null)
                {
                    Debug.LogWarning(
                        $"[{prefab.name}] " +
                        $"对象 {targetGo.name} 没有 Image 组件");

                    continue;
                }

                // 获取 YooLocalizedImage
                var yooComp =
                    targetGo.GetComponent<YooLocalizedImage>();

                // 不存在则添加
                if (yooComp == null)
                {
                    yooComp =
                        Undo.AddComponent<YooLocalizedImage>(targetGo);
                }

                // 同步数据
                yooComp.Key = key;

                yooComp.SetTarget(image);

                changed = true;

                convertCount++;

                Debug.Log(
                    $"同步完成 : {prefab.name} -> {key}");
            }

            // prefab 保存
            if (changed)
            {
                PrefabUtility.SavePrefabAsset(prefab);

                EditorUtility.SetDirty(prefab);
            }
        }

        // 保存配置
        EditorUtility.SetDirty(config);

        AssetDatabase.SaveAssets();

        AssetDatabase.Refresh();

        Debug.Log(
            $"Sprite Localization 同步完成 : {convertCount}");
    }

    /// <summary>
    /// AssetPath 转 YooAsset Address
    /// </summary>
    private static string ConvertToYooAddress(string assetPath)
    {
        assetPath = assetPath.Replace("\\", "/");

        // 去掉扩展名
        assetPath = Path.ChangeExtension(assetPath, null);

        return assetPath;
    }
}