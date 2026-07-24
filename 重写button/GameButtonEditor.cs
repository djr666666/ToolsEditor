using UnityEditor;
using UnityEditor.UI;

/// <summary>
/// GameButton 的自定义 Inspector。
///
/// 为什么需要它：Unity 给 Button 注册的 ButtonEditor 是 [CustomEditor(typeof(Button), true)]，
/// 对子类也生效，但它只手写绘制 Button 原生字段(Interactable/Transition/Navigation/OnClick)，
/// 不会画 GameButton 新增的字段。没有这个 Editor，缩放/音效/手势等在面板上就都看不到。
/// （纯代码配置的话没有它也能用；只影响 Inspector 显示。）
///
/// 做法：先 base 画原生 Button，再用 DrawPropertiesExcluding 排除掉原生字段、
/// 自动补画其余所有扩展字段(缩放/悬浮/音效/双击/长按/手势事件)。字段上的
/// [Header]/[Tooltip] 会照常显示，且以后 GameButton 再加字段这里无需改动。
/// </summary>
[CustomEditor(typeof(GameButton))]
[CanEditMultipleObjects]
public class GameButtonEditor : ButtonEditor
{
    // 原生 Button 的序列化字段，由 base.OnInspectorGUI() 负责绘制，这里排除避免重复。
    private static readonly string[] s_ButtonProps =
    {
        "m_Script", "m_Navigation", "m_Transition", "m_Colors", "m_SpriteState",
        "m_AnimationTriggers", "m_Interactable", "m_TargetGraphic", "m_OnClick"
    };

    public override void OnInspectorGUI()
    {
        // 1) 原生 Button 面板：Interactable / Transition / Navigation / OnClick
        base.OnInspectorGUI();

        EditorGUILayout.Space();

        // 2) GameButton 扩展字段：缩放 / 悬浮 / 音效 / 双击 / 长按 / 手势事件，全自动绘制
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, s_ButtonProps);
        serializedObject.ApplyModifiedProperties();
    }
}
