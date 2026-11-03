using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// 提供Luban导表入口 并在构建前阻止未重新导出的配置进入构建
/// </summary>
public class ConfigTableEditorCommands : IPreprocessBuildWithReport
{
    /// <summary>
    /// 防止编辑器同时启动多个导表进程
    /// </summary>
    private static bool s_isGenerating;

    /// <summary>
    /// 配置检查在其他构建步骤前执行
    /// </summary>
    int IOrderedCallback.callbackOrder => -100;

    /// <summary>
    /// 执行导出脚本并刷新Unity资源
    /// </summary>
    [MenuItem("工具/配置表/Luban/导出配置")]
    public static async void Generate()
    {
        if (s_isGenerating) return;
        s_isGenerating = true;
        try
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var result = await Task.Run(() => RunGenerator(projectRoot, false));
            if (result.ExitCode != 0) throw new InvalidOperationException(result.Output);
            AssetDatabase.Refresh();
            Debug.Log($"Luban导表完成\n{result.Output}");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Luban导表失败:{exception.Message}");
        }
        finally
        {
            s_isGenerating = false;
        }
    }

    /// <summary>
    /// 构建前确认源表与实际发布产物一致
    /// </summary>
    /// <param name="report">本次构建报告</param>
    public void OnPreprocessBuild(BuildReport report)
    {
        var result = RunGenerator(Directory.GetParent(Application.dataPath).FullName, true);
        if (result.ExitCode != 0) throw new BuildFailedException($"配置表构建前校验失败:{result.Output}");
    }

    /// <summary>
    /// 在独立进程执行固定版本导表工具
    /// </summary>
    /// <param name="projectRoot">项目根目录</param>
    /// <param name="checkPublished">是否只校验已发布产物</param>
    /// <returns>进程退出码和输出文本</returns>
    private static (int ExitCode, string Output) RunGenerator(string projectRoot, bool checkPublished)
    {
        var script = Path.Combine(projectRoot, "ConfigTables/Generation/Generate.ps1");
        var start = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"{(checkPublished ? " -CheckPublished" : "")}")
        {
            WorkingDirectory = projectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动配置生成进程");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
    }
}
