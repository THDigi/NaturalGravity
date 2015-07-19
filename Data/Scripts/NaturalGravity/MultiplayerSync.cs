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
using VRage.ModAPI;
using Digi.Utils;

namespace Digi.NaturalGravity
{
    public class MultiplayerSync
    {
        private static MultiplayerSync instance;
        
        public const ushort PACKET_SYNC = 12316;
        
        public static void Init()
        {
            if(instance == null)
                instance = new MultiplayerSync();
        }
        
        public static void Close()
        {
            if(instance != null)
            {
                instance.Unload();
                instance = null;
            }
        }
        
        public MultiplayerSync()
        {
            MyAPIGateway.Multiplayer.RegisterMessageHandler(PACKET_SYNC, ReceivedSyncPacket);
        }
        
        public void Unload()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(PACKET_SYNC, ReceivedSyncPacket);
        }
        
        public static void CreateGravity(long entityId, Vector3D position, int radius, float strength)
        {
            StringBuilder data = new StringBuilder();
            
            data.Append(0); // 0 = created
            data.Append(';');
            data.Append(entityId);
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
            
            MyAPIGateway.Multiplayer.SendMessageToOthers(MultiplayerSync.PACKET_SYNC, Settings.encode.GetBytes(data.ToString()), true);
            
            //MyAPIGateway.Multiplayer.SendEntitiesCreated(new List<MyObjectBuilder_EntityBase>() { ent as MyObjectBuilder_EntityBase });
        }
        
        public static void RemoveGravity(IMyEntity ent)
        {
            var pos = ent.GetPosition();
            
            StringBuilder data = new StringBuilder();
            
            data.Append(1); // 1 = removed
            data.Append(';');
            data.Append(ent.EntityId);
            
            MyAPIGateway.Multiplayer.SendMessageToOthers(PACKET_SYNC, Settings.encode.GetBytes(data.ToString()), true);
        }
        
        public void ReceivedSyncPacket(byte[] bytes)
        {
            string data = Settings.encode.GetString(bytes);
            string[] args = data.Split(';');
            
            Log.Info("Network Debug: ReceivedSyncPacket, data='" + data + "'");
            
            if(args.Length == 0)
            {
                Log.Error("No data!");
                return;
            }
            
            int type;
            
            if(!int.TryParse(args[0], out type))
            {
                Log.Error("Invalid type: " + args[0]);
                return;
            }
            
            switch(type)
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
            if(args.Length != 6)
            {
                Log.Error("Invalid number of arguments: "+args.Length+"; need exacly 6!");
                return;
            }
            
            long entityId;
            Vector3D position;
            int radius;
            float strength;
            int i = 1;
            
            if(!ParseLong(out entityId, args[i++]))
                return;
            
            if(!ParseVector(out position, args[i++], args[i++], args[i++]))
                return;
            
            if(!ParseInt(out radius, args[i++]))
                return;
            
            if(!ParseFloat(out strength, args[i++]))
                return;
            
            GravityPoint gravity;
            
            if(NaturalGravity.gravityPoints.TryGetValue(entityId, out gravity))
            {
                Log.Info("WARNING: Received sync to create gravity for id="+entityId+" but it already exists, removing and re-adding.");
                gravity.Remove(false);
                NaturalGravity.gravityPoints.Remove(entityId);
            }
            
            GravityPoint.Spawn(entityId, position, radius, strength, false);
        }
        
        private void SyncRemoved(string[] args)
        {
            if(args.Length != 4)
            {
                Log.Error("Invalid number of arguments: "+args.Length+"; need exacly 4!");
                return;
            }
            
            long entityId;
            
            if(!ParseLong(out entityId, args[1]))
                return;
            
            GravityPoint gravity;
            
            if(!NaturalGravity.gravityPoints.TryGetValue(entityId, out gravity))
            {
                Log.Error("Received remove request for unexistent gravity id!");
                return;
            }
            
            Log.Info("SyncRemoved; removed gravity with id="+entityId);
            gravity.Remove(false);
        }
        
        private bool ParseVector(out Vector3D position, string x, string y, string z)
        {
            position = new Vector3D(0);
            string[] args = {x, y, z};
            double d;
            
            for(int i = 0; i < 3; i++)
            {
                if(double.TryParse(args[i], out d))
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
            if(!int.TryParse(str, out num))
            {
                Log.Error("Invalid integer value: " + str);
                return false;
            }
            
            return true;
        }
        
        private bool ParseLong(out long num, string str)
        {
            if(!long.TryParse(str, out num))
            {
                Log.Error("Invalid long value: " + str);
                return false;
            }
            
            return true;
        }
        
        private bool ParseFloat(out float num, string str)
        {
            if(!float.TryParse(str, out num))
            {
                Log.Error("Invalid float value: " + str);
                return false;
            }
            
            return true;
        }
    }
}