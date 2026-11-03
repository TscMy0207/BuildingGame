using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

/// <summary>
/// 验证日志分流 诊断复用 原生日志转发和缺少符号时的降级信息
/// </summary>
public class LoggingServiceTests
{
    /// <summary>
    /// 隔离测试日志 避免滚动清理影响真实运行日志
    /// </summary>
    private string _directory;
    /// <summary>
    /// 待验证的日志服务及其泛型调用入口
    /// </summary>
    private IDisposable _service;
    /// <summary>
    /// 使用真实扩展方法捕获编译期调用位置
    /// </summary>
    private ILogger<LoggingServiceTests> _logger;
    /// <summary>
    /// 保存控制台消息与Unity自动附加的堆栈
    /// </summary>
    private readonly List<(string Message, string Stack)> _console = new();
    /// <summary>
    /// 保护可能从工作线程进入的控制台记录
    /// </summary>
    private readonly object _consoleSync = new();

    /// <summary>
    /// 创建独立日志服务并监听控制台 验证真实写入与退出排空
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "BuildingGameLoggingTests", Guid.NewGuid().ToString("N"));
        var type = typeof(GameComposition).Assembly.GetType("LoggingService", true);
        _service = (IDisposable)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { _directory }, null);
        var loggerType = typeof(GameComposition).Assembly.GetType("Logger`1", true).MakeGenericType(typeof(LoggingServiceTests));
        _logger = (ILogger<LoggingServiceTests>)Activator.CreateInstance(loggerType, _service);
        lock (_consoleSync) _console.Clear();
        Application.logMessageReceivedThreaded += CaptureConsole;
    }

    /// <summary>
    /// 解除监听并清理隔离目录 即使断言失败也恢复测试环境
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Application.logMessageReceivedThreaded -= CaptureConsole;
        _service?.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    /// <summary>
    /// 普通日志仅输出一次控制台 且不主动采集或附加堆栈
    /// </summary>
    [Test]
    public void InformationUsesConsoleOnlyWithoutStackCapture()
    {
        _logger.Log("普通日志标记");
        var entry = SingleConsole();
        Assert.That(entry.Message, Does.Contain("[信息] [LoggingServiceTests] 普通日志标记"));
        Assert.That(entry.Message, Does.Contain("调用位置（编译期）:"));
        Assert.That(entry.Message, Does.Not.Contain("PrepareDiagnostics"));
        Assert.That(entry.Stack, Is.Empty);
        Assert.That(FlushFiles(), Does.Not.Contain("普通日志标记"));
    }

    /// <summary>
    /// 警告和无异常错误复用同一份文本 且不被Unity回调重复写入
    /// </summary>
    [TestCase(LogType.Warning)]
    [TestCase(LogType.Error)]
    public void WarningAndErrorSharePreparedDiagnostics(LogType type)
    {
        if (type == LogType.Error) LogAssert.Expect(LogType.Error, new Regex("诊断复用标记"));
        _logger.Write(type, "诊断复用标记", null, "Assets/Caller.cs", 127);
        var entry = SingleConsole();
        var file = FlushFiles();
        Assert.That(file.TrimEnd(), Is.EqualTo(entry.Message));
        Assert.That(entry.Stack, Is.Empty);
        Assert.That(file, Does.Contain(nameof(WarningAndErrorSharePreparedDiagnostics)));
        Assert.That(file, Does.Contain("调用位置（编译期）: Assets/Caller.cs:127"));
        Assert.That(Regex.Matches(file, "诊断复用标记").Count, Is.EqualTo(1));
        Assert.That(file, Does.Not.Contain("[Unity]"));
        Assert.That(file, Does.Match(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}\]"));
        Assert.That(file, Does.Not.Contain("时间:[").And.Not.Contain("模块:[").And.Not.Contain("等级:["));
    }

    /// <summary>
    /// 堆栈已有同一源码位置时不再追加调用位置字段
    /// </summary>
    [Test]
    public void ExistingSourceLocationIsNotRepeated()
    {
        var stack = new StackTrace(new StackFrame("Assets/Caller.cs", 42));
        var result = FormatStack(stack, "Assets/Caller.cs", 42);
        Assert.That(result, Does.Not.Contain("调用位置（编译期）"));
        Assert.That(result, Does.Not.Contain("堆栈源码行号不可用"));
    }

    /// <summary>
    /// 没有源码符号时保留实际方法信息并明确区分编译期补充位置
    /// </summary>
    [Test]
    public void MissingSymbolsKeepRuntimeFramesAndCompilerLocation()
    {
        var stack = new StackTrace(false);
        var result = FormatStack(stack, "Assets/Caller.cs", 88);
        Assert.That(result, Does.Contain(nameof(MissingSymbolsKeepRuntimeFramesAndCompilerLocation)));
        Assert.That(result, Does.Contain("堆栈源码行号不可用"));
        Assert.That(result, Does.Contain("调用位置（编译期）: Assets/Caller.cs:88"));
    }

    /// <summary>
    /// 缺少堆栈与编译期位置时不输出伪造的零行号
    /// </summary>
    [Test]
    public void MissingCallerLocationIsExplicit()
    {
        var result = FormatStack(new StackTrace(new InvalidOperationException("未抛出的测试异常"), false), "", 0);
        Assert.That(result, Does.Contain("调用位置不可用（未提供编译期调用位置）"));
        Assert.That(result, Does.Not.Contain(":0"));
    }

    /// <summary>
    /// 只有编译期文件名时保留文件并说明行号缺失
    /// </summary>
    [Test]
    public void CallerFileWithoutLineDoesNotInventLineNumber()
    {
        var result = FormatStack(new StackTrace(false), "Assets/Caller.cs", 0);
        Assert.That(result, Does.Contain("Assets/Caller.cs（行号不可用）"));
        Assert.That(result, Does.Not.Contain("Assets/Caller.cs:0"));
    }

    /// <summary>
    /// 异常包含内部异常和原始抛出堆栈 并单独标记日志记录位置
    /// </summary>
    [Test]
    public void ExceptionPreservesOriginalStackAndRecordLocation()
    {
        Exception exception;
        try
        {
            ThrowOriginalException();
            throw new InvalidOperationException("不应到达");
        }
        catch (InvalidOperationException original)
        {
            exception = new AggregateException("外层异常标记", original);
        }
        var originalText = exception.ToString();
        LogAssert.Expect(LogType.Error, new Regex("异常日志标记"));
        _logger.Error("异常日志标记", exception, "Assets/Recorder.cs", 73);
        var entry = SingleConsole();
        var file = FlushFiles();
        Assert.That(file.TrimEnd(), Is.EqualTo(entry.Message));
        Assert.That(file, Does.Contain(originalText));
        Assert.That(file, Does.Contain(nameof(ThrowOriginalException)));
        Assert.That(file, Does.Contain("记录位置（编译期）: Assets/Recorder.cs:73"));
        Assert.That(file, Does.Not.Contain("PrepareDiagnostics"));
        Assert.That(entry.Stack, Is.Empty);
        Assert.That(Regex.Matches(file, "异常日志标记").Count, Is.EqualTo(1));
        Assert.That(exception.ToString(), Is.EqualTo(originalText));
    }

    /// <summary>
    /// 未抛出的异常如实说明原始堆栈不存在 不采集新的调用堆栈冒充
    /// </summary>
    [Test]
    public void UnthrownExceptionDoesNotAcquireReplacementStack()
    {
        LogAssert.Expect(LogType.Error, new Regex("未抛出异常标记"));
        _logger.Error("未抛出异常标记", new InvalidOperationException("原始消息"));
        var file = FlushFiles();
        Assert.That(file, Does.Contain("异常堆栈不可用"));
        Assert.That(file, Does.Not.Contain("PrepareDiagnostics"));
    }

    /// <summary>
    /// 原生警告 错误 异常和断言仅使用回调堆栈 不添加新的记录位置
    /// </summary>
    [TestCase(LogType.Warning)]
    [TestCase(LogType.Error)]
    [TestCase(LogType.Exception)]
    [TestCase(LogType.Assert)]
    public void NativeDiagnosticTypesKeepCallbackStack(LogType type)
    {
        InvokeNative("原生日志标记", "Original.Native.Frame (at Native.cs:21)", type);
        var file = FlushFiles();
        Assert.That(file, Does.Contain("[Unity] 原生日志标记\nOriginal.Native.Frame (at Native.cs:21)"));
        Assert.That(file, Does.Not.Contain("调用位置").And.Not.Contain("PrepareDiagnostics"));
        lock (_consoleSync) Assert.That(_console, Is.Empty);
    }

    /// <summary>
    /// 原生回调不含堆栈时明确标记 未重新采集回调处理器的堆栈
    /// </summary>
    [Test]
    public void NativeMissingStackIsExplicit()
    {
        InvokeNative("原生空堆栈标记", "", LogType.Error);
        Assert.That(FlushFiles(), Does.Contain("Unity未提供调用堆栈"));
    }

    /// <summary>
    /// 原生普通日志不写入文件
    /// </summary>
    [Test]
    public void NativeInformationIsNotPersisted()
    {
        InvokeNative("原生普通日志标记", "native stack", LogType.Log);
        Assert.That(FlushFiles(), Does.Not.Contain("原生普通日志标记"));
    }

    /// <summary>
    /// 真实Unity日志回调只写一次文件且不改变全局堆栈设置
    /// </summary>
    [Test]
    public void ActualUnityCallbackUsesProvidedStackOnce()
    {
        var before = Application.GetStackTraceLogType(LogType.Warning);
        Debug.LogWarning("真实原生回调标记");
        string stack;
        lock (_consoleSync) stack = _console.Single(item => item.Message == "真实原生回调标记").Stack;
        var file = FlushFiles();
        Assert.That(Regex.Matches(file, "真实原生回调标记").Count, Is.EqualTo(1));
        Assert.That(file, Does.Contain(string.IsNullOrWhiteSpace(stack) ? "Unity未提供调用堆栈" : stack));
        Assert.That(Application.GetStackTraceLogType(LogType.Warning), Is.EqualTo(before));
    }

    /// <summary>
    /// 并发写入后释放会排空全部已接收条目 且重复释放与后续写入安全
    /// </summary>
    [Test]
    public void ConcurrentWritesDrainBeforeIdempotentDispose()
    {
        Parallel.For(0, 12, index => _logger.Warning("并发日志标记_" + index, "Assets/Worker.cs", index + 1));
        var file = FlushFiles();
        Assert.That(Regex.Matches(file, "并发日志标记_").Count, Is.EqualTo(12));
        _service.Dispose();
        _logger.Error("释放后不写入标记");
        Assert.That(ReadFiles(), Is.EqualTo(file));
    }

    /// <summary>
    /// 捕获控制台文本和Unity附加堆栈 用于核对是否重复采集
    /// </summary>
    private void CaptureConsole(string message, string stack, LogType type)
    {
        lock (_consoleSync) _console.Add((message, stack));
    }

    /// <summary>
    /// 读取唯一控制台输出
    /// </summary>
    private (string Message, string Stack) SingleConsole()
    {
        lock (_consoleSync)
        {
            Assert.That(_console.Count, Is.EqualTo(1));
            return _console[0];
        }
    }

    /// <summary>
    /// 排空后台文件队列后读取本次测试的全部日志
    /// </summary>
    private string FlushFiles()
    {
        _service.Dispose();
        return ReadFiles();
    }

    /// <summary>
    /// 只读取隔离目录中的日志文件
    /// </summary>
    private string ReadFiles() => Directory.Exists(_directory)
        ? string.Concat(Directory.GetFiles(_directory, "*.log").OrderBy(path => path).Select(File.ReadAllText))
        : "";

    /// <summary>
    /// 验证缺少符号时的格式分支 无需依赖当前编辑器符号设置
    /// </summary>
    private string FormatStack(StackTrace stack, string file, int line) => (string)_service.GetType()
        .GetMethod("FormatCallStack", BindingFlags.NonPublic | BindingFlags.Static)
        .Invoke(null, new object[] { stack, file, line });

    /// <summary>
    /// 直接验证Unity回调各等级的分流 不向控制台注入真实异常或断言
    /// </summary>
    private void InvokeNative(string message, string stack, LogType type) => _service.GetType()
        .GetMethod("HandleUnityLog", BindingFlags.NonPublic | BindingFlags.Instance)
        .Invoke(_service, new object[] { message, stack, type });

    /// <summary>
    /// 提供不会被内联消除的原始异常抛出点
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowOriginalException() => throw new InvalidOperationException("原始异常标记");
}
