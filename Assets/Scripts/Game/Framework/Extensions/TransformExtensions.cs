using UnityEngine;

/// <summary>
/// Transform子节点查找扩展
/// </summary>
public static class TransformExtensions
{
    /// <summary>
    /// 按相对路径查找子节点
    /// </summary>
    /// <param name="root">查找起点</param>
    /// <param name="relativePath">以斜杠分隔的子节点相对路径</param>
    /// <returns>找到的子节点，未找到时返回null</returns>
    public static Transform GetChild(this Transform root, string relativePath)
    {
        if (root == null)
        {
            Debug.LogWarning($"无法查找UI子节点，根Transform为空，路径:{relativePath}");
            return null;
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            Debug.LogWarning($"无法查找UI子节点，路径为空，根节点:{root.name}", root);
            return null;
        }

        Transform child = root.Find(relativePath);
        if (child == null)
            Debug.LogWarning($"未找到子节点，根节点:{root.name}，路径:{relativePath}", root);

        return child;
    }

    /// <summary>
    /// 按相对路径查找子节点并获取该节点上的组件
    /// </summary>
    /// <typeparam name="T">目标组件类型</typeparam>
    /// <param name="root">查找起点</param>
    /// <param name="relativePath">以斜杠分隔的子节点相对路径</param>
    /// <returns>找到的组件，节点或组件未找到时返回null</returns>
    public static T GetChild<T>(this Transform root, string relativePath) where T : Component
    {
        Transform child = root.GetChild(relativePath);
        if (child == null)
            return null;

        T component = child.GetComponent<T>();
        if (component == null)
            Debug.LogWarning($"子节点缺少组件，根节点:{root.name}，路径:{relativePath}，组件:{typeof(T).Name}", child);

        return component;
    }
}
