# BuildingGame

基于Unity的经营模拟游戏开发项目，目标是实现游戏公司经营、员工管理、研发与建筑布局等玩法，目前处于基础框架建设阶段

项目自有代码采用[MIT许可证](LICENSE)，第三方组件遵循各自许可证，详见[第三方依赖说明](THIRD_PARTY_NOTICES.md)

## 开发环境

- Unity2022.3.62f2
- Windows，Luban导表需要.NET8 SDK与PowerShell
- 当前使用UGUI、URP和Cinemachine2.9.7

## 当前框架

- VContainer依赖注入与模块Scope
- UniTask异步流程与定时器
- YooAsset资源管理与Luban配置表
- MessagePipe事件与ZLogger日志
- Unity内置对象池及ObjectPoolService
- UI面板与摄像机服务
- Master Audio音频适配与Easy Save存档适配
- AstrBot连接Unity MCP的编辑器配置同步工具

## 克隆与恢复依赖

1. 克隆仓库，使用指定Unity版本打开，等待UPM恢复依赖
2. 自行取得合法授权并导入Master Audio与Easy Save，版本与目录见[第三方依赖说明](THIRD_PARTY_NOTICES.md)
3. 在项目根目录执行以下命令，恢复项目自定义的第三方程序集定义及原有GUID

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File Tools/ThirdPartyAssemblyDefinitions/Restore.ps1
   ```

4. 回到Unity等待重新导入与编译
5. 配置表日常使用与校验方法见[ConfigTables/README.md](ConfigTables/README.md)

商店插件源码及配套资源不包含在公开仓库中；未恢复插件时，音频、存档适配及相关测试会产生缺少类型或程序集的编译错误
当前NuGet依赖已随项目保存，可通过NuGetForUnity按`Assets/packages.config`恢复

## 开发状态与验证边界

当前仓库提供框架及配置、存档、音频、对象池、摄像机相关测试，尚未完成可游玩的经营流程
`Main`场景、UI根资源地址及Build Settings中的启动入口仍需按后续开发计划接通，直接克隆不代表可以立即运行完整游戏
发布前还需构建并内置YooAsset资源包，编辑器模拟加载成功不等于离线发布包可用

## 项目文档

- [完整功能与验收标准](Docs/Mad_Games_Tycoon_2_完整功能与验收标准.md)
- [Master Audio接入说明](Docs/MasterAudio接入说明.md)
- [对象池服务接入说明](Docs/对象池服务接入说明.md)

## AstrBot与Unity MCP

菜单：`Tools/KitWright MCP/Sync AstrBot Configuration`

工具将当前项目的服务地址写入本机`C:\Software\AstrBot\data\mcp_server.json`，使用独立连接名`kitwright-unity-buildinggame`
配置中的令牌仅保存在本机，不应提交到仓库；同步后需重新加载或重启AstrBot
