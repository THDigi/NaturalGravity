using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRageMath;
using VRage;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.Utils;
using Digi.Utils;

namespace Digi.NaturalGravity
{
    public class Settings
    {
        private const string CONFIG_FILE = "config.txt";
        private const byte CONFIG_SAVE = 0x0;
        private const byte CONFIG_LOAD = 0x1;
        private const byte CONFIG_RESET = 0x2;

        private const ushort PACKET_CONFIG = NaturalGravity.PACKET_SYNC + 1;
        private const ushort PACKET_ASKSETTINGS = PACKET_CONFIG + 1;
        private const ushort PACKET_SETTINGS = PACKET_CONFIG + 2;

        public static readonly Encoding encode = Encoding.ASCII;

        public static bool affect_ships;
        public static int mass_limit;
        public static int mass_divide;
        public static int jetpack;
        public static bool jetpack_hover;
        public static bool notify;
        public static string[] asteroid_prefix;
        public static int asteroid_maxsize;
        public static int radius_min;
        public static int radius_max;
        public static float strength_min;
        public static float strength_max;

        public const bool DEFAULT_AFFECT_SHIPS = true;
        public const int DEFAULT_MASS_LIMIT = 200000;
        public const int DEFAULT_MASS_DIVIDE = 10;
        public const int DEFAULT_JETPACK = 100;
        public const bool DEFAULT_JETPACK_HOVER = false;
        public const bool DEFAULT_NOTIFY = true;
        public const string DEFAULT_ASTEROID_PREFIX = "gravity_";
        public const int DEFAULT_ASTEROID_MAXSIZE = 4096;
        public const int DEFAULT_RADIUS_MIN = 1000;
        public const int DEFAULT_RADIUS_MAX = 50000;
        public const float DEFAULT_STRENGTH_MIN = 0.01f;
        public const float DEFAULT_STRENGTH_MAX = 1.0f;

        private static void ResetSettings()
        {
            Log.Info("Settings asigned to defaults.");

            affect_ships = DEFAULT_AFFECT_SHIPS;
            mass_limit = DEFAULT_MASS_LIMIT;
            mass_divide = DEFAULT_MASS_DIVIDE;
            jetpack = DEFAULT_JETPACK;
            jetpack_hover = DEFAULT_JETPACK_HOVER;
            notify = DEFAULT_NOTIFY;
            asteroid_prefix = DEFAULT_ASTEROID_PREFIX.Split(',').Select(s => s.Trim()).ToArray();
            asteroid_maxsize = DEFAULT_ASTEROID_MAXSIZE;
            radius_min = DEFAULT_RADIUS_MIN;
            radius_max = DEFAULT_RADIUS_MAX;
            strength_min = DEFAULT_STRENGTH_MIN;
            strength_max = DEFAULT_STRENGTH_MAX;
        }

        public static bool ParseSetting(string line, bool verbose, char splitCharacter)
        {
            string[] args = line.Split(splitCharacter);

            if (args.Length != 2)
            {
                Log.Error("Invalid setting: '" + line + "'");
                return false;
            }

            string param = args[0].Trim();
            string value = args[1].Trim();
            bool success;

            switch (param)
            {
                case "affect_ships":
                    return SetValue(ref affect_ships, param, value, verbose, bool.TryParse);
                case "mass_limit":
                    return SetValue(ref mass_limit, param, value, verbose, int.TryParse);
                case "mass_divide":
                    return SetValue(ref mass_divide, param, value, verbose, int.TryParse);
                case "jetpack":
                    return SetValue(ref jetpack, param, value, verbose, int.TryParse);
                case "jetpack_hover":
                    return SetValue(ref jetpack_hover, param, value, verbose, bool.TryParse);
                case "notify":
                    return SetValue(ref notify, param, value, verbose, bool.TryParse);
                case "asteroid_prefix":
                    return SetValue(ref asteroid_prefix, param, value, verbose);
                case "asteroid_maxsize":
                    return SetValue(ref asteroid_maxsize, param, value, verbose, int.TryParse);
                case "radius_min":
                    success = SetValue(ref radius_min, param, value, verbose, int.TryParse);
                    if (success)
                        radius_min = Math.Min(Math.Max(radius_min, NaturalGravity.RADIUS_MIN), NaturalGravity.RADIUS_MAX);
                    return success;
                case "radius_max":
                    success = SetValue(ref radius_max, param, value, verbose, int.TryParse);
                    if (success)
                        radius_max = Math.Min(Math.Max(radius_max, NaturalGravity.RADIUS_MIN), NaturalGravity.RADIUS_MAX);
                    return success;
                case "strength_min":
                    success = SetValue(ref strength_min, param, value, verbose, float.TryParse);
                    if (success)
                        strength_min = Math.Min(Math.Max(strength_min, NaturalGravity.STRENGTH_MIN), NaturalGravity.STRENGTH_MAX);
                    return success;
                case "strength_max":
                    success = SetValue(ref strength_max, param, value, verbose, float.TryParse);
                    if (success)
                        strength_max = Math.Min(Math.Max(strength_max, NaturalGravity.STRENGTH_MIN), NaturalGravity.STRENGTH_MAX);
                    return success;
            }

            if (verbose)
                MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "The '" + param + "' setting does not exist.");
            else
                Log.Error("The '" + param + "' setting does not exist.");

