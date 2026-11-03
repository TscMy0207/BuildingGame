using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

/// <summary>
/// 为自动注册类型生成构建保留规则
/// 避免Unity静态分析裁剪反射创建的业务类型
/// </summary>
public class AutoRegistrationLinkerProcessor : IUnityLinkerProcessor
{
    /// <summary>
    /// 使用默认构建回调顺序
    /// </summary>
    public int callbackOrder => 0;

    /// <summary>
    /// 生成自动注册类型的保留文件
    /// 规则仅写入构建临时目录
    /// </summary>
    /// <param name="report">当前构建报告</param>
    /// <param name="data">Unity链接器构建输入</param>
    /// <returns>本次构建附加的类型保留文件路径</returns>
    public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
    {
        var assembly = typeof(ModuleSystem).Assembly;
        var document = CreateLinkerDocument(GameComposition.GetAutoRegisteredTypes(assembly), assembly.GetName().Name);
        var path = Path.Combine(data.inputDirectory, "AutoRegistration.link.xml");
        document.Save(path);
        return path;
    }

    /// <summary>
    /// 从自动注册类型生成精确的保留清单
    /// </summary>
    /// <param name="types">参与自动注册的类型</param>
    /// <param name="assemblyName">类型所属程序集名称</param>
    /// <returns>符合Unity链接器格式的保留清单</returns>
    internal static XDocument CreateLinkerDocument(IEnumerable<Type> types, string assemblyName)
    {
        if (types == null) throw new ArgumentNullException(nameof(types));
        if (string.IsNullOrEmpty(assemblyName)) throw new ArgumentException("程序集名称不能为空", nameof(assemblyName));
        var entries = types.Where(type => type != null).Distinct().OrderBy(type => type.FullName, StringComparer.Ordinal);
        return new XDocument(new XElement("linker",
            new XElement("assembly", new XAttribute("fullname", assemblyName),
                entries.Select(type => new XElement("type",
                    new XAttribute("fullname", type.FullName.Replace('+', '/')),
                    new XAttribute("preserve", "all"))))));
    }
}
