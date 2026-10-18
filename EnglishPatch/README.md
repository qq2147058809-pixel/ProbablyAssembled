# PC expansion English Patch — 2.6

The optional patch matches PC expansion **2.6 (2026-10-19)**: 1,287 matching catalog entries and the complete twenty-page v27 manual. Author: 阿铭.

## Installation and language

Close the game. Install matching 2.6 `PCExpansion.dll` and `PCExpansionEnglishPatch.dll` in `Mods`, then fully restart. The base alone uses Chinese. Remove the patch and restart to return to Chinese. The patch registers translations only; it adds no duplicate items, gameplay or NPCs and does not change the game's language setting. Mismatched or invalid translations fall back to Chinese; invalid manual pages fall back as a complete book.

**Start a new save for 2.6.** Only valid save-generation markers of 2.6 or higher are accepted. Old saves are not migrated. Continue 1.0 saves with the retained 2.3.1 build; keep backups and do not edit markers or continue new saves with an old build.

## Current coverage

- Modular PC hardware, fixed crate contents, repair/recycling/discard and buyer eligibility. PSUs use 1-6 mixed capacitor banks with 100/200/400/600/1000W tier thresholds. Value Builds receive +30%; Vesper accepts intact T1-T3 Bottleneck Builds, while the lower builder refuses them. Vesper's ordinary visit shares an 18,000 buying budget.
- Workroom slot selection, search and multiple sources, E + left-click transfers, repair returns, partial auto-fill and catalog stock counts. Minimum Necessary for boards includes one SSD; 0GB means no capacity floor. Custom may request zero. Room/Box/Shop are the compact source-button labels.
- Three mining algorithms, per-GPU VRAM requirements, network difficulty, Steady/Variable Pools, Eco/Standard/Overclock, power/cooling, price events and maintenance. Tonight's power needs cash already available; forecast coin sales cannot fund it. Actual hardware and room plans determine output.
- Server 3/5/7-day prepaid leases, reserved capacity and locked configuration. The ledger separates available prepaid cash from earned service income.
- Five-coin depth/slippage trading, 0.0001-coin normal steps, 0.000001 wallet precision, 1% fees on both sides and whole-order cash rounding. Preview payment/proceeds and balance, with expandable details. Auto-sell defaults off, supports operating reserves or a share of new output, and applies net price floors. The ledger keeps 90 daily records and lifetime totals.
- Management/exchange/Vesper order sites, three browser themes, rack descriptions, notices, room audio settings, suppliers, starting perks, confiscated-parts boxes, story dialogue and the 27571 contact.
- A twenty-page manual with quick actions, recipes, build profiles and complete room-operation guidance. English uses native text with shared item illustrations. Untranslated wordless art remains shared.

Default story text can use English. Existing player-edited Chinese text remains exactly as configured in `Mods/PCExpansion/Stories/npc-stories.json`; restart after editing and read the supplied story guide. This patch does not reset story progress.

Read the [bilingual player guide](../MOD_README.md) for complete rules and the Release changelog for final 2.3-to-2.6 differences. Complete native gameplay, English glyphs, layout and language refresh remain subject to in-game verification.

## Build and maintenance

Use the official project entry with PowerShell 7:

```powershell
pwsh -NoProfile -File .\build.ps1 -UpdateEnglishPatch
```

Both DLLs are written to `bin/Release/net6.0`. `deploy.ps1 -UpdateEnglishPatch` updates the installed patch while preserving the local language choice; it does not install English where absent. Ordinary Chinese builds retain the last English DLL. Later deferred changes belong in the workspace's English backlog.

English JSON, manual resources and contracts stay in `EnglishPatch`. The catalog schema is `pcexpansion-localization-v1`; manual contract is `pcexpansion-computer-manual-v27`. Update actual content before refreshing the Chinese SHA256, entry count or page contract. Matching a fingerprint alone does not establish translation completeness or native display acceptance.
