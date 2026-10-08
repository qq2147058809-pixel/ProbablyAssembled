function Resolve-ProbablyStolenGameDir {
    param(
        [string]$ExplicitGameDir,
        [string]$ProjectDir
    )

    $candidate = $ExplicitGameDir
    $source = '-GameDir 参数'

    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $candidate = $env:PROBABLY_STOLEN_GAME_DIR
        $source = 'PROBABLY_STOLEN_GAME_DIR 环境变量'
    }

    if ([string]::IsNullOrWhiteSpace($candidate)) {
        $settingsPath = Join-Path $ProjectDir 'local.settings.json'
        if (Test-Path -LiteralPath $settingsPath) {
            try {
                $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
                $candidate = [string]$settings.GameDir
                $source = $settingsPath
            }
            catch {
                throw "无法读取本地配置 $settingsPath。请检查JSON格式，或从local.settings.example.json重新创建local.settings.json并填写GameDir/StorageRoot。$($_.Exception.Message)"
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($candidate)) {
        throw @'
尚未配置 Probably Stolen 游戏目录。请选择一种方式：
1. 复制local.settings.example.json为local.settings.json，填写GameDir和外部StorageRoot
2. 临时指定：.\build.ps1 -GameDir "你的游戏目录"
3. 设置环境变量 PROBABLY_STOLEN_GAME_DIR
'@
    }

    try { $resolved = (Resolve-Path -LiteralPath $candidate -ErrorAction Stop).Path }
    catch { throw "游戏目录不存在（来源：$source）：$candidate" }

    $assemblyPath = Join-Path $resolved 'MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll'
    if (-not (Test-Path -LiteralPath $assemblyPath)) {
        throw "目录不是已初始化的 Probably Stolen MelonLoader 游戏目录（来源：$source）：$resolved"
    }
    return $resolved.TrimEnd('\', '/')
}
