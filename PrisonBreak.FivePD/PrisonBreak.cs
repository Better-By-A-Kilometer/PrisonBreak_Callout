using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using FivePD.API;
using Kilo.Commons.Config;
using Kilo.Commons.Utils;
using Newtonsoft.Json.Linq;

namespace PrisonBreak.FivePD
{
    public class PrisonBreak : Callout
    {
        private Vector4 GetLocation()
        {
            if (!config.TryGetValue("Locations", out var locations))
                throw new NullReferenceException("Config may not be loaded! Failed to get locations.");
            List<Vector4> allLocations = [];

            allLocations.AddRange(((JArray)locations).Select(Utils.JObjectToVector4));
            return allLocations[new Random().Next(0, allLocations.Count - 1)];
        }

        public PrisonBreak()
        {
            config = new Config(AddonType.callouts, defaultConfig.ToString(), "PrisonBreakByKilo");

            var location = GetLocation();

            ShortName = "Prison Break";
            CalloutDescription = "Hurry! A prison break has occurred at the prison.";
            StartDistance = location.W;
            ResponseCode = 99;

            InitInfo((Vector3)location); // TODO: Replace this
        }

        private Config config;

        static List<Entity> SpawnedEntities = []; // Static because we don't want to lose them when the callout ends. Redundancy plan i guess

        private JObject defaultConfig = new()
        {
            ["Locations"] = new JArray()
            {
                new JObject()
                {
                    ["X"] = 0,
                    ["Y"] = 0,
                    ["Z"] = 0,
                    ["W"] = 100,
                    ["Note"] = "W = Callout's StartDistance",
                }
            }
        };

        public override Task OnAccept()
        {
            return base.OnAccept();
        }

        public override void OnStart(Ped closestPlayer)
        {
            // TODO: Spawn peds, either begin prison break or start a scenario for players to witness. Possible chaos outside the prison, like guards fighting with escaped convicts.


            base.OnStart(closestPlayer);
        }
    }
}