using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Reflection;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WhiteSpawn", "whitecristafer", "1.6.3")]
    [Description("WhiteSpawn - Advanced safe zone system with decay prevention and powerless devices support")]
    public class WhiteSpawn : RustPlugin
    {
        #region Constants

        private const ulong PluginIcon = 76561198209258869;
        private const string PluginVersion = "1.6.3";
        private const string Prefix = "<size=12><color=#66ccff><b>WhiteSpawn</b></color></size> |";
        private const string AdminPermission = "whitespawn.admin";
        private const string BypassTimerPermission = "whitespawn.bypasstimer";
        private const string BypassSpawnPermission = "whitespawn.bypassspawn";
        private const float DefaultSpawnTimer = 5f;
        private const float WarningCooldown = 5f;
        private const float TeleportDelay = 0.5f;
        private const float RespawnDelay = 0.2f;
        private const float ZoneCheckInterval = 1f;
        private const float DoorCheckInterval = 1f;

        private string _dataPath;
        private string _spawnDataFile;
        private string _playerDataFile;

        #endregion

        #region Config

        private PluginConfig _config;

        private sealed class PluginConfig
        {
            public SettingsConfig Settings { get; set; } = new SettingsConfig();
        }

        private sealed class SettingsConfig
        {
            public bool Enabled { get; set; } = true;
            public float SpawnTimer { get; set; } = 5f;
            public float Radius { get; set; } = 10f;
            public bool WelcomeMessageEnabled { get; set; } = true;
            public bool RespawnMessageEnabled { get; set; } = true;
            public bool RespawnCommandEnabled { get; set; } = true;
            public bool FindOutpostFirst { get; set; } = true;
            public bool BlockWeaponsAndTools { get; set; } = true;
            public bool FreezeMetabolismInZone { get; set; } = true;
            public bool ChatNotificationsEnabled { get; set; } = true;
            public bool BlockLootingInZone { get; set; } = true;
            public bool BlockBuildingInZone { get; set; } = true;
            public bool AutoOpenDoorsInZone { get; set; } = true;
            public float DoorOpenRadius { get; set; } = 2.5f;
            public float DoorOpenInterval { get; set; } = 0.75f;
            public float DoorCloseDelay { get; set; } = 3.0f;
            public bool RestoreLogoutPositionOnReconnect { get; set; } = false;
            // Developer: Advanced features for respawn and building management
            public bool RespawnOnlyByCommand { get; set; } = true;
            public bool PreventBuildingDecay { get; set; } = true;
            public bool PowerlessDevicesEnabled { get; set; } = false;
        }

        #endregion

        #region Data

        private SpawnData _spawnData;
        private PlayerData _playerData;

        private sealed class SpawnData
        {
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; }
            public bool IsSet { get; set; }
        }

        private sealed class PlayerData
        {
            public HashSet<ulong> SeenPlayers { get; set; } = new HashSet<ulong>();
            public Dictionary<ulong, SavedPosition> LogoutPositions { get; set; } = new Dictionary<ulong, SavedPosition>();
        }

        private sealed class SavedPosition
        {
            public Vector3 Position { get; set; }
            public Quaternion Rotation { get; set; }
        }

        // Developer: Zone tracking and warning throttle - cached for performance
        private Timer _zoneTimer;
        private Timer _doorTimer;
        private Timer _powerlessDeviceTimer;
        private readonly HashSet<ulong> _respawnCommandFlag = new HashSet<ulong>();
        private readonly HashSet<ulong> _inZoneTracker = new HashSet<ulong>();
        private readonly Dictionary<ulong, float> _lastWeaponWarn = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastLootWarn = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastBuildWarn = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastDamageWarn = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> _lastDoorPulse = new Dictionary<ulong, float>();
        private readonly HashSet<NetworkableId> _powerlessDevices = new HashSet<NetworkableId>();

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
                ["PlayerNotFound"] = "Player not found.",
                ["ReloadSuccess"] = "WhiteSpawn reloaded.",
                ["ReloadFailed"] = "Reload failed. See server logs.",
                ["SetUsage"] = "Usage: /ws set <option> <value>",
                ["GetUsage"] = "Usage: /ws get <option>",
                ["SetSuccess"] = "Set {0} = {1}",
                ["SetFailed"] = "Failed to set {0}: {1}",
                ["SetSpawnError"] = "An error occurred while setting spawn",
                ["TeleportedPlayer"] = "Teleported {0} to spawn.",
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
                ["HelpSpawnAdmin"] = "/spawn <steamid|nickname> - Teleport target to spawn (admin)",
                ["HelpSet"] = "/ws set <option> <value> - Set config option (admin)",
                ["HelpGet"] = "/ws get <option> - Get config option (admin)",
                ["HelpReload"] = "/ws reload - Reload plugin (admin)",
                ["HelpPowerless"] = "/ws set powerlessdevices <true|false> - Enable/disable powerless devices",
                ["HelpSetSpawn"] = "/setspawn - Set spawn point (admin)",
                ["HelpRadius"] = "/ws radius <num> - Set safe zone radius (admin)",
                ["HelpRespawn"] = "/respawn - Respawn yourself (lose items)",
                ["HelpStatus"] = "/ws status - Show plugin status",
                ["ConfigHeader"] = "<size=16><color=#66ccff><b>WhiteSpawn Configuration</b></color></size>",
                ["ConfigLine"] = "{0}: {1}",
                ["ConfigEnabled"] = "Plugin Enabled",
                ["ConfigSpawnTimer"] = "Spawn Timer (sec)",
                ["ConfigRadius"] = "Safe Zone Radius",
                ["ConfigWelcome"] = "Welcome Message",
                ["ConfigRespawnMsg"] = "Respawn Message",
                ["ConfigRespawnCmd"] = "Respawn Command",
                ["ConfigFindOutpost"] = "Find Outpost First",
                ["ConfigBlockWeapons"] = "Block Weapons/Tools",
                ["ConfigFreezeMeta"] = "Freeze Metabolism",
                ["ConfigChatNotif"] = "Chat Notifications",
                ["ConfigBlockLoot"] = "Block Looting",
                ["ConfigBlockBuild"] = "Block Building",
                ["ConfigAutoOpenDoors"] = "Auto Open Doors",
                ["ConfigDoorRadius"] = "Door Open Radius",
                ["ConfigDoorInterval"] = "Door Open Interval",
                ["ConfigDoorCloseDelay"] = "Door Close Delay",
                ["ConfigRestoreLogout"] = "Restore Logout Position",
                ["ConfigRespawnOnlyCmd"] = "Respawn Only by Command",
                ["ConfigPreventDecay"] = "Prevent Building Decay",
                ["ConfigPowerlessDevices"] = "Powerless Devices",
                ["ConfigHelp"] = "Use /ws set <option> <value> to change settings.",
                ["ConfigOptionNotFound"] = "Option not found. Use /ws config to see all options.",
                ["Status"] = "WhiteSpawn: Enabled: {0}, Radius: {1}, Spawn Timer: {2}s, Respawn cmd only: {3}, Decay prevention: {4}, Powerless devices: {5}",
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
                ["PlayerNotFound"] = "Игрок не найден.",
                ["ReloadSuccess"] = "WhiteSpawn перезагружен.",
                ["ReloadFailed"] = "Перезагрузка не удалась. Смотрите логи сервера.",
                ["SetUsage"] = "Использование: /ws set <опция> <значение>",
                ["GetUsage"] = "Использование: /ws get <опция>",
                ["SetSuccess"] = "Установлено {0} = {1}",
                ["SetFailed"] = "Не удалось установить {0}: {1}",
                ["SetSpawnError"] = "Произошла ошибка при установке точки спавна",
                ["TeleportedPlayer"] = "Игрок {0} телепортирован на спавн.",
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
                ["HelpSpawnAdmin"] = "/spawn <steamid|никнейм> - Телепортировать игрока на спавн (админ)",
                ["HelpSet"] = "/ws set <опция> <значение> - Установить параметр конфига (админ)",
                ["HelpGet"] = "/ws get <опция> - Получить параметр конфига (админ)",
                ["HelpReload"] = "/ws reload - Перезагрузить плагин (админ)",
                ["HelpPowerless"] = "/ws set powerlessdevices <true|false> - Включить/выключить устройства без питания",
                ["HelpSetSpawn"] = "/setspawn - Установить точку спавна (админ)",
                ["HelpRadius"] = "/ws radius <число> - Установить радиус зоны (админ)",
                ["HelpRespawn"] = "/respawn - Переродиться (потеря вещей)",
                ["HelpStatus"] = "/ws status - Показать статус плагина",
                ["ConfigHeader"] = "<size=16><color=#66ccff><b>Конфигурация WhiteSpawn</b></color></size>",
                ["ConfigLine"] = "{0}: {1}",
                ["ConfigEnabled"] = "Плагин включён",
                ["ConfigSpawnTimer"] = "Таймер спавна (сек)",
                ["ConfigRadius"] = "Радиус зоны",
                ["ConfigWelcome"] = "Приветственное сообщение",
                ["ConfigRespawnMsg"] = "Сообщение о респавне",
                ["ConfigRespawnCmd"] = "Команда респавна",
                ["ConfigFindOutpost"] = "Искать аванпост сначала",
                ["ConfigBlockWeapons"] = "Блокировать оружие/инструменты",
                ["ConfigFreezeMeta"] = "Замораживать метаболизм",
                ["ConfigChatNotif"] = "Уведомления в чат",
                ["ConfigBlockLoot"] = "Блокировать лутание",
                ["ConfigBlockBuild"] = "Блокировать строительство",
                ["ConfigAutoOpenDoors"] = "Автооткрытие дверей",
                ["ConfigDoorRadius"] = "Радиус открытия дверей",
                ["ConfigDoorInterval"] = "Интервал открытия дверей",
                ["ConfigDoorCloseDelay"] = "Задержка закрытия дверей",
                ["ConfigRestoreLogout"] = "Восстанавливать позицию выхода",
                ["ConfigRespawnOnlyCmd"] = "Респавн только по команде",
                ["ConfigPreventDecay"] = "Защита от гниения построек",
                ["ConfigPowerlessDevices"] = "Устройства без питания",
                ["ConfigHelp"] = "Используйте /ws set <опция> <значение> для изменения.",
                ["ConfigOptionNotFound"] = "Опция не найдена. Используйте /ws config для просмотра всех опций.",
                ["Status"] = "WhiteSpawn: Включён: {0}, Радиус: {1}, Таймер: {2}с, Спавн по команде: {3}, Защита от гниения: {4}, Устройства без питания: {5}",
            }, this, "ru");
        }

        #endregion

        #region Initialization

        private void Init()
        {
            try
            {
                EnsureFolders();
                RegisterPermissions();
            }
            catch (Exception ex)
            {
                PrintError($"Initialization error: {ex}");
            }
        }

        private void Loaded()
        {
            try
            {
                LoadConfig();
                LoadSpawnData();
                LoadPlayerData();
                RegisterCommands();
            }
            catch (Exception ex)
            {
                PrintError($"Load error: {ex}");
            }
        }

        private void OnServerInitialized()
        {
            try
            {
                if (!_spawnData.IsSet)
                    TryFindDefaultSpawn();

                StartZoneEffectsTimer();
                StartDoorMonitorTimer();
                if (_config.Settings.PowerlessDevicesEnabled)
                    StartPowerlessDeviceTimer();
                CloseAllDoorsInZone();
                PrintBanner();
            }
            catch (Exception ex)
            {
                PrintError($"Server initialization error: {ex}");
            }
        }

        private void Unload()
        {
            try
            {
                SaveSpawnData();
                SavePlayerData();
                _doorTimer?.Destroy();
                _zoneTimer?.Destroy();
                _powerlessDeviceTimer?.Destroy();
                
                foreach (NetworkableId id in _powerlessDevices)
                {
                    IOEntity ioEntity = BaseNetworkable.serverEntities.Find(id) as IOEntity;
                    if (ioEntity != null && !ioEntity.IsDestroyed)
                    {
                        ioEntity.UpdateFromInput(0, 0);
                        ioEntity.currentEnergy = 0;
                        ioEntity.SetFlag(BaseEntity.Flags.On, false);
                        if (ioEntity is AutoTurret turret && turret.IsOnline())
                        {
                            turret.Shutdown();
                        }
                        ioEntity.SendNetworkUpdate();
                    }
                }

                _inZoneTracker.Clear();
                _lastWeaponWarn.Clear();
                _lastLootWarn.Clear();
                _lastBuildWarn.Clear();
                _lastDamageWarn.Clear();
                _lastDoorPulse.Clear();
                _powerlessDevices.Clear();

                // Developer: Clean up SafeZone flag to prevent stuck state on unload
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (player?.HasPlayerFlag(BasePlayer.PlayerFlags.SafeZone) == true)
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, false);
                }
            }
            catch (Exception ex)
            {
                PrintError($"Unload error: {ex}");
            }
        }

        #endregion

        #region Permissions

        private void RegisterPermissions()
        {
            try
            {
                permission.RegisterPermission(AdminPermission, this);
                permission.RegisterPermission(BypassTimerPermission, this);
                permission.RegisterPermission(BypassSpawnPermission, this);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to register permissions: {ex}");
            }
        }

        // Developer: Inline permission checks with null safety for optimal performance
        private bool HasAdmin(BasePlayer player) => 
            player != null && permission.UserHasPermission(player.UserIDString, AdminPermission);

        private bool HasBypassTimer(BasePlayer player) => 
            player != null && permission.UserHasPermission(player.UserIDString, BypassTimerPermission);

        private bool HasBypassSpawn(BasePlayer player) => 
            player != null && permission.UserHasPermission(player.UserIDString, BypassSpawnPermission);

        #endregion

        #region Config Management

        private void LoadConfig()
        {
            try
            {
                _config = Config.ReadObject<PluginConfig>();
            }
            catch (Exception ex)
            {
                PrintWarning($"Config corrupted, creating default: {ex.Message}");
                _config = null;
            }

            _config ??= new PluginConfig();
            ValidateConfig();
            SaveConfig();
        }

        private void ValidateConfig()
        {
            // Developer: Validate and normalize configuration values for safety
            _config.Settings.SpawnTimer = Mathf.Max(_config.Settings.SpawnTimer, 0.1f);
            _config.Settings.Radius = Mathf.Max(_config.Settings.Radius, 1f);
            _config.Settings.DoorOpenRadius = Mathf.Max(_config.Settings.DoorOpenRadius, 0.5f);
            _config.Settings.DoorOpenInterval = Mathf.Max(_config.Settings.DoorOpenInterval, 0.1f);
            _config.Settings.DoorCloseDelay = Mathf.Max(_config.Settings.DoorCloseDelay, 0.5f);
        }

        protected override void LoadDefaultConfig() => _config = new PluginConfig();

        private void SaveConfig()
        {
            try
            {
                Config.WriteObject(_config, true);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to save config: {ex}");
            }
        }

        #endregion

        #region Data Management

        private void EnsureFolders()
        {
            try
            {
                _dataPath = Path.Combine(Interface.Oxide.DataDirectory, Name);
                _spawnDataFile = Path.Combine(_dataPath, "spawn.json");
                _playerDataFile = Path.Combine(_dataPath, "players.json");

                if (!Directory.Exists(_dataPath))
                    Directory.CreateDirectory(_dataPath);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to create data directories: {ex}");
            }
        }

        private void LoadSpawnData()
        {
            try
            {
                if (File.Exists(_spawnDataFile))
                {
                    string json = File.ReadAllText(_spawnDataFile);
                    _spawnData = JsonConvert.DeserializeObject<SpawnData>(json);
                }
                else
                {
                    _spawnData = new SpawnData();
                }
            }
            catch (Exception ex)
            {
                PrintError($"Failed to load spawn data: {ex}");
                _spawnData = new SpawnData();
            }

            _spawnData ??= new SpawnData();
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
                PrintError($"Failed to save spawn data: {ex}");
            }
        }

        private void LoadPlayerData()
        {
            try
            {
                if (File.Exists(_playerDataFile))
                {
                    string json = File.ReadAllText(_playerDataFile);
                    _playerData = JsonConvert.DeserializeObject<PlayerData>(json);
                }
                else
                {
                    _playerData = new PlayerData();
                }
            }
            catch (Exception ex)
            {
                PrintError($"Failed to load player data: {ex}");
                _playerData = new PlayerData();
            }

            _playerData ??= new PlayerData();
            _playerData.SeenPlayers ??= new HashSet<ulong>();
            _playerData.LogoutPositions ??= new Dictionary<ulong, SavedPosition>();
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
                PrintError($"Failed to save player data: {ex}");
            }
        }

        #endregion

        #region Commands

        private void RegisterCommands()
        {
            cmd.AddChatCommand("spawn", this, nameof(CmdSpawn));
            cmd.AddChatCommand("setspawn", this, nameof(CmdSetSpawn));
            cmd.AddChatCommand("ws", this, nameof(CmdWS));
            cmd.AddChatCommand("wsreload", this, nameof(CmdReload));
            cmd.AddChatCommand("respawn", this, nameof(CmdRespawn));
        }

        private void CmdReload(BasePlayer player, string command, string[] args)
        {
            if (!HasAdmin(player))
            {
                SendMessage(player, Lang("NoPermission"));
                return;
            }

            try
            {
                Unload();
                Init();
                Loaded();
                SendMessage(player, Lang("ReloadSuccess"));
            }
            catch (Exception ex)
            {
                PrintError($"Reload error: {ex}");
                SendMessage(player, Lang("ReloadFailed"));
            }
        }

        private void CmdSpawn(BasePlayer player, string command, string[] args)
        {
            if (player == null)
            {
                return;
            }

            // Admin teleport to other player: /spawn <id|name>
            if (args.Length > 0 && HasAdmin(player))
            {
                string targetId = args[0];
                BasePlayer target = FindPlayerByIdentifier(targetId);
                if (target == null)
                {
                    SendMessage(player, Lang("PlayerNotFound"));
                    return;
                }

                if (!_spawnData.IsSet)
                {
                    SendMessage(player, Lang("SpawnNotFound"));
                    return;
                }

                DoTeleportToSpawn(target);
                SendMessage(player, string.Format(Lang("TeleportedPlayer"), target.displayName));
                return;
            }

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

            // Allow sleeping bag respawns and restrict plugin teleportation on natural respawn
            if (player.IsHostile())
            {
                SendMessage(player, Lang("CannotSpawnHostile"));
                return;
            }

            // Developer: Check for bypass permissions to skip delay
            float delay = (HasBypassTimer(player) || HasAdmin(player)) ? 0f : _config.Settings.SpawnTimer;

            if (delay > 0)
            {
                SendMessage(player, string.Format(Lang("SpawnTeleportStart"), delay));
                timer.Once(delay, () =>
                {
                    if (player?.IsConnected == true && !player.IsHostile())
                        DoTeleportToSpawn(player);
                    else if (player?.IsConnected == true)
                        SendMessage(player, Lang("SpawnTeleportCancel"));
                });
            }
            else
            {
                DoTeleportToSpawn(player);
            }
        }

        private void DoTeleportToSpawn(BasePlayer player)
        {
            if (player?.IsConnected != true)
                return;

            try
            {
                if (player.IsHostile())
                {
                    player.State.unHostileTimestamp = 0;
                    player.ClientRPCPlayer(null, player, "SetHostileLength", 0f);
                }

                // Developer: Visual feedback for teleportation
                Effect.server.Run("assets/prefabs/misc/transferable/effects/teleport.prefab", player.transform.position, Vector3.up);
                player.Teleport(_spawnData.Position);
                player.eyes.rotation = _spawnData.Rotation;
                player.SendNetworkUpdateImmediate();
                player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, true);

                SendMessage(player, Lang("SpawnTeleportDone"));
                Effect.server.Run("assets/prefabs/misc/transferable/effects/teleport.prefab", player.transform.position, Vector3.up);
            }
            catch (Exception ex)
            {
                PrintError($"Teleport error for {player?.displayName}: {ex}");
            }
        }

        private void TeleportToSavedPosition(BasePlayer player, SavedPosition saved)
        {
            if (player?.IsConnected != true || saved == null)
                return;

            try
            {
                Effect.server.Run("assets/prefabs/misc/transferable/effects/teleport.prefab", player.transform.position, Vector3.up);
                player.Teleport(saved.Position);
                player.eyes.rotation = saved.Rotation;
                player.SendNetworkUpdateImmediate();
                player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, IsInSpawnZone(saved.Position));

                SendZoneMessage(player, Lang("LogoutPositionRestored"));
                Effect.server.Run("assets/prefabs/misc/transferable/effects/teleport.prefab", player.transform.position, Vector3.up);
            }
            catch (Exception ex)
            {
                PrintError($"Position restore error for {player?.displayName}: {ex}");
            }
        }

        private void CmdSetSpawn(BasePlayer player, string command, string[] args)
        {
            if (player == null || !HasAdmin(player))
            {
                SendMessage(player, Lang("NoPermission"));
                return;
            }

            try
            {
                _spawnData.Position = player.transform.position;
                _spawnData.Rotation = player.eyes.rotation;
                _spawnData.IsSet = true;
                SaveSpawnData();
                SendMessage(player, Lang("SpawnSet"));
                Puts($"{player.displayName} set spawn at {_spawnData.Position}");
            }
            catch (Exception ex)
            {
                PrintError($"SetSpawn error: {ex}");
                SendMessage(player, Lang("SetSpawnError"));
            }
        }

        private void CmdWS(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp(player);
                return;
            }

            try
            {
                if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    ShowHelp(player);
                    return;
                }

                switch (args[0].ToLower())
                {
                    case "config":
                        ShowConfig(player);
                        return;
                    case "status":
                        ShowStatus(player);
                        return;
                    case "set":
                        HandleSet(player, args);
                        return;
                    case "get":
                        HandleGet(player, args);
                        return;
                    case "reload":
                        if (!player.IsAdmin) { SendReply(player, lang.GetMessage("NoPermission", this, player.UserIDString)); return; }
                        ReloadPlugin(player);
                        return;
                    default:
                        SendReply(player, lang.GetMessage("InvalidCommand", this, player.UserIDString));
                        break;
                }
            }
            catch (Exception ex)
            {
                PrintError($"WS command error: {ex}");
                SendMessage(player, Lang("InvalidCommand"));
            }
        }

        private void HandleRadiusCommand(BasePlayer player, string[] args)
        {
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

            _config.Settings.Radius = Mathf.Max(radius, 1f);
            SaveConfig();
            SendMessage(player, string.Format(Lang("SpawnRadiusSet"), _config.Settings.Radius));
            Puts($"{player.displayName} set spawn radius to {_config.Settings.Radius}");
        }

        private void HandleSetCommand(BasePlayer player, string[] args)
        {
            if (!HasAdmin(player))
            {
                SendMessage(player, Lang("NoPermission"));
                return;
            }

            if (args.Length < 3)
            {
                SendMessage(player, Lang("SetUsage"));
                return;
            }

            string option = args[1].ToLowerInvariant();
            string value = args[2];
            if (TrySetConfigOption(option, value, out string err))
            {
                SaveConfig();
                SendMessage(player, string.Format(Lang("SetSuccess"), option, value));
            }
            else
            {
                SendMessage(player, string.Format(Lang("SetFailed"), option, err));
            }
        }

        private void HandleGetCommand(BasePlayer player, string[] args)
        {
            if (!HasAdmin(player))
            {
                SendMessage(player, Lang("NoPermission"));
                return;
            }

            if (args.Length < 2)
            {
                SendMessage(player, Lang("GetUsage"));
                return;
            }

            string option = args[1].ToLowerInvariant();
            string val = GetConfigOption(option);
            SendMessage(player, $"{option} = {val}");
        }

        private bool TrySetConfigOption(string option, string value, out string error)
        {
            error = null;
            try
            {
                switch (option)
                {
                    case "enabled":
                        _config.Settings.Enabled = bool.Parse(value);
                        return true;
                    case "spawntimer":
                        _config.Settings.SpawnTimer = float.Parse(value);
                        return true;
                    case "radius":
                        _config.Settings.Radius = float.Parse(value);
                        return true;
                    case "respawnonlybycommand":
                        _config.Settings.RespawnOnlyByCommand = bool.Parse(value);
                        return true;
                    case "preventbuildingdecay":
                        _config.Settings.PreventBuildingDecay = bool.Parse(value);
                        return true;
                    case "powerlessdevices":
                        _config.Settings.PowerlessDevicesEnabled = bool.Parse(value);
                        if (_config.Settings.PowerlessDevicesEnabled)
                            StartPowerlessDeviceTimer();
                        else
                            _powerlessDeviceTimer?.Destroy();
                        return true;
                    default:
                        error = "Unknown option";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private string GetConfigOption(string option)
        {
            switch (option)
            {
                case "enabled":
                    return _config.Settings.Enabled.ToString();
                case "spawntimer":
                    return _config.Settings.SpawnTimer.ToString();
                case "radius":
                    return _config.Settings.Radius.ToString();
                case "respawnonlybycommand":
                    return _config.Settings.RespawnOnlyByCommand.ToString();
                case "preventbuildingdecay":
                    return _config.Settings.PreventBuildingDecay.ToString();
                case "powerlessdevices":
                    return _config.Settings.PowerlessDevicesEnabled.ToString();
                default:
                    return "Unknown option";
            }
        }

        private void ShowStatus(BasePlayer player)
        {
            var cfg = _config.Settings;
            string status = string.Format(lang.GetMessage("Status", this, player.UserIDString),
                cfg.Enabled ? "<color=#00ff00>Yes</color>" : "<color=#ff0000>No</color>",
                cfg.Radius,
                cfg.SpawnTimer,
                cfg.RespawnOnlyByCommand ? "Yes" : "No",
                cfg.PreventBuildingDecay ? "Yes" : "No",
                cfg.PowerlessDevicesEnabled ? "Yes" : "No");
            SendReply(player, status);
        }

        private void ShowConfig(BasePlayer player)
        {
            var cfg = _config.Settings;
            var messages = lang.GetMessages(player.UserIDString);
            SendReply(player, messages["ConfigHeader"]);

            var lines = new[]
            {
                ("ConfigEnabled", cfg.Enabled),
                ("ConfigSpawnTimer", cfg.SpawnTimer),
                ("ConfigRadius", cfg.Radius),
                ("ConfigWelcome", cfg.WelcomeMessageEnabled),
                ("ConfigRespawnMsg", cfg.RespawnMessageEnabled),
                ("ConfigRespawnCmd", cfg.RespawnCommandEnabled),
                ("ConfigFindOutpost", cfg.FindOutpostFirst),
                ("ConfigBlockWeapons", cfg.BlockWeaponsAndTools),
                ("ConfigFreezeMeta", cfg.FreezeMetabolismInZone),
                ("ConfigChatNotif", cfg.ChatNotificationsEnabled),
                ("ConfigBlockLoot", cfg.BlockLootingInZone),
                ("ConfigBlockBuild", cfg.BlockBuildingInZone),
                ("ConfigAutoOpenDoors", cfg.AutoOpenDoorsInZone),
                ("ConfigDoorRadius", cfg.DoorOpenRadius),
                ("ConfigDoorInterval", cfg.DoorOpenInterval),
                ("ConfigDoorCloseDelay", cfg.DoorCloseDelay),
                ("ConfigRestoreLogout", cfg.RestoreLogoutPositionOnReconnect),
                ("ConfigRespawnOnlyCmd", cfg.RespawnOnlyByCommand),
                ("ConfigPreventDecay", cfg.PreventBuildingDecay),
                ("ConfigPowerlessDevices", cfg.PowerlessDevicesEnabled),
            };

            foreach (var (key, value) in lines)
            {
                string label = messages[key];
                string valStr = value is bool ? (bool)value ? "<color=#00ff00>ON</color>" : "<color=#ff0000>OFF</color>" : value.ToString();
                SendReply(player, string.Format(messages["ConfigLine"], label, valStr));
            }
            SendReply(player, messages["ConfigHelp"]);
        }

        private void ShowHelp(BasePlayer player)
        {
            var messages = lang.GetMessages(player.UserIDString);
            SendReply(player, messages["HelpHeader"]);
            SendReply(player, messages["HelpSpawn"]);
            SendReply(player, messages["HelpRespawn"]);
            SendReply(player, messages["HelpStatus"]);
            SendReply(player, messages["HelpSetSpawn"]);
            SendReply(player, messages["HelpSet"]);
            SendReply(player, messages["HelpGet"]);
            SendReply(player, messages["HelpRadius"]);
            SendReply(player, messages["HelpPowerless"]);
            SendReply(player, messages["HelpReload"]);
            SendReply(player, "<color=#ffcc00>/ws config</color> - Show all configuration options");
        }

        private void CmdRespawn(BasePlayer player, string command, string[] args)
        {
            if (player == null || !_config.Settings.Enabled || !_config.Settings.RespawnCommandEnabled)
            {
                SendMessage(player, Lang("PluginDisabled"));
                return;
            }

            if (!player.IsConnected || player.IsDead())
                return;

            try
            {
                _respawnCommandFlag.Add(player.userID);
                player.Die();

                // Developer: Force respawn if automatic respawn fails
                timer.Once(0.1f, () =>
                {
                    if (player?.IsConnected == true && player.IsDead())
                        player.Respawn();
                });
            }
            catch (Exception ex)
            {
                PrintError($"Respawn error for {player.displayName}: {ex}");
                _respawnCommandFlag.Remove(player.userID);
            }
        }

        #endregion

        #region Hooks

        // Developer: Handle player spawn and reconnection logic
        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || !_config.Settings.Enabled)
                return;

            try
            {
                bool seenBefore = _playerData.SeenPlayers.Contains(player.userID);

                if (!seenBefore)
                {
                    _playerData.SeenPlayers.Add(player.userID);
                    SavePlayerData();

                    if (HasBypassSpawn(player) || HasAdmin(player))
                        return;

                    if (_spawnData.IsSet)
                    {
                        timer.Once(TeleportDelay, () =>
                        {
                            if (player?.IsConnected == true)
                            {
                                DoTeleportToSpawn(player);
                                if (_config.Settings.WelcomeMessageEnabled)
                                    SendZoneMessage(player, Lang("WelcomeMessage"));
                            }
                        });
                    }
                    else
                    {
                        TryFindDefaultSpawn();
                        if (_spawnData.IsSet)
                        {
                            timer.Once(TeleportDelay, () =>
                            {
                                if (player?.IsConnected == true)
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

                if (_config.Settings.RestoreLogoutPositionOnReconnect && 
                    _playerData.LogoutPositions.TryGetValue(player.userID, out SavedPosition saved))
                {
                    timer.Once(TeleportDelay, () =>
                    {
                        if (player?.IsConnected == true)
                            TeleportToSavedPosition(player, saved);
                    });
                }
            }
            catch (Exception ex)
            {
                PrintError($"OnPlayerConnected error: {ex}");
            }
        }

        // Developer: Save player position on logout for potential restore
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;

            try
            {
                _inZoneTracker.Remove(player.userID);
                _lastWeaponWarn.Remove(player.userID);
                _lastLootWarn.Remove(player.userID);
                _lastBuildWarn.Remove(player.userID);
                _lastDamageWarn.Remove(player.userID);
                _lastDoorPulse.Remove(player.userID);

                if (!_config.Settings.Enabled || player.IsDead() || !_config.Settings.RestoreLogoutPositionOnReconnect)
                    return;

                _playerData.LogoutPositions[player.userID] = new SavedPosition
                {
                    Position = player.transform.position,
                    Rotation = player.eyes.rotation
                };
                SavePlayerData();
            }
            catch (Exception ex)
            {
                PrintError($"OnPlayerDisconnected error: {ex}");
            }
        }

        // Developer: Handle respawn event - only force spawn on /respawn command, not on natural death
        private void OnPlayerRespawn(BasePlayer player)
        {
            if (player == null || !_config.Settings.Enabled)
                return;

            try
            {
                // Developer: Check if respawn was triggered by /respawn command
                if (_respawnCommandFlag.Contains(player.userID))
                {
                    _respawnCommandFlag.Remove(player.userID);
                    if (_spawnData.IsSet && _config.Settings.RespawnCommandEnabled)
                    {
                        timer.Once(RespawnDelay, () =>
                        {
                            if (player?.IsConnected == true)
                            {
                                DoTeleportToSpawn(player);
                                SendMessage(player, Lang("RespawnCommandUsed"));
                            }
                        });
                    }
                    return;
                }

                // Developer: If RespawnOnlyByCommand is enabled, don't auto-respawn on natural death
                // This allows players to use their sleeping bags naturally
                if (_config.Settings.RespawnOnlyByCommand)
                    return;

                // Developer: Legacy behavior - only for players without bypass
                if (HasBypassSpawn(player) || HasAdmin(player))
                    return;

                if (_spawnData.IsSet)
                {
                    timer.Once(RespawnDelay, () =>
                    {
                        if (player?.IsConnected == true)
                        {
                            DoTeleportToSpawn(player);
                            if (_config.Settings.RespawnMessageEnabled)
                                SendZoneMessage(player, Lang("RespawnMessage"));
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                PrintError($"OnPlayerRespawn error: {ex}");
            }
        }

        // Developer: Prevent construction in safe zone before entity creation
        private object CanBuild(Planner plan, Construction prefab, Construction.Target target)
        {
            if (plan == null || !_config.Settings.Enabled || !_config.Settings.BlockBuildingInZone)
                return null;

            try
            {
                BasePlayer player = plan.GetOwnerPlayer();
                if (player == null || HasAdmin(player))
                    return null;

                Vector3 position = target.position != Vector3.zero ? target.position : player.transform.position;
                bool isAttachedToZoneEntity = target.entity != null && IsInSpawnZone(target.entity.transform.position);

                if (IsInSpawnZone(position) || IsInSpawnZone(player.transform.position) || isAttachedToZoneEntity)
                {
                    WarnBuildBlocked(player);
                    return false;
                }
            }
            catch (Exception ex)
            {
                PrintError($"CanBuild error: {ex}");
            }

            return null;
        }

        // Developer: Catch deployables that slip through CanBuild check
        private void OnEntityBuilt(Planner plan, GameObject gameObject)
        {
            if (plan == null || gameObject == null || !_config.Settings.Enabled || !_config.Settings.BlockBuildingInZone)
                return;

            try
            {
                BasePlayer player = plan.GetOwnerPlayer();
                if (player == null || HasAdmin(player))
                    return;

                BaseEntity entity = gameObject.GetComponent<BaseEntity>();
                if (entity == null || entity.IsDestroyed)
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
            catch (Exception ex)
            {
                PrintError($"OnEntityBuilt error: {ex}");
            }
        }

        // Developer: Hard safe zone protection - no damage exceptions. Also prevents building decay
        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null || !_config.Settings.Enabled)
                return null;

            try
            {
                if (!IsInSpawnZone(entity.transform.position))
                    return null;

                // Developer: Block damage to players
                if (entity is BasePlayer victim)
                {
                    float now = UnityEngine.Time.realtimeSinceStartup;
                    if (TryWarn(_lastDamageWarn, victim.userID, now, WarningCooldown))
                        SendZoneMessage(victim, Lang("DamageBlocked"));
                    return false;
                }

                // Developer: Prevent building decay if option enabled
                if (_config.Settings.PreventBuildingDecay && info?.Initiator == null && IsDecayDamage(info))
                    return false;

                // Developer: Block all other damage in safe zone
                return false;
            }
            catch (Exception ex)
            {
                PrintError($"OnEntityTakeDamage error: {ex}");
            }

            return null;
        }

        // Developer: Check if damage is from entity decay (when initiator is null)
        private bool IsDecayDamage(HitInfo info)
        {
            return info?.Initiator == null && info?.damageTypes != null && 
                   info.damageTypes.Get(Rust.DamageType.Decay) > 0;
        }

        // Developer: Block looting of protected players
        private object CanLootPlayer(BasePlayer target, BasePlayer looter)
        {
            if (!_config.Settings.Enabled || !_config.Settings.BlockLootingInZone || target == null || looter == null)
                return null;

            try
            {
                bool targetProtected = IsInSpawnZone(target.transform.position);
                bool looterProtected = IsInSpawnZone(looter.transform.position);

                if (targetProtected || looterProtected)
                {
                    WarnLootBlocked(looter);
                    return false;
                }
            }
            catch (Exception ex)
            {
                PrintError($"CanLootPlayer error: {ex}");
            }

            return null;
        }

        // Developer: Block looting of entities in safe zone
        private object CanLootEntity(BasePlayer looter, BaseEntity entity)
        {
            if (!_config.Settings.Enabled || !_config.Settings.BlockLootingInZone || looter == null || entity == null)
                return null;

            try
            {
                if (entity is BasePlayer target)
                {
                    if (IsInSpawnZone(target.transform.position) || IsInSpawnZone(looter.transform.position))
                    {
                        WarnLootBlocked(looter);
                        return false;
                    }
                }

                if (entity is PlayerCorpse corpse)
                {
                    if ((IsInSpawnZone(corpse.transform.position) || IsInSpawnZone(looter.transform.position)) &&
                        corpse.playerSteamID != looter.userID)
                    {
                        WarnLootBlocked(looter);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError($"CanLootEntity error: {ex}");
            }

            return null;
        }

        // Developer: Per-tick zone management - flag sync, weapons holster, metabolism freeze, door assist
        private object OnPlayerTick(BasePlayer player, PlayerTick msg, bool wasPlayerStalled)
        {
            if (player == null || !_config.Settings.Enabled || !_spawnData.IsSet || !player.IsConnected || player.IsDead())
                return null;

            try
            {
                bool inZone = IsInSpawnZone(player.transform.position);
                bool wasInZone = _inZoneTracker.Contains(player.userID);

                if (inZone)
                {
                    if (!wasInZone)
                    {
                        _inZoneTracker.Add(player.userID);
                        SendZoneMessage(player, Lang("EnteredSafeZone"));
                        // Enable powerless devices for this player if configured
                        if (_config.Settings.PowerlessDevicesEnabled)
                            TryEnableNearbyDevices(player);
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
                    // Disable devices when player leaves
                    if (_config.Settings.PowerlessDevicesEnabled)
                        DisableDevicesNearby(player);
                }
                else if (player.HasPlayerFlag(BasePlayer.PlayerFlags.SafeZone))
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.SafeZone, false);
                }
            }
            catch (Exception ex)
            {
                PrintError($"OnPlayerTick error: {ex}");
            }

            return null;
        }

        #endregion

        #region Helpers

        // Developer: Fast zone boundary check with early return
        private bool IsInSpawnZone(Vector3 position)
        {
            if (!_spawnData.IsSet)
                return false;

            return Vector3.Distance(position, _spawnData.Position) <= _config.Settings.Radius;
        }

        // Developer: Periodic metabolism freeze for in-zone players
        private void StartZoneEffectsTimer()
        {
            if (_zoneTimer != null) 
                return;

            _zoneTimer = timer.Every(ZoneCheckInterval, () =>
            {
                try
                {
                    if (!_config.Settings.Enabled || !_config.Settings.FreezeMetabolismInZone || _inZoneTracker.Count == 0)
                        return;

                    foreach (ulong userId in _inZoneTracker)
                    {
                        BasePlayer player = BasePlayer.FindByID(userId);
                        if (player?.IsConnected == true && !player.IsDead())
                            ApplyMetabolismFreeze(player);
                    }
                }
                catch (Exception ex)
                {
                    PrintError($"Zone effects timer error: {ex}");
                }
            });
        }

        // Developer: Freeze all metabolism parameters to prevent starvation in zone
        private void ApplyMetabolismFreeze(BasePlayer player)
        {
            try
            {
                PlayerMetabolism metabolism = player?.metabolism;
                if (metabolism == null)
                    return;

                metabolism.calories.value = metabolism.calories.max;
                metabolism.hydration.value = metabolism.hydration.max;
                metabolism.poison.value = 0f;
                metabolism.radiation_poison.value = 0f;
                metabolism.bleeding.value = 0f;

                player.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            }
            catch (Exception ex)
            {
                PrintError($"Metabolism freeze error: {ex}");
            }
        }

        // Developer: Force-holster restricted items for zone security
        private void EnforceNoWeapons(BasePlayer player)
        {
            try
            {
                if (player == null) return;

                // Developers: Admins should be able to use weapons regardless of zone
                if (HasAdmin(player))
                    return;

                Item active = player.GetActiveItem();
                if (!IsRestrictedItem(active))
                    return;

                player.UpdateActiveItem(default(ItemId));

                float now = UnityEngine.Time.realtimeSinceStartup;
                if (TryWarn(_lastWeaponWarn, player.userID, now, WarningCooldown))
                    SendZoneMessage(player, Lang("WeaponBlocked"));
            }
            catch (Exception ex)
            {
                PrintError($"Enforce weapons error: {ex}");
            }
        }

        // Developer: Check if item belongs to restricted category
        private static bool IsRestrictedItem(Item item)
        {
            if (item?.info == null)
                return false;
            
            return item.info.category == ItemCategory.Weapon || item.info.category == ItemCategory.Tool;
        }

        // Developer: Close all initially open doors in spawn zone
        private void CloseAllDoorsInZone()
        {
            if (!_spawnData.IsSet || !_config.Settings.AutoOpenDoorsInZone)
                return;

            try
            {
                Collider[] colliders = Physics.OverlapSphere(_spawnData.Position, _config.Settings.Radius, ~0, QueryTriggerInteraction.Collide);
                if (colliders?.Length == 0)
                    return;

                var processed = new HashSet<Door>();

                foreach (Collider collider in colliders)
                {
                    if (collider == null)
                        continue;

                    Door door = collider.GetComponentInParent<Door>();
                    if (door != null && !door.IsDestroyed && door.IsOpen() && !processed.Contains(door))
                    {
                        processed.Add(door);
                        door.SetOpen(false, true);
                    }
                }
            }
            catch (Exception ex)
            {
                PrintError($"Close doors error: {ex}");
            }
        }

        // Developer: Periodic door monitor - keep doors closed when no players nearby
        private void StartDoorMonitorTimer()
        {
            if (_doorTimer != null) 
                return;

            _doorTimer = timer.Every(DoorCheckInterval, () =>
            {
                try
                {
                    if (!_config.Settings.Enabled || !_config.Settings.AutoOpenDoorsInZone || !_spawnData.IsSet)
                        return;

                    Collider[] colliders = Physics.OverlapSphere(_spawnData.Position, _config.Settings.Radius, ~0, QueryTriggerInteraction.Collide);
                    if (colliders?.Length == 0)
                        return;

                    var processedDoors = new HashSet<Door>();
                    float checkRadius = _config.Settings.DoorOpenRadius;

                    foreach (Collider collider in colliders)
                    {
                        if (collider == null)
                            continue;

                        Door door = collider.GetComponentInParent<Door>();
                        if (door == null || door.IsDestroyed || !door.IsOpen() || processedDoors.Contains(door))
                            continue;

                        processedDoors.Add(door);

                        // Developer: Check for nearby living players using Physics.OverlapSphere on Player layer
                        Collider[] doorColliders = Physics.OverlapSphere(door.transform.position, checkRadius, 1 << 17, QueryTriggerInteraction.Collide);
                        bool playerNearby = false;

                        if (doorColliders?.Length > 0)
                        {
                            foreach (Collider dc in doorColliders)
                            {
                                BasePlayer nearbyPlayer = dc.GetComponentInParent<BasePlayer>();
                                if (nearbyPlayer?.IsConnected == true && !nearbyPlayer.IsDead())
                                {
                                    playerNearby = true;
                                    break;
                                }
                            }
                        }

                        if (!playerNearby)
                            door.SetOpen(false, true);
                    }
                }
                catch (Exception ex)
                {
                    PrintError($"Door monitor error: {ex}");
                }
            });
        }

        // Developer: Open nearby doors for zone player convenience
        private void TryOpenNearbyDoors(BasePlayer player)
        {
            if (player == null || !_config.Settings.AutoOpenDoorsInZone)
                return;

            try
            {
                float now = UnityEngine.Time.realtimeSinceStartup;
                if (!TryWarn(_lastDoorPulse, player.userID, now, _config.Settings.DoorOpenInterval))
                    return;

                Collider[] colliders = Physics.OverlapSphere(player.transform.position, _config.Settings.DoorOpenRadius, ~0, QueryTriggerInteraction.Collide);
                if (colliders?.Length == 0)
                    return;

                foreach (Collider collider in colliders)
                {
                    if (collider == null)
                        continue;

                    Door door = collider.GetComponentInParent<Door>();
                    if (door != null && !door.IsDestroyed && !door.IsOpen())
                        door.SetOpen(true, true);
                }
            }
            catch (Exception ex)
            {
                PrintError($"Open doors error: {ex}");
            }
        }

        // Developer: Enable nearby devices for player convenience inside zone
        private void TryEnableNearbyDevices(BasePlayer player)
        {
            if (player == null || !_config.Settings.PowerlessDevicesEnabled)
                return;

            try
            {
                Collider[] colliders = Physics.OverlapSphere(player.transform.position, _config.Settings.DoorOpenRadius * 2f, ~0, QueryTriggerInteraction.Collide);
                if (colliders?.Length == 0) return;

                foreach (Collider collider in colliders)
                {
                    if (collider == null) continue;

                    BaseEntity entity = collider.GetComponentInParent<BaseEntity>();
                    if (entity == null || entity.IsDestroyed) continue;

                    NetworkableId id = entity.net?.ID ?? default(NetworkableId);
                    if (id == default(NetworkableId) || _powerlessDevices.Contains(id)) continue;

                    string shortName = entity.ShortPrefabName?.ToLowerInvariant();

                    if (shortName == "electric.heater" || shortName == "electricheater" || shortName == "autoturret" || shortName == "searchlight")
                    {
                        IOEntity ioEntity = entity as IOEntity;
                        if (ioEntity == null) continue;

                        _powerlessDevices.Add(id);
                        int powerNeeded = Mathf.Max(ioEntity.ConsumptionAmount(), 10);
                        ioEntity.currentEnergy = powerNeeded;
                        ioEntity.SetFlag(BaseEntity.Flags.On, true);
                        if (ioEntity is AutoTurret turret && !turret.IsOnline())
                        {
                            turret.InitiateStartup();
                        }
                        ioEntity.SendNetworkUpdate();
                        Puts($"Powered device {shortName} ({id}) at {entity.transform.position}");
                        continue;
                    }

                }
            }
            catch (Exception ex)
            {
                PrintError($"TryEnableNearbyDevices error: {ex}");
            }
        }

        // Developer: Try invoking a named method by reflection as a fallback
        private void TryInvokeMethod(object target, string methodName, params object[] args)
        {
            if (target == null || string.IsNullOrEmpty(methodName)) return;

            try
            {
                MethodInfo mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null)
                {
                    mi.Invoke(target, args);
                }
            }
            catch (Exception ex)
            {
                // Swallow errors silently but log for debugging
                PrintError($"Reflection invoke {methodName} failed on {target.GetType().Name}: {ex.Message}");
            }
        }

        // Developer: Disable devices previously enabled for this player/zone
        private void DisableDevicesNearby(BasePlayer player)
        {
            if (!_config.Settings.PowerlessDevicesEnabled) return;

            try
            {
                var toRemove = new List<NetworkableId>();

                foreach (NetworkableId id in _powerlessDevices)
                {
                    IOEntity ioEntity = BaseNetworkable.serverEntities.Find(id) as IOEntity;
                    if (ioEntity == null || ioEntity.IsDestroyed)
                    {
                        toRemove.Add(id);
                        continue;
                    }

                    if (!IsInSpawnZone(ioEntity.transform.position))
                    {
                        ioEntity.UpdateFromInput(0, 0);
                        ioEntity.currentEnergy = 0;
                        ioEntity.SetFlag(BaseEntity.Flags.On, false);
                        
                        if (ioEntity is AutoTurret turret && turret.IsOnline())
                        {
                            turret.Shutdown();
                        }
                        
                        ioEntity.SendNetworkUpdate();
                        toRemove.Add(id);
                    }
                }

                foreach (NetworkableId id in toRemove)
                    _powerlessDevices.Remove(id);
            }
            catch (Exception ex)
            {
                PrintError($"DisableDevicesNearby error: {ex}");
            }
        }
        // Developer: Throttled warning for loot attempts
        private void WarnLootBlocked(BasePlayer player)
        {
            if (player == null)
                return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (TryWarn(_lastLootWarn, player.userID, now, WarningCooldown))
                SendZoneMessage(player, Lang("LootBlocked"));
        }

        // Developer: Throttled warning for build attempts
        private void WarnBuildBlocked(BasePlayer player)
        {
            if (player == null)
                return;

            float now = UnityEngine.Time.realtimeSinceStartup;
            if (TryWarn(_lastBuildWarn, player.userID, now, WarningCooldown))
                SendZoneMessage(player, Lang("BuildingBlocked"));
        }

        // Developer: Cooldown check to avoid chat spam
        private bool TryWarn(Dictionary<ulong, float> storage, ulong userId, float now, float cooldown)
        {
            if (storage == null)
                return true;

            if (storage.TryGetValue(userId, out float last) && now - last < cooldown)
                return false;

            storage[userId] = now;
            return true;
        }

        // Developer: Find player by steamid, userid or partial nickname
        private BasePlayer FindPlayerByIdentifier(string ident)
        {
            if (string.IsNullOrEmpty(ident)) return null;
            // Try SteamID
            if (ulong.TryParse(ident, out ulong id))
                return BasePlayer.FindByID(id);

            // By partial name (first match)
            var match = BasePlayer.activePlayerList
                .Where(p => p != null && !string.IsNullOrEmpty(p.displayName) && p.displayName.IndexOf(ident, StringComparison.OrdinalIgnoreCase) >= 0)
                .FirstOrDefault();

            if (match != null) return match;

            // By UserIDString exact
            return BasePlayer.activePlayerList.FirstOrDefault(p => p != null && p.UserIDString == ident);
        }

        // Developer: Auto-locate spawn at Outpost or Bandit if not manually set
        private void TryFindDefaultSpawn()
        {
            try
            {
                string prefabName = _config.Settings.FindOutpostFirst ? 
                    "assets/bundled/prefabs/static/outpost.prefab" : 
                    "assets/bundled/prefabs/static/bandit_town.prefab";
                
                GameObject obj = GameObject.Find(prefabName);
                if (obj == null && _config.Settings.FindOutpostFirst)
                {
                    // Fallback to Bandit if Outpost not found
                    obj = GameObject.Find("assets/bundled/prefabs/static/bandit_town.prefab");
                }

                if (obj == null)
                {
                    PrintWarning("Default spawn (Outpost/Bandit) not found. Please set spawn manually.");
                    return;
                }

                Vector3 pos = obj.transform.position;
                pos.y += 1f;

                _spawnData.Position = pos;
                _spawnData.Rotation = Quaternion.identity;
                _spawnData.IsSet = true;
                SaveSpawnData();
                
                Puts($"Default spawn set to {prefabName} at {pos}");
            }
            catch (Exception ex)
            {
                PrintError($"Find default spawn error: {ex}");
            }
        }

        // Developer: Send chat message to player with plugin prefix
        private void SendMessage(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message))
                return;

            try
            {
                string final = $"{Prefix} {message}";
                player.SendConsoleCommand("chat.add", 2, PluginIcon, final);
            }
            catch (Exception ex)
            {
                PrintError($"Send message error: {ex}");
            }
        }

        // Developer: Send zone-conditional message (respects ChatNotificationsEnabled setting)
        private void SendZoneMessage(BasePlayer player, string message)
        {
            if (_config.Settings.ChatNotificationsEnabled)
                SendMessage(player, message);
        }

        // Developer: Localization lookup with optional formatting
        private string Lang(string key, params object[] args)
        {
            string msg = lang.GetMessage(key, this);
            if (args.Length > 0)
                msg = string.Format(msg, args);
            return msg;
        }

        // Developer: Print initialization banner with plugin status
        // Developer: Periodic powerless device power-up system - allows certain devices to work without external power
        private void StartPowerlessDeviceTimer()
        {
            if (_powerlessDeviceTimer != null) 
                return;

            _powerlessDeviceTimer = timer.Every(1f, () =>
            {
                try
                {
                    if (!_config.Settings.Enabled || !_config.Settings.PowerlessDevicesEnabled || !_spawnData.IsSet)
                        return;

                    Collider[] colliders = Physics.OverlapSphere(_spawnData.Position, _config.Settings.Radius, ~0, QueryTriggerInteraction.Collide);
                    if (colliders?.Length == 0)
                        return;

                    var processedDevices = new HashSet<NetworkableId>();

                    foreach (Collider collider in colliders)
                    {
                        if (collider == null)
                            continue;

                        BaseEntity entity = collider.GetComponentInParent<BaseEntity>();
                        if (entity == null || entity.IsDestroyed || processedDevices.Contains(entity.net.ID))
                            continue;

                        processedDevices.Add(entity.net.ID);

                        string shortName = entity.ShortPrefabName?.ToLowerInvariant();

                        // Power up devices without requiring external power
                        if (shortName == "electric.heater" || shortName == "electricheater" || shortName == "autoturret" || shortName == "searchlight")
                        {
                            IOEntity ioEntity = entity as IOEntity;
                            if (ioEntity == null) continue;

                            NetworkableId id = ioEntity.net.ID;
                            if (!_powerlessDevices.Contains(id))
                            {
                                _powerlessDevices.Add(id);
                            }

                            int powerNeeded = Mathf.Max(ioEntity.ConsumptionAmount(), 10);
                            
                            // If the device has lost power or it has turned off, turn on the power again.
                            if (ioEntity.currentEnergy < powerNeeded || !ioEntity.HasFlag(BaseEntity.Flags.On))
                            {
                                ioEntity.currentEnergy = powerNeeded;
                                ioEntity.SetFlag(BaseEntity.Flags.On, true);
                                
                                if (ioEntity is AutoTurret turret && !turret.IsOnline())
                                {
                                    turret.InitiateStartup();
                                }
                                ioEntity.SendNetworkUpdate();
                            }
                        }
                    }

                    // Turning off devices that have been removed from the spawn zone
                    var devicesToRemove = new HashSet<NetworkableId>();
                    foreach (NetworkableId deviceId in _powerlessDevices)
                    {
                        BaseEntity entity = BaseNetworkable.serverEntities.Find(deviceId) as BaseEntity;
                        if (entity == null || entity.IsDestroyed || !IsInSpawnZone(entity.transform.position))
                        {
                            devicesToRemove.Add(deviceId);
                            if (entity is IOEntity ioEntity)
                            {
                                ioEntity.currentEnergy = 0;
                                ioEntity.SetFlag(BaseEntity.Flags.On, false);
                                
                                if (ioEntity is AutoTurret turret && turret.IsOnline())
                                {
                                    turret.Shutdown();
                                }
                                ioEntity.SendNetworkUpdate();
                            }
                        }
                    }

                    foreach (NetworkableId deviceId in devicesToRemove)
                        _powerlessDevices.Remove(deviceId);
                }
                catch (Exception ex)
                {
                    PrintError($"Powerless device timer error: {ex}");
                }
            });
        }

        private void PrintBanner()
        {
            Puts("==================================================");
            Puts($"{Name} loaded successfully.");
            Puts($"Version: {PluginVersion}");
            Puts($"Spawn set: {_spawnData.IsSet}, Radius: {_config.Settings.Radius}, Timer: {_config.Settings.SpawnTimer}s");
            Puts($"Decay prevention: {_config.Settings.PreventBuildingDecay}, Powerless devices: {_config.Settings.PowerlessDevicesEnabled}, Respawn by command only: {_config.Settings.RespawnOnlyByCommand}");
            Puts("==================================================");
        }

        #endregion
    }
}
