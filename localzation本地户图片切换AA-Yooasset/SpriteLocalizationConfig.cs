using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Localization/Sprite Localization Config")]
public class SpriteLocalizationConfig : ScriptableObject
{
    public List<SpriteLocalizationItem> Items = new();
}

[Serializable]
public class SpriteLocalizationItem
{
    public string Key;

    [Header("中文简体")]
    public string ZhHans;

    [Header("中文繁体")]
    public string ZhHant;

    [Header("英文")]
    public string En;

    [Header("日文")]
    public string Ja;

    [Header("韩文")]
    public string Ko;

    [Header("法文")]
    public string Fr;

    [Header("德文")]
    public string De;

    [Header("西班牙文")]
    public string Es;

    [Header("葡萄牙文")]
    public string Pt;

    [Header("俄文")]
    public string Ru;

    public string GetAddress(string locale)
    {
        switch (locale)
        {
            case "zh-Hans":
                return ZhHans;

            case "zh-Hant":
                return ZhHant;

            case "en":
                return En;

            case "ja":
                return Ja;

            case "ko":
                return Ko;

            case "fr":
                return Fr;

            case "de":
                return De;

            case "es":
                return Es;

            case "pt":
                return Pt;

            case "ru":
                return Ru;
        }

        return En;
    }
}