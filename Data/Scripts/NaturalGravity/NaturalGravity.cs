using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Engine;
using Sandbox.Engine.Physics;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using VRage.Common.Utils;
using VRageMath;
using VRage;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Components;
using VRage.Utils;
using VRage.Voxels;
using Digi.Utils;

namespace Digi.NaturalGravity
{
    // TODO FIX: jetpack_hover not working on clients ?

    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    class NaturalGravity : MySessionComponentBase
    {
        public static bool init { get; private set; }
        public static bool isServer { get; private set; }
        public static bool isDedicated { get; private set; }

        public const string MOD_SHORTNAME = "[NG]";
        public const string PREFAB_NAME = "NaturalGravityBlocks";

        public const float STRENGTH_MIN = 0.0f;
        public const float STRENGTH_MAX = 1.0f;
        public const int RADIUS_MIN = 1;
        public const int RADIUS_MAX = 999999999;

        public const float G = 9.80665f;
        public const float GG_MAX_GRAVITY = 9.81f;

        private const int SKIP_TICKS = 10;
        private const int SKIP_EXTRA_TICKS = 30;

        public static Nullable<Vector3D> playerPos = null;
        private static int tick = SKIP_TICKS;
        private static int extraTick = 0;
        public static List<Gravity> gravityPoints = new List<Gravity>();
        private static HashSet<IMyEntity> entities = new HashSet<IMyEntity>();
        private static MyStorageDataCache cache = new MyStorageDataCache();

        private const string LOG_FILE = "info.log";

        public const ushort PACKET_SYNC = 12316;

        public void Init()
        {
            Log.Info("Initialized.");
            init = true;
            isServer = MyAPIGateway.Multiplayer.IsServer || MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE;
            isDedicated = (MyAPIGateway.Utilities.IsDedicated && isServer);

            MyAPIGateway.Entities.OnEntityAdd += EventEntityAdded;
            MyAPIGateway.Entities.OnEntityRemove += EventEntityRemoved;
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;

            MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET_SYNC, ReceivedSyncPacket);

            MyAPIGateway.Session.DamageSystem.RegisterBeforeDamageHandler(0, BeforeDamage);

            Settings.Init();
        }

        protected override void UnloadData()
        {
            init = false;
            entities.Clear();
            gravityPoints.Clear();

            MyAPIGateway.Entities.OnEntityAdd -= EventEntityAdded;
            MyAPIGateway.Entities.OnEntityRemove -= EventEntityRemoved;
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;

            MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET_SYNC, ReceivedSyncPacket);

