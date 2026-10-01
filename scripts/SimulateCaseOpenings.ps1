param(
    [int]$OpensPerTier = 100,
    [int]$Seed = 260100
)

$ErrorActionPreference = 'Stop'

# Mirrors CaseInteriorUI.SlotTable, Components.BaseValue/ValueFor,
# CaseEconomy.TierRolls/LootChance/BrokenChance and CaseUnboxing.RollLoot.
$slotBaseValues = @(30, 75, 100, 150, 45, 55, 55, 55, 55, 130, 65, 65)
$tierMultipliers = @(1.0, 1.55, 2.4, 3.7, 5.7)
$tierRolls = @(
    @{ Offset = -2; Weight = 0.20 },
    @{ Offset = -1; Weight = 0.34 },
    @{ Offset =  0; Weight = 0.45 },
    @{ Offset =  1; Weight = 0.01 }
)
$lootChance = 0.65
$brokenChance = 0.40
$targetAverageReturn = 0.78

function Clamp-Tier([int]$Tier) {
    [Math]::Max(1, [Math]::Min(5, $Tier))
}

function Get-ItemValue([int]$BaseValue, [int]$Tier, [bool]$Broken) {
    $value = [Math]::Round($BaseValue * $tierMultipliers[$Tier - 1])
    if ($Broken) {
        $value = [Math]::Max(1, [Math]::Round($value * 0.20))
    }
    [long]$value
}

function Roll-Tier([int]$CaseTier, [System.Random]$Random) {
    $roll = $Random.NextDouble()
    $cumulative = 0.0
    foreach ($entry in $tierRolls) {
        $cumulative += $entry.Weight
        if ($roll -lt $cumulative) {
            return (Clamp-Tier ($CaseTier + $entry.Offset))
        }
    }
    Clamp-Tier $CaseTier
}

function Get-ExpectedPayout([int]$CaseTier) {
    $expected = 0.0
    foreach ($baseValue in $slotBaseValues) {
        $slotAverage = 0.0
        foreach ($entry in $tierRolls) {
            $partTier = Clamp-Tier ($CaseTier + $entry.Offset)
            $intactValue = Get-ItemValue $baseValue $partTier $false
            $brokenValue = Get-ItemValue $baseValue $partTier $true
            $slotAverage += $entry.Weight *
                ($intactValue * (1.0 - $brokenChance) + $brokenValue * $brokenChance)
        }
        $expected += $lootChance * $slotAverage
    }

    $minimumTier = [Math]::Max(1, $CaseTier - 2)
    $emptyProbability = [Math]::Pow(1.0 - $lootChance, $slotBaseValues.Count)
    $expected += $emptyProbability * (Get-ItemValue 150 $minimumTier $false)
    $expected
}

$allResults = [System.Collections.Generic.List[object]]::new()
$tierSummary = foreach ($caseTier in 1..5) {
    $expected = Get-ExpectedPayout $caseTier
    $oldPrice = [Math]::Max(1, [Math]::Round($expected * 0.78))
    $newPrice = [Math]::Max(1, [Math]::Round($expected / $targetAverageReturn))
    $random = [System.Random]::new($Seed + $caseTier)
    $tierResults = [System.Collections.Generic.List[object]]::new()

    for ($open = 0; $open -lt $OpensPerTier; $open++) {
        $payout = 0L
        foreach ($baseValue in $slotBaseValues) {
            if ($random.NextDouble() -ge $lootChance) { continue }
            $partTier = Roll-Tier $caseTier $random
            $broken = $random.NextDouble() -lt $brokenChance
            $payout += Get-ItemValue $baseValue $partTier $broken
        }
        if ($payout -eq 0) {
            $minimumTier = [Math]::Max(1, $caseTier - 2)
            $payout = Get-ItemValue 150 $minimumTier $false
        }

        $result = [pscustomobject]@{
            Tier = $caseTier
            Payout = $payout
            OldReturn = $payout / $oldPrice
            TargetReturn = $payout / $newPrice
        }
        $tierResults.Add($result)
        $allResults.Add($result)
    }

    $wins = @($tierResults | Where-Object { $_.TargetReturn -gt 1.0 })
    $losses = @($tierResults | Where-Object { $_.TargetReturn -le 1.0 })
    [pscustomobject]@{
        Tier = $caseTier
        ExpectedPayout = [Math]::Round($expected, 1)
        OldPrice = $oldPrice
        NewPrice = $newPrice
        OldProfitable = @($tierResults | Where-Object { $_.OldReturn -gt 1.0 }).Count
        NewProfitable = $wins.Count
        LossAtLeast30 = @($tierResults | Where-Object { $_.TargetReturn -le 0.70 }).Count
        GainAtLeast10 = @($tierResults | Where-Object { $_.TargetReturn -ge 1.10 }).Count
        MeanReturnPct = [Math]::Round((($tierResults | Measure-Object TargetReturn -Average).Average) * 100, 1)
        MeanLossPct = if ($losses.Count) {
            [Math]::Round((1.0 - (($losses | Measure-Object TargetReturn -Average).Average)) * 100, 1)
        } else { 0 }
        MeanGainPct = if ($wins.Count) {
            [Math]::Round(((($wins | Measure-Object TargetReturn -Average).Average) - 1.0) * 100, 1)
        } else { 0 }
    }
}

$allWins = @($allResults | Where-Object { $_.TargetReturn -gt 1.0 })
$allLosses = @($allResults | Where-Object { $_.TargetReturn -le 1.0 })
$totalOpens = $OpensPerTier * 5

Write-Output "Seed=$Seed; $OpensPerTier opens per tier; $totalOpens total opens."
Write-Output 'Per-tier results:'
$tierSummary | Format-Table -AutoSize
Write-Output 'Aggregate results:'
[pscustomobject]@{
    TotalOpens = $totalOpens
    OldProfitable = @($allResults | Where-Object { $_.OldReturn -gt 1.0 }).Count
    NewProfitable = $allWins.Count
    NewNotProfitable = $allLosses.Count
    LossAtLeast30 = @($allResults | Where-Object { $_.TargetReturn -le 0.70 }).Count
    GainAtLeast10 = @($allResults | Where-Object { $_.TargetReturn -ge 1.10 }).Count
    MeanReturnPct = [Math]::Round((($allResults | Measure-Object TargetReturn -Average).Average) * 100, 1)
    MeanLossPct = [Math]::Round((1.0 - (($allLosses | Measure-Object TargetReturn -Average).Average)) * 100, 1)
    MeanGainPct = [Math]::Round(((($allWins | Measure-Object TargetReturn -Average).Average) - 1.0) * 100, 1)
} | Format-List
