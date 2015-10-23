using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Common;
using Sandbox.Common.Components;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Common.ObjectBuilders.Definitions;
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

using Ingame = Sandbox.ModAPI.Ingame;

namespace Digi.NaturalGravity
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), "NaturalGravityReactor")]
    class NaturalGravityReactor : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase objectBuilder;

        private static MyObjectBuilder_Ingot fuel = new MyObjectBuilder_Ingot() { SubtypeName = "Uranium" };

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            this.objectBuilder = objectBuilder;
            Entity.NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME | MyEntityUpdateEnum.EACH_100TH_FRAME;
        }
        
        public override void UpdateOnceBeforeFrame()
        {
            try
            {
                var reactor = Entity as Ingame.IMyReactor;
                reactor.RequestEnable(true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        
        public override void UpdateAfterSimulation100()
        {
            try
            {
                var reactor = Entity as Ingame.IMyReactor;
                var inv = (reactor as IMyInventoryOwner).GetInventory(0) as Sandbox.ModAPI.IMyInventory;
                
                if (!inv.ContainItems(2, fuel))
                {
                    inv.AddItems(2, fuel);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return copy ? (MyObjectBuilder_EntityBase)objectBuilder.Clone() : objectBuilder;
        }
    }
}