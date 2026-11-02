using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

/// <summary>为运行时通过反射发现的业务类型生成构建保留规则</summary>
public sealed class CoreModuleLinkerProcessor : IUnityLinkerProcessor
{
    /// <summary>使用默认构建回调顺序</summary>
    public int callbackOrder => 0;

    /// <summary>保留Game程序集中的具体业务子类及其成员 规则仅写入构建临时目录</summary>
    /// <param name="report">当前构建报告</param>
    /// <param name="data">Unity链接器构建输入</param>
    /// <returns>本次构建附加的类型保留文件路径</returns>
    public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
    {
        var document = CreateLinkerDocument(typeof(ModuleSystem).Assembly);
        var path = Path.Combine(data.inputDirectory, "CoreModules.link.xml");
        document.Save(path);
        return path;
    }

    /// <summary>从指定程序集生成精确的业务类型保留清单</summary>
    /// <param name="assembly">需要在运行时扫描的程序集</param>
    /// <returns>符合Unity链接器格式的保留清单</returns>
    internal static XDocument CreateLinkerDocument(Assembly assembly)
    {
        var types = assembly.GetTypes().Where(type => type.IsClass && !type.IsAbstract &&
            !type.ContainsGenericParameters &&
            (type.IsSubclassOf(typeof(ModuleSystem)) || type.IsSubclassOf(typeof(ModuleModel))));
        return new XDocument(new XElement("linker",
            new XElement("assembly", new XAttribute("fullname", assembly.GetName().Name),
                types.OrderBy(type => type.FullName, StringComparer.Ordinal).Select(type =>
                    new XElement("type", new XAttribute("fullname", type.FullName.Replace('+', '/')),
                        new XAttribute("preserve", "all"))))));
    }
}
