# PC expansion English Patch

The independent English patch is synchronized to the 2026-10-13 version 2.0 base: **737 catalog entries** and **ten manual pages under contract v14**. It supplies English only for this mod and registers no gameplay, items, or NPCs. Author: 阿铭.

## Matching Files and Language

Install `PCExpansion.dll` and `PCExpansionEnglishPatch.dll` from the same build in the game's `Mods` folder, then fully restart. The base alone uses Chinese; the matching patch activates English without changing the game's language setting. Remove the patch and restart to return to Chinese. Missing/invalid text falls back to Chinese; incompatible catalogs and manual pages are rejected.

PC expansion 2.0 retains the current save checks: a valid save-version marker of 1.0 or higher is required. Missing, invalid, or lower markers are rejected; saves are not migrated or directly rewritten. Remove old-named base/patch DLLs before installing to avoid duplicate loading. Building the patch does not automatically enable it. The official deployment command updates it only if English is already installed, preserving the current language choice.

## Current Gameplay Text

The catalog covers all whole items and subparts, the workroom entrance and unified parts workbench, motherboard/case assembly and material repair, cash repair, batch recycling, two-step discard, storage search/filters/grouped rows, dedicated supplier dialogue, Vesper's 0504 contact, buyer checks, prices, and failure/recovery messages. Stable model and save IDs are preserved.

Use the desk keychain to enter the workroom. Cases open there, with fixed contents and no reroll on insufficient space. All repairs use the workroom and complete their recipe in one transaction; no material-drag progress is carried forward. Common parts and capacity modules do not lower configuration tier. Storage keeps and withdraws actual items individually even when unassembled items with the same displayed name, unit price and condition share a row.

The catalog also translates the recycling quantity shortfall, stock-based exact counts, target-value estimates, zero-value explanation, and input-limit message. Storage admits only whole PC parts, assembly subparts, and six repair/recycling materials. Manuals, contact cards, signs, and Energy Cells cannot be newly stored; existing stored items can still be withdrawn.

Version 2.0 adds the complete server room catalog: dedicated miner and server cases at T1-T5, activation and joint power/cooling expansion to ten slots, machine parts and rack actions, mining, five-coin trading, server leases, Vesper's reservations and delivery dialogue. The control PC opens three browser sites for room management, the coin exchange, and Vesper's hardware orders. Shared machine, rack, state, browser, and coin art comes from the base.

Choose a dedicated case from workroom inventory or storage and rack it in an unlocked server-room slot. Use the rack panel to install parts: miners include their motherboard, PSU and cooling and need whole GPUs; servers need CPUs and RAM. Pay miner running fees at overnight settlement in priority order; machines you cannot afford stop for that night. Mining progress is shared per coin and retained when switching coins or powering off. Trades use 0.1-coin steps and a 1% fee on each side; the full buy total rounds up and the full sell total rounds down. Holdings P/L uses purchase cash cost and excludes running fees, so it is not total room profit.

Server leases run for three, five or seven days. Net rent is paid upfront after prepaid running fees, and the machine stays locked until expiry; there is no early cancellation. Vesper takes up to three unpaid reservations and delivers first in the queue next game day. Buy at the shop with normal markup and bargaining. Goods that cannot fit the counter return the next day; displayed but unbought orders cancel. The current Chinese rules govern these texts; retained M1/M2 preview strings do not describe the active 2.0 features.

Storage now combines category, condition and type filters. Subparts selects dismantled subparts; Parts selects unassembled whole hardware and dedicated cases. Materials belong to neither type. The three filter summary fields and compact labels are translated. Condition and type default to unrestricted, persist within the same loaded session, and reset when changing saves or restarting; they are not written into saves.

Both desk speakers open audio settings for area music, ambience and action sounds. These channels follow the game's master/music settings, and setting a channel to 0 mutes it. Calendar price-effect labels, all five rack states, manual stop and forecast unpaid-stop reasons are translated.

The Computer Sign raises the PC Materials Seller's daily chance from 30% to 75% (day 5 onward) and the PC Parts Thief's from 20% to 40% (day 12 onward). These dedicated visitors are rolled at opening and appended independently, outside the vanilla expected-normal-customer list. The seller supplies 1-2 random intact T1-T3 parts, an additional intact T1-T3 CPU, and four materials. The extra CPU rolls T1/T2/T3 independently at 50%/30%/20%, takes no random-part slot, and is still supplied if a random part is also a CPU. Screws and electronics are guaranteed. The thief supplies 2-3 high-tier stolen parts with at least one intact item. Vanilla scavenger/thief bonus stock and retired tool sales have been removed.

Vesper's name, dialogue, phone book, nightly report, and 0504 card are translated. Actual card ownership/purchase unlocks her permanently in the new save; an offer alone does not. The three-day cooldown begins only after successful queue insertion.

The catalog also translates Jiang Bai's visit and two confiscated-parts boxes, including separate box descriptions and their shared flavor line; the specific refusal lines for Bottleneck Builds and Uptown low-tier goods; the save-version message; and both five-day PC market events. AI Demand Surge doubles whole GPU and RAM prices, while Crypto Boom triples whole GPU prices. The events are mutually exclusive and affect buy, sell, and estimate prices.

## Manual and Shared Assets

The ten English pages use enlarged bold native TMP text and independent item illustrations on the existing paper. They follow the current workroom, cash/material repair, recycling/discard, motherboard SSD, dual-GPU, trading, and high-end markup rules. An invalid or oversized English page falls back to the complete Chinese manual. The base retains Chinese text and its native Chinese font preparation, including fallback glyphs.

All current item art, portraits, and wordless workroom backgrounds are shared base assets. No English copies of those images are needed. Source checks and compilation do not establish in-game glyph coverage or layout acceptance; those still require player verification.

See the English section of [the player guide](../MOD_README.md) for the complete current rules.

## Build and Maintenance

From the project directory, use the official entry:

```powershell
..\pwsh\pwsh.exe -NoProfile -File .\build.ps1 -UpdateEnglishPatch
```

Both DLLs are written to `bin/Release/net6.0`. Use `deploy.ps1 -UpdateEnglishPatch` for the normal paired deployment while preserving installed-language state. Ordinary Chinese builds leave the previous English patch unchanged; the workspace backlog records later deferred changes.

The base fingerprints its Chinese catalog. `translation-contract.json` binds that SHA256, entry count, localization API version, and manual page contract. Refresh contracts only after updating the actual English content. English JSON and resources stay inside this patch; the Chinese base has no dependency on its assembly.
