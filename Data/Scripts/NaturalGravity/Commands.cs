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
    public class Commands
    {
        private static Commands instance;
        
        public static void Init()
        {
            if(instance == null)
                instance = new Commands();
        }
        
        public static void Close()
        {
            if(instance != null)
            {
                instance.Unload();
                instance = null;
            }
        }
        
        public Commands()
        {
            MyAPIGateway.Utilities.MessageEntered += MessageEntered;
        }
        
        public void Unload()
        {
            MyAPIGateway.Utilities.MessageEntered -= MessageEntered;
        }
        
        public void MessageEntered(string message, ref bool visible)
        {
            if(!message.StartsWith("/ng", StringComparison.InvariantCultureIgnoreCase))
                return;
            
            visible = false;
            
            if(message.Length > "/ng".Length)
            {
                message = message.Substring("/ng ".Length).Trim().ToLower();
                
                if(message.Equals("settings"))
                {
                    MyAPIGateway.Utilities.ShowMissionScreen("Natural Gravity Settings", "", "", Settings.GetSettingsString(false), null, "CLOSE");
                    return;
                }
                
                if(message.StartsWith("info"))
                {
                    if(!IsAdmin(true))
                        return;
                    
                    MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Gravity points: " + NaturalGravity.gravityPoints.Count + ".");
                    
                    if(NaturalGravity.isDedicated)
                    {
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Can't see extra info as the dedicated server!");
                        return;
                    }
                    
                    var asteroid = Utils.GetAimedAsteroid();
                    
                    if(asteroid == null)
                    {
                        MyAPIGateway.Utilities.ShowMessage("Asteroid info", "You're not near or aiming at an asteroid.");
                    }
                    else
                    {
                        MyAPIGateway.Utilities.ShowMessage("Asteroid info", "Name: " + asteroid.StorageName + "; Boundary size: " + asteroid.Storage.Size.AbsMax());
                        
                        var gravity = Utils.GetGravityInAsteroid(asteroid);
                        
                        if(gravity == null)
                            MyAPIGateway.Utilities.ShowMessage("Gravity info", "The asteroid doesn't have natural gravity.");
                        else
                            MyAPIGateway.Utilities.ShowMessage("Gravity info", "Enabled: " + gravity.generator.Enabled + "; Radius: " + gravity.generator.Radius + "; Strength: " + (gravity.generator.Gravity / NaturalGravity.GG_MAX_GRAVITY) + "; Center: " + gravity.center + ".");
                    }
                    
                    return;
                }
                
                if(message.StartsWith("set"))
                {
                    if(!IsAdmin(true))
                        return;
                    
                    message = message.Substring("set".Length).Trim();
                    
                    if(message == "")
                    {
                        MyAPIGateway.Utilities.ShowMissionScreen("Natural Gravity Settings", "", "", Settings.GetSettingsString(false), null, "CLOSE");
                        return;
                    }
                    
                    if(message.IndexOf(' ') == -1)
                    {
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Invalid format, use: /ng set <setting> <value>");
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Type '/ng set' or '/ng settings' to see available settings.");
                        return;
                    }
                    
                    if(Settings.ParseSetting(message, true, ' '))
                    {
                        Settings.SyncSettings();
                    }
                    
                    return;
                }
                
                if(message.StartsWith("config"))
                {
                    if(!IsAdmin(true))
                        return;
                    
                    message = message.Substring("config".Length).Trim();
                    
                    if(message.Equals("save"))
                    {
                        Settings.SaveConfig();
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Config saved.");
                        return;
                    }
                    else if(message.Equals("reload"))
                    {
                        Settings.LoadConfig();
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Config reloaded.");
                        return;
                    }
                    else if(message.Equals("reset"))
                    {
                        Settings.ResetConfig();
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Config reset to defaults (but not saved).");
                        return;
                    }
                }
                
                if(message.StartsWith("create"))
                {
                    if(NaturalGravity.isDedicated)
                    {
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Can't create asteroids as the dedicated server!");
                        return;
                    }
                    
                    if(!IsAdmin(true))
                        return;
                    
                    var asteroid = Utils.GetAimedAsteroid();
                    
                    if(asteroid == null)
                    {
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "You're not near or aiming at an asteroid.");
                        return;
                    }
                    
                    var gravity = Utils.GetGravityInAsteroid(asteroid);
                    
                    if(gravity != null)
                    {
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "The asteroid you're near ('" + asteroid.StorageName + "') already has natural gravity.");
                        return;
                    }
                    
                    int radius;
                    float strength;
                    Vector3D center;
                    Utils.GetAsteroidData(asteroid, out center, out radius, out strength);
                    GravityPoint.Spawn(null, center, radius, strength);
                    
                    MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Added natural gravity to the '" + asteroid.StorageName + "' asteroid, radius=" + radius + ", strength="+strength+", at:" + center + ".");
                    return;
                }
                
                bool msgRadius = message.StartsWith("radius");
                bool msgStrength = message.StartsWith("strength");
                bool on = message.Equals("on");
                bool msgOnOff = on || message.Equals("off");
                bool msgRemove = message.StartsWith("remove");
                
                if(msgRadius || msgStrength || msgOnOff || msgRemove)
                {
                    if(NaturalGravity.isDedicated)
                    {
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Can't edit asteroids as the dedicated server!");
                        return;
                    }
                    
                    if(!IsAdmin(true))
                        return;
                    
                    var asteroid = Utils.GetAimedAsteroid();
                    
                    if(asteroid == null)
                    {
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "You're not near or aiming at an asteroid.");
                        return;
                    }
                    
                    var gravity = Utils.GetGravityInAsteroid(asteroid);
                    
                    if(gravity == null)
                    {
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "The asteroid you're near ('" + asteroid.StorageName + "') does not have natural gravity.");
                        return;
                    }
                    
                    if(msgRadius)
                    {
                        message = message.Substring("radius".Length).Trim();
                        
                        if(message.Equals("reset"))
                        {
                            gravity.SetRadius(Utils.CalculateAsteroidRadius(asteroid));
                            
                            MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Reset the range to the calculated value: " + gravity.generator.Radius + ".");
                            return;
                        }
                        
                        int radius;
                        
                        if(!int.TryParse(message, out radius))
                        {
                            MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Invalid parameter, not an integer: '" + message + "'.");
                            return;
                        }
                        
                        radius = gravity.SetRadius(radius);
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Radius set to: '" + radius + "'.");
                        return;
                    }
                    
                    if(msgStrength)
                    {
                        message = message.Substring("strength".Length).Trim();
                        
                        if(message.Equals("reset"))
                        {
                            gravity.SetStrength(Utils.CalculateAsteroidStrength(asteroid));
                            MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Reset the strength to the calculated value: " + (gravity.generator.Gravity / NaturalGravity.GG_MAX_GRAVITY) + ".");
                            return;
                        }
                        
                        float strength;
                        
                        if(!float.TryParse(message, out strength))
                        {
                            MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Invalid parameter, not a float: '" + message + "'.");
                            return;
                        }
                        
                        strength = gravity.SetStrength(strength);
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Strength set to: '" + strength + "'.");
                        return;
                    }
                    
                    if(msgOnOff)
                    {
                        gravity.SetEnabled(on);
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Asteroid's gravity turned " + (on ? "ON" : "OFF") + ".");
                        return;
                    }
                    
                    if(msgRemove)
                    {
                        gravity.Remove();
                        MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Removed natural gravity from '" + asteroid.StorageName + "'.");
                        return;
                    }
                }
                
                MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Unknown parameter: " + message);
            }
            
            StringBuilder builder = new StringBuilder();
            bool admin = IsAdmin(false);
            
            if(admin)
            {
                builder.AppendLine("/ng create");
                builder.AppendLine("/ng <on/off>");
                builder.AppendLine("/ng radius <"+NaturalGravity.RADIUS_MIN+"-"+NaturalGravity.RADIUS_MAX+"/reset>");
                builder.AppendFormat("/ng strength <{0:0.00}-{1:0.00}/reset>\n", NaturalGravity.STRENGTH_MIN, NaturalGravity.STRENGTH_MAX);
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
        
        private bool IsAdmin(bool verbose)
        {
            bool admin = NaturalGravity.isServer; // || MyAPIGateway.Utilities.ConfigDedicated.Administrators.Contains(MyAPIGateway.Session.Player.SteamUserId.ToString());
            
            if(!admin)
            {
                var clients = MyAPIGateway.Session.GetCheckpoint("null").Clients;
                
                if(clients != null)
                {
                    var client = clients.FirstOrDefault(c => c.SteamId == MyAPIGateway.Session.Player.SteamUserId && c.IsAdmin);
                    admin = (client != null);
                }
            }
            
            if(verbose && !admin)
                MyAPIGateway.Utilities.ShowMessage(NaturalGravity.MOD_SHORTNAME, "Only admins can use this command.");
            
            return admin;
        }
    }
}