using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using DG.Tweening;
using Utils;

/// <summary>
/// 在 Unity 原生 Button 基础上扩展的按钮组件。
/// 继承自 UnityEngine.UI.Button，因此原生的 onClick、interactable、
/// 导航、以及各种 Transition（颜色/图片/动画）等功能全部保留。
///
/// 额外功能：
///   1. 按下缩放（enableClickScale）
///   2. 悬浮缩放（enableHoverScale）
///   3. 点击音效（enableClickSound），可配置音效类型 / 名称 / 播放时机
///   4. 手势：双击 / 长按(一次) / 长按(持续) —— 默认关闭，勾选对应开关才启用
///
/// 事件对应：
///   onClick        —— 单击（沿用原生 Button.onClick）
///   onDoubleClick  —— 双击
///   onPress        —— 长按触发一次（按住达到 pressDurationTime 那一刻）
///   onPressEvents  —— 长按持续触发（需 longPressRepeat=true）
///
/// 兼容性：enableDoubleClick / enableLongPress 都不勾时，本组件行为与原生
/// Button 完全一致（onClick 抬起即触发），不影响现有任何按钮。
/// </summary>
[AddComponentMenu("UI/Game Button")]
[RequireComponent(typeof(RectTransform))]
public class GameButton : Button
{
    public enum SoundTiming
    {
        OnPointerDown, // 一按下就播（反馈最跟手）
        OnClick        // 抬起且判定为有效点击时才播
    }

    [Header("=== 按下缩放 ===")]
    public bool enableClickScale = true;
    public float pressScale = 0.95f;
    public float pressAnimTime = 0.1f;

    [Header("=== 悬浮缩放 ===")]
    public bool enableHoverScale = false;
    public float hoverScale = 1.05f;
    public float hoverAnimTime = 0.12f;

    [Header("=== 点击音效 ===")]
    public bool enableClickSound = true;
    public AudioEffectType soundType = AudioEffectType.InGame;
    public string soundClipName = "ClickBtn";
    public SoundTiming soundTiming = SoundTiming.OnClick;

    [Header("=== 手势·双击 ===")]
    [Tooltip("勾选后启用双击检测。注意：启用后单击会延迟 doubleClickInterval 秒才确认(要留时间判断是不是双击)")]
    public bool enableDoubleClick = false;
    [Tooltip("两次点击算双击的最大间隔(秒)")]
    public float doubleClickInterval = 0.3f;

    [Header("=== 手势·长按 ===")]
    [Tooltip("勾选后启用长按检测(onPress 触发一次；onPressEvents 可持续触发)")]
    public bool enableLongPress = false;
    [Tooltip("按住多久算长按(秒)")]
    public float pressDurationTime = 0.5f;
    [Tooltip("长按达到后是否持续触发 onPressEvents(如长按加速)")]
    public bool longPressRepeat = false;
    [Tooltip("持续触发的间隔(秒)；<=0 表示每帧触发")]
    public float pressRepeatInterval = 0.1f;

    [Header("=== 手势事件 ===")]
    [Tooltip("双击")]
    public UnityEvent onDoubleClick = new UnityEvent();
    [Tooltip("长按触发一次(按住达到 pressDurationTime 那一刻)")]
    public UnityEvent onPress = new UnityEvent();
    [Tooltip("长按持续触发(需 longPressRepeat=true)")]
    public UnityEvent onPressEvents = new UnityEvent();
    // 单击直接沿用继承来的原生 onClick(Button.onClick)，不另声明。

    // —— 缩放状态 ——
    private Vector3 originalScale = Vector3.one;
    private bool scaleCached;
    private bool isHovering;
    private bool isPressing;

    // —— 手势状态 ——
    private bool GestureEnabled => enableDoubleClick || enableLongPress;
    private bool isPointerDown;
    private float pressElapsed;
    private bool longPressFired;           // 本次按住已触发过 onPress(长按一次)
    private bool longPressRepeating;       // 正在持续触发 onPressEvents
    private float nextRepeatTime;
    private bool suppressClickByLongPress; // 本次按住已判定长按 → 抬起不再算单击/双击
    private int pendingClicks;             // 待确认的点击次数(双击判定)
    private float lastClickTime;           // 上次有效点击(抬起)时间

    protected override void Awake()
    {
        base.Awake();
        CacheOriginalScale();
    }

    private void CacheOriginalScale()
    {
        if (scaleCached) return;
        originalScale = transform.localScale;
        scaleCached = true;
    }

    #region Pointer Events —— 均先调用 base 保留原生 Button 行为

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        if (!enableHoverScale || !IsInteractable()) return;

