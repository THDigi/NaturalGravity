using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;
using VRage;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.Utils;
using Digi.Utils;

namespace Digi.NaturalGravity
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_GravityGeneratorSphere), "NaturalGravityGenerator")]
    class NaturalGravityGenerator : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase objectBuilder;
        private IMyGravityGeneratorSphere generator;
        private bool added;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            this.objectBuilder = objectBuilder;
            generator = Entity as IMyGravityGeneratorSphere;
            generator.NeedsUpdate |= MyEntityUpdateEnum.EACH_10TH_FRAME;
            added = false;
        }

        public override void UpdateAfterSimulation10()
        {
            if (generator != null && !added && NaturalGravity.init)
            {
                if (!generator.IsVisible())
                {
                    Log.Info("Natural gravity generator found but it's not visible; pos=" + Entity.GetPosition() + "; id=" + Entity.EntityId);
                    return;
                }

                added = true;
                NaturalGravity.AddGravityPoint(new Gravity(generator));
                Log.Info("Natural gravity added; pos=" + Entity.GetPosition() + "; id=" + Entity.EntityId);
            }
        }

        public override void Close()
        {
            if (generator != null && NaturalGravity.init)
            {
                added = false;
                NaturalGravity.RemoveGravityPoint(generator);
                Log.Info("Natural gravity generator removed");
            }
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return copy ? (MyObjectBuilder_EntityBase)objectBuilder.Clone() : objectBuilder;
        }
    }
}