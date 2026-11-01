# 导入已授权的商店插件后恢复本项目的程序集接入配置及GUID
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$definitions = @(
    @{ Name = 'DarkTonic.MasterAudio.asmdef'; Target = 'Assets/Plugins/DarkTonic/MasterAudio/Scripts' },
    @{ Name = 'EasySave3.asmdef'; Target = 'Assets/Plugins/Easy Save 3' },
    @{ Name = 'EasySave3Editor.asmdef'; Target = 'Assets/Plugins/Easy Save 3/Editor' }
)

foreach ($definition in $definitions) {
    $targetDirectory = Join-Path $projectRoot $definition.Target
    if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
        throw "尚未导入所需商店插件，缺少目录：$($definition.Target)"
    }
    foreach ($suffix in @('', '.meta')) {
        $source = Join-Path $PSScriptRoot ($definition.Name + $suffix)
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "程序集恢复模板缺失：$($definition.Name)$suffix"
        }
    }
}

foreach ($definition in $definitions) {
    $targetDirectory = Join-Path $projectRoot $definition.Target
    foreach ($suffix in @('', '.meta')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ($definition.Name + $suffix)) `
            -Destination (Join-Path $targetDirectory ($definition.Name + $suffix)) -Force
    }
    Write-Host "已恢复程序集配置及GUID：$($definition.Name)"
}
Write-Host '请回到Unity等待资源刷新与脚本编译'
