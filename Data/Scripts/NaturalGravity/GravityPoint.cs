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
using VRage.Components;
using Digi.Utils;

namespace Digi.NaturalGravity
{
    public class GravityPoint
    {
        public Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere generator;
        public Sandbox.ModAPI.Ingame.IMyReactor reactor;
        public Vector3D center { get; private set; }
        
        public List<IMyEntity> entsInRadius = new List<IMyEntity>();
        private bool notified = false;
        
        public static GravityPoint Spawn(long? entityId, Vector3D position, int radius, float strength, bool sync = true)
        {
            try
            {
                var prefab = MyDefinitionManager.Static.GetPrefabDefinition(NaturalGravity.PREFAB_NAME);
                
                if(prefab == null)
                {
                    Log.Error("Can't find prefab: " + NaturalGravity.PREFAB_NAME);
                    return null;
                }
                
                if(prefab.CubeGrids == null)
                {
                    MyDefinitionManager.Static.ReloadPrefabsFromFile(prefab.PrefabPath);
                    prefab = MyDefinitionManager.Static.GetPrefabDefinition(NaturalGravity.PREFAB_NAME);
                }
                
                if(prefab.CubeGrids.Length == 0)
                {
                    Log.Error("Prefab is broken, it does not have any grids!");
                    return null;
                }
                
                GravityPoint gravity;
                
                if(entityId.HasValue && NaturalGravity.gravityPoints.TryGetValue(entityId.Value, out gravity))
                {
                    gravity.Remove();
                    NaturalGravity.gravityPoints.Remove(entityId.Value);
                    Log.Info("WARNING: Gravity entity already existed with entity id "+entityId.Value+", removed and re-added.");
                }
                
                MyObjectBuilder_CubeGrid builder = prefab.CubeGrids[0].Clone() as MyObjectBuilder_CubeGrid;
                builder.Name = NaturalGravity.PREFAB_NAME;
                builder.PositionAndOrientation = new MyPositionAndOrientation(position, Vector3D.Forward, Vector3D.Up);
                
                MyObjectBuilder_GravityGeneratorSphere generator = builder.CubeBlocks[0] as MyObjectBuilder_GravityGeneratorSphere;
                generator.Radius = radius;
                generator.GravityAcceleration = strength * NaturalGravity.G;
                
                MyAPIGateway.Entities.RemapObjectBuilder(builder);
                
                if(entityId.HasValue)
                    builder.EntityId = entityId.Value;
                
                IMyEntity ent = MyAPIGateway.Entities.CreateFromObjectBuilderAndAdd(builder);
                
                if(ent == null)
                {
                    Log.Error("Prefab created a null entity!");
                    return null;
                }
                
                ent.Flags |= EntityFlags.Sync | EntityFlags.Save;
                
                gravity = GravityPoint.Create(ent);
                
                if(gravity == null)
                {
                    Log.Error("Couldn't create the GravityPoint.");
                    return null;
                }
                
                NaturalGravity.gravityPoints.Add(ent.EntityId, gravity);
                
                if(sync)
                {
                    Log.Info("Spawned "+NaturalGravity.PREFAB_NAME+" and sent over network; pos="+position+"; radius="+radius+"; strength="+strength);
                    MultiplayerSync.CreateGravity(ent.EntityId, position, radius, strength);
                }
                else
                {
                    Log.Info("Spawned "+NaturalGravity.PREFAB_NAME+"; pos="+position+"; radius="+radius+"; strength="+strength);
                }
                
                return gravity;
            }
            catch(Exception e)
            {
                Log.Error(e);
                return null;
            }
        }
        
        public static GravityPoint Create(IMyEntity ent)
        {
            var grid = ent as IMyCubeGrid;
            
            if(grid == null)
                return null;
            
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, b => b.FatBlock != null);
            var gravity = new GravityPoint();
            
            foreach(var slimBlock in blocks)
            {
                var block = slimBlock.FatBlock;
                
                if(block is Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere)
                    gravity.generator = block as Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere;
                else if(block is Sandbox.ModAPI.Ingame.IMyReactor)
                    gravity.reactor = block as Sandbox.ModAPI.Ingame.IMyReactor;
            }
            