        isHovering = true;
        RefreshScale();
    }

    public override void OnPointerExit(PointerEventData eventData)
    {
        base.OnPointerExit(eventData);

        // 手势：指针移出即取消按住，避免拖出后仍触发长按/持续
        if (GestureEnabled)
        {
            isPointerDown = false;
            longPressRepeating = false;
        }

        if (!enableHoverScale) return;
        isHovering = false;
        RefreshScale();
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (enableClickScale && IsInteractable())
        {
            isPressing = true;
            RefreshScale();
        }

        if (soundTiming == SoundTiming.OnPointerDown)
            TryPlayClickSound();

        // 手势：开始按住计时
        if (GestureEnabled)
        {
            isPointerDown = true;
            pressElapsed = 0f;
            longPressFired = false;
            longPressRepeating = false;
            suppressClickByLongPress = false;
        }
    }

    public override void OnPointerUp(PointerEventData eventData)
    {
        base.OnPointerUp(eventData);
        if (eventData.button != PointerEventData.InputButton.Left) return;

        isPressing = false;
        RefreshScale();

        // 手势：结束按住(是否算点击由 OnPointerClick 判定)
        isPointerDown = false;
        longPressRepeating = false;
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            base.OnPointerClick(eventData);
            return;
        }

        // 手势未启用 → 完全保持原生行为：base 立即触发原生 onClick + 原音效时机。现有按钮零影响。
        if (!GestureEnabled)
        {
            base.OnPointerClick(eventData);
            if (soundTiming == SoundTiming.OnClick)
                TryPlayClickSound();
            return;
        }

        // 手势启用 → 不让 base 立即触发 onClick，改由手势判定后触发对应事件。
        // 本次按住已判定长按 → 抬起既不算单击也不算双击。
        if (suppressClickByLongPress || longPressFired)
            return;

        if (soundTiming == SoundTiming.OnClick)
            TryPlayClickSound();

        if (enableDoubleClick)
        {
            pendingClicks++;
            lastClickTime = Time.unscaledTime;
            if (pendingClicks >= 2)
            {
                pendingClicks = 0;
                InvokeDoubleClick();
            }
            // pendingClicks==1 时，交给 Update 等 doubleClickInterval 超时后确认为单击
        }
        else
        {
            // 只开了长按、没开双击 → 单击立即触发
            InvokeSingleClick();
        }
    }

    #endregion

    private void Update()
    {
        if (!GestureEnabled) return;

        // —— 长按 ——
        if (enableLongPress && isPointerDown && IsInteractable())
        {
            pressElapsed += Time.unscaledDeltaTime;

            if (!longPressFired && pressElapsed >= pressDurationTime)
            {
                longPressFired = true;
                suppressClickByLongPress = true;   // 长按后抬起不再算点击
                onPress?.Invoke();                 // 长按触发一次

                if (longPressRepeat)
                {
                    longPressRepeating = true;
                    nextRepeatTime = Time.unscaledTime + Mathf.Max(0f, pressRepeatInterval);
                }
            }

            if (longPressRepeating)
            {
                if (pressRepeatInterval <= 0f)
                {
                    onPressEvents?.Invoke();        // 每帧持续触发
                }
                else if (Time.unscaledTime >= nextRepeatTime)
                {
                    onPressEvents?.Invoke();        // 按间隔持续触发
                    nextRepeatTime += pressRepeatInterval;
                }
            }
        }

        // —— 双击：等待窗口内没有第二击 → 确认为单击 ——
        if (enableDoubleClick && pendingClicks == 1
            && Time.unscaledTime - lastClickTime >= doubleClickInterval)
        {
            pendingClicks = 0;
            InvokeSingleClick();
        }
    }

    private void InvokeSingleClick()
    {
        if (!IsActive() || !IsInteractable()) return;
        onClick?.Invoke();   // 沿用原生 Button.onClick 作为"单击"
    }

    private void InvokeDoubleClick()
    {
        if (!IsActive() || !IsInteractable()) return;
        onDoubleClick?.Invoke();
    }

    /// <summary>
    /// 依据当前 悬浮 / 按下 状态，始终以 originalScale 为基准计算目标缩放，避免误差累积。
    /// </summary>
    private void RefreshScale()
    {
        if (!scaleCached) CacheOriginalScale();

        transform.DOKill();

        Vector3 targetScale = originalScale;
        if (isPressing && enableClickScale)
            targetScale = originalScale * pressScale;
        else if (isHovering && enableHoverScale)
            targetScale = originalScale * hoverScale;

        float time = isPressing ? pressAnimTime : hoverAnimTime;

        transform.DOScale(targetScale, time)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true); // 不受 timeScale 影响，暂停时依然生效
    }

    private void TryPlayClickSound()
    {
        if (!enableClickSound) return;
        if (!IsInteractable()) return;
        if (string.IsNullOrEmpty(soundClipName)) return;
        if (AudioManager.Instance == null) return;

        AudioManager.Instance.PlayUrlEffect(soundType, soundClipName);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        isHovering = false;
        isPressing = false;
        transform.DOKill();
        if (scaleCached)
            transform.localScale = originalScale;

        // 手势状态复位，避免下次启用时残留
        isPointerDown = false;
        longPressFired = false;
        longPressRepeating = false;
        suppressClickByLongPress = false;
        pendingClicks = 0;
    }
}
