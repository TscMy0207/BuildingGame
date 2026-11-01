using Cysharp.Threading.Tasks;
using System;
using UnityEngine;

/// <summary>
/// 资源加载契约
/// </summary>
public interface IResourcesAssetLoader
{
    /// <summary>
    /// 异步加载资源
    /// 加载成功的资源会进入缓存 调用方使用完毕后必须调用ReleaseAsset配对释放
    /// 同一路径重复加载会复用缓存并增加引用计数
    /// </summary>
    /// <param name="location">资源路径</param>
    /// <typeparam name="T">资源类型</typeparam>
    /// <returns>异步任务</returns>
    UniTask<T> LoadAssetAsync<T>(string location) where T : UnityEngine.Object;

    /// <summary>
    /// 释放资源
    /// 只能释放由LoadAssetAsync加载过且仍在缓存中的资源
    /// 引用计数未归零时不会真正卸载
    /// </summary>
    /// <param name="location">资源路径</param>
    /// <param name="assetType">资源类型</param>
    void ReleaseAsset(string location, Type assetType);
}

/// <summary>
/// 提供带资源类型的释放调用
/// </summary>
public static class ResourcesAssetLoaderExtensions
{
    /// <summary>
    /// 释放指定类型的资源引用
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="resources">资源加载服务</param>
    /// <param name="location">资源路径</param>
    public static void ReleaseAsset<T>(this IResourcesAssetLoader resources, string location) where T : UnityEngine.Object
    {
        resources.ReleaseAsset(location, typeof(T));
    }
}
