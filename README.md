# WhiteSpawn

[English](README.md) | [Русский](README.ru.md)

[![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)](#)
[![Status](https://img.shields.io/badge/status-stable-green.svg)](#)
[![Rust](https://img.shields.io/badge/game-Rust-orange.svg)](#)
[![Oxide](https://img.shields.io/badge/framework-Oxide%20%2F%20uMod-yellow.svg)](#)
[![Language](https://img.shields.io/badge/language-C%23-239120.svg)](#)
[![License](https://img.shields.io/badge/license-MIT-lightgrey.svg)](LICENSE)

WhiteSpawn is a Rust plugin for Oxide/uMod that provides a safe spawn system with a protected zone, teleport timers, hostility checks, and automatic respawn management. Developed by **whitecristafer**, sponsored by **infunv.ru** for **evolve.infunv.ru**.

## Overview

WhiteSpawn gives server administrators a complete spawn solution:

- One‑click teleport to a configurable spawn point
- Configurable teleport delay with hostile status cancellation
- Protected zone around the spawn point (invulnerability, building block, damage block)
- Automatic teleport on first join and after death
- Welcome and respawn messages
- Optional `/respawn` command (lose items)
- Permission‑based bypasses for timers and forced spawn

The plugin automatically detects the Outpost or Bandit Town as default spawn if no custom point is set.

## Features

- **Spawn teleport**: `/spawn` teleports the player to the set spawn point
- **Configurable timer**: adjustable delay before teleport (default 5 seconds)
- **Hostility check**: teleport is cancelled if the player becomes hostile during the countdown
- **Safe zone**: a sphere around the spawn point where players are invulnerable, building and damage are blocked (except admins)
- **Auto‑spawn on first join**: teleports new players to spawn and shows a welcome message
- **Auto‑spawn after death**: teleports players back to spawn after respawn with a respawn message
- **Admin bypass**: `whitespawn.admin` allows building, damaging, and bypasses restrictions in the safe zone
- **Timer bypass**: `whitespawn.bypasstimer` removes the teleport delay
- **Spawn bypass**: `whitespawn.bypassspawn` prevents automatic teleport on first join and after death
- **Respawn command**: `/respawn` kills the player and respawns them at spawn (items lost) – can be disabled
- **Auto‑detection**: if no spawn point is set, the plugin searches for Outpost or Bandit Town on startup
- **Radius configuration**: safe zone radius can be changed via command or config
- **Localization**: built‑in support for English and Russian
- **Chat messages**: customizable via lang files, with SteamID avatar support

## Commands

| Command | Description | Permission |
| --- | --- | --- |
| `/spawn` | Teleport to the spawn point | None (if enabled) |
| `/setspawn` | Set the spawn point at your current position | `whitespawn.admin` |
| `/ws radius <number>` | Set safe zone radius (in meters) | `whitespawn.admin` |
| `/ws status` | Show plugin status (enabled, radius, timer) | None |
| `/ws help` | Show help message | None |
| `/respawn` | Kill yourself and respawn at spawn (lose items) | None (if enabled) |

## Permissions

| Permission | Description |
| --- | --- |
| `whitespawn.admin` | Full access: set spawn, change radius, bypass all restrictions (build, damage, timer, spawn) in the safe zone |
| `whitespawn.bypasstimer` | Removes the teleport delay; teleport is instant |
| `whitespawn.bypassspawn` | Prevents automatic teleport on first join and after death (player spawns normally) |

## Configuration

WhiteSpawn creates its configuration file automatically on first load.  
Main settings (in `oxide/config/WhiteSpawn.json`):

```json
{
  "Settings": {
    "Enabled": true,
    "SpawnTimer": 5.0,
    "Radius": 10.0,
    "WelcomeMessageEnabled": true,
    "RespawnMessageEnabled": true,
    "RespawnCommandEnabled": true,
    "FindOutpostFirst": true
  }
}
```

| Field | Description |
| --- | --- |
| `Enabled` | Enable/disable the entire plugin |
| `SpawnTimer` | Delay in seconds before teleport (0 = instant) |
| `Radius` | Safe zone radius around spawn point (in meters) |
| `WelcomeMessageEnabled` | Show welcome message on first join |
| `RespawnMessageEnabled` | Show respawn message after death |
| `RespawnCommandEnabled` | Enable/disable the `/respawn` command |
| `FindOutpostFirst` | If true, search for Outpost as default spawn; if false, search for Bandit Town |

## How It Works

1. **Spawn teleport**:  
   When a player uses `/spawn`, the plugin checks hostility. If not hostile, it starts a timer (unless bypassed). On timer completion, the player is teleported to the spawn point with teleport sound effects. If the player becomes hostile during the countdown, the teleport is cancelled.

2. **Safe zone**:  
   Any player inside the radius around the spawn point is invulnerable. Building and structure damage are blocked. Admins with `whitespawn.admin` can ignore all these restrictions.

3. **First join**:  
   New players are automatically teleported to spawn after 0.5 seconds (unless they have `whitespawn.bypassspawn`). A welcome message is shown.

4. **After death**:  
   Upon respawn, players are teleported back to spawn (unless they have the bypass). A respawn message is shown.

5. **Respawn command**:  
   `/respawn` clears the player's inventory, kills them, and forces a respawn. The player is then teleported to spawn. This command can be disabled in the config.

6. **Default spawn**:  
   If no spawn point is manually set, the plugin searches for Outpost (or Bandit Town) on server startup and sets the spawn to its position.

## Update System

WhiteSpawn does **not** include an automatic update checker by default. Since the plugin is maintained by whitecristafer, updates are released manually. Administrators should periodically check the repository for new versions.

## Installation

1. Download `WhiteSpawn.cs` and place it in the `oxide/plugins` folder.
2. Restart the server or run `oxide.reload WhiteSpawn`.
3. The plugin will generate default configuration and data files.
4. Adjust `oxide/config/WhiteSpawn.json` to your needs.
5. Grant permissions to your staff (e.g., `oxide.grant group admin whitespawn.admin`).
6. Set a spawn point using `/setspawn` (or let the plugin auto‑detect Outpost/Bandit).

## Localization

Built‑in languages:
- English (default)
- Russian

You can add or modify translations by editing `lang/en.json` and `lang/ru.json` in the `oxide/lang` folder. The plugin uses the same message keys as defined in the `LoadDefaultMessages` method.

## Logging

WhiteSpawn logs important events to the server console:
- Spawn point set (with player name and position)
- Radius changes
- Spawn teleport attempts (blocked/cancelled)
- Startup banner with plugin info

All messages are prefixed with `[WhiteSpawn]` for easy filtering.

## Requirements

- Rust dedicated server
- Oxide/uMod (latest version recommended)
- C# plugin support

## Notes

- Teleport sound effects use the built‑in teleport prefab.
- The safe zone is centered exactly at the spawn point set with `/setspawn`.
- Hostility detection uses `player.IsHostile()` (available in recent Rust versions).
- The plugin stores spawn data in `oxide/data/WhiteSpawn/spawn.json`.
- The `/respawn` command strips all items – players will respawn with default gear (rock, torch, etc.).

## License

This project is open‑source. Released under the Apache 2.0 License. See `LICENSE` for full details.

