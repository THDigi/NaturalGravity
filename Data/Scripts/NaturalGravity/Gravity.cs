using System;
using System.Collections.Generic;
using System.Linq;
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
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Components;
using VRage.Utils;
using Digi.Utils;

namespace Digi.NaturalGravity
{
    public class Gravity
    {
        public Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere generator;
        //public Sandbox.ModAPI.Ingame.IMyReactor reactor;

        public List<IMyEntity> entities = new List<IMyEntity>();
        public Vector3D center { get; private set; }
        //public float strength { get; private set; }
        //public float radius { get; private set; }
        //private float radiusSquared;

        private bool notified = false;

        /*
        public Gravity(IMyCubeGrid grid)
        {
            if(grid == null)
                throw new Exception("grid can not be null!");
            
            List<IMySlimBlock> blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, b => b.FatBlock != null && b.FatBlock is Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere);
            
            if(blocks.Count == 0 || blocks[0] == null)
                throw new Exception("grid does not contain a spherical gravity generator!");
            
            CreateFromGG(blocks[0].FatBlock as Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere);
        }
         */

        public Gravity(Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere generator)
        {
            CreateFromGG(generator);
        }

        private void CreateFromGG(Sandbox.ModAPI.Ingame.IMyGravityGeneratorSphere generator)
        {
            if (generator == null)
                throw new Exception("generator can not be null!");

            this.generator = generator;
            center = (generator.CubeGrid as IMyCubeGrid).GridIntegerToWorld(generator.Position);
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

        public bool AddInRange(IMyEntity ent)
        {
            if (InRadius(ent))
            {
                entities.Add(ent);
                return true;
            }

            return false;
        }

        public void Update()
        {
            if (!NaturalGravity.init || !generator.Enabled || generator.Gravity <= 0)
                return;

            float strength = NaturalGravity.G * (generator.Gravity / NaturalGravity.GG_MAX_GRAVITY); // TODO remove division once spherical values are fixed

            if (Settings.affect_ships)
            {
                foreach (IMyEntity ent in entities)
                {
                    if (ent.Closed || ent.MarkedForClose)
                        continue;

                    var mass = ent.Physics.Mass;

                    if (Settings.mass_divide > 0)
                        mass = (mass > Settings.mass_limit ? Settings.mass_limit + ((mass - Settings.mass_limit) / Settings.mass_divide) : mass);
                    else if (Settings.mass_limit > 0)
                        mass = Math.Min(mass, Settings.mass_limit);

                    var force = Vector3D.Normalize(center - ent.GetPosition()) * mass * strength;

                    ent.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_FORCE, force, null, null);
                }
            }

            if (NaturalGravity.playerPos != null)
            {
                if (InRadius(NaturalGravity.playerPos.Value))
                {
                    if (Settings.notify && !notified)
                    {
                        MyAPIGateway.Utilities.ShowNotification("Entering natural gravity...", 3000, MyFontEnum.DarkBlue);
                        notified = true;
                    }

                    var ent = MyAPIGateway.Session.Player.Controller.ControlledEntity.Entity;

                    if (Settings.jetpack > 0 && ent is IMyCharacter)
                    {
                        var character = ent.GetObjectBuilder(false) as MyObjectBuilder_Character;

                        if (character.JetpackEnabled && (!Settings.jetpack_hover || !character.DampenersEnabled))
                        {
                            var force = Vector3D.Normalize(center - NaturalGravity.playerPos.Value) * strength * (Settings.jetpack / 100.0);

                            ent.Physics.LinearVelocity += force * 0.0166666666666667;
                        }
                    }
                }
                else
                {
                    if (Settings.notify && notified)
                    {
                        MyAPIGateway.Utilities.ShowNotification("Leaving natural gravity...", 3000, MyFontEnum.DarkBlue);
                        notified = false;
                    }
                }
            }
        }

        public void Remove(bool sync = true)
        {
            var ent = (generator.CubeGrid as IMyEntity);

            if (sync) // TODO remove temporary fix
            {
                var pos = ent.GetPosition();

                StringBuilder data = new StringBuilder();

                data.Append(1); // 1 = removed
                data.Append(';');
                data.Append(pos.GetDim(0));
                data.Append(';');
                data.Append(pos.GetDim(1));
                data.Append(';');
                data.Append(pos.GetDim(2));

                MyAPIGateway.Multiplayer.SendMessageToOthers(NaturalGravity.PACKET_SYNC, Settings.encode.GetBytes(data.ToString()), true);
            }

            ent.Close();
        }
    }
}