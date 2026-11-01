using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>保存存档服务测试结果</summary>
[InitializeOnLoad]
internal sealed class SaveServiceTestReports : ICallbacks
{
    /// <summary>持有测试API以保持结果回调有效</summary>
    private static readonly TestRunnerApi s_api;

    /// <summary>每次域加载后重新注册测试结果监听</summary>
    static SaveServiceTestReports()
    {
        s_api = ScriptableObject.CreateInstance<TestRunnerApi>();
        s_api.RegisterCallbacks(new SaveServiceTestReports());
    }

    /// <summary>接收测试运行开始通知</summary>
    /// <param name="testsToRun">待执行测试树</param>
    public void RunStarted(ITestAdaptor testsToRun) { }

    /// <summary>仅保存包含存档测试程序集的运行结果</summary>
    /// <param name="result">测试结果树</param>
    public void RunFinished(ITestResultAdaptor result)
    {
        if (ContainsSaveTests(result)) TestRunnerApi.SaveResultToFile(result, "Logs/SaveService-EditMode-results.xml");
    }

    /// <summary>接收单项测试开始通知</summary>
    /// <param name="test">测试节点</param>
    public void TestStarted(ITestAdaptor test) { }

    /// <summary>接收单项测试结束通知</summary>
    /// <param name="result">单项结果</param>
    public void TestFinished(ITestResultAdaptor result) { }

    /// <summary>判断结果树是否属于存档测试</summary>
    /// <param name="result">结果节点</param>
    /// <returns>是否找到目标程序集</returns>
    private static bool ContainsSaveTests(ITestResultAdaptor result) =>
        result.Name == "BuildingGame.Save.Tests.dll" || result.Name == "BuildingGame.Save.Tests" ||
        result.HasChildren && result.Children.Any(ContainsSaveTests);
}
