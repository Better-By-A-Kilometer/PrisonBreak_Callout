using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using FivePD.API;
using FivePD.API.Utils;
using Kilo.Commons.Config;
using Kilo.Commons.Utils;
using Newtonsoft.Json.Linq;

namespace PrisonBreak.FivePD
{
    [CalloutProperties("Prison Break", "^3DevKilo^0", "1.0.0")]
    public class PrisonBreak : Callout
    {
        private Vector4 GetLocation()
        {
            if (!config.TryGetValue("Locations", out var locations))
                throw new NullReferenceException("Config may not be loaded! Failed to get locations.");
            List<Vector4> allLocations = [];

            foreach (var vector4 in ((JArray)locations))
            {
                allLocations.Add(Utils.JObjectToVector4(vector4));
            }
            
            return allLocations[Math.Max(0, new Random().Next(0, allLocations.Count - 1))];
        }

        private RelationshipGroup prisonGuard = World.AddRelationshipGroup("PRISON_GUARD");

        private RelationshipGroup convict = World.AddRelationshipGroup("PRISON_CONVICT");

        private RelationshipGroup players = World.AddRelationshipGroup("PRISON_PLAYER");

        private RelationshipGroup arrested = World.AddRelationshipGroup("PRISON_ARRESTED_CONVICT");

        public PrisonBreak()
        {
            config = new Config(AddonType.callouts, defaultConfig.ToString(), "PrisonBreakByKilo");

            var location = GetLocation();
            InitInfo(/*new Vector3(1869.4685058594f, 2606.9453125f, 45.265308380127f)*/((Vector3)location));

            ShortName = "~r~Prison Break";
            CalloutDescription = "Hurry! A prison break has occurred at the prison.";
            StartDistance = location.W;
            ResponseCode = 3;

        }

        private Config config;

        static List<Entity>
            SpawnedEntities =
                []; // Static because we don't want to lose them when the callout ends. Redundancy plan i guess

        private int MaxNumberOfSuspects = 12;
        private int MaxNumberOfGuards = 4;
        
        private JObject defaultConfig = new()
        {
            ["Locations"] = new JArray()
            {
                new JObject()
                {
                    ["X"] = 1931.2392578125,
                    ["Y"] = 2607.0620117188,
                    ["Z"] = 46.377307891846,
                    ["W"] = 200,
                    ["Note"] = "W = Callout's StartDistance",
                }
            },
            ["MaxNumberOfSuspects"] = 18,
            ["MaxNumberOfGuards"] = 10
        };

        public override Task OnAccept()
        {
            InitBlip(color: BlipColor.Red);
            try
            {
                convict.SetRelationshipBetweenGroups(prisonGuard, Relationship.Hate, true);
                prisonGuard.SetRelationshipBetweenGroups(convict, Relationship.Hate, true);

                Game.PlayerPed.RelationshipGroup = players;
                Game.PlayerPed.RelationshipGroup.SetRelationshipBetweenGroups(convict, Relationship.Hate, true);

                if (config.TryGetValue("MaxNumberOfSuspects", out var maxNumberOfSuspects))
                    MaxNumberOfSuspects = (int)maxNumberOfSuspects;
                if (config.TryGetValue("MaxNumberOfGuards", out var maxNumberOfGuards))
                    MaxNumberOfGuards = (int)maxNumberOfGuards;
            }
            catch (Exception)
            {
                // ignored
            }

            return base.OnAccept();
        }

        private async Task ConvictAI(Ped ped)
        {
            bool surrendering = false;
            while (ped is not null && SpawnedEntities.Contains(ped))
            {
                await BaseScript.Delay(100);
                if (ped.IsDead)
                {
                    if (ped.AttachedBlip is Blip blip)
                        blip.Delete();
                    return;
                }

                if (ped.IsCuffed)
                {
                    if (ped.RelationshipGroup.Equals(convict))
                        ped.RelationshipGroup = arrested;
                    if (ped.AttachedBlips.FirstOrDefault(b =>
                            b.Sprite == BlipSprite.Standard && b.Color != BlipColor.Yellow) is Blip blip)
                    {
                        blip.Color = BlipColor.Yellow;
                        blip.IsShortRange = false;
                        
                    }
                    continue; // Because activity can pick up again if uncuffed.
                }

                if (ped.RelationshipGroup.Equals(arrested))
                    ped.RelationshipGroup = convict;
                
                if (ped.AttachedBlip is Blip yellowblip && yellowblip.Color != BlipColor.Red)
                {
                    yellowblip.Color = BlipColor.Red;
                    yellowblip.IsShortRange = false;
                }
                    

                var combatPlr = AssignedPlayers
                    .FirstOrDefault(p => p.Position.DistanceTo(ped.Position) < 20f &&
                        (p.Weapons.Current is Weapon weapon &&
                            weapon.AmmoInClip < 2 || p.Weapons.Current is null) || p.IsInCombatAgainst(ped));

                if (combatPlr is not null && !ped.IsInCombat)
                {
                    // Decides to attack.
                    ped.Task.ClearAll();
                    ped.Task.FightAgainst(combatPlr);
                    ped.Task.FightAgainstHatedTargets(10f, -1);
                }

                var fleeFrom = AssignedPlayers.OrderByDescending(p => p.Position.DistanceToSquared(ped.Position))
                    .FirstOrDefault();

                if (fleeFrom is not null && !ped.IsInCombat && !ped.IsInMeleeCombat && !ped.IsBeingStunned)
                {
                    if (surrendering)
                    {
                        ped.Task.ClearAll();
                        surrendering = false;
                    }
                    API.TaskSmartFleePed(ped.Handle, fleeFrom.Handle, 999f, -1, false, false);
                    // Now check if it makes sense for the ped to surrender.
                }

                if (ped.IsBeingStunned || ped.IsRagdoll)
                {
                    await BaseScript.Delay(500);
                    if (new Random().Next(101) > 50)
                    {
                        ped.Task.ClearAll();
                        ped.Task.HandsUp(12000);
                        surrendering = true;
                        await BaseScript.Delay(12000);
                    }
                }
            }
        }

