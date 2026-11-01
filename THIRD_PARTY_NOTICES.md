# 第三方依赖与分发范围

根目录MIT许可证适用于项目自有代码和文档，不替代或覆盖第三方许可证
第三方源码、二进制、字体与资源的版权归各自权利人，使用时保留其版权和许可声明

## 不随公开仓库分发的商店插件

| 组件 | 当前接入来源 | 恢复目录 |
| --- | --- | --- |
| Master Audio | Master Audio 2024 AAA Sound v1.0.4.unitypackage，插件实际版本以导入包为准 | `Assets/Plugins/DarkTonic`、`Assets/Plugins/Editor/DarkTonic` |
| Easy Save | Easy Save - The Complete Save Data Serializer System 3.5.6.unitypackage | `Assets/Plugins/Easy Save 3` |
| RelationsInspector及配套资源 | Master Audio包附带的编辑器工具 | `Assets/Plugins/RelationsInspector`、`Assets/Plugins/Editor/RelationsInspector` |

Master Audio配套的`Assets/Editor Default Resources/MasterAudio`、`Assets/Gizmos/MasterAudio`和`Assets/Resources/MasterAudio`也已排除
这些文件仍保留在开发者本机，克隆者需自行获取授权并导入，不能从本仓库获得商店插件源码
公开仓库中的音频与存档适配代码依赖这些组件，尚未恢复时不能通过完整项目编译

项目自定义的三份程序集定义与对应GUID保存在`Tools/ThirdPartyAssemblyDefinitions`
导入插件后执行其中的`Restore.ps1`，保留插件资源原有GUID，并恢复Game程序集需要的`DarkTonic.MasterAudio`、`EasySave3`与`EasySave3Editor`
该目录仅包含项目程序集接入配置，不包含商店插件实现

Unity Asset Store资源使用与分发范围参见[官方条款](https://unity.com/legal/as-terms)

## 随项目保存或通过包管理器恢复的依赖

| 组件 | 来源与许可位置 |
| --- | --- |
| UniTask | `Packages/com.cysharp.unitask/LICENSE`，MIT |
| VContainer | `Packages/jp.hadashikick.vcontainer/LICENSE`，MIT |
| YooAsset | `Packages/com.tuyoogame.yooasset/LICENSE.md`，MIT |
| Luban运行时与导表工具 | `Packages/com.code-philosophy.luban/LICENSE`、`Tools/Luban/LICENSE`及上游依赖各自许可 |
| ZLogger与CsprojModifier | 各嵌入包内的`LICENSE`及`Assets/Packages`中的组件声明，MIT |
| NuGetForUnity | `Packages/com.github-glitchenzo.nugetforunity`内的许可文件 |
| MessagePipe及VContainer适配 | `Packages/manifest.json`固定Git版本，由UPM恢复，遵循上游许可证 |
| KitWright Unity MCP | `Packages/manifest.json`声明的Git依赖，由UPM恢复，遵循上游许可证 |
| Scriptable Build Pipeline | `Packages/com.unity.scriptablebuildpipeline/LICENSE.md`，Unity Companion License，仅用于Unity相关项目 |
| 其他Unity包 | 由UPM恢复，遵循各包随附许可 |
| NuGet依赖 | 版本见`Assets/packages.config`，各包许可见`Assets/Packages`内随附的许可证、包元数据及上游项目 |

Luban导表发行包包含多种第三方工具库，根目录MIT许可证不对这些库重新授权；使用与再分发前应查阅对应上游许可
项目引用《Mad Games Tycoon 2》作为玩法设计参考，不包含该游戏源码或资源，也不授予其名称、商标或素材的使用权
