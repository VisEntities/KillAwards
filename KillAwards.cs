/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Kill Awards", "VisEntities", "1.0.0")]
    [Description(" ")]
    public class KillAwards : RustPlugin
    {
        #region 3rd Party Dependencies

        [PluginReference]
        private readonly Plugin GearCore;

        #endregion 3rd Party Dependencies

        #region Fields

        private static KillAwards _plugin;
        private static Configuration _config;
        private StoredData _storedData;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Include NPC Kills")]
            public bool IncludeNPCKills { get; set; }
            
            [JsonProperty("Include Animal Kills")]
            public bool IncludeAnimalKills { get; set; }

            [JsonProperty("Ignore Teammate Kills")]
            public bool IgnoreTeammateKills { get; set; }

            [JsonProperty("Reset Milestone On Death")]
            public bool ResetMilestoneOnDeath { get; set; }

            [JsonProperty("Kill Milestones")]
            public Dictionary<int, RewardConfig> KillMilestones { get; set; } = new Dictionary<int, RewardConfig>();
        }

        private class RewardConfig
        {
            [JsonProperty("Amount Of Health Restored")]
            public float AmountOfHealthRestored { get; set; }

            [JsonProperty("Refill Weapon Ammo")]
            public bool RefillWeaponAmmo { get; set; }

            [JsonProperty("Gear Set To Equip")]
            public string GearSetToEquip { get; set; }

            [JsonProperty("Commands To Run")]
            public List<CommandConfig> CommandsToRun { get; set; }
        }

        private class CommandConfig
        {
            [JsonProperty("Type")]
            [JsonConverter(typeof(StringEnumConverter))]
            public CommandType Type { get; set; }

            [JsonProperty("Command")]
            public string Command { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                IncludeNPCKills = false,
                IncludeAnimalKills = false,
                IgnoreTeammateKills = true,
                ResetMilestoneOnDeath = true,
                KillMilestones = new Dictionary<int, RewardConfig>
                {
                    {
                        1, new RewardConfig
                        {
                            AmountOfHealthRestored = 10.0f,
                            RefillWeaponAmmo = false,
                            GearSetToEquip = "",
                            CommandsToRun = new List<CommandConfig>()
                        }
                    },
                    {
                        2, new RewardConfig
                        {
                            AmountOfHealthRestored = 15.0f,
                            RefillWeaponAmmo = true,
                            GearSetToEquip = "",
                            CommandsToRun = new List<CommandConfig>()
                        }
                    },
                    {
                        3, new RewardConfig
                        {
                            AmountOfHealthRestored = 20.0f,
                            RefillWeaponAmmo = true,
                            GearSetToEquip = "",
                            CommandsToRun = new List<CommandConfig>
                            {
                               new CommandConfig
                                {
                                    Type = CommandType.Server,
                                    Command = "inventory.giveto {playerId} scrap 50"
                                }
                            }
                        }
                    }
                }
            };
        }

        #endregion Configuration

        #region Stored Data

        public class StoredData
        {
            [JsonProperty("Players")]
            public Dictionary<ulong, PlayerData> Players { get; set; } = new Dictionary<ulong, PlayerData>();
        }

        public class PlayerData
        {
            [JsonProperty("Kills")]
            public int Kills { get; set; }
        }

        #endregion Stored Data

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            _storedData = DataFileUtil.LoadOrCreate<StoredData>(DataFileUtil.GetFilePath());
        }

        private void Unload()
        {
            _config = null;
            _plugin = null;
        }

        private void OnNewSave()
        {
            DataFileUtil.Delete(DataFileUtil.GetFilePath());
        }

        private void OnEntityDeath(BaseEntity victim, HitInfo deathInfo)
        {
            if (victim == null || deathInfo == null)
                return;

            if (victim is BasePlayer victimPlayer && _config.ResetMilestoneOnDeath)
            {
                if (_storedData.Players.TryGetValue(victimPlayer.userID, out PlayerData victimData))
                {
                    victimData.Kills = 0;
                    DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);
                }
            }

            BasePlayer killer = deathInfo.InitiatorPlayer;
            if (killer == null || killer == victim || !killer.userID.IsSteamId())
                return;

            if (victim is BasePlayer victimBasePlayer)
            {
                if (PlayerUtil.AreTeammates(killer.userID, victimBasePlayer.userID) && _config.IgnoreTeammateKills)
                    return;

                if (victimBasePlayer.IsNpc && !_config.IncludeNPCKills)
                    return;
            }
            else if (victim is BaseAnimalNPC)
            {
                if (!_config.IncludeAnimalKills)
                    return;
            }
            else
                return;

            Award(killer);
        }

        #endregion Oxide Hooks

        #region Awarding

        private void Award(BasePlayer player)
        {
            if (!_storedData.Players.TryGetValue(player.userID, out PlayerData playerData))
            {
                playerData = new PlayerData();
                _storedData.Players[player.userID] = playerData;
            }

            playerData.Kills++;

            int maxMilestone = _config.KillMilestones.Keys.Max();
            if (playerData.Kills > maxMilestone)
                playerData.Kills = 0;

            if (_config.KillMilestones.TryGetValue(playerData.Kills, out RewardConfig reward))
            {
                if (reward.AmountOfHealthRestored > 0)
                {
                    player.Heal(reward.AmountOfHealthRestored);
                    MessagePlayer(player, Lang.HealthRestored, reward.AmountOfHealthRestored);
                }

                if (reward.RefillWeaponAmmo)
                {
                    PlayerUtil.TopUpAmmo(player);
                    MessagePlayer(player, Lang.AmmoRefilled);
                }

                if (!string.IsNullOrEmpty(reward.GearSetToEquip) && EquipGearSet(player, reward.GearSetToEquip))
                {
                    MessagePlayer(player, Lang.GearSetGiven, reward.GearSetToEquip);
                }

                if (reward.CommandsToRun != null)
                {
                    foreach (CommandConfig commandConfig in reward.CommandsToRun)
                    {
                        RunCommand(player, commandConfig.Type, commandConfig.Command);
                    }
                }
            }

            DataFileUtil.Save(DataFileUtil.GetFilePath(), _storedData);
        }

        #endregion Awarding

        #region Helper Functions

        public static bool PluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.IsLoaded)
                return true;
            else
                return false;
        }

        #endregion Helper Functions

        #region Helper Classes

        public static class PlayerUtil
        {
            public static bool AreTeammates(ulong firstPlayerId, ulong secondPlayerId)
            {
                RelationshipManager.PlayerTeam team = RelationshipManager.ServerInstance.FindPlayersTeam(firstPlayerId);
                if (team != null && team.members.Contains(secondPlayerId))
                    return true;

                return false;
            }

            public static void TopUpAmmo(BasePlayer player)
            {
                var activeItem = player.GetActiveItem();
                if (activeItem == null)
                    return;

                BaseProjectile weapon = activeItem.GetHeldEntity() as BaseProjectile;
                if (weapon == null || weapon.primaryMagazine == null)
                    return;

                int ammoCapacity = weapon.primaryMagazine.capacity;
                weapon.SetAmmoCount(ammoCapacity);
                weapon.SendNetworkUpdateImmediate();
            }
        }

        public static class DataFileUtil
        {
            private const string FOLDER = "";

            public static string GetFilePath(string filename = null)
            {
                if (filename == null)
                    filename = _plugin.Name;

                return Path.Combine(FOLDER, filename);
            }

            public static string[] GetAllFilePaths()
            {
                string[] filePaths = Interface.Oxide.DataFileSystem.GetFiles(FOLDER);

                for (int i = 0; i < filePaths.Length; i++)
                {
                    filePaths[i] = filePaths[i].Substring(0, filePaths[i].Length - 5);
                }

                return filePaths;
            }

            public static bool Exists(string filePath)
            {
                return Interface.Oxide.DataFileSystem.ExistsDatafile(filePath);
            }

            public static T Load<T>(string filePath) where T : class, new()
            {
                T data = Interface.Oxide.DataFileSystem.ReadObject<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static T LoadIfExists<T>(string filePath) where T : class, new()
            {
                if (Exists(filePath))
                    return Load<T>(filePath);
                else
                    return null;
            }

            public static T LoadOrCreate<T>(string filePath) where T : class, new()
            {
                T data = LoadIfExists<T>(filePath);
                if (data == null)
                    data = new T();

                return data;
            }

            public static void Save<T>(string filePath, T data)
            {
                Interface.Oxide.DataFileSystem.WriteObject<T>(filePath, data);
            }

            public static void Delete(string filePath)
            {
                Interface.Oxide.DataFileSystem.DeleteDataFile(filePath);
            }
        }

        #endregion Helper Classes

        #region Gear Set Equipping

        private bool EquipGearSet(BasePlayer player, string gearSetName, bool clearInventory = true)
        {
            if (!PluginLoaded(_plugin.GearCore))
                return false;

            return _plugin.GearCore.Call<bool>("EquipGearSet", player, gearSetName, clearInventory);
        }

        #endregion Gear Set Equipping

        #region Command Execution

        private enum CommandType
        {
            Chat,
            Server,
            Client
        }

        private void RunCommand(BasePlayer player, CommandType type, string command)
        {
            string withPlaceholdersReplaced = command
                .Replace("{PlayerId}", player.UserIDString)
                .Replace("{PlayerName}", player.displayName)
                .Replace("{PositionX}", player.transform.position.x.ToString())
                .Replace("{PositionY}", player.transform.position.y.ToString())
                .Replace("{PositionZ}", player.transform.position.z.ToString())
                .Replace("{Grid}", PhoneController.PositionToGridCoord(player.transform.position));

            if (type == CommandType.Chat)
            {
                player.Command(string.Format("chat.say \"{0}\"", withPlaceholdersReplaced));
            }
            else if (type == CommandType.Client)
            {
                player.Command(withPlaceholdersReplaced);
            }
            else if (type == CommandType.Server)
            {
                Server.Command(withPlaceholdersReplaced);
            }
        }

        #endregion Command Execution

        #region Localization

        private class Lang
        {
            public const string HealthRestored = "HealthRestored";
            public const string AmmoRefilled = "AmmoRefilled";
            public const string GearSetGiven = "GearSetGiven";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.HealthRestored] = "You have been healed by <color=#75A838>{0}</color> health points!",
                [Lang.AmmoRefilled] = "Your ammo has been fully topped up!",
                [Lang.GearSetGiven] = "You have received the gear set <color=#CACF52>{0}</color>!"

            }, this, "en");
        }

        private static string GetMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = _plugin.lang.GetMessage(messageKey, _plugin, player.UserIDString);

            if (args.Length > 0)
                message = string.Format(message, args);

            return message;
        }

        public static void MessagePlayer(BasePlayer player, string messageKey, params object[] args)
        {
            string message = GetMessage(player, messageKey, args);
            _plugin.SendReply(player, message);
        }

        #endregion Localization
    }
}