            if(gravity.generator == null || gravity.reactor == null)
            {
                Log.Error("Can't find the generator and the reactor!");
                return null;
            }
            
            gravity.center = gravity.generator.GetPosition();
            return gravity;
        }
        
        private GravityPoint()
        {
        }
        
        public void SlowUpdate(HashSet<IMyEntity> ents)
        {
            entsInRadius.Clear();
            
            foreach(var ent in ents)
            {
                if(!ent.Physics.IsStatic && InRadius(ent))
                {
                    entsInRadius.Add(ent);
                }
            }
        }
        
        public void Update()
        {
            try
            {
                if(!NaturalGravity.init || generator == null || generator.Closed || generator.MarkedForClose || !generator.Enabled || generator.Gravity <= 0.0f)
                    return;
                
                float strength = NaturalGravity.G * (generator.Gravity / NaturalGravity.GG_MAX_GRAVITY);
                
                if(Settings.affect_ships)
                {
                    foreach(var ent in entsInRadius)
                    {
                        if(ent.Closed || ent.MarkedForClose)
                            continue;
                        
                        var mass = ent.Physics.Mass;
                        
                        if(Settings.mass_divide > 0)
                            mass = (mass > Settings.mass_limit ? Settings.mass_limit + ((mass - Settings.mass_limit) / Settings.mass_divide) : mass);
                        else if(Settings.mass_limit > 0)
                            mass = Math.Min(mass, Settings.mass_limit);
                        
                        var force = Vector3D.Normalize(center - ent.GetPosition()) * mass * strength;
                        
                        ent.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force, null, null);
                    }
                }
                
                if(NaturalGravity.playerPos != null)
                {
                    if(InRadius(NaturalGravity.playerPos.Value))
                    {
                        if(Settings.notify && !notified)
                        {
                            MyAPIGateway.Utilities.ShowNotification("Entering natural gravity...", 3000, MyFontEnum.DarkBlue);
                            notified = true;
                        }
                        
                        if(Settings.jetpack > 0)
                        {
                            var ent = MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity;
                            
                            if(ent is IMyCharacter)
                            {
                                var character = ent.GetObjectBuilder(false) as MyObjectBuilder_Character;
                                
                                if(character.JetpackEnabled && (Settings.jetpack_hover ? !character.DampenersEnabled : true))
                                {
                                    var force = Vector3D.Normalize(center - NaturalGravity.playerPos.Value) * strength * (Settings.jetpack / 100.0);
                                    
                                    ent.Physics.LinearVelocity += force * 0.0166666666666667;
                                }
                            }
                        }
                    }
                    else
                    {
                        if(Settings.notify && notified)
                        {
                            MyAPIGateway.Utilities.ShowNotification("Leaving natural gravity...", 3000, MyFontEnum.DarkBlue);
                            notified = false;
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public bool InRadius(IMyEntity ent)
        {
            return InRadius(ent.GetPosition());
        }
        
        public bool InRadius(Vector3D pos)
        {
            return Vector3D.DistanceSquared(center, pos) <= (generator.Radius * generator.Radius);
        }
        
        public void SetEnabled(bool on)
        {
            generator.RequestEnable(on);
        }
        
        public int SetRadius(int value)
        {
            int radius = Math.Abs(Math.Min(Math.Max(value, NaturalGravity.RADIUS_MIN), NaturalGravity.RADIUS_MAX));
            generator.SetValueFloat("Radius", radius);
            return radius;
        }
        
        public float SetStrength(float value)
        {
            float strength = Math.Abs(Math.Min(Math.Max(value, NaturalGravity.STRENGTH_MIN), NaturalGravity.STRENGTH_MAX));
            generator.SetValueFloat("Gravity", NaturalGravity.GG_MAX_GRAVITY * strength);
            return strength;
        }
        
        public void Remove(bool sync = true)
        {
            var ent = (generator.CubeGrid as IMyEntity);
            
            if(sync)
            {
                Log.Info("Removed gravity point and synchronized at "+ent.GetPosition());
                MultiplayerSync.RemoveGravity(ent);
            }
            else
            {
                Log.Info("Removed gravity point at "+ent.GetPosition());
            }
            
            ent.Close();
        }
    }
}