        public override async void OnStart(Ped closestPlayer)
        {
            // TODO: Spawn peds, either begin prison break or start a scenario for players to witness. Possible chaos outside the prison, like guards fighting with escaped convicts.
            List<Ped> convicts = [];
            for (int i = 0; i < new Random().Next(2, MaxNumberOfSuspects); i++)
            {
                List<PedHash> list =
                [
                    PedHash.Prisoner01,
                    PedHash.Prisoner01SMY,
                    PedHash.PrisMuscl01SMY
                ];

                var ped = await World.CreatePed(new Model(list[new Random().Next(list.Count - 1)]),
                    Location.Around(35f).ClosestPedPlacement());
                ped.IsPersistent = true;
                ped.AlwaysKeepTask = true;
                ped.RelationshipGroup = convict;
                SpawnedEntities.Add(ped);
                convicts.Add(ped);
                await BaseScript.Delay(100);
            }

            decimal min = MaxNumberOfSuspects / 3;
            var guardsNum = new Random().Next((int)Math.Round(min), MaxNumberOfGuards);
            int guardsNearSuspects = new Random().Next(guardsNum);

            List<Ped> guards = [];
            
            for (int i = 0; i < guardsNum; i++)
            {
                var closestPedPlacement = Location.Around(35f).ClosestPedPlacement();
                if (guardsNearSuspects > 0 && i <= (guardsNearSuspects - 1))
                {
                    closestPedPlacement = convicts[new Random().Next(convicts.Count - 1)].Position.Around(2f);
                }

                var ped = await World.CreatePed(new Model(PedHash.Prisguard01SMM), closestPedPlacement);
                ped.IsPersistent = true;
                ped.AlwaysKeepTask = true;
                ped.RelationshipGroup = prisonGuard;
                SpawnedEntities.Add(ped);
                guards.Add(ped);
                API.SetPedAsCop(ped.Handle, true);
                ped.RelationshipGroup.SetRelationshipBetweenGroups(convict, Relationship.Hate, true);
                bool stick = new Random().Next(101) > 50;
                ped.Weapons.RemoveAll();
                ped.Weapons.Give(WeaponHash.Nightstick, 255, stick, false);
                ped.Weapons.Give(WeaponHash.StunGun, 255, !stick, false);
                
                ped.Task.FightAgainstHatedTargets(100f, -1);
                var blip = ped.AttachBlip();
                blip.Color = BlipColor.MichaelBlue;
                await BaseScript.Delay(100);
            }

            while (!AssignedPlayers.Any(plr =>
                       plr.Position.DistanceTo(Location) < 150f ||
                       convicts.FirstOrDefault(p => plr.Position.DistanceTo(p.Position) < 50f) is not null))
            {
                await BaseScript.Delay(500);
            }

            ShowNetworkedNotification("Please help us contain the ~r~convicts~s~!", "CHAR_CALL911", "CHAR_CALL911",
                "State Prison", "Prison Break", 10000f);


            foreach (var convict in convicts.ToArray())
            {
                if (convict is null) continue;
                var blip = convict.AttachBlip();
                blip.IsShortRange = true;
                _ = ConvictAI(convict);
            }

            while (convicts.Any(ped => !ped.IsDead && !ped.IsCuffed))
            {
                await BaseScript.Delay(1000);
            }

            var guard = guards.Where(p => !p.IsDead && !p.IsCuffed).FirstOrDefault();
            if (guard is null) return;
            guard.Position = Game.PlayerPed.Position.Around(30f);
            guard.Task.GoTo(Game.PlayerPed);

            await Utils.WaitUntilPedIsAtPosition(Game.PlayerPed.Position, guard);
            await BaseScript.Delay(500);
            string playerName = Utilities.GetPlayerData().DisplayName ?? "";
            await Utils.SubtitleChat(guard, $"Thanks for the assist, ~f~Officer {playerName}~s~!",
                Utils.UserFriendlyColors.LightBlue);
            EndCallout();
            
            base.OnStart(closestPlayer);
        }

        private static Dictionary<Player, RelationshipGroup> savedRelationshipGroups = new();

        public override void OnCancelBefore()
        {
            foreach (var spawnedEntity in SpawnedEntities.ToArray())
            {
                if (spawnedEntity is null) continue;
                if (spawnedEntity is Ped { IsCuffed: false, IsDead: false } ped)
                {
                    ped.MarkAsNoLongerNeeded();
                }
                else if (spawnedEntity is Ped { IsDead: true } deadped)
                {
                    deadped.IsPersistent = false;
                }

                spawnedEntity?.MarkAsNoLongerNeeded();
            }

            base.OnCancelBefore();
        }

        public override void OnCancelAfter()
        {
            SpawnedEntities.Clear();
            base.OnCancelAfter();
        }

        public override void OnBackupReceived(Player player)
        {
            savedRelationshipGroups[player] = player.Character.RelationshipGroup;
            player.Character.RelationshipGroup = players;
            base.OnBackupReceived(player);
        }

        public override void OnPlayerRevokedBackup(Player player)
        {
            if (savedRelationshipGroups.ContainsKey(player))
                player.Character.RelationshipGroup = savedRelationshipGroups[player];
            base.OnPlayerRevokedBackup(player);
        }
    }
}