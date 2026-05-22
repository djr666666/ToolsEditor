using System.Collections.Generic;
using UnityEngine;

public class SpriteLocalizationManager : MonoBehaviour
{
    public static SpriteLocalizationManager Instance;

    [SerializeField]
    private SpriteLocalizationConfig config;

    private readonly Dictionary<string, SpriteLocalizationItem> map =
        new();

    private bool initialized;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        Initialize();
    }

    private void Initialize()
    {
        if (initialized)
            return;

        initialized = true;

        map.Clear();

        if (config == null)
        {
            Debug.LogError("SpriteLocalizationConfig 未赋值");
            return;
        }

        if (config.Items == null)
        {
            Debug.LogError("SpriteLocalizationConfig.Items 为空");
            return;
        }

        foreach (var item in config.Items)
        {
            if (item == null)
                continue;

            if (string.IsNullOrEmpty(item.Key))
                continue;

            if (map.ContainsKey(item.Key))
            {
                Debug.LogWarning($"重复 Key : {item.Key}");
                continue;
            }

            map.Add(item.Key, item);
        }

        Debug.Log($"SpriteLocalization 初始化完成 : {map.Count}");
    }

    public string GetAddress(string key, string locale)
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("Localization Key 为空");
            return null;
        }

        if (!map.TryGetValue(key, out var item))
        {
            Debug.LogError($"找不到 SpriteLocalization Key : {key}");
            return null;
        }

        return item.GetAddress(locale);
    }
}