# Luban配置表

本项目使用Luban生成强类型C#配置与二进制数据，通过现有配置子服务和YooAsset加载

## 版本与环境

- 生成器固定为Luban5.1.0，二进制和模板位于`Tools/Luban`
- Unity运行时包以嵌入包方式放在`Packages/com.code-philosophy.luban`，保持官方源码原样
- 版本、提交和发行包校验值见`Tools/Luban/VERSION.json`
- Windows导表需要安装.NET8 SDK，支持PATH或Windows标准安装目录中的`dotnet`；游戏运行不需要安装SDK
- 当前接入在Unity2022.3.62f2验证
- 官方文档：https://www.datable.cn/docs/guide/load-runtime

## 日常使用

1. 编辑`ConfigTables/Datas/UIPanel.xlsx`，保留前四行表头，从第五行起填写面板数据
2. 在项目根目录执行`ConfigTables\Generation\Generate.cmd`，或者在Unity点击`工具/配置表/Luban/导出配置`
3. 检查导表结果，再构建YooAsset资源包和游戏

只校验源表、生成代码和二进制，不更新项目产物：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ConfigTables/Generation/Generate.ps1 -ValidateOnly
```

检查源表是否与已经发布到Assets的产物一致：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ConfigTables/Generation/Generate.ps1 -CheckPublished
```

Unity构建前会自动执行`-CheckPublished`，源表或结构已修改却未重新导出时会阻止构建

## UI面板表

当前没有业务面板预制体，初始表为空，不包含虚假的资源地址

| 字段 | 含义 | 约束 |
| --- | --- | --- |
| id | 稳定面板ID，与代码中的UIKey一致 | 正整数，不重复 |
| prefab_location | YooAsset资源地址 | `包名/采集地址`，例如实际采集的预制体完整Assets路径 |
| layer | UI层级 | Page、Popup、Overlay、Toast |

UI层级由配置服务显式转换为现有`UIPanelLayer`，不依赖枚举数值一致

XML定义位于`Defines/framework.xml`，新增字段时同步修改XML与Excel表头，重新导出即可；不要手工修改生成代码

## 加载与资源包

- 配置包名：`FrameworkConfig`
- 采集设置：`Assets/Settings/BundleCollectorSetting.asset`
- 采集目录：`Assets/GameData/ConfigTables/Framework`
- 资源地址前缀：`FrameworkConfig/Assets/GameData/ConfigTables/Framework`
- 生成代码目录：`Assets/Scripts/Game/Framework/ConfigTables/Generated`
- `manifest.json`保存结构哈希及每个二进制文件的SHA256

现有资源服务先启动，再由配置总服务调用配置子服务，最后由UI服务使用配置

子服务逐个异步读取资源，成功取得的TextAsset引用都会配对释放；所有二进制读取、哈希检查、解析和引用解析成功后才发布配置实例

资源接口没有单次取消加载的能力，因此加载期间取消时会等待当前资源返回、释放该引用，再结束任务；Clear会使正在进行的加载失效，不能在清理后重新发布结果

编辑器使用YooAsset模拟模式，发布时需要先通过YooAsset构建`FrameworkConfig`资源包并复制到内置资源目录；仅构建Unity Player不会自动生成YooAsset资源包

本次未创建业务UI、UIRoot或AudioRoot预制体，也未新增游戏启动入口；完整游戏启动仍需这些已有框架要求的项目资源与入口，不能将配置加载测试成功视为整套游戏已可运行

## 导出保护与限制

- 使用系统临时目录生成，Luban不会清空手写代码目录
- 先执行严格数据校验，再编译生成代码，并加载实际二进制检查项目约束
- 校验失败不修改Assets中的上一份有效产物
- 同时发布代码和数据时，写入异常会恢复本次涉及文件的旧内容；这不是进程崩溃或断电级别的跨目录事务
- 内容未变化的文件不会重写，已有`.meta`与GUID保持不变
- 清单绑定生成代码结构，并校验每个二进制文件，阻止代码与数据结构不匹配或文件损坏
- 为避免命令行擅自删除Unity资源，删除或重命名表后，应通过Unity删除对应的过期生成文件；不要直接删除仍被引用的类型
- 生成失败时临时目录保留，便于诊断；终端输出中会显示其位置
- 导表会检查完整Assets路径是否存在；自定义采集地址是否有效、资源是否为所需面板预制体，仍需按YooAsset采集规则和实际打开面板验证
- 不提供运行时热重载；不要在玩法中修改配置对象，运行状态写入业务模型或存档

## 测试

Unity Test Runner中的测试程序集：

- `BuildingGame.ConfigTables.Tests`：配置查询、层级映射、损坏和缺失数据、结构不匹配、取消、清理及失败重载
- `BuildingGame.ConfigTables.PlayModeTests`：真实VContainer注册、资源服务、YooAsset模拟包与Luban配置加载

框架配置的公开入口为`IFrameworkConfigTableService.FrameworkTables`；UI通过`TryGetPanelConfig`读取只读配置视图

后续员工、房间、科技等玩法表可增加独立的GameTables导出目标与配置子服务，本次没有预先创建空的玩法架构
