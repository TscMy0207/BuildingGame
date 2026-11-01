using System;
using System.IO;
using BuildingGame.Config.Framework;
using Luban;

/// <summary>
/// 在生成产物进入Unity前验证二进制可读性与项目约束
/// </summary>
internal static class Program
{
    /// <summary>
    /// 加载实际二进制并验证UI面板字段
    /// </summary>
    /// <param name="args">二进制目录与项目根目录</param>
    /// <returns>成功返回零 失败返回非零</returns>
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("需要配置目录和项目目录");
            var tables = new FrameworkTables(file => new ByteBuf(File.ReadAllBytes(Path.Combine(args[0], file + ".bytes"))));
            foreach (var record in tables.TbUIPanel.DataList)
            {
                if (record.Id <= 0 || !Enum.IsDefined(typeof(PanelLayer), record.Layer))
                    throw new InvalidDataException($"面板ID或层级无效:{record.Id}");
                var separator = record.PrefabLocation?.IndexOf('/') ?? -1;
                if (separator <= 0 || separator == record.PrefabLocation.Length - 1 || record.PrefabLocation.Contains(".."))
                    throw new InvalidDataException($"面板资源地址必须为包名/采集地址:{record.Id}");
                var address = record.PrefabLocation.Substring(separator + 1);
                if (address.StartsWith("Assets/", StringComparison.Ordinal) && !File.Exists(Path.Combine(args[1], address)))
                    throw new InvalidDataException($"面板预制体不存在:{record.Id}:{address}");
            }
            Console.WriteLine($"二进制配置验证完成 UI面板数量:{tables.TbUIPanel.DataList.Count}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"配置验证失败:{exception.Message}");
            return 1;
        }
    }
}
