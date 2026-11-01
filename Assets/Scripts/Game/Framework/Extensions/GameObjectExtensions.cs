using UnityEngine;

/// <summary>
/// GameObject通用扩展
/// </summary>
public static class GameObjectExtensions
{

    public static void SetActiveIfNeeded(this GameObject gameObject, bool active)
    {
        if (gameObject == null) return;
        if (gameObject.activeSelf == active) return;
        gameObject.SetActive(active);
    }

}