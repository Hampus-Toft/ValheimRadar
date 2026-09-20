# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

ValheimRadar is a BepInEx 5 / Harmony plugin for Valheim that scans around the local player and pins clustered results on the in-game Minimap. All shared guidance - commands, build gotchas, architecture, module-boundary rules, playbooks, the mandatory `PluginVersion` bump policy, and the PR format - lives in `AGENTS.md`. Read it before changing anything:

@AGENTS.md

## Claude Code specifics

- Never run a bare `dotnet build` (Debug): it kills and relaunches the user's live Valheim. Use `dotnet build -c Release` (see Critical Gotchas in `AGENTS.md`).
- Exclude `.claude/worktrees/` from Glob/Grep, and stage files by name rather than `git add -A`.