            return false;
        }

        public static string GetSettingsString(bool comments)
        {
            StringBuilder str = new StringBuilder();

            if (comments)
            {
                str.AppendLine("// Config file, anything that starts with // is a comment");
                str.AppendLine("// This file gets re-saved after being loaded to keep comments and variables up-to-date.");
                str.AppendLine("");
                str.AppendLine("");
                str.AppendLine("// Enable/disable if ships are affected by natural gravity, default " + DEFAULT_AFFECT_SHIPS + ".");
            }
            str.AppendLine("affect_ships=" + affect_ships);

            if (comments)
            {
                str.AppendLine("");
                str.AppendLine("// Ship mass above this limit will be ignored (or divided by the mass_divide option).");
                str.AppendLine("// Set to 0 to disable mass limits; default: " + DEFAULT_MASS_LIMIT);
            }
            str.AppendLine("mass_limit=" + mass_limit);

            if (comments)
            {
                str.AppendLine("");
                str.AppendLine("// Divide the ship mass used in gravity by this number.");
                str.AppendLine("// Set to 0 to disable mass dividing; default: " + DEFAULT_MASS_DIVIDE);
            }
            str.AppendLine("mass_divide=" + mass_divide);

            if (comments)
            {
                str.AppendLine("// Exact formula for mass_limit and mass_divide is:");
                str.AppendLine("// if ship_mass > mass_limit");
                str.AppendLine("//     gravity_mass = mass_limit + ((ship_mass - mass_limit) / mass_divide)");
                str.AppendLine("// otherwise");
                str.AppendLine("//     gravity_mass = ship_mass");
            }

            if (comments)
            {
                str.AppendLine("");
                str.AppendLine("// Percentage of gravity affecting players with jetpack on.");
                str.AppendLine("// Set to 0 to disable, values beyond 100 are supported; default: " + DEFAULT_JETPACK);
            }
            str.AppendLine("jetpack=" + jetpack);

            if (comments)
            {
                str.AppendLine("");
                str.AppendLine("// Enable/disable ignoring players with dampeners on, default: " + DEFAULT_JETPACK_HOVER);
            }
            str.AppendLine("jetpack_hover=" + jetpack_hover);

            if (comments)
            {
                str.AppendLine("");
                str.AppendLine("// Enable/disable the notifications when entering/leaving natural gravity fields, default: " + DEFAULT_NOTIFY);
            }
            str.AppendLine("notify=" + notify);

            if (comments)
            {
                str.AppendLine("");
                str.AppendLine("// All asteroids with this prefix will be added natural gravity, letter case does not matter.");
                str.AppendLine("// You can specify more than one prefix separated by comma.");
                str.AppendLine("// Set to null to disable, Default: " + DEFAULT_ASTEROID_PREFIX);
            }

            str.AppendLine("asteroid_prefix=" + String.Join(", ", asteroid_prefix));

            if (comments)
            {
                str.AppendLine("");
                str.AppendLine("// This number is used in radius and strength calculation, the maximum size that radius_max will be applied to.");
                str.AppendLine("// The asteroid size is its boundary size, so an 511x513x511m asteroid will have a boundary size of 1024.");
                str.AppendLine("// Default: " + DEFAULT_ASTEROID_MAXSIZE);
            }

            str.AppendLine("asteroid_maxsize=" + asteroid_maxsize);

            if (comments)
            {
                str.AppendLine("");
                str.AppendLine("// The minimum and maximum range that the asteroid will have relative to their size.");
                str.AppendLine("// These values are used when creating gravity for an asteroid (manually or automatically).");
                str.AppendLine("// Exact formula: (asteroid_size / asteroid_maxsize) * radius_max limiting between radius_min and radius_max");
                str.AppendLine("// Default: " + DEFAULT_RADIUS_MIN + " and " + DEFAULT_RADIUS_MAX + "; Values limited between " + NaturalGravity.RADIUS_MIN + " and + " + NaturalGravity.RADIUS_MAX + ".");
            }

            str.AppendLine("radius_min=" + radius_min);
            str.AppendLine("radius_max=" + radius_max);

            if (comments)
            {
                str.AppendLine("");
                str.AppendLine("// These values determine the minimum and maximum strength in Gs that asteroids have when they're created (manually or automatically).");
                str.AppendLine("// Exact formula: (asteroid_size / asteroid_maxsize) * strength_max limiting between strength_min and strength_max");
                str.AppendLine("// Default: " + DEFAULT_STRENGTH_MIN + " and " + DEFAULT_STRENGTH_MAX + "; Values limited between " + NaturalGravity.STRENGTH_MIN + " and " + NaturalGravity.STRENGTH_MAX + ".");
            }

            str.AppendLine("strength_min=" + strength_min);
            str.AppendLine("strength_max=" + strength_max);

            return str.ToString().Trim(' ', '\r', '\n');
        }

