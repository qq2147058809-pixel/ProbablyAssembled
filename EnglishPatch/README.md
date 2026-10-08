# PC expansion English Patch

The independent English patch is synchronized to the 2026-10-08 base content: **489 catalog entries** and **eight manual pages under contract v12**. It supplies English only for this mod and registers no gameplay, items, or NPCs. Author: 阿铭.

## Matching Files and Language

Install `PCExpansion.dll` and `PCExpansionEnglishPatch.dll` from the same build in the game's `Mods` folder, then fully restart. The base alone uses Chinese; the matching patch activates English without changing the game's language setting. Remove the patch and restart to return to Chinese. Missing/invalid text falls back to Chinese; incompatible catalogs and manual pages are rejected.

This base version supports only saves started after installing it. Old saves are not loaded, migrated, or overwritten. Remove old-named base/patch DLLs before installing to avoid duplicate loading. Building the patch does not automatically enable it. The official deployment command updates it only if English is already installed, preserving the current language choice.

## Current Gameplay Text

The catalog covers all whole items and subparts, the workroom entrance and unified parts workbench, motherboard/case assembly and material repair, cash repair, batch recycling, two-step discard, storage search/filters/grouped rows, dedicated supplier dialogue, Vesper's 0504 contact, buyer checks, prices, and failure/recovery messages. Stable model and save IDs are preserved.

Use the desk keychain to enter the workroom. Cases open there, with fixed contents and no reroll on insufficient space. All repairs use the workroom and complete their recipe in one transaction; no material-drag progress is carried forward. Common parts and capacity modules do not lower configuration tier. Storage keeps and withdraws actual items individually even when identical unassembled items share a row.

The Computer Sign raises the PC Materials Seller's daily chance from 30% to 75% (day 5 onward) and the PC Parts Thief's from 20% to 40% (day 12 onward). These dedicated visitors are rolled at opening and appended independently, outside the vanilla expected-normal-customer list. The seller supplies intact low-tier parts and four materials, always including screws and electronics. The thief supplies 2-3 high-tier stolen parts with at least one intact item. Vanilla scavenger/thief bonus stock and retired tool sales have been removed.

Vesper's name, dialogue, phone book, nightly report, and 0504 card are translated. Actual card ownership/purchase unlocks her permanently in the new save; an offer alone does not. The three-day cooldown begins only after successful queue insertion.

## Manual and Shared Assets

The eight English pages use native TMP text and independent item illustrations on the existing paper. They follow the current workroom, cash/material repair, recycling/discard, motherboard SSD, dual-GPU, trading, and high-end markup rules. An invalid or oversized English page falls back to the complete Chinese manual. The base retains Chinese text and its native Chinese font preparation, including fallback glyphs.

All current item art, portraits, and wordless workroom backgrounds are shared base assets. No English copies of those images are needed. Source checks and compilation do not establish in-game glyph coverage or layout acceptance; those still require player verification.

See the English section of [the player guide](../MOD_README.md) for the complete current rules.

## Build and Maintenance

From the project directory, use the official entry:

```powershell
..\pwsh\pwsh.exe -NoProfile -File .\build.ps1 -UpdateEnglishPatch
```

Both DLLs are written to `bin/Release/net6.0`. Use `deploy.ps1 -UpdateEnglishPatch` for the normal paired deployment while preserving installed-language state. Ordinary Chinese builds leave the previous English patch unchanged; the workspace backlog records later deferred changes.

The base fingerprints its Chinese catalog. `translation-contract.json` binds that SHA256, entry count, localization API version, and manual page contract. Refresh contracts only after updating the actual English content. English JSON and resources stay inside this patch; the Chinese base has no dependency on its assembly.
