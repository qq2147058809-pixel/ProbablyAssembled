param([string]$GameDir = '', [switch]$UpdateEnglishPatch)

# 唯一受支持的编译入口。不要改用 dotnet build / MSBuild，也不要为此下载 SDK。
# 日常基线/开发构建可直接运行；正式身份冻结后的最终构建请先阅读 RELEASE_GUIDE.md。

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $projectDir 'scripts/BuildOperation.ps1')
$buildLease = Enter-PCExpansionBuildOperation -ProjectDir $projectDir
try {
$sourceDir = Join-Path $projectDir 'src'
$outputDir = Join-Path $projectDir 'bin\Release\net6.0'
$outputDll = Join-Path $outputDir 'PCExpansion.dll'
$englishPatchSourceDir = Join-Path $projectDir 'EnglishPatch'
$englishPatchOutputDll = Join-Path $outputDir 'PCExpansionEnglishPatch.dll'

. (Join-Path $projectDir 'scripts\GamePath.ps1')
$GameDir = Resolve-ProbablyStolenGameDir -ExplicitGameDir $GameDir -ProjectDir $projectDir
. (Join-Path $projectDir 'scripts\ValidateTranslations.ps1')
Assert-PCExpansionTranslations -ProjectDir $projectDir -UpdateEnglishPatch:$UpdateEnglishPatch
. (Join-Path $projectDir 'scripts\ValidateChineseManual.ps1')
Assert-PCExpansionChineseManual -ProjectDir $projectDir
if ($UpdateEnglishPatch) {
    . (Join-Path $projectDir 'scripts\ValidateComputerManual.ps1')
    Assert-PCExpansionComputerManual -ProjectDir $projectDir
}

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
    (Join-Path $interopDir 'Assembly-CSharp-firstpass.dll'),
    (Join-Path $interopDir 'Il2Cppmscorlib.dll'),
    (Join-Path $interopDir 'Il2CppSystem.Core.dll'),
    (Join-Path $interopDir 'Il2CppSystem.dll'),
    (Join-Path $interopDir 'UnityEngine.CoreModule.dll'),
    (Join-Path $interopDir 'UnityEngine.ImageConversionModule.dll'),
    (Join-Path $interopDir 'UnityEngine.JSONSerializeModule.dll'),
    (Join-Path $interopDir 'UnityEngine.UI.dll'),
    (Join-Path $interopDir 'UnityEngine.IMGUIModule.dll'),
    (Join-Path $interopDir 'Unity.TextMeshPro.dll'),
    (Join-Path $interopDir 'UnityEngine.UIModule.dll'),
    (Join-Path $interopDir 'UnityEngine.InputLegacyModule.dll'),
    (Join-Path $interopDir 'UnityEngine.TextRenderingModule.dll')
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
    (Join-Path $interopDir 'Assembly-CSharp-firstpass.dll')
    (Join-Path $interopDir 'Il2Cppmscorlib.dll')
    (Join-Path $interopDir 'Il2CppSystem.Core.dll')
    (Join-Path $interopDir 'Il2CppSystem.dll')
    (Join-Path $interopDir 'UnityEngine.CoreModule.dll')
    (Join-Path $interopDir 'UnityEngine.PhysicsModule.dll')
    (Join-Path $interopDir 'UnityEngine.Physics2DModule.dll')
    (Join-Path $interopDir 'UnityEngine.ImageConversionModule.dll')
    (Join-Path $interopDir 'UnityEngine.JSONSerializeModule.dll')
    (Join-Path $interopDir 'UnityEngine.UI.dll')
    (Join-Path $interopDir 'UnityEngine.IMGUIModule.dll')
    (Join-Path $interopDir 'Unity.TextMeshPro.dll')
    (Join-Path $interopDir 'UnityEngine.UIModule.dll')
    (Join-Path $interopDir 'UnityEngine.InputLegacyModule.dll')
    (Join-Path $interopDir 'UnityEngine.TextRenderingModule.dll')
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
    'PCExpansion', $syntaxTrees, $references, $options)

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
        $resourceName = 'PCExpansion.Assets.' + $_.Name
        [Microsoft.CodeAnalysis.ResourceDescription]::new($resourceName, $provider, $true)
    }
    $chineseCatalogPath = Join-Path $projectDir 'src\Infrastructure\translations.zh.json'
    $chineseProvider = [System.Func[System.IO.Stream]]({ [IO.File]::OpenRead($chineseCatalogPath) }.GetNewClosure())
    [Microsoft.CodeAnalysis.ResourceDescription]::new('PCExpansion.Localization.zh.json', $chineseProvider, $true)
    $chineseManualPath = Join-Path $projectDir 'src\Infrastructure\computer_manual.zh.json'
    $chineseManualProvider = [System.Func[System.IO.Stream]]({ [IO.File]::OpenRead($chineseManualPath) }.GetNewClosure())
    [Microsoft.CodeAnalysis.ResourceDescription]::new('PCExpansion.Manual.zh.json', $chineseManualProvider, $true)
)

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$stream = [System.IO.MemoryStream]::new()
try {
    $result = $compilation.Emit($stream, $null, $null, $null, $resources)
    $mainBytes = $stream.ToArray()
} finally { $stream.Dispose() }
foreach ($diagnostic in $result.Diagnostics) {
    if ($diagnostic.Severity -ne [Microsoft.CodeAnalysis.DiagnosticSeverity]::Hidden) {
        Write-Host $diagnostic.ToString()
    }
}
if (-not $result.Success) { throw '编译失败。' }

