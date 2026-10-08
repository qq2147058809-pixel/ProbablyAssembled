function Assert-PCExpansionTranslations {
    param([string]$ProjectDir, [switch]$UpdateEnglishPatch)
    $zhPath = Join-Path $ProjectDir 'src\Infrastructure\translations.zh.json'
    $enPath = Join-Path $ProjectDir 'EnglishPatch\translations.en.json'
    $contractPath = Join-Path $ProjectDir 'EnglishPatch\translation-contract.json'
    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    $catalogs = @()
    $catalogPaths = @($zhPath)
    if ($UpdateEnglishPatch) { $catalogPaths += $enPath }
    $slots = [object[]](@('0') * 16)
    foreach ($catalogPath in $catalogPaths) {
        $raw = $strictUtf8.GetString([IO.File]::ReadAllBytes($catalogPath))
        if ($raw.Contains([char]0xFFFD) -or $raw.Contains([char]0)) { throw "文本资源含损坏字符：$catalogPath" }
        $json = [System.Text.Json.JsonDocument]::Parse($raw)
        try {
            $properties = @($json.RootElement.EnumerateObject())
            if (@($properties | Group-Object Name | Where-Object Count -gt 1).Count -gt 0) {
                throw "文本资源含重复键：$catalogPath"
            }
            foreach ($property in $properties) {
                if ($property.Value.ValueKind -ne [System.Text.Json.JsonValueKind]::String) {
                    throw "文本资源值必须为字符串：$($property.Name)"
                }
                $value = $property.Value.GetString()
                if ([string]::IsNullOrWhiteSpace($value)) { throw "译文不能为空：$($property.Name)" }
                try { [void][string]::Format([Globalization.CultureInfo]::InvariantCulture, $value, $slots) }
                catch { throw "译文模板格式错误：$($property.Name)。$($_.Exception.Message)" }
            }
        } finally { $json.Dispose() }
        $catalogs += ,($raw | ConvertFrom-Json -AsHashtable)
    }
    $zh = $catalogs[0]
    if (-not $UpdateEnglishPatch) {
        Write-Host "中文文本资源核对通过：$($zh.Count) 项文本，UTF-8、重复键、非空字符串及模板有效；英文同步延后。"
        return
    }
    $en = $catalogs[1]
    $keyDifference = @(Compare-Object @($zh.Keys | Sort-Object) @($en.Keys | Sort-Object))
    if ($keyDifference.Count -gt 0) { throw ('中英文文本键不一致：' + ($keyDifference.InputObject -join ', ')) }
    foreach ($key in $zh.Keys) {
        foreach ($catalog in @($zh, $en)) {
            if ([string]::IsNullOrWhiteSpace($catalog[$key])) { throw "译文不能为空：$key" }
            if ($catalog[$key].Contains([char]0xFFFD) -or $catalog[$key].Contains([char]0)) {
                throw "译文含损坏字符：$key"
            }
            try { [void][string]::Format([Globalization.CultureInfo]::InvariantCulture, $catalog[$key], $slots) }
            catch { throw "译文模板格式错误：$key。$($_.Exception.Message)" }
        }
        $pattern = '(?<!\{)\{(\d+)(?:[,}:])'
        $zhSlots = @([regex]::Matches($zh[$key], $pattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        $enSlots = @([regex]::Matches($en[$key], $pattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        if (($zhSlots -join ',') -ne ($enSlots -join ',')) { throw "中英文模板参数不一致：$key" }
    }
    $contract = $strictUtf8.GetString([IO.File]::ReadAllBytes($contractPath)) | ConvertFrom-Json
    $hash = (Get-FileHash -LiteralPath $zhPath -Algorithm SHA256).Hash
    if ($contract.ContractVersion -ne 'pcexpansion-localization-v1' -or
        $contract.BaseCatalogSha256 -ne $hash -or $contract.Entries -ne $zh.Count) {
        throw '英文补丁文本契约过时。请同步译文和 translation-contract.json 后再构建。'
    }
    Write-Host "独立翻译资源核对通过：$($zh.Count) 对文本，UTF-8、参数和契约一致。"
}
