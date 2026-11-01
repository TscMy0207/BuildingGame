# Master Audio接入说明

## 接入范围

- 来源：用户提供的Master Audio 2024 AAA Sound v1.0.4.unitypackage
- 原包SHA-256：eea7689758263143c77a295f0b05d48f6f12e61925168c222a62ae555e357ec7
- Unity版本：2022.3.62f2
- 导入插件运行代码、编辑器工具、内部模板、默认资源及图标，未导入ExampleScenes
- 保留第三方资源原有GUID，原包导入文件内容不变
- 新增DarkTonic.MasterAudio.asmdef，引用UnityEngine.UI；Framework引用该运行程序集
- 现有IAudioSettings、audioSettings存档段及设置变化事件保留，未接入插件PersistentAudioSettings持久化API
- IAudioService原来的AudioClip接口替换为声音组与播放列表名称接口，接入时没有旧接口业务调用者

## 文件与启动关系

| 文件 | 用途 |
| --- | --- |
| Assets/Scripts/Game/Framework/Audio/AudioService.cs | 播放入口、初始化、设置同步和释放 |
| Assets/Scripts/Game/Framework/Audio/AudioServiceHost.cs | 宿主组件及插件引用 |
| Assets/GameData/Audio/AudioRoot.prefab | 项目音频预制体，应在这里编辑声音组和列表 |
| Assets/Settings/BundleCollectorSetting.asset | FrameworkAudio资源采集包 |
| Assets/Scripts/Game/Framework/Composition/FrameworkSettings.cs | 本地包清单和宿主加载地址 |
| Assets/Plugins/DarkTonic/MasterAudio | 第三方插件运行资源 |
| Assets/Plugins/Editor/DarkTonic/MasterAudio | 插件编辑器工具 |
| Assets/Tests/PlayMode/Audio | 实际插件与YooAsset联动测试 |

FrameworkComposition继续注册AudioService；资源服务和事件服务先启动，AudioService通过YooAsset加载FrameworkAudio/Assets/GameData/Audio/AudioRoot.prefab，等待Master Audio声音组与PlaylistController完成初始化后对外提供播放能力

宿主层级：

```text
AudioRoot                         AudioServiceHost，统一DontDestroyOnLoad
├─ MasterAudio                    插件MasterAudio组件
│  └─ 自定义声音组                各声音组包含其音频变体
└─ FrameworkMusic                 插件PlaylistController及双AudioSource
```

MasterAudio的直接子节点必须用于声音组，不能把音乐控制器放在MasterAudio下面
插件persistBetweenScenes保持关闭，由AudioRoot统一持久化和销毁
不要向其他场景手动添加第二套MasterAudio或FrameworkMusic
场景需有一个有效AudioListener，音频服务不额外创建Listener

## 添加实际游戏音频

1. 打开Assets/GameData/Audio/AudioRoot.prefab
2. 选择MasterAudio节点，在插件Inspector中拖入音频创建Sound Group，按用途命名，例如UI_Click、Build_Place
3. 在同一MasterAudio组件的Playlists区域创建列表，例如BGM_MainMenu、BGM_Office，并添加音乐片段
4. 通过声音组配置变体、随机音高、音量及并发限制；音乐通过Playlist和FrameworkMusic配置循环、随机与过渡
5. 保存预制体，在框架启动完成后调用IAudioService

当前没有虚构的游戏声音组或音乐列表，需要添加正式素材后使用
默认按2D播放，适合界面和经营游戏背景音乐；未来需要场景位置音效时再扩展对应3D播放接口

建议音频放入Assets/GameData/Audio/Clips，使用插件Audio Clip直接引用模式
YooAsset会收集预制体引用依赖，不需要启用Master Audio的Addressables支持，也不需要为同一片段再写一套加载缓存

## 业务调用

业务类通过现有VContainer依赖注入获取IAudioService和IAudioSettings：

```csharp
audioService.PlaySfx("UI_Click");
audioService.PlaySfx("Build_Place", 0.8f);
audioService.PlayMusic("BGM_Office");
audioService.StopMusic();

audioSettings.SetMusicVolume(0.5f);
audioSettings.SetSfxVolume(0.8f);
audioSettings.SetMusicEnabled(false);
audioSettings.SetSfxEnabled(true);
```

- PlaySfx返回false：服务未就绪或已释放、音效关闭、声音组不存在、参数无效，或者插件并发与播放概率规则拒绝
- PlayMusic返回false：服务未就绪或已释放，或者列表不存在或没有歌曲
- PlayMusic返回true表示接受请求，实际播放仍取决于歌曲素材有效性及插件调度
- 关闭音乐会停止播放，重新开启会重播最近一次有效请求
- 关闭期间调用PlayMusic只更新待播放列表
- StopMusic清除待播放请求，之后切换音乐开关不会自动恢复已明确停止的音乐
- 无效名称请求不会覆盖此前有效的音乐请求
- IAudioSettings仍是设置唯一来源，避免业务直接修改MasterAudio全局音量造成设置不一致
- 声音组名和播放列表名大小写应与预制体配置保持一致

## 生命周期及失败处理

- 启动前取消不创建宿主
- 资源加载期间取消或服务释放后，加载完成的资源引用会归还，不再实例化宿主
- 场景中已有MasterAudio时拒绝启动，不销毁既有实例
- 宿主缺少组件引用或插件超过10秒仍未就绪时启动失败并清理本次对象
- Dispose取消事件订阅、停止音频、销毁AudioRoot并归还资源引用，可重复调用
- 只支持同一框架生命周期内启动一次；需要重建时创建新的容器和服务实例

## 编辑器与发布

编辑器使用项目现有YooAsset EditorSimulate模式
发布时仍使用现有Offline模式，需要使用YooAsset构建并内置FrameworkConfig和FrameworkAudio两个资源包
仅成功编译脚本不能替代离线资源包构建及完整游戏验收

项目Main场景与UIRoot等业务入口还需按游戏开发进度配置；本次测试只启动资源、事件与音频相关服务，不代表整个游戏启动流程已经完成

## 验证方法

Unity Test Runner的PlayMode中运行MasterAudioServiceTests：

- NativePlaybackAndSettings：通过真实资源包创建宿主，实际播放声音组和音乐，验证音量、静音、恢复、释放和事件解绑
- StopMusicCancelsDeferredPlayback：关闭音乐期间请求播放、重新开启，以及显式停止后的恢复边界
- CancellationDuringLoadLeavesNoHost：加载期间取消后的对象清理
- DuplicateServiceDoesNotDestroyExistingHost：重复服务启动被拒绝，既有音乐继续播放

测试片段运行时在内存创建并保持静音，不添加游戏示例音频
结果保存在Logs/MasterAudio-PlayMode-results.xml
销毁YooAsset测试包时可能出现DownloadSchedulerOperation已中止警告，表示测试退出时停止包调度器，不是音频播放异常

## 本次验证结果

2026-10-31，Unity 2022.3.62f2：

- 4项音频PlayMode测试与1项配置资源回归测试全部通过
- AudioRoot及其37项依赖路径检查通过，未发现丢失脚本
- Windows玩家脚本编译通过，共30个程序集
- 已核对本次导入文件与原包内容一致，未修改第三方源码
- 未进行听感验收、完整游戏启动验收或离线资源包打包运行验收
