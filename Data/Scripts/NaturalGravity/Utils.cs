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
using VRageMath;
using VRage;
using VRage.Voxels;
using Digi.Utils;

namespace Digi.NaturalGravity
{
    public class Utils
    {
        private static MyStorageDataCache cache = new MyStorageDataCache();
        
        public static GravityPoint GetGravityInAsteroid(IMyVoxelBase asteroid)
        {
            Vector3D min = asteroid.PositionLeftBottomCorner;
            Vector3D max = min + asteroid.Storage.Size;
            
            foreach(var gravity in NaturalGravity.gravityPoints.Values)
            {
                if(gravity.center.IsInsideInclusive(ref min, ref max))
                {
                    return gravity;
                }
            }
            
            return null;
        }
        
        public static IMyVoxelBase GetAimedAsteroid()
        {
            if(MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null || MyAPIGateway.Session.ControlledObject == null || MyAPIGateway.Session.ControlledObject.Entity == null)
                return null;
            
            List<IMyVoxelBase> maps = new List<IMyVoxelBase>();
            MyAPIGateway.Session.VoxelMaps.GetInstances(maps);
            var playerPos = MyAPIGateway.Session.Player.GetPosition();
            var matrix = MyAPIGateway.Session.ControlledObject.GetHeadMatrix(true, true, true);
            var line = new LineD(playerPos, matrix.Forward * 10000);
            double distance;
            
            foreach(var map in maps)
            {
                if(map.WorldAABB.Intersects(line, out distance))
                    return map;
            }
            
            return null;
        }
        
        /*
        public static IMyVoxelBase GetNearbyAsteroid()
        {
            List<IMyVoxelBase> maps = new List<IMyVoxelBase>();
            MyAPIGateway.Session.VoxelMaps.GetInstances(maps);
            Vector3D playerPos = MyAPIGateway.Session.Player.GetPosition();
            
            foreach(IMyVoxelBase map in maps)
            {
                if(map.WorldAABB.Contains(playerPos) == ContainmentType.Contains)
                    return map;
            }
            
            return null;
        }
         */
        
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
            
            if(diff > 1)
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
            
            for(p.Z = 0; p.Z < maxSize.Z; p.Z++)
            {
                for(p.Y = 0; p.Y < maxSize.Y; p.Y++)
                {
                    for(p.X = 0; p.X < maxSize.X; p.X++)
                    {
                        content = cache.Content(ref p);
                        
                        if(content > 0)
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