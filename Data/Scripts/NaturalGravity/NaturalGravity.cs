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
using VRage.Voxels;
using VRage.ModAPI;
using Digi.Utils;

namespace Digi.NaturalGravity
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public class NaturalGravity : MySessionComponentBase
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
        
        private const int SKIP_TICKS = 5;
        private const int SKIP_EXTRA_TICKS = 10;
        
        public static Nullable<Vector3D> playerPos = null;
        private static int tick = SKIP_TICKS;
        private static int extraTick = 0;
        public static Dictionary<long, GravityPoint> gravityPoints = new Dictionary<long, GravityPoint>();
        private static HashSet<IMyEntity> ents = new HashSet<IMyEntity>();
        
        public void Init()
        {
            Log.Info("Initialized");
            
            init = true;
            isServer = MyAPIGateway.Multiplayer.IsServer || MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE;
            isDedicated = (MyAPIGateway.Utilities.IsDedicated && isServer);
            
            Settings.Init();
            Commands.Init();
            MultiplayerSync.Init();
        }
        
        protected override void UnloadData()
        {
            init = false;
            ents.Clear();
            gravityPoints.Clear();
            
            Settings.Close();
            Commands.Close();
            MultiplayerSync.Close();
            Log.Close();
        }
        
        public static bool ValidateEntity(IMyEntity ent)
        {
            return (ent.Physics != null && ent.Physics.Enabled && !ent.Physics.IsStatic && !ent.Physics.IsPhantom && !ent.IsVolumetric);
        }
        
        public override void UpdateBeforeSimulation()
        {
            if(!init)
            {
                if(MyAPIGateway.Session == null)
                    return;
                
                Init();
            }
            
            if(++tick >= SKIP_TICKS)
            {
                tick = 0;
                ents.Clear();
                MyAPIGateway.Entities.GetEntities(ents, e => e is IMyCubeGrid && e.Physics != null && !e.Physics.IsPhantom);
                
                foreach(var ent in ents)
                {
                    if(ent.Physics.IsStatic && ent.Name == PREFAB_NAME && !gravityPoints.ContainsKey(ent.EntityId))
                    {
                        var gravity = GravityPoint.Create(ent);
                        gravityPoints.Add(ent.EntityId, gravity);
                    }
                }
                
                foreach(var gravity in gravityPoints.Values)
                {
                    gravity.SlowUpdate(ents);
                }
                
                if(++extraTick >= SKIP_EXTRA_TICKS)
                {
                    extraTick = 0;
                    CheckAsteroids();
                }
            }
            
            if(gravityPoints.Count > 0)
            {
                if(!NaturalGravity.isDedicated)
                {
                    if((Settings.notify || Settings.jetpack > 0) && MyAPIGateway.Session.Player != null && MyAPIGateway.Session.Player.Controller != null && MyAPIGateway.Session.Player.Controller.ControlledEntity != null && MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity != null)
                    {
                        playerPos = MyAPIGateway.Session.Player.GetPosition();
                    }
                    else
                    {
                        playerPos = null;
                    }
                }
                
                foreach(var gravity in gravityPoints.Values)
                {
                    gravity.Update();
                }
            }
        }
        
        private List<IMyVoxelBase> tmp = new List<IMyVoxelBase>();
        private Queue<IMyVoxelBase> checkAsteroids = new Queue<IMyVoxelBase>();
        
        private void CheckAsteroids()
        {
            if(Settings.asteroid_prefix == null)
                return;
            
            if(checkAsteroids.Count > 0)
            {
                IMyVoxelBase asteroid;
                bool match;
                int checks = 10;
                
                while(checkAsteroids.Count > 0)
                {
                    if(--checks < 0)
                        return;
                    
                    asteroid = checkAsteroids.Dequeue();
                    match = Settings.asteroid_prefix_all;
                    
                    if(!match)
                    {
                        foreach(string prefix in Settings.asteroid_prefix)
                        {
                            if(asteroid.StorageName.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase))
                            {
                                match = true;
                                break;
                            }
                        }
                    }
                    
                    if(match && asteroid.Storage.Size.AbsMax() >= Settings.asteroid_ignoresmaller && Utils.GetGravityInAsteroid(asteroid) == null)
                    {
                        Log.Info("Added gravity to prefixed asteroid: " + asteroid.StorageName);
                        
                        int radius;
                        float strength;
                        Vector3D center;
                        Utils.GetAsteroidData(asteroid, out center, out radius, out strength);
                        GravityPoint.Spawn(null, center, radius, strength);
                    }
                }
            }
            else
            {
                MyAPIGateway.Session.VoxelMaps.GetInstances(tmp, delegate(IMyVoxelBase a)
                                                            {
                                                                if(a.Physics == null
                                                                   || !a.Physics.Enabled
                                                                   || a.Physics.IsPhantom)
                                                                    return false;
                                                                
                                                                checkAsteroids.Enqueue(a);
                                                                return false;
                                                            });
            }
        }
    }
}