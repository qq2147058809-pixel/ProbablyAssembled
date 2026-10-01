param([string]$GameDir = '')

# 唯一受支持的编译入口。不要改用 dotnet build / MSBuild，也不要为此下载 SDK。
# 日常基线/开发构建可直接运行；正式身份冻结后的最终构建请先阅读 RELEASE_GUIDE.md。

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $projectDir 'src'
$outputDir = Join-Path $projectDir 'bin\Release\net6.0'
$outputDll = Join-Path $outputDir 'ProbablyAssembled.dll'
$englishPatchSourceDir = Join-Path $projectDir 'EnglishPatch'
$englishPatchOutputDll = Join-Path $outputDir 'ProbablyAssembledEnglishPatch.dll'

. (Join-Path $projectDir 'scripts\GamePath.ps1')
$GameDir = Resolve-ProbablyStolenGameDir -ExplicitGameDir $GameDir -ProjectDir $projectDir

$dotnetRoot = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Join-Path $env:ProgramFiles 'dotnet' }
$runtimeRoot = Join-Path $dotnetRoot 'shared\Microsoft.NETCore.App'
$runtimeDir = Get-ChildItem -LiteralPath $runtimeRoot -Directory |
    Where-Object { $_.Name -like '6.*' } |
    Sort-Object { [version]$_.Name } -Descending |
    Select-Object -First 1 -ExpandProperty FullName
$melonDir = Join-Path $GameDir 'MelonLoader\net6'
$interopDir = Join-Path $GameDir 'MelonLoader\Il2CppAssemblies'

$required = @(
    (Join-Path $melonDir 'MelonLoader.dll'),
    (Join-Path $melonDir '0Harmony.dll'),
    (Join-Path $melonDir 'Il2CppInterop.Runtime.dll'),
    (Join-Path $interopDir 'Assembly-CSharp.dll'),
    (Join-Path $interopDir 'Il2Cppmscorlib.dll'),
    (Join-Path $interopDir 'UnityEngine.CoreModule.dll'),
    (Join-Path $interopDir 'UnityEngine.ImageConversionModule.dll'),
    (Join-Path $interopDir 'UnityEngine.UI.dll'),
    (Join-Path $interopDir 'UnityEngine.IMGUIModule.dll')
)
foreach ($path in $required) {
    if (-not (Test-Path -LiteralPath $path)) { throw "缺少编译依赖：$path" }
}

Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
$parseOptions = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::Default.WithLanguageVersion(
    [Microsoft.CodeAnalysis.CSharp.LanguageVersion]::Latest)
[Microsoft.CodeAnalysis.SyntaxTree[]]$syntaxTrees = @(
    Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' -File -Recurse | ForEach-Object {
        $text = [System.IO.File]::ReadAllText($_.FullName)
        [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
            $text, $parseOptions, $_.FullName)
    }
)

$managedRuntimeAssemblies = Get-ChildItem -LiteralPath $runtimeDir -Filter '*.dll' -File | Where-Object {
    try { [void][System.Reflection.AssemblyName]::GetAssemblyName($_.FullName); $true } catch { $false }
} | ForEach-Object FullName
$referencePaths = @(
    $managedRuntimeAssemblies
    (Join-Path $melonDir 'MelonLoader.dll')
    (Join-Path $melonDir '0Harmony.dll')
    (Join-Path $melonDir 'Il2CppInterop.Runtime.dll')
    (Join-Path $interopDir 'Assembly-CSharp.dll')
    (Join-Path $interopDir 'Il2Cppmscorlib.dll')
    (Join-Path $interopDir 'UnityEngine.CoreModule.dll')
    (Join-Path $interopDir 'UnityEngine.ImageConversionModule.dll')
    (Join-Path $interopDir 'UnityEngine.UI.dll')
    (Join-Path $interopDir 'UnityEngine.IMGUIModule.dll')
)
[Microsoft.CodeAnalysis.MetadataReference[]]$references = @(
    $referencePaths | Sort-Object -Unique | ForEach-Object {
        [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_)
    }
)

$options = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new(
    [Microsoft.CodeAnalysis.OutputKind]::DynamicallyLinkedLibrary)
$options = $options.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release)
$options = $options.WithNullableContextOptions([Microsoft.CodeAnalysis.NullableContextOptions]::Enable)
$compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'ProbablyAssembled', $syntaxTrees, $references, $options)

$assetFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'assets') -Filter '*.png' -File -Recurse)
$duplicateNames = @($assetFiles | Group-Object Name | Where-Object Count -gt 1)
if ($duplicateNames.Count -gt 0) {
    throw ('assets 中存在同名 PNG：' + (($duplicateNames | ForEach-Object Name) -join ', '))
}
[Microsoft.CodeAnalysis.ResourceDescription[]]$resources = @(
    $assetFiles | ForEach-Object {
        $assetPath = $_.FullName
        $provider = [System.Func[System.IO.Stream]]({
            [System.IO.File]::OpenRead($assetPath)
        }.GetNewClosure())
        $resourceName = 'ProbablyAssembled.Assets.' + $_.Name
        [Microsoft.CodeAnalysis.ResourceDescription]::new($resourceName, $provider, $true)
    }
)

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$stream = [System.IO.File]::Open($outputDll, [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
try { $result = $compilation.Emit($stream, $null, $null, $null, $resources) } finally { $stream.Dispose() }
foreach ($diagnostic in $result.Diagnostics) {
    if ($diagnostic.Severity -ne [Microsoft.CodeAnalysis.DiagnosticSeverity]::Hidden) {
        Write-Host $diagnostic.ToString()
    }
}
if (-not $result.Success) { throw '编译失败。' }
Write-Host "深空装机 / Probably Assembled 编译成功：$outputDll"

# The optional English patch is a separate MelonLoader mod. It only selects the
# English strings already defined by the base mod; it does not change the game's locale.
[Microsoft.CodeAnalysis.SyntaxTree[]]$englishPatchSyntaxTrees = @(
    Get-ChildItem -LiteralPath $englishPatchSourceDir -Filter '*.cs' -File -Recurse | ForEach-Object {
        $text = [System.IO.File]::ReadAllText($_.FullName)
        [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText(
            $text, $parseOptions, $_.FullName)
    }
)
[Microsoft.CodeAnalysis.MetadataReference[]]$englishPatchReferences = @(
    $managedRuntimeAssemblies
    (Join-Path $melonDir 'MelonLoader.dll')
) | Sort-Object -Unique | ForEach-Object {
    [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile($_)
}
$englishPatchCompilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create(
    'ProbablyAssembledEnglishPatch', $englishPatchSyntaxTrees, $englishPatchReferences, $options)
$englishPatchStream = [System.IO.File]::Open($englishPatchOutputDll, [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
try { $englishPatchResult = $englishPatchCompilation.Emit($englishPatchStream) } finally { $englishPatchStream.Dispose() }
foreach ($diagnostic in $englishPatchResult.Diagnostics) {
    if ($diagnostic.Severity -ne [Microsoft.CodeAnalysis.DiagnosticSeverity]::Hidden) {
        Write-Host $diagnostic.ToString()
    }
}
if (-not $englishPatchResult.Success) { throw '英文补丁编译失败。' }
Write-Host "英文补丁编译成功：$englishPatchOutputDll"
Write-Host "游戏目录：$GameDir"
