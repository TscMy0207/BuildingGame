using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 在跨播放模式域重载后仍能保存音频测试结果
/// </summary>
[InitializeOnLoad]
internal sealed class MasterAudioTestReports : ICallbacks
{
    /// <summary>
    /// 当前编辑器域中的测试API实例
    /// </summary>
    private static readonly TestRunnerApi s_api;

    /// <summary>
    /// 每次编辑器域加载时重新注册结果回调
    /// </summary>
    static MasterAudioTestReports()
    {
        s_api = ScriptableObject.CreateInstance<TestRunnerApi>();
        s_api.RegisterCallbacks(new MasterAudioTestReports());
    }

    /// <summary>
    /// 接收测试开始通知
    /// </summary>
    /// <param name="testsToRun">测试树</param>
    public void RunStarted(ITestAdaptor testsToRun) { }

    /// <summary>
    /// 仅为音频测试保存结果 不处理其他测试程序集
    /// </summary>
    /// <param name="result">本次测试结果</param>
    public void RunFinished(ITestResultAdaptor result)
    {
        if (ContainsAssembly(result, "BuildingGame.Audio.PlayModeTests"))
            TestRunnerApi.SaveResultToFile(result, "Logs/MasterAudio-PlayMode-results.xml");
    }

    /// <summary>
    /// 接收单个测试开始通知
    /// </summary>
    /// <param name="test">测试节点</param>
    public void TestStarted(ITestAdaptor test) { }

    /// <summary>
    /// 接收单个测试结束通知
    /// </summary>
    /// <param name="result">测试结果</param>
    public void TestFinished(ITestResultAdaptor result) { }

    /// <summary>
    /// 判断结果树是否包含指定测试程序集
    /// </summary>
    /// <param name="result">当前结果节点</param>
    /// <param name="assemblyName">目标程序集名称</param>
    /// <returns>是否找到目标程序集</returns>
    private static bool ContainsAssembly(ITestResultAdaptor result, string assemblyName) =>
        result.Name == assemblyName + ".dll" || result.Name == assemblyName ||
        result.HasChildren && result.Children.Any(child => ContainsAssembly(child, assemblyName));
}