# 英文补丁独立保存和提供译文，不把翻译资源嵌入中文本体。
if ($UpdateEnglishPatch) {
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
    'PCExpansionEnglishPatch', $englishPatchSyntaxTrees, $englishPatchReferences, $options)
$englishPatchStream = [System.IO.MemoryStream]::new()
[Microsoft.CodeAnalysis.ResourceDescription[]]$englishPatchResources = @(
    foreach ($entry in @(
        @{ Name = 'PCExpansionEnglishPatch.Localization.en.json'; File = 'translations.en.json' },
        @{ Name = 'PCExpansionEnglishPatch.Localization.contract.json'; File = 'translation-contract.json' },
        @{ Name = 'PCExpansionEnglishPatch.Manual.en.json'; File = 'manual\computer_manual.en.json' }
    )) {
        $translationPath = Join-Path $englishPatchSourceDir $entry.File
        $translationProvider = [System.Func[System.IO.Stream]]({ [IO.File]::OpenRead($translationPath) }.GetNewClosure())
        [Microsoft.CodeAnalysis.ResourceDescription]::new($entry.Name, $translationProvider, $true)
    }
)
try {
    $englishPatchResult = $englishPatchCompilation.Emit($englishPatchStream, $null, $null, $null, $englishPatchResources)
    $englishBytes = $englishPatchStream.ToArray()
} finally { $englishPatchStream.Dispose() }
foreach ($diagnostic in $englishPatchResult.Diagnostics) {
    if ($diagnostic.Severity -ne [Microsoft.CodeAnalysis.DiagnosticSeverity]::Hidden) {
        Write-Host $diagnostic.ToString()
    }
}
if (-not $englishPatchResult.Success) { throw '英文补丁编译失败。' }
}

# 集中更新时双编译均成功后发布；默认只发布本体，不触碰已有英文产物。
$buildToken = [Guid]::NewGuid().ToString('N')
$publications = @(
    @{ Target = $outputDll; Bytes = $mainBytes }
)
if ($UpdateEnglishPatch) { $publications += @{ Target = $englishPatchOutputDll; Bytes = $englishBytes } }
$committed = [System.Collections.Generic.List[object]]::new()
try {
    foreach ($entry in $publications) {
        $entry.Stage = $entry.Target + '.' + $buildToken + '.tmp'
        $entry.Backup = $entry.Target + '.' + $buildToken + '.bak'
        $entry.Existed = [System.IO.File]::Exists($entry.Target)
        [System.IO.File]::WriteAllBytes($entry.Stage, $entry.Bytes)
    }
    foreach ($entry in $publications) {
        if ($entry.Existed) {
            [System.IO.File]::Replace($entry.Stage, $entry.Target, $entry.Backup)
        } else {
            [System.IO.File]::Move($entry.Stage, $entry.Target)
        }
        $committed.Add($entry)
    }
} catch {
    for ($i = $committed.Count - 1; $i -ge 0; $i--) {
        $entry = $committed[$i]
        if ($entry.Existed) {
            [System.IO.File]::Move($entry.Backup, $entry.Target, $true)
        } else {
            [System.IO.File]::Delete($entry.Target)
        }
    }
    throw
} finally {
    foreach ($entry in $publications) {
        if ($entry.Stage) { [System.IO.File]::Delete($entry.Stage) }
        # If rollback itself failed, keep the backup for manual recovery.
        if ($entry.Backup -and $committed.Count -eq $publications.Count) {
            [System.IO.File]::Delete($entry.Backup)
        }
    }
}
Write-Host "深空装机 / PC expansion 编译成功：$outputDll"
if ($UpdateEnglishPatch) { Write-Host "英文补丁编译成功：$englishPatchOutputDll" }
else { Write-Host '英文补丁延后更新：本次仅构建中文本体，已有英文 DLL 保持原样。' }
Write-Host "游戏目录：$GameDir"
} finally { $buildLease.Dispose() }

