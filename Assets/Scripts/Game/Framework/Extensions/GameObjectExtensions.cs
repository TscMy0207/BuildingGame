using UnityEngine;

/// <summary>
/// GameObject通用扩展
/// </summary>
public static class GameObjectExtensions
{

    /// <summary>
    /// 仅在激活状态变化时调用SetActive 避免重复触发状态设置
    /// </summary>
    public static void SetActiveIfNeeded(this GameObject gameObject, bool active)
    {
        if (gameObject == null) return;
        if (gameObject.activeSelf == active) return;
        gameObject.SetActive(active);
    }

}