        public static void Init()
        {
            ResetSettings(); // make sure all settings have values

            MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET_SETTINGS, ReceivedSettings);

            if (NaturalGravity.isServer)
            {
                MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET_ASKSETTINGS, ReceivedSettingsRequest);
                MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET_CONFIG, ReceivedConfigRequest);

                LoadConfig(); // load changed settings
                SaveConfig(); // re-save to update comments and variables or create the file

                Log.Info("Loaded settings:\n" + GetSettingsString(false));
            }
            else
            {
                RequestSettings(); // When you're joining as a player this will ask the server for the settings
            }
        }

        public static void Close()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET_SETTINGS, ReceivedSettings);

            if (NaturalGravity.isServer)
            {
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET_ASKSETTINGS, ReceivedSettingsRequest);
                MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET_CONFIG, ReceivedConfigRequest);
            }
        }

        public static void RequestSettings()
        {
            if (NaturalGravity.isServer)
                return;

            Log.Info("Requesting settings from server...");

            byte[] bytes = encode.GetBytes(MyAPIGateway.Multiplayer.MyId.ToString());
            MyAPIGateway.Multiplayer.SendMessageToServer(PACKET_ASKSETTINGS, bytes, true);
        }

        public static void SyncSettings()
        {
            if (NaturalGravity.isServer)
                SendSettingsToClients();
            else
                SendSettings(0);
        }

        public static void SendSettingsToClients()
        {
            if (!NaturalGravity.isServer)
                return;

            Log.Info("Sending settings to all clients...");

            string data = GetSettingsString(false);
            byte[] bytes = encode.GetBytes(data);

            MyAPIGateway.Multiplayer.SendMessageToOthers(PACKET_SETTINGS, bytes, true);
        }

        private static void SendSettings(ulong sendTo)
        {
            string data = GetSettingsString(false);
            byte[] bytes = encode.GetBytes(data);

            if (sendTo == 0)
            {
                Log.Info("Sending settings to server...");
                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET_SETTINGS, bytes, true);
            }
            else
            {
                Log.Info("Sending settings to client " + sendTo + "...");
                MyAPIGateway.Multiplayer.SendMessageTo(PACKET_SETTINGS, bytes, sendTo, true);
            }
        }

        protected static void ReceivedSettings(byte[] bytes)
        {
            string data = encode.GetString(bytes);
            string[] lines = data.Split('\n');

            Log.Info("Network Debug: ReceivedSettings, settings='" + data + "'");
            Log.IncreaseIndent();

            foreach (string line in lines)
            {
                if (!ParseSetting(line.Trim(), false, '='))
                    Log.Info("Failed to parse line:" + line.Trim());
            }

            if (NaturalGravity.isServer)
                SendSettingsToClients();

            Log.DecreaseIndent();
        }

        protected static void ReceivedSettingsRequest(byte[] bytes)
        {
            string data = encode.GetString(bytes);
            ulong sendTo;

            Log.Info("Network Debug: ReceivedSettingsRequest, sender='" + data + "'");

            if (!ulong.TryParse(data, out sendTo))
            {
                Log.Error("unable to convert '" + data + "' to ulong!");
                return;
            }

            SendSettings(sendTo);
        }

        protected static void ReceivedConfigRequest(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                Log.Error("ReceivedConfigRequest has no data!");
                return;
            }

            Log.Info("Network Debug: ReceivedConfigRequest, action='" + bytes[0] + "'");

            switch (bytes[0])
            {
                case CONFIG_SAVE:
                    SaveConfig();
                    return;
                case CONFIG_LOAD:
                    LoadConfig();
                    return;
                case CONFIG_RESET:
                    ResetConfig();
                    return;
            }

            Log.Error("ReceivedConfigRequest has unknown action: '" + bytes[0] + "'");
        }

        public static void SaveConfig()
        {
            if (NaturalGravity.isServer)
            {
                Log.Info("Saving config to storage...");
                Log.IncreaseIndent();

                try
                {
                    var write = MyAPIGateway.Utilities.WriteFileInLocalStorage(CONFIG_FILE, typeof(Settings));
                    write.Write(GetSettingsString(true));
                    write.Flush();
                    write.Close();

                    Log.Info("Finished writing.");
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }

                Log.DecreaseIndent();
            }
            else
            {
                Log.Info("Sending config save request...");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET_CONFIG, new byte[] { CONFIG_SAVE }, true);
            }
        }

        public static void LoadConfig()
        {
            if (NaturalGravity.isServer)
            {
                Log.Info("Loading config from storage...");
                Log.IncreaseIndent();

                try
                {
                    if (!MyAPIGateway.Utilities.FileExistsInLocalStorage(CONFIG_FILE, typeof(Settings)))
                    {
                        Log.Info("File does not exist.");
                    }
                    else
                    {
                        var read = MyAPIGateway.Utilities.ReadFileInLocalStorage(CONFIG_FILE, typeof(Settings));
                        string line;

                        while ((line = read.ReadLine()) != null)
                        {
                            line = line.Trim(' ', '\r', '\n');

                            if (line == String.Empty || line.StartsWith("//"))
                                continue;

                            int commentIndex = line.IndexOf("//");

                            if (commentIndex > 0)
                                line = line.Substring(0, line.Length - commentIndex);

                            if (!ParseSetting(line, false, '='))
                                Log.Info("Failed to parse line:" + line);
                        }

                        read.Close();

                        Log.Info("Finished reading.");

                        SendSettingsToClients();
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e);
                }

                Log.DecreaseIndent();
            }
            else
            {
                Log.Info("Sending config load request...");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET_CONFIG, new byte[] { CONFIG_LOAD }, true);
            }
        }

        public static void ResetConfig()
        {
            if (NaturalGravity.isServer)
            {
                Log.Info("Reset config method called...");
                Log.IncreaseIndent();

                ResetSettings();
                SendSettingsToClients();

                Log.DecreaseIndent();
            }
            else
            {
                Log.Info("Sending config reset request...");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET_CONFIG, new byte[] { CONFIG_RESET }, true);
            }
        }

        public static bool SetValue(ref string[] key, string param, string value, bool verbose)
        {
            if (value == "null")
            {
                key = null;
                value = "(disabled)";
            }
            else
            {
                key = value.Split(',');

                for (int i = 0; i < key.Length; i++)
                {
                    key[i] = key[i].Trim();
                }

                value = String.Join(", ", key);
            }

            SetValueResult(true, param, value, verbose);
            return true;
        }

        public static bool SetValue<T>(ref T key, string param, string value, bool verbose, TryParseHandler<T> handler) where T : struct
        {
            T result = default(T);
            bool success = !String.IsNullOrEmpty(value) && handler(value, out result);

            if (success)
            {
                key = result;
                value = key.ToString();
            }

            SetValueResult(success, param, value, verbose);
            return success;
        }

        public delegate bool TryParseHandler<T>(string value, out T result);

        private static void SetValueResult(bool result, string param, string value, bool verbose)
        {
            if (verbose)
            {
                if (result)
                    MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Set '" + param + "' to '" + value + "'");
                else
                    MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Invalid value: '" + value + "'.");
            }
            else if (!result)
            {
                Log.Error("'" + param + "' has an invalid value: '" + value + "'.");
            }
        }
    }
}