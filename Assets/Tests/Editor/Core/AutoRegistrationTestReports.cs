using System.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// 保存自动注册测试结果
/// </summary>
[InitializeOnLoad]
internal class AutoRegistrationTestReports : ICallbacks
{
    /// <summary>
    /// 持有测试API以保持回调有效
    /// </summary>
    private static readonly TestRunnerApi s_api;

    /// <summary>
    /// 当前编辑器域注册的结果监听器 用于卸载时精确解除订阅
    /// </summary>
    private static readonly AutoRegistrationTestReports s_callbacks;

    /// <summary>
    /// 域加载后注册测试结果监听
    /// </summary>
    static AutoRegistrationTestReports()
    {
        s_api = ScriptableObject.CreateInstance<TestRunnerApi>();
        s_callbacks = new AutoRegistrationTestReports();
        s_api.RegisterCallbacks(s_callbacks);
        AssemblyReloadEvents.beforeAssemblyReload += Release;
        EditorApplication.quitting += Release;
    }

    /// <summary>
    /// 接收测试运行开始通知
    /// </summary>
    /// <param name="testsToRun">待执行测试树</param>
    public void RunStarted(ITestAdaptor testsToRun) { }

    /// <summary>
    /// 保存包含当前测试程序集的结果
    /// </summary>
    /// <param name="result">运行结果树</param>
    public void RunFinished(ITestResultAdaptor result)
    {
        if (ContainsTests(result, "BuildingGame.Core.Tests"))
            TestRunnerApi.SaveResultToFile(result, "Logs/AutoRegistration-EditMode-results.xml");
        if (ContainsTests(result, "BuildingGame.Core.PlayModeTests"))
            TestRunnerApi.SaveResultToFile(result, "Logs/AutoRegistration-PlayMode-results.xml");
    }

    /// <summary>
    /// 接收单项测试开始通知
    /// </summary>
    /// <param name="test">待执行测试</param>
    public void TestStarted(ITestAdaptor test) { }

    /// <summary>
    /// 接收单项测试完成通知
    /// </summary>
    /// <param name="result">单项测试结果</param>
    public void TestFinished(ITestResultAdaptor result) { }

    /// <summary>
    /// 判断结果树是否包含目标测试程序集
    /// </summary>
    /// <param name="result">待检查的结果节点</param>
    /// <param name="assemblyName">目标程序集名称</param>
    /// <returns>是否包含目标程序集</returns>
    private static bool ContainsTests(ITestResultAdaptor result, string assemblyName) =>
        result.Name == assemblyName + ".dll" || result.Name == assemblyName ||
        result.HasChildren && result.Children.Any(child => ContainsTests(child, assemblyName));

    /// <summary>
    /// 在域重载或编辑器退出前解除监听并销毁持有的测试API
    /// </summary>
    private static void Release()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= Release;
        EditorApplication.quitting -= Release;
        if (s_api == null) return;
        s_api.UnregisterCallbacks(s_callbacks);
        UnityEngine.Object.DestroyImmediate(s_api);
    }
}
