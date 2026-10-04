# ValheimRadar

ValheimRadar by Hampus Toft - a minimap radar for Valheim. Scans around the player and pins nearby creatures,
resources (ores, berries, mushrooms...) and points of interest (dungeons, altars, runestones, ruins...), clustered,
remembered per world and kept up to date as things are mined, picked or regrow. Client-side BepInEx mod; requires
Jotunn.

- What it does and how to install: [ValheimRadar/Thunderstore/README.md](ValheimRadar/Thunderstore/README.md)
- Pin icons and how to override them: [ValheimRadar/docs/ICONS.md](ValheimRadar/docs/ICONS.md)
- Building, architecture and contribution rules: [AGENTS.md](AGENTS.md)

Release zip: `dotnet build -c Release -t:ThunderstorePack` in `ValheimRadar/`.

## AI disclaimer

This mod was mostly made using [Claude Code](https://claude.com/claude-code), Anthropic's AI coding assistant. The
design and requirements are Hampus Toft's; Claude Code did most of the work: analysing Valheim's game code, and
writing the code, tests and documentation, all under the author's direction and review. Please report any problems in
the [issue tracker](https://github.com/Hampus-Toft/ValheimRadar/issues).
