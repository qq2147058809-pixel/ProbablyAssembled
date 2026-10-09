function Assert-PCExpansionChineseManual {
    param([string]$ProjectDir)
    $manualPath = Join-Path $ProjectDir 'src\Infrastructure\computer_manual.zh.json'
    if (-not [IO.File]::Exists($manualPath)) { throw "缺少中文手册布局：$manualPath" }
    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $json = $utf8.GetString([IO.File]::ReadAllBytes($manualPath))
    if ($json.Length -gt 131072) { throw '中文手册布局过大。' }
    $document = [System.Text.Json.JsonDocument]::Parse($json)
    try {
        function Assert-Fields($Entry, [string[]]$Required, [string[]]$Optional = @()) {
            if ($Entry.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw '中文手册字段类型错误。' }
            $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($property in $Entry.EnumerateObject()) {
                if (-not $seen.Add($property.Name) -or ($Required -cnotcontains $property.Name -and $Optional -cnotcontains $property.Name)) {
                    throw "中文手册字段重复或未知：$($property.Name)"
                }
            }
            foreach ($name in $Required) { if (-not $seen.Contains($name)) { throw "中文手册字段缺失：$name" } }
        }
        function Read-ManualText($Entry, [int]$Maximum) {
            if ($Entry.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { throw '中文手册文字类型错误。' }
            $value = $Entry.GetString()
            if ([string]::IsNullOrWhiteSpace($value) -or $value.Length -gt $Maximum -or
                $value.Contains([char]0) -or $value.Contains([char]0xFFFD) -or $value.Contains('<') -or $value.Contains('>')) {
                throw '中文手册含空白、损坏或富文本标签。'
            }
            return $value
        }
        function Read-ManualNumber($Entry, [string]$Name, [double]$Min, [double]$Max) {
            $property = $Entry.GetProperty($Name)
            if ($property.ValueKind -ne [System.Text.Json.JsonValueKind]::Number) { throw "中文手册数值类型错误：$Name" }
            $number = $property.GetDouble()
            if (-not [double]::IsFinite($number) -or $number -lt $Min -or $number -gt $Max) { throw "中文手册数值超限：$Name" }
            return $number
        }
        $root = $document.RootElement
        Assert-Fields $root @('contract','title','pages')
        $contract = 'pcexpansion-computer-manual-v13'
        if ((Read-ManualText $root.GetProperty('contract') 80) -cne $contract) { throw '中文手册契约不是v13。' }
        if ((Read-ManualText $root.GetProperty('title') 80) -cne '电脑装机与交易手册') { throw '中文手册标题不匹配。' }
        $source = [IO.File]::ReadAllText((Join-Path $ProjectDir 'src\Infrastructure\ComputerManualPages.cs'))
        if (-not $source.Contains('public const string ContractVersion = "' + $contract + '";')) { throw '中文手册契约与源码不一致。' }
        if (-not $source.Contains('internal const int PageCount = 10;')) { throw '中文手册页数与源码不一致。' }
        $pages = $root.GetProperty('pages')
        if ($pages.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or $pages.GetArrayLength() -ne 10) { throw '中文手册必须完整十页。' }
        $headings = @('简介','简介（续）','拆开机箱','可以修理的物品','维修与单次回收','维修与单次回收（续）','开机检测','三种机型','机型示例','高端配件')
        $nativeIcons = @('vanilla:electronic1','vanilla:wire','vanilla:nuts_metal_pile','vanilla:printer_plastic','vanilla:scrap_metal','vanilla:metal_ingot','vanilla:energy_credit')
        $count = 0
        for ($pageIndex = 0; $pageIndex -lt $headings.Count; $pageIndex++) {
            $page = $pages[$pageIndex]
            Assert-Fields $page @('heading','elements')
            if ((Read-ManualText $page.GetProperty('heading') 80) -cne $headings[$pageIndex]) { throw "中文第$($pageIndex+1)页标题/页序错误。" }
            $elements = $page.GetProperty('elements')
            if ($elements.ValueKind -ne [System.Text.Json.JsonValueKind]::Array -or $elements.GetArrayLength() -lt 1 -or $elements.GetArrayLength() -gt 160) {
                throw '中文手册页面元素数量无效。'
            }
            foreach ($element in $elements.EnumerateArray()) {
                $type = $element.GetProperty('type').GetString()
                if ($type -ceq 'text') {
                    Assert-Fields $element @('type','text','x','y','width','height','fontSize','style','align')
                    [void](Read-ManualText $element.GetProperty('text') 3200)
                } elseif ($type -ceq 'icon') {
                    Assert-Fields $element @('type','id','x','y','width','height') @('fontSize','style','align')
                    $id = Read-ManualText $element.GetProperty('id') 120
                    if ($nativeIcons -cnotcontains $id) {
                        if ($id -cmatch '^pcrepair\.(computer_case|component_(psu|motherboard|hdd|ram|gpu|fan|cpu|cooler))_t[1-5](_broken)?$') {
                            $file = $id.Substring('pcrepair.'.Length) + '.png'
                        } else { throw "中文手册配图未登记：$id" }
                        if (-not [IO.File]::Exists((Join-Path $ProjectDir ('assets\icons\' + $file)))) { throw "中文手册独立配图缺失：$file" }
                    }
                } else { throw "中文手册元素类型未知：$type" }
                $x = Read-ManualNumber $element 'x' 0 1
                $y = Read-ManualNumber $element 'y' 0 1
                $width = Read-ManualNumber $element 'width' .001 1
                $height = Read-ManualNumber $element 'height' .001 1
                if ($x + $width -gt 1.00001 -or $y + $height -gt 1.00001) { throw '中文手册元素超出真实纸张边界。' }
                $optional = [System.Text.Json.JsonElement]::new()
                if ($element.TryGetProperty('fontSize', [ref]$optional)) { [void](Read-ManualNumber $element 'fontSize' 9 80) }
                if ($element.TryGetProperty('style', [ref]$optional) -and @('body','blue','green','caption','arrow') -cnotcontains $optional.GetString()) { throw '中文手册文字样式未知。' }
                if ($element.TryGetProperty('align', [ref]$optional) -and @('left','center') -cnotcontains $optional.GetString()) { throw '中文手册文字对齐未知。' }
                $count++
            }
        }
        if (-not [IO.File]::Exists((Join-Path $ProjectDir 'assets\icons\computer_manual_icon.png'))) { throw '缺少电脑手册封面。' }
        Write-Host "中文原生电脑手册校验通过：v13、十页、$count 个独立图文元素与封面。"
    } finally { $document.Dispose() }
}
