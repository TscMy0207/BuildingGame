using Cinemachine;
using UnityEngine;

/// <summary>
/// 统一管理场景摄像机宿主及其Cinemachine镜头 不处理玩家输入或玩法规则
/// 所有操作均应在Unity主线程调用
/// </summary>
public interface ICameraService
{
    /// <summary>
    /// 服务已启动且当前宿主及输出组件可用
    /// </summary>
    bool IsReady { get; }

    /// <summary>
    /// 当前宿主的实际输出摄像机 未绑定可用宿主时为空
    /// </summary>
    Camera OutputCamera { get; }

    /// <summary>
    /// 服务请求的目标镜头标识 混合或Timeline控制期间不代表实际输出镜头
    /// </summary>
    string CurrentCameraId { get; }

    /// <summary>
    /// 绑定场景宿主并注册其预设镜头 替换前恢复旧宿主及镜头的原始设置
    /// </summary>
    /// <param name="host">可用的场景摄像机宿主</param>
    void BindHost(CameraServiceHost host);

    /// <summary>
    /// 仅当参数为当前宿主时解除绑定 不销毁场景对象
    /// </summary>
    /// <param name="host">需要解绑的宿主</param>
    void UnbindHost(CameraServiceHost host);

    /// <summary>
    /// 注册动态顶层镜头 标识和组件均不能重复 生命周期由调用方管理
    /// </summary>
    /// <param name="id">当前宿主内唯一的非空标识</param>
    /// <param name="camera">与输出摄像机层遮罩匹配的顶层虚拟镜头</param>
    void RegisterCamera(string id, CinemachineVirtualCameraBase camera);

    /// <summary>
    /// 注销镜头并恢复原始优先级及目标 当前镜头注销后尝试选择其他可用镜头
    /// </summary>
    /// <param name="id">已注册的镜头标识</param>
    /// <returns>是否存在并注销了该镜头</returns>
    bool UnregisterCamera(string id);

    /// <summary>
    /// 请求切换到已启用的注册镜头 过渡遵循Brain的默认和自定义混合设置
    /// </summary>
    /// <param name="id">目标镜头标识</param>
    /// <returns>是否接受切换请求 未就绪或镜头不可用时返回false</returns>
    bool SwitchTo(string id);

    /// <summary>
    /// 设置镜头跟随及注视目标 传入空值可清除对应目标
    /// </summary>
    /// <param name="id">镜头标识</param>
    /// <param name="follow">跟随目标</param>
    /// <param name="lookAt">注视目标</param>
    /// <returns>是否找到镜头并完成设置</returns>
    bool SetTargets(string id, Transform follow, Transform lookAt);

    /// <summary>
    /// 修改默认混合时长 自定义镜头混合及Timeline仍遵循Cinemachine自身规则
    /// </summary>
    /// <param name="seconds">大于等于零的有限秒数 零表示默认直接切换</param>
    /// <returns>是否接受有效设置</returns>
    bool SetDefaultBlendDuration(float seconds);

    /// <summary>
    /// 通过宿主的ImpulseSource发出震动 镜头需配置匹配频道的ImpulseListener
    /// </summary>
    /// <param name="strength">大于零的有限强度倍率</param>
    /// <returns>是否发出震动信号</returns>
    bool Shake(float strength = 1f);
}