            Settings.Close();
            Log.Close();
        }

        public void BeforeDamage(Object target, ref MyDamageInformation info)
        {
            if (target is IMySlimBlock)
            {
                var block = target as IMySlimBlock;

                if (block.CubeGrid.IsStatic && block.CubeGrid.Name == PREFAB_NAME)
                {
                    info.Amount = 0; // prevent damage to the natural gravity blocks.
                }
            }
        }

        private Gravity GetGravityInAsteroid(IMyVoxelBase asteroid)
        {
            Vector3D min = asteroid.PositionLeftBottomCorner;
            Vector3D max = min + asteroid.Storage.Size;

            foreach (Gravity gravity in gravityPoints)
            {
                if (gravity.center.IsInsideInclusive(ref min, ref max))
                {
                    return gravity;
                }
            }

            return null;
        }

        private IMyVoxelBase GetNearbyAsteroid()
        {
            entities.Clear();
            MyAPIGateway.Entities.GetEntities(entities, ent => ent is IMyVoxelBase);
            Vector3D pos = MyAPIGateway.Session.Player.GetPosition();
            Vector3D min;
            Vector3D max;

            foreach (IMyVoxelBase asteroid in entities)
            {
                min = asteroid.PositionLeftBottomCorner;
                max = min + asteroid.Storage.Size;

                if (min.X <= pos.X && pos.X <= max.X && min.Y <= pos.Y && pos.Y <= max.Y && min.Z <= pos.Z && pos.Z <= max.Z)
                {
                    return asteroid;
                }
            }

            return null;
        }

        private bool IsAdmin(bool verbose)
        {
            bool admin = isServer; // || MyAPIGateway.Utilities.ConfigDedicated.Administrators.Contains(MyAPIGateway.Session.Player.SteamUserId.ToString());

            if (!admin)
            {
                var clients = MyAPIGateway.Session.GetCheckpoint("null").Clients;

                if (clients != null)
                {
                    var client = clients.FirstOrDefault(c => c.SteamId == MyAPIGateway.Session.Player.SteamUserId && c.IsAdmin);
                    admin = (client != null);
                }
            }

            if (verbose && !admin)
                MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Only admins can use this command.");

            return admin;
        }

        public void MessageEntered(string message, ref bool visible)
        {
            if (!message.StartsWith("/ng", StringComparison.InvariantCultureIgnoreCase))
                return;

            visible = false;

            if (message.Length > "/ng".Length)
            {
                message = message.Substring("/ng ".Length).Trim().ToLower();

                if (message.Equals("settings"))
                {
                    MyAPIGateway.Utilities.ShowMissionScreen("Natural Gravity Settings", "", "", Settings.GetSettingsString(false), null, "CLOSE");
                    return;
                }

                if (message.StartsWith("info"))
                {
                    if (!IsAdmin(true))
                        return;

                    MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Gravity points: " + gravityPoints.Count + ".");

                    if (isDedicated)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Can't see extra info as the dedicated server!");
                        return;
                    }

                    IMyVoxelBase asteroid = GetNearbyAsteroid();

                    if (asteroid == null)
                    {
                        MyAPIGateway.Utilities.ShowMessage("Asteroid info", "You're not within an asteroids' boundary box.");
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage("Asteroid info", "Name: " + asteroid.StorageName + "; Boundary size: " + asteroid.Storage.Size.AbsMax());

                        Gravity gravity = GetGravityInAsteroid(asteroid);

                        if (gravity == null)
                            MyAPIGateway.Utilities.ShowMessage("Gravity info", "The asteroid doesn't have natural gravity.");
                        else
                            MyAPIGateway.Utilities.ShowMessage("Gravity info", "Enabled: " + gravity.generator.Enabled + "; Radius: " + gravity.generator.Radius + "; Strength: " + (gravity.generator.Gravity / NaturalGravity.GG_MAX_GRAVITY) + "; Center: " + gravity.center + ".");
                    }

                    return;
                }

                if (message.StartsWith("set"))
                {
                    if (!IsAdmin(true))
                        return;

                    message = message.Substring("set".Length).Trim();

                    if (message == "")
                    {
                        MyAPIGateway.Utilities.ShowMissionScreen("Natural Gravity Settings", "", "", Settings.GetSettingsString(false), null, "CLOSE");
                        return;
                    }

                    if (message.IndexOf(' ') == -1)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Invalid format, use: /ng set <setting> <value>");
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Type '/ng set' or '/ng settings' to see available settings.");
                        return;
                    }

                    if (Settings.ParseSetting(message, true, ' '))
                    {
                        Settings.SyncSettings();
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Error parsing setting or its value!");
                    }

                    return;
                }

                if (message.StartsWith("config"))
                {
                    if (!IsAdmin(true))
                        return;

                    message = message.Substring("config".Length).Trim();

                    if (message.Equals("save"))
                    {
                        Settings.SaveConfig();
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Config saved.");
                        return;
                    }
                    else if (message.Equals("reload"))
                    {
                        Settings.LoadConfig();
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Config reloaded.");
                        return;
                    }
                    else if (message.Equals("reset"))
                    {
                        Settings.ResetConfig();
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Config reset to defaults (but not saved).");
                        return;
                    }
                }

                if (message.StartsWith("create"))
                {
                    if (isDedicated)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Can't create asteroids as the dedicated server!");
                        return;
                    }

                    if (!IsAdmin(true))
                        return;

                    IMyVoxelBase asteroid = GetNearbyAsteroid();

                    if (asteroid == null)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "You are not standing within an asteroid's bounding box.");
                        return;
                    }

                    Gravity gravity = GetGravityInAsteroid(asteroid);

                    if (gravity != null)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "The asteroid you're near ('" + asteroid.StorageName + "') already has natural gravity.");
                        return;
                    }

                    int radius;
                    float strength;
                    Vector3D center;
                    GetAsteroidData(asteroid, out center, out radius, out strength);
                    AddGravityPrefab(center, radius, strength);

                    MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Added natural gravity to the '" + asteroid.StorageName + "' asteroid, radius=" + radius + ", strength=" + strength + ", at:" + center + ".");
                    return;
                }

                bool msgRadius = message.StartsWith("radius");
                bool msgStrength = message.StartsWith("strength");
                bool on = message.Equals("on");
                bool msgOnOff = on || message.Equals("off");
                bool msgRemove = message.StartsWith("remove");

                if (msgRadius || msgStrength || msgOnOff || msgRemove)
                {
                    if (isDedicated)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Can't edit asteroids as the dedicated server!");
                        return;
                    }

                    if (!IsAdmin(true))
                        return;

                    IMyVoxelBase asteroid = GetNearbyAsteroid();

                    if (asteroid == null)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "You're not within an asteroids' boundary box.");
                        return;
                    }

                    Gravity gravity = GetGravityInAsteroid(asteroid);

                    if (gravity == null)
                    {
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "The asteroid you're near ('" + asteroid.StorageName + "') does not have natural gravity.");
                        return;
                    }

                    if (msgRadius)
                    {
                        message = message.Substring("radius".Length).Trim();

                        if (message.Equals("reset"))
                        {
                            gravity.SetRadius(CalculateAsteroidRadius(asteroid));

                            MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Reset the range to the calculated value: " + gravity.generator.Radius + ".");
                            return;
                        }

                        int radius;

                        if (!int.TryParse(message, out radius))
                        {
                            MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Invalid parameter, not an integer: '" + message + "'.");
                            return;
                        }

                        radius = gravity.SetRadius(radius);
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Radius set to: '" + radius + "'.");
                        return;
                    }

                    if (msgStrength)
                    {
                        message = message.Substring("strength".Length).Trim();

                        if (message.Equals("reset"))
                        {
                            gravity.SetStrength(CalculateAsteroidStrength(asteroid));
                            MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Reset the strength to the calculated value: " + (gravity.generator.Gravity / NaturalGravity.GG_MAX_GRAVITY) + ".");
                            return;
                        }

                        float strength;

                        if (!float.TryParse(message, out strength))
                        {
                            MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Invalid parameter, not a float: '" + message + "'.");
                            return;
                        }

                        strength = gravity.SetStrength(strength);
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Strength set to: '" + strength + "'.");
                        return;
                    }

                    if (msgOnOff)
                    {
                        gravity.SetEnabled(on);
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Asteroid's gravity turned " + (on ? "ON" : "OFF") + ".");
                        return;
                    }

                    if (msgRemove)
                    {
                        gravity.Remove();
                        MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Removed natural gravity from '" + asteroid.StorageName + "'.");
                        return;
                    }
                }

                MyAPIGateway.Utilities.ShowMessage(MOD_SHORTNAME, "Unknown parameter: " + message);
            }

            StringBuilder builder = new StringBuilder();
            bool admin = IsAdmin(false);

            if (admin)
            {
                builder.AppendLine("/ng create");
                builder.AppendLine("/ng <on/off>");
                builder.AppendLine("/ng radius <" + RADIUS_MIN + "-" + RADIUS_MAX + "/reset>");
                builder.AppendFormat("/ng strength <{0:0.00}-{1:0.00}/reset>\n", STRENGTH_MIN, STRENGTH_MAX);
                builder.AppendLine("/ng remove");
                builder.AppendLine("/ng info");
                builder.AppendLine("/ng config save");
                builder.AppendLine("/ng config reload");
                builder.AppendLine("/ng config reset");
                builder.AppendLine("/ng set <setting> <value>");
            }

            builder.Append("/ng settings - shows mod settings");

            MyAPIGateway.Utilities.ShowMissionScreen("Natural Gravity Commands", "", "", builder.ToString(), null, "CLOSE");
        }

        public void EventEntityAdded(IMyEntity ent)
        {
            if (ValidateEntity(ent))
            {
                GravityPointsChangeEntity(ent, true);
            }
        }

        public void EventEntityRemoved(IMyEntity ent)
        {
            if (ValidateEntity(ent))
            {
                GravityPointsChangeEntity(ent, false);
            }
        }

        private bool ValidateEntity(IMyEntity ent)
        {
            return ent is IMyCubeGrid && ent.Physics != null && !ent.Physics.IsStatic && ent.Physics.Enabled && !ent.Physics.IsPhantom;
        }

        public static void AddGravityPrefab(Vector3D position, int radius, float strength, bool sync = true)
        {
            try
            {
                var prefab = MyDefinitionManager.Static.GetPrefabDefinition(PREFAB_NAME);

                if (prefab == null)
                {
                    Log.Error("Can't find prefab: " + PREFAB_NAME);
                    return;
                }

                if (prefab.CubeGrids == null)
                {
                    MyDefinitionManager.Static.ReloadPrefabsFromFile(prefab.PrefabPath);
                    prefab = MyDefinitionManager.Static.GetPrefabDefinition(PREFAB_NAME);
                }

                if (prefab.CubeGrids.Length == 0)
                {
                    Log.Error("Prefab is broken, does not have any grids in it!");
                    return;
                }

                MyObjectBuilder_CubeGrid builder = prefab.CubeGrids[0].Clone() as MyObjectBuilder_CubeGrid;
                builder.Name = PREFAB_NAME;
                builder.PositionAndOrientation = new MyPositionAndOrientation(position, Vector3D.Forward, Vector3D.Up);

                MyObjectBuilder_GravityGeneratorSphere generator = builder.CubeBlocks[0] as MyObjectBuilder_GravityGeneratorSphere;
                generator.Radius = radius;
                generator.GravityAcceleration = strength * G;

                MyAPIGateway.Entities.RemapObjectBuilder(builder);
                IMyEntity ent = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(builder);

                if (ent == null)
                {
                    Log.Error("Prefab created a null entity!");
                    return;
                }

                ent.Flags |= EntityFlags.Sync | EntityFlags.Save;

                if (sync)
                {
                    Log.Info("Spawned " + PREFAB_NAME + " and sent over network; pos=" + position + "; radius=" + radius + "; strength=" + strength);

                    // TODO remove temporary fix
                    StringBuilder data = new StringBuilder();

                    data.Append(0); // 0 = created
                    data.Append(';');
                    data.Append(position.GetDim(0));
                    data.Append(';');
                    data.Append(position.GetDim(1));
                    data.Append(';');
                    data.Append(position.GetDim(2));
                    data.Append(';');
                    data.Append(radius);
                    data.Append(';');
                    data.Append(strength);

                    MyAPIGateway.Multiplayer.SendMessageToOthers(PACKET_SYNC, Settings.encode.GetBytes(data.ToString()), true);

                    //MyAPIGateway.Multiplayer.SendEntitiesCreated(new List<MyObjectBuilder_EntityBase>() { ent as MyObjectBuilder_EntityBase });
                }
                else
                {
                    Log.Info("Spawned " + PREFAB_NAME + "; pos=" + position + "; radius=" + radius + "; strength=" + strength);
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
            }
        }

        public void ReceivedSyncPacket(byte[] bytes)
        {
            string data = Settings.encode.GetString(bytes);
            string[] args = data.Split(';');

            Log.Info("Network Debug: ReceivedSyncPacket, data='" + data + "'");

            if (args.Length == 0)
            {
                Log.Error("No data!");
                return;
            }

            int type;

            if (!int.TryParse(args[0], out type))
            {
                Log.Error("Invalid type: " + args[0]);
                return;
            }

            switch (type)
            {
                case 0:
                    SyncCreated(args);
                    return;
                case 1:
                    SyncRemoved(args);
                    return;
            }

            Log.Error("Unknown type: " + args[0]);
        }

        private void SyncCreated(string[] args)
        {
            if (args.Length != 6)
            {
                Log.Error("Invalid number of arguments: " + args.Length + "; need exacly 6!");
                return;
            }

            Vector3D position;
            int radius;
            float strength;

            if (!ParseVector(out position, args[1], args[2], args[3]))
                return;

            if (!ParseInt(out radius, args[4]))
                return;

            if (!ParseFloat(out strength, args[5]))
                return;

            AddGravityPrefab(position, radius, strength, false);
        }

        private void SyncRemoved(string[] args)
        {
            if (args.Length != 4)
            {
                Log.Error("Invalid number of arguments: " + args.Length + "; need exacly 4!");
                return;
            }

            Vector3D position = new Vector3D(0);

            if (!ParseVector(out position, args[1], args[2], args[3]))
                return;

            BoundingBoxD box = new BoundingBoxD(position - Vector3D.One, position + Vector3D.One);

            foreach (Gravity gravity in gravityPoints)
            {
                if (gravity.center.IsInsideInclusive(ref box.Min, ref box.Max))
                {
                    Log.Info("SyncRemoved; removed gravity at " + gravity.center);
                    gravity.Remove(false);
                    break;
                }
            }
        }

        private bool ParseVector(out Vector3D position, string x, string y, string z)
        {
            position = new Vector3D(0);
            string[] args = { x, y, z };
            double d;

            for (int i = 0; i < 3; i++)
            {
                if (double.TryParse(args[i], out d))
                {
                    position.SetDim(i, d);
                }
                else
                {
                    Log.Error("Invalid double value: " + args[i]);
                    return false;
                }
            }

            return true;
        }

        private bool ParseInt(out int num, string str)
        {
            if (!int.TryParse(str, out num))
            {
                Log.Error("Invalid integer value: " + str);
                return false;
            }

            return true;
        }

        private bool ParseFloat(out float num, string str)
        {
            if (!float.TryParse(str, out num))
            {
                Log.Error("Invalid float value: " + str);
                return false;
            }

            return true;
        }

        public static void AddGravityPoint(Gravity gravity)
        {
            if (gravity == null)
                throw new Exception("Gravity point can not be null when adding it!");

            gravityPoints.Add(gravity);
        }

        public static void RemoveGravityPoint(Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere generator)
        {
            gravityPoints.RemoveAll(gravity => gravity.generator == generator);
        }

        public static void GravityPointsClearEnities()
        {
            foreach (Gravity gravity in gravityPoints)
            {
                gravity.entities.Clear();
            }
        }

        public static void GravityPointsAddInRange(IMyEntity ent)
        {
            foreach (Gravity gravity in gravityPoints)
            {
                gravity.AddInRange(ent);
            }
        }

        public static void GravityPointsChangeEntity(IMyEntity ent, bool add)
        {
            foreach (Gravity gravity in gravityPoints)
            {
                if (gravity.InRadius(ent))
                {
                    if (add)
                        gravity.entities.Add(ent);
                    else
                        gravity.entities.Remove(ent);
                }
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (!init)
            {
                if (MyAPIGateway.Session == null)
                    return;

                Init();
            }

            if (++tick >= SKIP_TICKS)
            {
                tick = 0;

                if (++extraTick >= SKIP_EXTRA_TICKS)
                {
                    extraTick = 0;
                    CheckAsteroids();
                }

                if (Settings.affect_ships && gravityPoints.Count > 0)
                {
                    entities.Clear();
                    MyAPIGateway.Entities.GetEntities(entities, ent => ValidateEntity(ent));

                    GravityPointsClearEnities();

                    foreach (IMyEntity ent in entities)
                    {
                        GravityPointsAddInRange(ent);
                    }
                }
            }

            if (gravityPoints.Count > 0)
            {
                if (!NaturalGravity.isDedicated && (Settings.notify || Settings.jetpack > 0) && MyAPIGateway.Session.Player != null && MyAPIGateway.Session.Player.Controller != null && MyAPIGateway.Session.Player.Controller.ControlledEntity != null && MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity != null)
                {
                    playerPos = MyAPIGateway.Session.Player.GetPosition();
                }
                else
                {
                    playerPos = null;
                }

                foreach (Gravity gravity in gravityPoints)
                {
                    gravity.Update();
                }
            }
        }

        private void CheckAsteroids()
        {
            if (Settings.asteroid_prefix == null)
                return;

            var asteroids = new List<IMyVoxelBase>();
            bool match;

            MyAPIGateway.Session.VoxelMaps.GetInstances(asteroids, a => (a as IMyEntity).Physics != null && (a as IMyEntity).Physics.Enabled && !(a as IMyEntity).Physics.IsPhantom);

            foreach (IMyVoxelBase asteroid in asteroids)
            {
                match = false;

                foreach (string prefix in Settings.asteroid_prefix)
                {
                    if (asteroid.StorageName.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }

                if (match && GetGravityInAsteroid(asteroid) == null)
                {
                    Log.Info("Found prefixed asteroid: " + asteroid.StorageName);

                    int radius;
                    float strength;
                    Vector3D center;
                    GetAsteroidData(asteroid, out center, out radius, out strength);
                    AddGravityPrefab(center, radius, strength);
                }
            }

            // FIXME temporary workaround
            HashSet<Vector3D> existing = new HashSet<Vector3D>();

            foreach (Gravity gravity in gravityPoints)
            {
                if (existing.Contains(gravity.center))
                {
                    Log.Info("WARNING: found and removed duplicated gravity at " + gravity.center);
                    gravity.Remove(false);
                }
                else
                {
                    existing.Add(gravity.center);
                }
            }
        }

        public static int CalculateAsteroidRadius(IMyVoxelBase asteroid)
        {
            double size = ((double)asteroid.Storage.Size.AbsMax() / (double)Settings.asteroid_maxsize);
            return Math.Min(Math.Max((int)Math.Round(size * Settings.radius_max), Settings.radius_min), Settings.radius_max);
        }

        public static float CalculateAsteroidStrength(IMyVoxelBase asteroid)
        {
            double size = ((double)asteroid.Storage.Size.AbsMax() / (double)Settings.asteroid_maxsize);
            return Math.Min(Math.Max((float)(size * Settings.strength_max), Settings.strength_min), Settings.strength_max);
        }

        public static void GetAsteroidData(IMyVoxelBase asteroid, out Vector3D center, out int radius, out float strength)
        {
            GetAsteroidData(asteroid, 2, out center, out radius, out strength);
        }

        /*
         * Credits to midspace for the help on this method
         * http://forums.keenswh.com/post/show_single_post?pid=1286008590&postcount=12
         */
        public static void GetAsteroidData(IMyVoxelBase asteroid, int lod, out Vector3D center, out int radius, out float strength)
        {
            int scale = Math.Max((int)Math.Pow(lod, 2), 1);
            Vector3I maxSize = asteroid.Storage.Size / scale;
            int diff = maxSize.AbsMax() / 512;

            if (diff > 1)
            {
                GetAsteroidData(asteroid, lod + diff, out center, out radius, out strength);
                return;
            }

            cache.Resize(maxSize);

            asteroid.Storage.ReadRange(cache, MyStorageDataTypeFlags.ContentAndMaterial, lod, Vector3I.Zero, maxSize - 1);

            Vector3I min = Vector3I.MaxValue;
            Vector3I max = Vector3I.MinValue;
            Vector3I p;
            byte content;

            for (p.Z = 0; p.Z < maxSize.Z; p.Z++)
            {
                for (p.Y = 0; p.Y < maxSize.Y; p.Y++)
                {
                    for (p.X = 0; p.X < maxSize.X; p.X++)
                    {
                        content = cache.Content(ref p);

                        if (content > 0)
                        {
                            min = Vector3I.Min(min, p);
                            max = Vector3I.Max(max, p + 1);
                        }
                    }
                }
            }

            min *= scale;
            max *= scale;
            center = new BoundingBoxD(asteroid.PositionLeftBottomCorner + min, asteroid.PositionLeftBottomCorner + max).Center;
            radius = CalculateAsteroidRadius(asteroid);
            strength = CalculateAsteroidStrength(asteroid);
        }
    }
}