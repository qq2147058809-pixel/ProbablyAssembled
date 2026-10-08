function Assert-PCExpansionComputerManual {
    param([string]$ProjectDir)
    $taskManualPath = Join-Path $ProjectDir 'EnglishPatch\manual\computer_manual.en.json'
    $taskStrictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    $taskBaseSource = [IO.File]::ReadAllText((Join-Path $ProjectDir 'src\Infrastructure\ComputerManualPages.cs'))
    $taskVersion = [regex]::Match($taskBaseSource, 'public const string ContractVersion = "([^"]+)";').Groups[1].Value
    if ($taskVersion -notmatch '^pcexpansion-computer-manual-v\d+$') { throw '无法识别本体手册契约。' }
    $taskChinese = $taskStrictUtf8.GetString([IO.File]::ReadAllBytes((Join-Path $ProjectDir 'src\Infrastructure\computer_manual.zh.json'))) | ConvertFrom-Json
    if ($taskChinese.contract -cne $taskVersion) { throw '中文手册与本体契约不一致。' }
    $taskRaw = $taskStrictUtf8.GetString([IO.File]::ReadAllBytes($taskManualPath))
    if ($taskRaw.Contains([char]0xFFFD) -or $taskRaw.Contains([char]0)) { throw '英文手册含损坏字符。' }
    $taskDocument = [System.Text.Json.JsonDocument]::Parse($taskRaw)
    try {
        $taskRoot = $taskDocument.RootElement
        if ($taskRoot.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw '手册根必须为对象。' }
        $taskNames = @($taskRoot.EnumerateObject() | ForEach-Object Name)
        if (($taskNames | Sort-Object) -join ',' -ne 'contract,pages') { throw '手册根字段不匹配或重复。' }
        if ($taskRoot.GetProperty('contract').GetString() -cne $taskVersion) { throw '英文手册版本契约错误。' }
        $taskPages = $taskRoot.GetProperty('pages')
        if ($taskPages.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or $taskPages.GetArrayLength() -ne 8) { throw '英文手册必须是完整八页。' }
        $taskComponentsSource = [IO.File]::ReadAllText((Join-Path $ProjectDir 'src\Items\Components.cs'))
        $taskStems = @([regex]::Matches($taskComponentsSource, 'new\("(computer_case|component_[a-z]+)"') | ForEach-Object { $_.Groups[1].Value })
        if ($taskStems.Count -ne 9) { throw '无法识别正式配件目录。' }
        $taskIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($taskStem in $taskStems) {
            foreach ($taskTier in 1..5) {
                [void]$taskIds.Add("pcrepair.${taskStem}_t$taskTier")
                [void]$taskIds.Add("pcrepair.${taskStem}_t${taskTier}_broken")
            }
        }
        $taskHeadings = @('Introduction','Opening a Case','What Can Be Repaired?','Repair and Batch Recycling','Power-on Check','Three PC Build Profiles','Build Examples','High-End Parts')
        $taskPageIndex = 0
        foreach ($taskPage in $taskPages.EnumerateArray()) {
            if ($taskPage.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw '每页必须为对象。' }
            $taskNames = @($taskPage.EnumerateObject() | ForEach-Object Name)
            if (($taskNames | Sort-Object) -join ',' -ne 'body,illustrations,title') { throw '页面字段不匹配或重复。' }
            if ($taskPage.GetProperty('title').GetString() -cne $taskHeadings[$taskPageIndex++]) { throw '英文手册标题或页序不匹配。' }
            foreach ($taskField in @('title', 'body')) {
                $taskValue = $taskPage.GetProperty($taskField)
                if ($taskValue.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { throw "手册$taskField 必须为字符串。" }
                $taskText = $taskValue.GetString()
                $taskLimit = if ($taskField -eq 'title') { 100 } else { 2600 }
                if ([string]::IsNullOrWhiteSpace($taskText) -or $taskText.Length -gt $taskLimit -or $taskText -match '[\u3400-\u9FFF<>]') { throw "手册$taskField 内容无效。" }
            }
            $taskImages = $taskPage.GetProperty('illustrations')
            if ($taskImages.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or $taskImages.GetArrayLength() -gt 6) { throw '手册配图列表无效。' }
            foreach ($taskImage in $taskImages.EnumerateArray()) {
                if ($taskImage.ValueKind -ne [System.Text.Json.JsonValueKind]::String -or -not $taskIds.Contains($taskImage.GetString())) { throw "手册配图ID未登记：$taskImage" }
                $taskIcon = Join-Path $ProjectDir ('assets\icons\' + $taskImage.GetString().Substring('pcrepair.'.Length) + '.png')
                if (-not [IO.File]::Exists($taskIcon)) { throw "英文手册配图文件缺失：$taskIcon" }
            }
        }
        $taskContract = $taskStrictUtf8.GetString([IO.File]::ReadAllBytes((Join-Path $ProjectDir 'EnglishPatch\translation-contract.json'))) | ConvertFrom-Json
        if ($taskContract.ManualContractVersion -cne $taskVersion) { throw '英文补丁缺少配套手册契约。' }
    } finally { $taskDocument.Dispose() }
    if (-not [IO.File]::Exists((Join-Path $ProjectDir 'assets\icons\computer_manual_icon.png'))) { throw '缺少电脑手册封面。' }
    Write-Host "电脑手册核对通过：$taskVersion、完整八页英文、当前配图ID/PNG及原生中文布局契约一致。"
}
