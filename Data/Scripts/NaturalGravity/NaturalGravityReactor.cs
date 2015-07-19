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

namespace Digi.NaturalGravity
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_Reactor), "NaturalGravityReactor")]
    class NaturalGravityReactor : MyGameLogicComponent
    {
        private MyObjectBuilder_EntityBase objectBuilder;
        private Sandbox.ModAPI.Ingame.IMyReactor reactor;
        
        private static MyObjectBuilder_Ingot fuel = new MyObjectBuilder_Ingot() { SubtypeName = "Uranium" };
        
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            this.objectBuilder = objectBuilder;
            reactor = Entity as Sandbox.ModAPI.Ingame.IMyReactor;
            reactor.RequestEnable(true);
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            
            /*
            if(reactor != null && reactor.BlockDefinition.SubtypeId == "NaturalGravityReactor")
            {
                reactor.RequestEnable(true);
                Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;
            }
            else
            {
                reactor = null;
            }
             */
        }
        
        public override void UpdateBeforeSimulation100()
        {
            if(reactor == null || !reactor.IsVisible())
                return;
            
            var inv = (reactor as Sandbox.ModAPI.Interfaces.IMyInventoryOwner).GetInventory(0) as Sandbox.ModAPI.IMyInventory;
            
            if(!inv.ContainItems(2, fuel))
            {
                inv.AddItems(2, fuel);
            }
        }
        
        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return copy ? (MyObjectBuilder_EntityBase)objectBuilder.Clone() : objectBuilder;
        }
    }
}