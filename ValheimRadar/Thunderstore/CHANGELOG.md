# Changelog

## 1.12.4

- **Fixed:** fish are pinned on the map again. They had stopped showing up in 1.10.0, when loose
  item drops (ore, ingots) were excluded from scanning - fish count as item drops in Valheim, so
  they were excluded too.
- **Fixed:** config sections are now listed in order in `HampusToft.ValheimRadar.cfg` (section
  10 used to come before section 2). Single-digit section numbers now have a leading zero
  (`01 - General`, `02 - Master Groups`, ...). Your existing settings are carried over to the
  renamed sections automatically - nothing to redo.
