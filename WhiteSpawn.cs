using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("WhiteSpawn", "whitecristafer", "1.1.0")]
    [Description("WhiteSpawn - система спавна с безопасной зоной")]
    public class WhiteSpawn : RustPlugin
    {
        #region Constants

        private const ulong PluginIcon = 76561198209258869; // SteamID
        private const string PluginVersion = "1.1.0";
        private const string Prefix = "<size=12><color=#66ccff><b>WhiteSpawn</b></color></size> |";

        // Access rights
        private const string AdminPermission = "whitespawn.admin";
        private const string BypassTimerPermission = "whitespawn.bypasstimer";
        private const string BypassSpawnPermission = "whitespawn.bypassspawn";

        // Data paths
        private string _dataPath;
        private string _spawnDataFile;

        // Default teleportation timer
        private const float DefaultSpawnTimer = 5f;

        #endregion

        #region Config

        private PluginConfig _config;

        private sealed class PluginConfig
        {
            public SettingsConfig Settings = new SettingsConfig();
        }

        private sealed class SettingsConfig
        {
            public bool Enabled = true;
            public float SpawnTimer = 5f; // seconds before teleportation
                public float Radius = 10f; // radius of the safe zone
            public bool WelcomeMessageEnabled = true;
            public bool RespawnMessageEnabled = true;
            public bool RespawnCommandEnabled = true;
            public bool FindOutpostFirst = true; // if true, look for Outpost; otherwise, look for Bandit
        }

        #endregion

        #region Data

        private SpawnData _spawnData;

        private sealed class SpawnData
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public bool IsSet; // is spawn installed manually
        }

        // We store information about a player's first visit
        private readonly HashSet<ulong> _firstSpawnDone = new HashSet<ulong>();

        // The flag for the respawn command
        private readonly HashSet<ulong> _respawnCommandFlag = new HashSet<ulong>();

        // zone tracking
        private Timer _zoneTimer;
        private readonly HashSet<ulong> _inZoneTracker = new HashSet<ulong>();

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            // English
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You don't have permission to use this command.",
                ["PluginDisabled"] = "Plugin is disabled in config.",
                ["SpawnNotFound"] = "Spawn point not set. Please contact administrator.",
                ["SpawnTeleportStart"] = "Teleporting to spawn in {0} seconds...",
                ["SpawnTeleportCancel"] = "Teleportation canceled due to hostile status.",
                ["SpawnTeleportDone"] = "You have been teleported to spawn.",
                ["SpawnSet"] = "Spawn point set at your position.",
                ["SpawnRadiusSet"] = "Spawn radius set to {0}.",
                ["SpawnRadiusInvalid"] = "Invalid radius. Usage: /ws radius <number>",
                ["WelcomeMessage"] = "Welcome to the server! You have been teleported to the safe spawn zone.",
                ["RespawnMessage"] = "You have been respawned to the safe spawn zone.",
                ["RespawnCommandUsed"] = "You have been respawned (items lost).",
                ["CannotSpawnHostile"] = "You are hostile. Please wait until hostility ends.",
                ["BuildingBlocked"] = "You cannot build in the safe zone.",
                ["DamageBlocked"] = "You cannot deal damage in the safe zone.",
                ["AdminBypass"] = "Admin bypass active.",
                ["InvalidCommand"] = "Unknown command. Use /spawn or /ws help.",
                ["HelpHeader"] = "WhiteSpawn Commands:",
                ["HelpSpawn"] = "/spawn - Teleport to spawn",
                ["HelpSetSpawn"] = "/setspawn - Set spawn point (admin)",
                ["HelpRadius"] = "/ws radius <num> - Set safe zone radius (admin)",
                ["HelpRespawn"] = "/respawn - Respawn yourself (lose items)",
                ["HelpStatus"] = "/ws status - Show plugin status",
                ["Status"] = "WhiteSpawn: Enabled: {0}, Radius: {1}, Spawn Timer: {2}s",
                ["LeaveSafeZone"] = "You have left the safe spawn zone. You are now vulnerable!"
            }, this, "en");

            // Russian
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "У вас нет прав на использование этой команды.",
                ["PluginDisabled"] = "Плагин отключён в конфиге.",
                ["SpawnNotFound"] = "Точка спавна не установлена. Свяжитесь с администратором.",
                ["SpawnTeleportStart"] = "Телепортация на спавн через {0} секунд...",
                ["SpawnTeleportCancel"] = "Телепортация отменена из-за враждебного статуса.",
                ["SpawnTeleportDone"] = "Вы телепортированы на спавн.",
                ["SpawnSet"] = "Точка спавна установлена на вашей позиции.",
                ["SpawnRadiusSet"] = "Радиус спавна установлен на {0}.",
                ["SpawnRadiusInvalid"] = "Неверный радиус. Использование: /ws radius <число>",
                ["WelcomeMessage"] = "Добро пожаловать на сервер! Вы телепортированы в безопасную зону спавна.",
                ["RespawnMessage"] = "Вы были респавнены в безопасную зону спавна.",
                ["RespawnCommandUsed"] = "Вы переродились (вещи потеряны).",
                ["CannotSpawnHostile"] = "Вы враждебны. Подождите окончания враждебности.",
                ["BuildingBlocked"] = "Вы не можете строить в безопасной зоне.",
                ["DamageBlocked"] = "Вы не можете наносить урон в безопасной зоне.",
                ["AdminBypass"] = "Администратор имеет право на обход.",
                ["InvalidCommand"] = "Неизвестная команда. Используйте /spawn или /ws help.",
                ["HelpHeader"] = "Команды WhiteSpawn:",
                ["HelpSpawn"] = "/spawn - Телепорт на спавн",
                ["HelpSetSpawn"] = "/setspawn - Установить точку спавна (админ)",
                ["HelpRadius"] = "/ws radius <число> - Установить радиус зоны (админ)",
                ["HelpRespawn"] = "/respawn - Переродиться (потеря вещей)",
                ["HelpStatus"] = "/ws status - Показать статус плагина",
                ["Status"] = "WhiteSpawn: Включён: {0}, Радиус: {1}, Таймер: {2}с",
                ["LeaveSafeZone"] = "Вы покинули безопасную зону спавна. Теперь вы уязвимы!"
            }, this, "ru");
        }

        #endregion

        #region Initialization

        private void Init()
        {
            EnsureFolders();
            RegisterPermissions();
        }

        private void Loaded()
        {
            LoadConfig();
            LoadSpawnData();
            RegisterCommands();
        }

        private void OnServerInitialized()
        {
            // If the spawn is not set, we will try to find an Outpost or Bandit automatically
            if (!_spawnData.IsSet)
            {
                TryFindDefaultSpawn();
            }

            // Start tracking players entering and leaving the spawn zone
            StartZoneTracker();

            PrintBanner();
        }

        private void Unload()
        {
            SaveSpawnData();
            _firstSpawnDone.Clear();
            _respawnCommandFlag.Clear();

            // Clean up tracking timer and collections to prevent memory leaks
            _zoneTimer?.Destroy();
            _inZoneTracker.Clear();
        }

        #endregion

        #region Permissions

        private void RegisterPermissions()
        {
            permission.RegisterPermission(AdminPermission, this);
            permission.RegisterPermission(BypassTimerPermission, this);
            permission.RegisterPermission(BypassSpawnPermission, this);
        }

        private bool HasAdmin(BasePlayer player) => permission.UserHasPermission(player.UserIDString, AdminPermission);
        private bool HasBypassTimer(BasePlayer player) => permission.UserHasPermission(player.UserIDString, BypassTimerPermission);
        private bool HasBypassSpawn(BasePlayer player) => permission.UserHasPermission(player.UserIDString, BypassSpawnPermission);

        #endregion

        #region Config Management

        private void LoadConfig()
        {
            try
            {
                _config = Config.ReadObject<PluginConfig>();
            }
            catch
            {
                PrintWarning("Config corrupted, creating default.");
                _config = null;
            }

            if (_config == null)
                _config = new PluginConfig();

            // Conversion of values
            if (_config.Settings.SpawnTimer <= 0) _config.Settings.SpawnTimer = DefaultSpawnTimer;
            if (_config.Settings.Radius <= 0) _config.Settings.Radius = 10f;

            SaveConfig();
        }

        protected override void LoadDefaultConfig() => _config = new PluginConfig();

        private void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Data Management

        private void EnsureFolders()
        {
            _dataPath = Path.Combine(Interface.Oxide.DataDirectory, Name);
            _spawnDataFile = Path.Combine(_dataPath, "spawn.json");
            if (!Directory.Exists(_dataPath))
                Directory.CreateDirectory(_dataPath);
        }

        private void LoadSpawnData()
        {
            if (File.Exists(_spawnDataFile))
            {
                try
                {
                    string json = File.ReadAllText(_spawnDataFile);
                    _spawnData = JsonConvert.DeserializeObject<SpawnData>(json);
                }
                catch (Exception ex)
                {
                    PrintError($"Failed to load spawn data: {ex.Message}");
                    _spawnData = new SpawnData { IsSet = false };
                }
            }
            else
            {
                _spawnData = new SpawnData { IsSet = false };
            }
        }

        private void SaveSpawnData()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_spawnData, Formatting.Indented);
                File.WriteAllText(_spawnDataFile, json);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to save spawn data: {ex.Message}");
            }
        }

        #endregion

        #region Commands

        private void RegisterCommands()
        {
            cmd.AddChatCommand("spawn", this, nameof(CmdSpawn));
            cmd.AddChatCommand("setspawn", this, nameof(CmdSetSpawn));
            cmd.AddChatCommand("ws", this, nameof(CmdWS));
            cmd.AddChatCommand("respawn", this, nameof(CmdRespawn));
        }

        private void CmdSpawn(BasePlayer player, string command, string[] args)
        {
            if (!_config.Settings.Enabled)
            {
                SendMessage(player, Lang("PluginDisabled"));
                return;
            }

            if (!_spawnData.IsSet)
            {
                SendMessage(player, Lang("SpawnNotFound"));
                return;
            }

            // Checking for hostility
            if (player.IsHostile())
            {
                SendMessage(player, Lang("CannotSpawnHostile"));
                return;
            }

            // Timer or instant
            float delay = _config.Settings.SpawnTimer;
            if (HasBypassTimer(player) || HasAdmin(player))
                delay = 0f;

            if (delay > 0)
            {
                SendMessage(player, string.Format(Lang("SpawnTeleportStart"), delay));
                timer.Once(delay, () =>
                {
                    // Checking if the player has become hostile during the waiting time
                    if (player == null || !player.IsConnected || player.IsHostile())
                    {
                        if (player != null && player.IsConnected)
                            SendMessage(player, Lang("SpawnTeleportCancel"));
                        return;
                    }
                    DoTeleport(player);
                });
            }
            else
            {
                DoTeleport(player);
            }
        }

        private void DoTeleport(BasePlayer player)
        {
            if (player == null || !player.IsConnected)
                return;

            // Clear hostile status immediately to prevent Outpost/Bandit turrets from killing the player
            if (player.IsHostile())
            {
                player.State.unHostileTimestamp = 0;
                player.ClientRPCPlayer(null, player, "SetHostileLength", 0f);
            }

            // Teleportation sound and effect
            Effect.server.Run("assets/prefabs/misc/transferable/effects/teleport.prefab", player.transform.position, Vector3.up);

            player.Teleport(_spawnData.Position);
            player.eyes.rotation = _spawnData.Rotation;
            player.SendNetworkUpdateImmediate();

            // Apply native Rust Safe Zone UI flag instantly
            player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, true);

            SendMessage(player, Lang("SpawnTeleportDone"));
            Effect.server.Run("assets/prefabs/misc/transferable/effects/teleport.prefab", player.transform.position, Vector3.up);
        }

        private void CmdSetSpawn(BasePlayer player, string command, string[] args)
        {
            if (!HasAdmin(player))
            {
                SendMessage(player, Lang("NoPermission"));
                return;
            }

            _spawnData.Position = player.transform.position;
            _spawnData.Rotation = player.eyes.rotation;
            _spawnData.IsSet = true;
            SaveSpawnData();

            SendMessage(player, Lang("SpawnSet"));
            Puts($"{player.displayName} set spawn at {_spawnData.Position}");
        }

        private void CmdWS(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp(player);
                return;
            }

            string sub = args[0].ToLowerInvariant();
            switch (sub)
            {
                case "help":
                    ShowHelp(player);
                    break;

                case "radius":
                    if (!HasAdmin(player))
                    {
                        SendMessage(player, Lang("NoPermission"));
                        return;
                    }
                    if (args.Length < 2 || !float.TryParse(args[1], out float radius) || radius <= 0)
                    {
                        SendMessage(player, Lang("SpawnRadiusInvalid"));
                        return;
                    }
                    _config.Settings.Radius = radius;
                    SaveConfig();
                    SendMessage(player, string.Format(Lang("SpawnRadiusSet"), radius));
                    Puts($"{player.displayName} set spawn radius to {radius}");
                    break;

                case "status":
                    string status = string.Format(Lang("Status"), _config.Settings.Enabled, _config.Settings.Radius, _config.Settings.SpawnTimer);
                    SendMessage(player, status);
                    break;

                default:
                    SendMessage(player, Lang("InvalidCommand"));
                    break;
            }
        }

        private void ShowHelp(BasePlayer player)
        {
            SendMessage(player, Lang("HelpHeader"));
            SendMessage(player, Lang("HelpSpawn"));
            SendMessage(player, Lang("HelpSetSpawn"));
            SendMessage(player, Lang("HelpRadius"));
            if (_config.Settings.RespawnCommandEnabled)
                SendMessage(player, Lang("HelpRespawn"));
            SendMessage(player, Lang("HelpStatus"));
        }

        private void CmdRespawn(BasePlayer player, string command, string[] args)
        {
            if (!_config.Settings.Enabled || !_config.Settings.RespawnCommandEnabled)
            {
                SendMessage(player, Lang("PluginDisabled"));
                return;
            }

            if (player == null || !player.IsConnected || player.IsDead())
                return;

            // Clearing inventory (DISABLED)
            // player.inventory.Strip();

            // Set the flag for processing in OnPlayerRespawn
            _respawnCommandFlag.Add(player.userID);

            // Killing the player
            player.Die();
            // Forced respawn after 0.1 seconds (if it doesn't work automatically)
            timer.Once(0.1f, () =>
            {
                if (player != null && player.IsConnected && player.IsDead())
                {
                    player.Respawn();
                }
            });
        }

        #endregion

        #region Hooks

        // When a player logs in (for the first time)
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !_config.Settings.Enabled)
                return;

            // Check if the player has already been on the server
            if (!_firstSpawnDone.Contains(player.userID))
            {
                _firstSpawnDone.Add(player.userID);
                // If the player has bypass permission, don't teleport
                if (HasBypassSpawn(player) || HasAdmin(player))
                    return;

                if (_spawnData.IsSet)
                {
                    // Teleport with a delay (so that the player can load)
                    timer.Once(0.5f, () =>
                    {
                        if (player != null && player.IsConnected)
                        {
                            DoTeleport(player);
                            if (_config.Settings.WelcomeMessageEnabled)
                                SendMessage(player, Lang("WelcomeMessage"));
                        }
                    });
                }
                else
                {
                    // If the spawn is not set, we try to find the default one
                    TryFindDefaultSpawn();
                    if (_spawnData.IsSet)
                    {
                        timer.Once(0.5f, () =>
                        {
                            if (player != null && player.IsConnected)
                            {
                                DoTeleport(player);
                                if (_config.Settings.WelcomeMessageEnabled)
                                    SendMessage(player, Lang("WelcomeMessage"));
                            }
                        });
                    }
                }
            }
        }

        // After death (respawn)
        private void OnPlayerRespawn(BasePlayer player)
        {
            if (player == null || !_config.Settings.Enabled)
                return;

            // Check if the respawn was called by the /respawn command
            if (_respawnCommandFlag.Contains(player.userID))
            {
                _respawnCommandFlag.Remove(player.userID);
                // If this is a respawn from the command, teleport to the spawn and display a message
                if (_spawnData.IsSet)
                {
                    timer.Once(0.2f, () =>
                    {
                        if (player != null && player.IsConnected)
                        {
                            DoTeleport(player);
                            SendMessage(player, Lang("RespawnCommandUsed"));
                        }
                    });
                }
                return;
            }

            // Ordinary death - teleport to spawn, if no bypass permission
            if (HasBypassSpawn(player) || HasAdmin(player))
                return;

            if (_spawnData.IsSet)
            {
                timer.Once(0.2f, () =>
                {
                    if (player != null && player.IsConnected)
                    {
                        DoTeleport(player);
                        if (_config.Settings.RespawnMessageEnabled)
                            SendMessage(player, Lang("RespawnMessage"));
                    }
                });
            }
        }

        // Prohibition of construction in the zone
        private object CanBuild(Planner plan, Construction prefab, Vector3 position)
        {
            if (plan == null || !_config.Settings.Enabled)
                return null;

            BasePlayer player = plan.GetOwnerPlayer();
            if (player == null)
                return null;

            if (HasAdmin(player))
                return null;

            if (IsInSpawnZone(position))
            {
                SendMessage(player, Lang("BuildingBlocked"));
                return false;
            }
            return null;
        }

        // Prohibition of dealing damage to structures
        private object OnStructureDamage(BuildingBlock block, HitInfo info)
        {
            if (block == null || info == null || !_config.Settings.Enabled)
                return null;

            BasePlayer attacker = info.Initiator as BasePlayer;
            if (attacker != null && HasAdmin(attacker))
                return null;

            if (IsInSpawnZone(block.transform.position))
            {
                return false; // prohibiting damage
            }
            return null;
        }

        // Protecting players from damage in the area
        private object OnEntityTakeDamage(BasePlayer player, HitInfo info)
        {
            if (player == null || info == null || !_config.Settings.Enabled)
                return null;

            // If the player is in the zone, cancel the damage
            if (IsInSpawnZone(player.transform.position))
            {
                // check only if the attacker is not an admin, then damage is prohibited. But if the attacker is an admin, can he do damage? In the requirement "whitespawn.admin can do all of the above" - i.e. the admin can build, break, damage. So if the attacker is an admin, then damage passes. If the attacker is an admin, can he also take damage? But usually the admin can do everything, but the immortality of the zone for everyone except admins? It is better to do this: if the attacker is an admin, then damage is allowed. If the attacker is an admin, but the attacker is not an admin, then damage is prohibited (since the player in the zone is immortal). However, if the attacker is not an admin, and the attacker is an admin, then damage should be prohibited, because the admin is also immortal in the zone? But the admin may wish to take damage? Most likely, the administrator should be able to take damage if he wants, but by condition, the zone gives immortality to everyone except admins? In the condition: "in the established spawn zone, there is a radius of the zone where players are immortal", without exceptions. But further "whitespawn.admin can do all of the above" - i.e. the admin can build, break, damage. This means that the admin can ignore prohibitions, but immortality affects everyone. Probably the admin is also immortal, but can damage others. Therefore, the logic is: if the target is in the zone, then damage is prohibited if the attacker is not an admin. If the attacker is an admin, then damage is allowed (even if the target is in the zone). If the target is not in the zone, then damage is allowed. If the attacker is an admin and the target is in the zone, damage is allowed. Also, if the target is an admin and the attacker is not an admin, then damage is prohibited, since the admin is also in the zone and is immortal.
                // Implement: if the target is in the zone, and the attacker does not have the right to admin, then we prohibit damage.
                BasePlayer attacker = info.Initiator as BasePlayer;
                if (attacker != null && HasAdmin(attacker))
                    return null; // admin can do damage
                else
                    return false; // prohibiting damage
            }
            return null;
        }

        #endregion

        #region Helpers

        private void StartZoneTracker()
        {
            if (_zoneTimer != null) return;

            // Check player positions every 1 second
            _zoneTimer = timer.Every(1f, () =>
            {
                if (!_spawnData.IsSet || !_config.Settings.Enabled) return;

                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player == null || !player.IsConnected || player.IsDead()) continue;

                    bool inZone = IsInSpawnZone(player.transform.position);
                    bool wasInZone = _inZoneTracker.Contains(player.userID);

                    if (inZone)
                    {
                        if (!wasInZone)
                        {
                            _inZoneTracker.Add(player.userID);
                        }
                        
                        // Force native SafeZone UI flag while inside
                        if (!player.HasPlayerFlag(BasePlayer.PlayerFlags.SafeZone))
                        {
                            player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, true);
                            player.SendNetworkUpdateImmediate();
                        }
                    }
                    else if (!inZone && wasInZone)
                    {
                        _inZoneTracker.Remove(player.userID);
                        
                        // Remove SafeZone UI flag when exiting the custom radius
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, false);
                        player.SendNetworkUpdateImmediate();

                        // Notify player that they are vulnerable
                        SendMessage(player, Lang("LeaveSafeZone"));
                    }
                }
            });
        }

        private bool IsInSpawnZone(Vector3 position)
        {
            if (!_spawnData.IsSet)
                return false;
            return Vector3.Distance(position, _spawnData.Position) <= _config.Settings.Radius;
        }

        private void TryFindDefaultSpawn()
        {
            // Looking for an Outpost or a Bandit
            string prefabName = _config.Settings.FindOutpostFirst ? "assets/bundled/prefabs/static/outpost.prefab" : "assets/bundled/prefabs/static/bandit_town.prefab";
            GameObject obj = GameObject.Find(prefabName);
            if (obj == null && _config.Settings.FindOutpostFirst)
            {
                // If Outpost is not found, search for Bandit
                obj = GameObject.Find("assets/bundled/prefabs/static/bandit_town.prefab");
            }
            if (obj == null)
            {
                PrintWarning("Default spawn (Outpost/Bandit) not found. Please set spawn manually.");
                return;
            }

            // Do we take the center or a random point? It is better to take a spawn position inside (for example, the center)
            Vector3 pos = obj.transform.position;
            // You can add a Y offset to avoid being underground
            pos.y += 1f;

            _spawnData.Position = pos;
            _spawnData.Rotation = Quaternion.identity;
            _spawnData.IsSet = true;
            SaveSpawnData();
            Puts($"Default spawn set to {prefabName} at {pos}");
        }

        private void SendMessage(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message))
                return;

            string final = $"{Prefix} {message}";
            player.SendConsoleCommand("chat.add", 2, PluginIcon, final);
        }

        private string Lang(string key, params object[] args)
        {
            string msg = lang.GetMessage(key, this);
            if (args.Length > 0)
                msg = string.Format(msg, args);
            return msg;
        }

        private void PrintBanner()
        {
            Puts("==================================================");
            Puts($"{Name} loaded successfully.");
            Puts($"Version: {PluginVersion}");
            Puts($"Spawn set: {_spawnData.IsSet}, Radius: {_config.Settings.Radius}, Timer: {_config.Settings.SpawnTimer}s");
            Puts("==================================================");
        }

        #endregion
    }
}