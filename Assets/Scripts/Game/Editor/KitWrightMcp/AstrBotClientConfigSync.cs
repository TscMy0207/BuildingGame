#if UNITY_EDITOR
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildingGame.EditorTools.KitWrightMcp
{
    /// <summary>
    /// 将AstrBot中的KitWright MCP配置同步为当前Unity项目的服务地址
    /// </summary>
    [InitializeOnLoad]
    internal static class AstrBotClientConfigSync
    {
        /// <summary>
        /// 固定的AstrBot配置文件路径
        /// </summary>
        private const string AstrBotConfigPath = @"C:\Software\AstrBot\data\mcp_server.json";
        /// <summary>
        /// BuildingGame独立的MCP连接配置名称
        /// </summary>
        private const string ServerEntryName = "kitwright-unity-buildinggame";
        /// <summary>
        /// KitWright项目设置的相对路径
        /// </summary>
        private const string SettingsRelativePath = "UserSettings/KitWrightMcpSettings.json";
        /// <summary>
        /// 项目访问令牌的编辑器偏好键前缀
        /// </summary>
        private const string TokenPreferencePrefix = "KitWright.MCP.ServerToken.";
        /// <summary>
        /// AstrBot使用的MCP传输协议
        /// </summary>
        private const string DefaultTransport = "streamable_http";
        /// <summary>
        /// 缺少项目设置时使用的基础端口
        /// </summary>
        private const int DefaultPort = 9535;
        /// <summary>
        /// 端口冲突时探测的端口数量
        /// </summary>
        private const int PortFallbackRange = 10;
        /// <summary>
        /// 自动同步的最大尝试次数
        /// </summary>
        private const int MaxAttempts = 30;
        /// <summary>
        /// 自动同步的重试间隔 单位秒
        /// </summary>
        private const double RetryDelaySeconds = 1.0d;

        /// <summary>
        /// 本次编辑器脚本加载后的同步尝试次数
        /// </summary>
        private static int s_attemptCount;
        /// <summary>
        /// 下次尝试同步的编辑器启动时间
        /// </summary>
        private static double s_nextAttemptTime;

        /// <summary>
        /// 编辑器加载脚本后等待当前项目的MCP服务并同步连接配置
        /// </summary>
        static AstrBotClientConfigSync()
        {
            EditorApplication.update += WaitForKitWright;
        }

        /// <summary>
        /// 重试同步配置，直到KitWright就绪或启动等待时间结束
        /// </summary>
        private static void WaitForKitWright()
        {
            if (EditorApplication.timeSinceStartup < s_nextAttemptTime)
                return;

            s_attemptCount++;
            if (SyncNow(false) || s_attemptCount >= MaxAttempts)
            {
                EditorApplication.update -= WaitForKitWright;
                if (s_attemptCount >= MaxAttempts)
                    Debug.LogWarning("[AstrBot KitWright Sync]KitWright未能就绪，请在服务启动后运行Tools/KitWright MCP/Sync AstrBot Configuration");
                return;
            }

            s_nextAttemptTime = EditorApplication.timeSinceStartup + RetryDelaySeconds;
        }

        /// <summary>
        /// 使用当前KitWright服务地址更新AstrBot已有的MCP配置
        /// </summary>
        [MenuItem("Tools/KitWright MCP/Sync AstrBot Configuration")]
        private static void SyncFromMenu()
        {
            if (!SyncNow(true))
                Debug.LogWarning("[AstrBot KitWright Sync]KitWright尚未就绪，请在服务启动后重试");
        }

        /// <summary>
        /// 查找当前运行的KitWright服务并将地址写入AstrBot
        /// </summary>
        /// <param name="logSuccess">配置已是最新时是否记录日志</param>
        /// <returns>配置同步完成或无需重试时返回true</returns>
        private static bool SyncNow(bool logSuccess)
        {
            try
            {
                if (!File.Exists(AstrBotConfigPath))
                {
                    Debug.LogWarning("[AstrBot KitWright Sync]找不到AstrBot MCP配置：" + AstrBotConfigPath);
                    return true;
                }

                string projectRoot = ProjectRootPath();
                string pin = PinFromProjectPath(projectRoot);
                string token = EditorPrefs.GetString(TokenPreferencePrefix + pin, string.Empty);
                if (token.Length != 32)
                {
                    Debug.Log("[AstrBot KitWright Sync]正在等待KitWright项目访问令牌");
                    return false;
                }

                int basePort = ReadKitWrightPort(projectRoot);
                string serverUrl;
                int port;
                if (!TryFindLiveServer(basePort, pin, token, out port))
                {
                    Debug.Log("[AstrBot KitWright Sync]正在等待KitWright MCP服务");
                    return false;
                }

                serverUrl = string.Format("http://127.0.0.1:{0}/p/{1}/t/{2}/", port, pin, token);
                string original = File.ReadAllText(AstrBotConfigPath, Encoding.UTF8);
                JObject root = JObject.Parse(original);
                JObject servers = root["mcpServers"] as JObject;
                if (servers == null)
                {
                    if (root["mcpServers"] != null)
                    {
                        Debug.LogWarning("[AstrBot KitWright Sync]AstrBot的mcpServers值不是对象，配置未修改");
                        return true;
                    }

                    servers = new JObject();
                    root["mcpServers"] = servers;
                }

                JObject entry = servers[ServerEntryName] as JObject;
                if (entry == null)
                {
                    if (servers[ServerEntryName] != null)
                    {
                        Debug.LogWarning("[AstrBot KitWright Sync]KitWright配置项格式异常，配置未修改");
                        return true;
                    }

                    entry = new JObject
                    {
                        ["active"] = true,
                        ["transport"] = DefaultTransport,
                        ["url"] = serverUrl
                    };
                    servers[ServerEntryName] = entry;
                    AtomicWrite(AstrBotConfigPath, root.ToString(Formatting.Indented) + Environment.NewLine);
                    Debug.Log("[AstrBot KitWright Sync]已将KitWright MCP配置添加到AstrBot，请重新加载或重启AstrBot以建立连接");
                    return true;
                }

                bool needsActiveDefault = entry["active"] == null;
                bool changed = !UrlsEqual((string)entry["url"], serverUrl) ||
                               !string.Equals((string)entry["transport"], DefaultTransport, StringComparison.Ordinal) ||
                               needsActiveDefault;
                if (!UrlsEqual((string)entry["url"], serverUrl))
                    entry["url"] = serverUrl;
                if (!string.Equals((string)entry["transport"], DefaultTransport, StringComparison.Ordinal))
                    entry["transport"] = DefaultTransport;
                if (needsActiveDefault)
                    entry["active"] = true;

                if (changed)
                    AtomicWrite(AstrBotConfigPath, root.ToString(Formatting.Indented) + Environment.NewLine);

                if (changed)
                    Debug.Log("[AstrBot KitWright Sync]已更新AstrBot中的KitWright MCP地址，请重新加载或重启AstrBot以重新连接");
                else if (logSuccess)
                    Debug.Log("[AstrBot KitWright Sync]AstrBot中的KitWright MCP地址已是最新");

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AstrBot KitWright Sync]更新AstrBot MCP配置失败：" + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// 从项目设置读取KitWright配置的端口
        /// </summary>
        /// <param name="projectRoot">Unity项目根目录</param>
        /// <returns>配置的基础端口</returns>
        private static int ReadKitWrightPort(string projectRoot)
        {
            string settingsPath = Path.Combine(projectRoot, SettingsRelativePath);
            if (!File.Exists(settingsPath))
                return DefaultPort;

            JObject settings = JObject.Parse(File.ReadAllText(settingsPath, Encoding.UTF8));
            int configuredPort = (int?)settings["port"] ?? DefaultPort;
            return configuredPort > 0 && configuredPort <= 65535 ? configuredPort : DefaultPort;
        }

        /// <summary>
        /// 查找接受当前项目标识和访问令牌的服务
        /// </summary>
        /// <param name="basePort">KitWright配置的基础端口</param>
        /// <param name="pin">KitWright使用的项目短标识</param>
        /// <param name="token">当前项目访问令牌</param>
        /// <param name="port">找到服务时返回的端口</param>
        /// <returns>匹配的服务响应MCP初始化请求时返回true</returns>
        private static bool TryFindLiveServer(int basePort, string pin, string token, out int port)
        {
            for (int offset = 0; offset < PortFallbackRange && basePort + offset <= 65535; offset++)
            {
                int candidatePort = basePort + offset;
                if (!IsListeningOnLoopback(candidatePort))
                    continue;

                string candidateUrl = string.Format("http://127.0.0.1:{0}/p/{1}/t/{2}/", candidatePort, pin, token);
                if (!AcceptsInitialize(candidateUrl))
                    continue;

                port = candidatePort;
                return true;
            }

            port = 0;
            return false;
        }

        /// <summary>
        /// 检查本机回环端口是否存在监听服务
        /// </summary>
        /// <param name="port">需要检查的端口</param>
        /// <returns>是否成功建立连接</returns>
        private static bool IsListeningOnLoopback(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    IAsyncResult result = client.BeginConnect("127.0.0.1", port, null, null);
                    bool connected = result.AsyncWaitHandle.WaitOne(100);
                    if (!connected)
                        return false;

                    client.EndConnect(result);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 验证服务地址是否接受当前项目的MCP初始化请求
        /// </summary>
        /// <param name="serverUrl">包含访问令牌的KitWright服务地址</param>
        /// <returns>服务地址返回HTTP 200时返回true</returns>
        private static bool AcceptsInitialize(string serverUrl)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(serverUrl);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Accept = "application/json, text/event-stream";
                request.Timeout = 500;
                request.ReadWriteTimeout = 500;
                byte[] body = Encoding.UTF8.GetBytes(
                    "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":0," +
                    "\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{}," +
                    "\"clientInfo\":{\"name\":\"AstrBotConfigSync\",\"version\":\"1.0\"}}}");
                request.ContentLength = body.Length;
                using (Stream stream = request.GetRequestStream())
                    stream.Write(body, 0, body.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                    return response.StatusCode == HttpStatusCode.OK;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 通过临时文件写入更新后的AstrBot配置
        /// </summary>
        /// <param name="destinationPath">AstrBot配置文件路径</param>
        /// <param name="content">序列化后的配置内容</param>
        private static void AtomicWrite(string destinationPath, string content)
        {
            string directory = Path.GetDirectoryName(destinationPath);
            string temporaryPath = Path.Combine(directory, Path.GetFileName(destinationPath) + ".kitwright.tmp");
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            if (File.Exists(destinationPath))
                File.Replace(temporaryPath, destinationPath, null);
            else
                File.Move(temporaryPath, destinationPath);
        }

        /// <summary>
        /// 获取当前打开的Unity项目绝对路径
        /// </summary>
        /// <returns>Unity项目根目录</returns>
        private static string ProjectRootPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        /// <summary>
        /// 根据项目路径生成KitWright简短标识
        /// </summary>
        /// <param name="projectPath">Unity项目根目录</param>
        /// <returns>路径哈希值的前八位十六进制字符</returns>
        private static string PinFromProjectPath(string projectPath)
        {
            string normalized = Path.GetFullPath(projectPath).Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                var builder = new StringBuilder(bytes.Length * 2);
                for (int index = 0; index < bytes.Length; index++)
                    builder.Append(bytes[index].ToString("x2"));
                return builder.ToString().Substring(0, 8);
            }
        }

        /// <summary>
        /// 比较MCP地址并忽略末尾斜杠
        /// </summary>
        /// <param name="left">第一个服务地址</param>
        /// <param name="right">第二个服务地址</param>
        /// <returns>标准化后的地址相同时返回true</returns>
        private static bool UrlsEqual(string left, string right)
        {
            return string.Equals(left?.Trim().TrimEnd('/'), right?.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }
    }
}
#endif
