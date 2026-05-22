using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using YooAsset;

[RequireComponent(typeof(Image))]
public class YooLocalizedImage : MonoBehaviour
{
    public string Key;

    [SerializeField]
    private Image target;

    public Image Target => target;

    private AssetHandle currentHandle;

    private int loadVersion;

    public void SetTarget(Image image)
    {
        target = image;
    }

    private void Awake()
    {
        target ??= GetComponent<Image>();

        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;

        Refresh().Forget();
    }

    private void OnDestroy()
    {
        LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged;

        ReleaseHandle();
    }

    private void OnLocaleChanged(Locale locale)
    {
        Refresh().Forget();
    }

    private async UniTaskVoid Refresh()
    {
        if (target == null)
            return;

        if (SpriteLocalizationManager.Instance == null)
            return;

        string locale =
            LocalizationSettings.SelectedLocale.Identifier.Code;

        string address =
            SpriteLocalizationManager.Instance
            .GetAddress(Key, locale);

        if (string.IsNullOrEmpty(address))
            return;

        int version = ++loadVersion;

        ReleaseHandle();

        currentHandle =
            YooAssets.LoadAssetAsync<Sprite>(address);

        await currentHandle.Task;

        if (version != loadVersion)
            return;

        if (currentHandle.Status != EOperationStatus.Succeed)
        {
            Debug.LogError($"Sprite加载失败 : {address}");
            return;
        }

        var sprite = currentHandle.AssetObject as Sprite;

        if (sprite == null)
            return;

        target.sprite = sprite;
    }

    private void ReleaseHandle()
    {
        if (currentHandle != null)
        {
            currentHandle.Release();
            currentHandle = null;
        }
    }
}