using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WhiteSpawn", "whitecristafer", "1.5.0")]
    [Description("WhiteSpawn - The system is paired with a safe zone")]
    public class WhiteSpawn : RustPlugin
    {
        #region Constants

        private const ulong PluginIcon = 76561198209258869; // SteamID
        private const string PluginVersion = "1.5.0";
        private const string Prefix = "<size=12><color=#66ccff><b>WhiteSpawn</b></color></size> |";

        // Access rights
        private const string AdminPermission = "whitespawn.admin";
        private const string BypassTimerPermission = "whitespawn.bypasstimer";
        private const string BypassSpawnPermission = "whitespawn.bypassspawn";

        // Data paths
        private string _dataPath;
        private string _spawnDataFile;
        private string _playerDataFile;

        // Default teleportation timer
        private const float DefaultSpawnTimer = 5f;

        // Warning cooldown to keep chat readable
        private const float WarningCooldown = 5f;

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
            public bool BlockWeaponsAndTools = true; // force-holster weapons/tools while inside the zone
            public bool FreezeMetabolismInZone = true; // no hunger/thirst/poison/radiation/bleeding loss inside the zone

            public bool ChatNotificationsEnabled = true; // extra chat feedback for zone actions
            public bool BlockLootingInZone = true; // players inside the zone cannot be looted
            public bool BlockBuildingInZone = true; // players cannot build or deploy inside the zone
            public bool AutoOpenDoorsInZone = true; // nearby doors are opened for players in the zone
            public float DoorOpenRadius = 2.5f; // near distance for door assistance
            public float DoorOpenInterval = 0.75f; // throttle for door scanning
            public float DoorCloseDelay = 3.0f;

            public bool RestoreLogoutPositionOnReconnect = false; // keep players at last logout position on reconnect
        }

        #endregion

        #region Data

        private SpawnData _spawnData;
        private PlayerData _playerData;

        private sealed class SpawnData
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public bool IsSet; // is spawn installed manually
        }

        private sealed class PlayerData
        {
            public HashSet<ulong> SeenPlayers = new HashSet<ulong>();
            public Dictionary<ulong, SavedPosition> LogoutPositions = new Dictionary<ulong, SavedPosition>();
        }

        private sealed class SavedPosition
        {
            public Vector3 Position;
            public Quaternion Rotation;
        }

        // zone tracking
        private Timer _zoneTimer;
        private Timer _doorTimer;
        private readonly HashSet<ulong> _respawnCommandFlag = new HashSet<ulong>();
        private readonly HashSet<ulong> _inZoneTracker = new HashSet<ulong>();
        private readonly Dictionary<ulong, float> _lastWeaponWarn = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastLootWarn = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastBuildWarn = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastDamageWarn = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastDoorPulse = new Dictionary<ulong, float>();

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
                ["BuildingBlocked"] = "You cannot build or deploy in the safe zone.",
                ["LootBlocked"] = "You cannot loot players inside the safe zone.",
                ["DamageBlocked"] = "Damage is blocked inside the safe zone.",
                ["EnteredSafeZone"] = "You entered the safe spawn zone.",
                ["LeaveSafeZone"] = "You have left the safe spawn zone. You are now vulnerable!",
                ["WeaponBlocked"] = "You cannot hold weapons or tools inside the safe zone.",
                ["LogoutPositionRestored"] = "Your last logout position has been restored.",
                ["AdminBypass"] = "Admin bypass active.",
                ["InvalidCommand"] = "Unknown command. Use /spawn or /ws help.",
                ["HelpHeader"] = "WhiteSpawn Commands:",
                ["HelpSpawn"] = "/spawn - Teleport to spawn",
                ["HelpSetSpawn"] = "/setspawn - Set spawn point (admin)",
                ["HelpRadius"] = "/ws radius <num> - Set safe zone radius (admin)",
                ["HelpRespawn"] = "/respawn - Respawn yourself (lose items)",
                ["HelpStatus"] = "/ws status - Show plugin status",
                ["Status"] = "WhiteSpawn: Enabled: {0}, Radius: {1}, Spawn Timer: {2}s, Restore Logout Position: {3}, Door Assist: {4}, Chat Notices: {5}",
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
                ["BuildingBlocked"] = "Вы не можете строить или ставить предметы в безопасной зоне.",
                ["LootBlocked"] = "В безопасной зоне нельзя лутать игроков.",
                ["DamageBlocked"] = "Урон в безопасной зоне заблокирован.",
                ["EnteredSafeZone"] = "Вы вошли в безопасную зону спавна.",
                ["LeaveSafeZone"] = "Вы покинули безопасную зону спавна. Теперь вы уязвимы!",
                ["WeaponBlocked"] = "В безопасной зоне нельзя держать в руках оружие или инструменты.",
                ["LogoutPositionRestored"] = "Ваша последняя точка выхода восстановлена.",
                ["AdminBypass"] = "Администратор имеет право на обход.",
                ["InvalidCommand"] = "Неизвестная команда. Используйте /spawn или /ws help.",
                ["HelpHeader"] = "Команды WhiteSpawn:",
                ["HelpSpawn"] = "/spawn - Телепорт на спавн",
                ["HelpSetSpawn"] = "/setspawn - Установить точку спавна (админ)",
                ["HelpRadius"] = "/ws radius <число> - Установить радиус зоны (админ)",
                ["HelpRespawn"] = "/respawn - Переродиться (потеря вещей)",
                ["HelpStatus"] = "/ws status - Показать статус плагина",
                ["Status"] = "WhiteSpawn: Включён: {0}, Радиус: {1}, Таймер: {2}с, Возврат на выход: {3}, Авто-движение дверей: {4}, Чат-уведомления: {5}",
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
            LoadPlayerData();
            RegisterCommands();
        }

        private void OnServerInitialized()
        {
            // If the spawn is not set, we will try to find an Outpost or Bandit automatically
            if (!_spawnData.IsSet)
                TryFindDefaultSpawn();

            // Start the periodic in-zone effects ticker (metabolism freeze).
            // Entering/leaving the zone itself, and the safe-zone flag, are handled per-tick in OnPlayerTick.
            StartZoneEffectsTimer();
            StartDoorMonitorTimer();
            CloseAllDoorsInZone();

            PrintBanner();
        }

        private void Unload()
        {
            SaveSpawnData();
            SavePlayerData();

            _doorTimer?.Destroy();

            _zoneTimer?.Destroy();

            _inZoneTracker.Clear();
            _lastWeaponWarn.Clear();
            _lastLootWarn.Clear();
            _lastBuildWarn.Clear();
            _lastDamageWarn.Clear();
            _lastDoorPulse.Clear();

            // Make sure nobody is left with the SafeZone flag stuck on if the plugin unloads
            // while a player is standing inside the zone.
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.HasPlayerFlag(BasePlayer.PlayerFlags.SafeZone))
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, false);
            }
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
            if (_config.Settings.DoorOpenRadius <= 0) _config.Settings.DoorOpenRadius = 2.5f;
            if (_config.Settings.DoorOpenInterval < 0.1f) _config.Settings.DoorOpenInterval = 0.75f;
            if (_config.Settings.DoorCloseDelay <= 0f) _config.Settings.DoorCloseDelay = 3.0f;

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
            _playerDataFile = Path.Combine(_dataPath, "players.json");

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

            if (_spawnData == null)
                _spawnData = new SpawnData { IsSet = false };
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

        private void LoadPlayerData()
        {
            if (File.Exists(_playerDataFile))
            {
                try
                {
                    string json = File.ReadAllText(_playerDataFile);
                    _playerData = JsonConvert.DeserializeObject<PlayerData>(json);
                }
                catch (Exception ex)
                {
                    PrintError($"Failed to load player data: {ex.Message}");
                    _playerData = new PlayerData();
                }
            }
            else
            {
                _playerData = new PlayerData();
            }

            if (_playerData == null)
                _playerData = new PlayerData();

            if (_playerData.SeenPlayers == null)
                _playerData.SeenPlayers = new HashSet<ulong>();

            if (_playerData.LogoutPositions == null)
                _playerData.LogoutPositions = new Dictionary<ulong, SavedPosition>();
        }

        private void SavePlayerData()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_playerData, Formatting.Indented);
                File.WriteAllText(_playerDataFile, json);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to save player data: {ex.Message}");
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

                    DoTeleportToSpawn(player);
                });
            }
            else
            {
                DoTeleportToSpawn(player);
            }
        }

        private void DoTeleportToSpawn(BasePlayer player)
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

        private void TeleportToSavedPosition(BasePlayer player, SavedPosition saved)
        {
            if (player == null || saved == null || !player.IsConnected)
                return;

            Effect.server.Run("assets/prefabs/misc/transferable/effects/teleport.prefab", player.transform.position, Vector3.up);

            player.Teleport(saved.Position);
            player.eyes.rotation = saved.Rotation;
            player.SendNetworkUpdateImmediate();

            player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, IsInSpawnZone(saved.Position));

            SendZoneMessage(player, Lang("LogoutPositionRestored"));
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
                    string status = string.Format(
                        Lang("Status"),
                        _config.Settings.Enabled,
                        _config.Settings.Radius,
                        _config.Settings.SpawnTimer,
                        _config.Settings.RestoreLogoutPositionOnReconnect,
                        _config.Settings.AutoOpenDoorsInZone,
                        _config.Settings.ChatNotificationsEnabled
                    );
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
                    player.Respawn();
            });
        }

        #endregion

        #region Hooks

        // When a player logs in
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !_config.Settings.Enabled)
                return;

            bool seenBefore = _playerData.SeenPlayers.Contains(player.userID);

            if (!seenBefore)
            {
                _playerData.SeenPlayers.Add(player.userID);
                SavePlayerData();

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
                            DoTeleportToSpawn(player);
                            if (_config.Settings.WelcomeMessageEnabled)
                                SendZoneMessage(player, Lang("WelcomeMessage"));
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
                                DoTeleportToSpawn(player);
                                if (_config.Settings.WelcomeMessageEnabled)
                                    SendZoneMessage(player, Lang("WelcomeMessage"));
                            }
                        });
                    }
                }

                return;
            }

            if (HasBypassSpawn(player) || HasAdmin(player))
                return;

            if (_config.Settings.RestoreLogoutPositionOnReconnect && _playerData.LogoutPositions.TryGetValue(player.userID, out SavedPosition saved))
            {
                timer.Once(0.5f, () =>
                {
                    if (player != null && player.IsConnected)
                        TeleportToSavedPosition(player, saved);
                });
            }
        }

        // Save last known position so reconnects can be restored if the option is enabled.
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            _inZoneTracker.Remove(player.userID);
            _lastWeaponWarn.Remove(player.userID);
            _lastLootWarn.Remove(player.userID);
            _lastBuildWarn.Remove(player.userID);
            _lastDamageWarn.Remove(player.userID);
            _lastDoorPulse.Remove(player.userID);

            if (!_config.Settings.Enabled || player.IsDead())
                return;

            if (!_config.Settings.RestoreLogoutPositionOnReconnect)
                return;

            _playerData.LogoutPositions[player.userID] = new SavedPosition
            {
                Position = player.transform.position,
                Rotation = player.eyes.rotation
            };
            SavePlayerData();
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
                            DoTeleportToSpawn(player);
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
                        DoTeleportToSpawn(player);
                        if (_config.Settings.RespawnMessageEnabled)
                            SendZoneMessage(player, Lang("RespawnMessage"));
                    }
                });
            }
        }

        // Prohibition of construction in the zone.
        // This hook blocks the placement before the entity even exists.
        private object CanBuild(Planner plan, Construction prefab, Construction.Target target)
        {
            if (plan == null || !_config.Settings.Enabled || !_config.Settings.BlockBuildingInZone)
                return null;

            BasePlayer player = plan.GetOwnerPlayer();
            if (player == null)
                return null;
            
            if (HasAdmin(player))
                return null;

            // target.position can occasionally be Vector3.zero (e.g. for some socketed pieces) - fall back to the player's own position in that case.
            Vector3 position = target.position != Vector3.zero ? target.position : player.transform.position;
            bool isAttachedToZoneEntity = target.entity != null && IsInSpawnZone(target.entity.transform.position);

            if (IsInSpawnZone(position) || IsInSpawnZone(player.transform.position) || isAttachedToZoneEntity)
            {
                WarnBuildBlocked(player);
                return false;
            }

            return null;
        }

        // Extra catch for deployables and edge cases where a piece slips through CanBuild.
        private void OnEntityBuilt(Planner plan, GameObject gameObject)
        {
            if (plan == null || gameObject == null || !_config.Settings.Enabled || !_config.Settings.BlockBuildingInZone)
                return;

            BasePlayer player = plan.GetOwnerPlayer();

            if (HasAdmin(player))
                    return;

            BaseEntity entity = gameObject.GetComponent<BaseEntity>();
            if (player == null || entity == null || entity.IsDestroyed)
                return;

            if (IsInSpawnZone(player.transform.position) || IsInSpawnZone(entity.transform.position))
            {
                WarnBuildBlocked(player);
                NextTick(() =>
                {
                    if (entity != null && !entity.IsDestroyed)
                        entity.Kill();
                });
            }
        }

        // Protection from damage for anything inside the zone (players AND structures).
        // The zone is treated as hard safe-state: no damage exceptions.
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || !_config.Settings.Enabled)
                return null;

            if (!IsInSpawnZone(entity.transform.position))
                return null;

            if (entity is BasePlayer victim)
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (TryWarn(_lastDamageWarn, victim.userID, now, WarningCooldown))
                    SendZoneMessage(victim, Lang("DamageBlocked"));
            }

            return false; // block all damage to anything inside the safe zone
        }

        // Blocks looting of players that are inside the safe zone.
        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (!_config.Settings.Enabled || !_config.Settings.BlockLootingInZone)
                return null;

            if (target == null || looter == null)
                return null;

            bool targetProtected = IsInSpawnZone(target.transform.position);
            bool looterProtected = IsInSpawnZone(looter.transform.position);

            if (!targetProtected && !looterProtected)
                return null;

            WarnLootBlocked(looter);
            return false;
        }

        // Some inventory looting paths use the generic entity loot hook.
        private object CanLootEntity(BasePlayer looter, BaseEntity entity)
        {
            if (!_config.Settings.Enabled || !_config.Settings.BlockLootingInZone)
                return null;

            if (looter == null || entity == null)
                return null;

            // If they try to trick a live player
            if (entity is BasePlayer target)
            {
                if (IsInSpawnZone(target.transform.position) || IsInSpawnZone(looter.transform.position))
                {
                    WarnLootBlocked(looter);
                    return false;
                }
            }

            // If they try to hide the player's corpse (PlayerCorpse)
            if (entity is PlayerCorpse corpse)
            {
                // We check whether the corpse or the loot is in a safe zone.
                if (IsInSpawnZone(corpse.transform.position) || IsInSpawnZone(looter.transform.position))
                {
                    // If the looter is not the owner of this corpse block the loot
                    if (corpse.playerSteamID != looter.userID)
                    {
                        WarnLootBlocked(looter);
                        return false;
                    }
                }
            }

            if (IsInSpawnZone(looter.transform.position) && entity is BasePlayer)
            {
                WarnLootBlocked(looter);
                return false;
            }

            return null;
        }

        // Runs on (almost) every player tick - much more frequent than the old 1-second timer.
        // This keeps the safe-zone flag stable, forces restricted items away, and can assist doors.
        private object OnPlayerTick(BasePlayer player, PlayerTick msg, bool wasPlayerStalled)
        {
            if (player == null || !_config.Settings.Enabled || !_spawnData.IsSet)
                return null;

            if (!player.IsConnected || player.IsDead())
                return null;

            bool inZone = IsInSpawnZone(player.transform.position);
            bool wasInZone = _inZoneTracker.Contains(player.userID);

            if (inZone)
            {
                if (!wasInZone)
                {
                    _inZoneTracker.Add(player.userID);
                    SendZoneMessage(player, Lang("EnteredSafeZone"));
                }

                if (!player.HasPlayerFlag(BasePlayer.PlayerFlags.SafeZone))
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, true);

                if (_config.Settings.BlockWeaponsAndTools)
                    EnforceNoWeapons(player);

                if (_config.Settings.FreezeMetabolismInZone)
                    ApplyMetabolismFreeze(player);

                if (_config.Settings.AutoOpenDoorsInZone)
                    TryOpenNearbyDoors(player);
            }
            else if (wasInZone)
            {
                _inZoneTracker.Remove(player.userID);

                if (player.HasPlayerFlag(BasePlayer.PlayerFlags.SafeZone))
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, false);

                SendZoneMessage(player, Lang("LeaveSafeZone"));
            }
            else if (player.HasPlayerFlag(BasePlayer.PlayerFlags.SafeZone))
            {
                // Keep the flag aligned even if the player was moved by another plugin.
                player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, false);
            }

            return null;
        }

        #endregion

        #region Helpers

        private bool IsInSpawnZone(Vector3 position)
        {
            if (!_spawnData.IsSet)
                return false;

            return Vector3.Distance(position, _spawnData.Position) <= _config.Settings.Radius;
        }

        // Periodic ticker that keeps hunger/thirst/poison/radiation/bleeding frozen for
        // players that OnPlayerTick has already marked as being inside the zone.
        private void StartZoneEffectsTimer()
        {
            if (_zoneTimer != null) return;

            _zoneTimer = timer.Every(1f, () =>
            {
                if (!_config.Settings.Enabled || !_config.Settings.FreezeMetabolismInZone) return;
                if (_inZoneTracker.Count == 0) return;

                foreach (ulong userId in _inZoneTracker)
                {
                    BasePlayer player = BasePlayer.FindByID(userId);
                    if (player == null || !player.IsConnected || player.IsDead()) continue;
                    ApplyMetabolismFreeze(player);
                }
            });
        }

        // Keeps calories/hydration topped up and zeroes out poison/radiation/bleeding,
        // so players can't lose health (or get hungry/thirsty) while inside the zone.
        private void ApplyMetabolismFreeze(BasePlayer player)
        {
            PlayerMetabolism metabolism = player.metabolism;
            if (metabolism == null) return;

            metabolism.calories.value = metabolism.calories.max;
            metabolism.hydration.value = metabolism.hydration.max;
            metabolism.poison.value = 0f;
            metabolism.radiation_poison.value = 0f;
            metabolism.bleeding.value = 0f;

            player.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
        }

        // Force-holsters the player's current item if it's a weapon or tool.
        // Runs from OnPlayerTick, so this kicks in almost immediately after a player
        // raises a banned item, rather than fighting the client every single frame.
        private void EnforceNoWeapons(BasePlayer player)
        {
            Item active = player.GetActiveItem();
            if (!IsRestrictedItem(active))
                return;

            player.UpdateActiveItem(default(ItemId));

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (TryWarn(_lastWeaponWarn, player.userID, now, WarningCooldown))
                SendZoneMessage(player, Lang("WeaponBlocked"));
        }

        private static bool IsRestrictedItem(Item item)
        {
            if (item?.info == null) return false;
            return item.info.category == ItemCategory.Weapon || item.info.category == ItemCategory.Tool;
        }

        // Method for forcibly closing all doors on the spawn (called at startup)
        private void CloseAllDoorsInZone()
        {
            if (!_spawnData.IsSet || !_config.Settings.AutoOpenDoorsInZone) return;

            Collider[] colliders = Physics.OverlapSphere(_spawnData.Position, _config.Settings.Radius, ~0, QueryTriggerInteraction.Collide);
            if (colliders == null || colliders.Length == 0) return;

            HashSet<Door> processed = new HashSet<Door>();

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;

                Door door = collider.GetComponentInParent<Door>();
                if (door == null || door.IsDestroyed || !door.IsOpen() || processed.Contains(door))
                    continue;

                processed.Add(door);
                door.SetOpen(false, true);
            }
        }

        // Global timer: checks ALL open doors within the spawn radius once per second
        private void StartDoorMonitorTimer()
        {
            if (_doorTimer != null) return;

            _doorTimer = timer.Every(1f, () =>
            {
                if (!_config.Settings.Enabled || !_config.Settings.AutoOpenDoorsInZone || !_spawnData.IsSet) return;

                // We find absolutely all objects in the spawn zone
                Collider[] colliders = Physics.OverlapSphere(_spawnData.Position, _config.Settings.Radius, ~0, QueryTriggerInteraction.Collide);
                if (colliders == null || colliders.Length == 0) return;

                HashSet<Door> processedDoors = new HashSet<Door>();
                float checkRadius = _config.Settings.DoorOpenRadius;

                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider collider = colliders[i];
                    if (collider == null) continue;

                    Door door = collider.GetComponentInParent<Door>();
                    // We are only interested in OPEN doors that we haven't checked yet in this cycle
                    if (door == null || door.IsDestroyed || !door.IsOpen() || processedDoors.Contains(door))
                        continue;

                    processedDoors.Add(door);

                    // We check for the presence of living players within the radius of this specific door (layer 17 — Player_Server)
                    Collider[] doorColliders = Physics.OverlapSphere(door.transform.position, checkRadius, 1 << 17, QueryTriggerInteraction.Collide);
                    bool playerNearby = false;

                    if (doorColliders != null)
                    {
                        for (int j = 0; j < doorColliders.Length; j++)
                        {
                            BasePlayer nearbyPlayer = doorColliders[j].GetComponentInParent<BasePlayer>();
                            if (nearbyPlayer != null && nearbyPlayer.IsConnected && !nearbyPlayer.IsDead())
                            {
                                playerNearby = true;
                                break;
                            }
                        }
                    }

                    // If there are no players near the open door — forcibly close it
                    if (!playerNearby)
                    {
                        door.SetOpen(false, true);
                    }
                }
            });
        }

        private void TryOpenNearbyDoors(BasePlayer player)
        {
            if (player == null || !_config.Settings.AutoOpenDoorsInZone)
                return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (!TryWarn(_lastDoorPulse, player.userID, now, _config.Settings.DoorOpenInterval))
                return;

            Collider[] colliders = Physics.OverlapSphere(player.transform.position, _config.Settings.DoorOpenRadius, ~0, QueryTriggerInteraction.Collide);
            if (colliders == null || colliders.Length == 0)
                return;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                Door door = collider.GetComponentInParent<Door>();
                if (door == null || door.IsDestroyed || door.IsOpen())
                    continue;

                door.SetOpen(true, true);
            }
        }

        private void WarnLootBlocked(BasePlayer player)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (TryWarn(_lastLootWarn, player.userID, now, WarningCooldown))
                SendZoneMessage(player, Lang("LootBlocked"));
        }

        private void WarnBuildBlocked(BasePlayer player)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (TryWarn(_lastBuildWarn, player.userID, now, WarningCooldown))
                SendZoneMessage(player, Lang("BuildingBlocked"));
        }

        private bool TryWarn(Dictionary<ulong, float> storage, ulong userId, float now, float cooldown)
        {
            if (storage == null)
                return true;

            if (storage.TryGetValue(userId, out float last) && now - last < cooldown)
                return false;

            storage[userId] = now;
            return true;
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

            // We take the center and lift it a little so the player doesn't get stuck in terrain.
            Vector3 pos = obj.transform.position;
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

        private void SendZoneMessage(BasePlayer player, string message)
        {
            if (!_config.Settings.ChatNotificationsEnabled)
                return;

            SendMessage(player, message);
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
            Puts($"Reconnect restore: {_config.Settings.RestoreLogoutPositionOnReconnect}, Door assist: {_config.Settings.AutoOpenDoorsInZone}, Chat notices: {_config.Settings.ChatNotificationsEnabled}");
            Puts("==================================================");
        }

        #endregion
    }
}
