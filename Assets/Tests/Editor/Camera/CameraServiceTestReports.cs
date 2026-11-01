using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>在播放模式域重载后保存摄像机服务测试结果</summary>
[InitializeOnLoad]
internal sealed class CameraServiceTestReports : ICallbacks
{
    /// <summary>持有测试API以保持结果回调有效</summary>
    private static readonly TestRunnerApi s_api;

    /// <summary>每次域加载后重新注册测试结果监听</summary>
    static CameraServiceTestReports()
    {
        s_api = ScriptableObject.CreateInstance<TestRunnerApi>();
        s_api.RegisterCallbacks(new CameraServiceTestReports());
    }

    /// <summary>接收测试运行开始通知</summary>
    /// <param name="testsToRun">待执行测试树</param>
    public void RunStarted(ITestAdaptor testsToRun) { }

    /// <summary>仅保存包含摄像机测试程序集的运行结果</summary>
    /// <param name="result">测试结果树</param>
    public void RunFinished(ITestResultAdaptor result)
    {
        if (ContainsCameraTests(result)) TestRunnerApi.SaveResultToFile(result, "Logs/CameraService-PlayMode-results.xml");
    }

    /// <summary>接收单项测试开始通知</summary>
    /// <param name="test">测试节点</param>
    public void TestStarted(ITestAdaptor test) { }

    /// <summary>接收单项测试结束通知</summary>
    /// <param name="result">单项结果</param>
    public void TestFinished(ITestResultAdaptor result) { }

    /// <summary>判断结果树是否属于摄像机测试</summary>
    /// <param name="result">结果节点</param>
    /// <returns>是否找到目标程序集</returns>
    private static bool ContainsCameraTests(ITestResultAdaptor result) =>
        result.Name == "BuildingGame.Camera.PlayModeTests.dll" || result.Name == "BuildingGame.Camera.PlayModeTests" ||
        result.HasChildren && result.Children.Any(ContainsCameraTests);
}
