#Requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$ValidateOnly,
    [switch]$CheckPublished,
    [string]$ConfigRoot = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
$OutputEncoding = [Console]::OutputEncoding
if ([string]::IsNullOrEmpty($ConfigRoot)) { $ConfigRoot = Split-Path -Parent $PSScriptRoot }
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$configRootPath = [IO.Path]::GetFullPath($ConfigRoot)
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ('BuildingGame-Luban-' + [Guid]::NewGuid().ToString('N'))
$codeStage = Join-Path $stageRoot 'Code'
$dataStage = Join-Path $stageRoot 'Data'
$jsonStage = Join-Path $stageRoot 'Json'
$utf8 = New-Object Text.UTF8Encoding($false)
$lockPath = Join-Path $PSScriptRoot '.generation.lock'
$generationLock = $null

try {
    $generationLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    $dotnetPath = if ($null -ne $dotnetCommand) { $dotnetCommand.Source } else { Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'dotnet/dotnet.exe' }
    if (!(Test-Path -LiteralPath $dotnetPath)) { throw '未找到.NET SDK 请安装.NET8 SDK后重新导表' }
    $toolPath = Join-Path $projectRoot 'Tools/Luban/Luban.dll'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($toolPath).ProductVersion
    if ($version -ne '5.1.0+df34215d2e22035073d1af83d44e3ac0744dc3b3') { throw 'Luban生成器版本必须为固定的5.1.0发行版本' }
    & $dotnetPath $toolPath --conf (Join-Path $configRootPath 'luban.conf') -t framework -c cs-bin -d bin -d json --strict --locale zh `
        -x "outputCodeDir=$codeStage" -x "bin.outputDataDir=$dataStage" -x "json.outputDataDir=$jsonStage" -x bin.outputDataFileExtension=bytes
    if ($LASTEXITCODE -ne 0) { throw 'Luban导出或数据校验失败 原有产物未修改' }

    # 结构版本取自实际生成代码 不随Excel中的纯数值改动变化
    $schemaText = ((Get-ChildItem -LiteralPath $codeStage -Filter '*.cs' | Sort-Object Name | ForEach-Object {
        $_.Name + "`n" + [IO.File]::ReadAllText($_.FullName).Replace("`r`n", "`n")
    }) -join "`n")
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $schemaHash = ([BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes($schemaText)))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
    $schemaCode = @"
// 此文件由ConfigTables/Generation/Generate.ps1生成 请勿手工修改
namespace BuildingGame.Config.Framework
{
    public partial class FrameworkTables
    {
        public const string SchemaHash = "$schemaHash";
    }
}
"@
    [IO.File]::WriteAllText((Join-Path $codeStage 'FrameworkTables.Schema.cs'), $schemaCode, $utf8)
    $files = @(Get-ChildItem -LiteralPath $dataStage -Filter '*.bytes' | Sort-Object Name | ForEach-Object {
        [ordered]@{ Name = $_.BaseName; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $manifest = [ordered]@{ FormatVersion = 1; SchemaHash = $schemaHash; Files = $files }
    [IO.File]::WriteAllText((Join-Path $dataStage 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5), $utf8)

    $validator = Join-Path $projectRoot 'Tools/ConfigTablesValidator/ConfigTablesValidator.csproj'
    & $dotnetPath build $validator "-p:GeneratedConfigDir=$codeStage" "-p:BaseIntermediateOutputPath=$stageRoot/obj/" -o (Join-Path $stageRoot 'Validator') --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw '生成代码编译失败 原有产物未修改' }
    & $dotnetPath (Join-Path $stageRoot 'Validator/ConfigTablesValidator.dll') $dataStage $projectRoot
    if ($LASTEXITCODE -ne 0) { throw '二进制或业务配置验证失败 原有产物未修改' }
    if ($ValidateOnly) { Write-Host '配置校验完成 未更新Unity产物'; return }

    # 所有校验完成后才发布 两个目录发生写入异常时恢复本次涉及的旧文件
    $targets = @(
        @{ Source = $codeStage; Destination = (Join-Path $projectRoot 'Assets/Scripts/Game/Framework/ConfigTables/Generated') },
        @{ Source = $dataStage; Destination = (Join-Path $projectRoot 'Assets/GameData/ConfigTables/Framework') }
    )
    if ($CheckPublished) {
        foreach ($target in $targets) {
            foreach ($sourceFile in Get-ChildItem -LiteralPath $target.Source -File) {
                $destination = Join-Path $target.Destination $sourceFile.Name
                if (!(Test-Path -LiteralPath $destination) -or
                    (Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $sourceFile.FullName).Hash) {
                    throw "配置源文件与已发布产物不一致 请重新导表:$destination"
                }
            }
        }
        Write-Host '已发布配置与源文件一致'
        return
    }
    $rollback = New-Object Collections.Generic.List[object]
    try {
        foreach ($target in $targets) {
            New-Item -ItemType Directory -Path $target.Destination -Force | Out-Null
            foreach ($sourceFile in Get-ChildItem -LiteralPath $target.Source -File) {
                $destination = Join-Path $target.Destination $sourceFile.Name
                $bytes = [IO.File]::ReadAllBytes($sourceFile.FullName)
                if ((Test-Path -LiteralPath $destination) -and
                    (Get-FileHash -LiteralPath $destination).Hash -eq (Get-FileHash -LiteralPath $sourceFile.FullName).Hash) { continue }
                $previous = if (Test-Path -LiteralPath $destination) { [IO.File]::ReadAllBytes($destination) } else { $null }
                $rollback.Add(@{ Path = $destination; Previous = $previous })
                [IO.File]::WriteAllBytes($destination, $bytes)
            }
        }
    }
    catch {
        for ($index = $rollback.Count - 1; $index -ge 0; $index--) {
            $entry = $rollback[$index]
            if ($null -eq $entry.Previous) { [IO.File]::Delete($entry.Path) }
            else { [IO.File]::WriteAllBytes($entry.Path, $entry.Previous) }
        }
        throw
    }
    Write-Host 'Luban配置导出完成 已保留现有资源meta与GUID'
    Write-Host '请在Unity刷新资源 后续删除表时通过Unity删除对应的过期生成文件'
}
catch {
    Write-Error "配置生成失败:$($_.Exception.Message)"
    exit 1
}
finally {
    if ($null -ne $generationLock) { $generationLock.Dispose() }
    # 临时目录保留在系统临时区 便于定位失败产物 不进入项目版本管理
}
