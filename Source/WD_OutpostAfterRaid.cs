using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using WorldDomination;
using TSA_WorldDomination.Outposts;

namespace OutpostMVP
{
    [StaticConstructorOnStartup]
    public static class OutpostMVP_Setup
    {
        static OutpostMVP_Setup()
        {
            var harmony = new Harmony("com.tsa.worlddomination.outpostmvp");
            harmony.PatchAll();
            Log.Message("[TSA WorldDomination] Outpost Manual Interceptor Initialized.");
        }
    }

    [HarmonyPatch(typeof(SettlementDefeatUtility), "CheckDefeated")]
    public static class Patch_InterceptDefeat
    {
        [HarmonyPriority(Priority.High)]
        public static bool Prefix(Settlement factionBase)
        {
            if (factionBase == null || factionBase.Faction == null || factionBase.Faction.IsPlayer) return true;
            Map map = factionBase.Map;
            if (map == null || !SettlementDefeatUtility.IsDefeated(map, factionBase.Faction)) return true;

            SettlementTier capturedTier = SettlementTier.T1;
            var viralComp = factionBase.GetComponent<CompViralSpread>();
            if (viralComp != null) capturedTier = viralComp.tier;

            // Manual swap to ensure map stays open so player can reform caravan/loot
            DestroyedSettlement ruins = (DestroyedSettlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.DestroyedSettlement);
            ruins.Tile = factionBase.Tile;
            ruins.SetFaction(factionBase.Faction);
            map.info.parent = ruins;
            Find.WorldObjects.Add(ruins);

            OutpostDataTracker.Register(ruins.Tile, factionBase.Name ?? factionBase.LabelCap, capturedTier);

            factionBase.Destroy();
            return false;
        }
    }

    [HarmonyPatch(typeof(MapDeiniter), "Deinit")]
    public static class Patch_MapDeinit
    {
        public static void Postfix(Map map)
        {
            // Triggers when the player finally leaves the map after victory
            if (map.Parent is DestroyedSettlement ruins && OutpostDataTracker.IsPending(ruins.Tile))
            {
                var data = OutpostDataTracker.GetData(ruins.Tile);

                ChoiceLetter_OutpostOpportunity letter = new ChoiceLetter_OutpostOpportunity();
                letter.def = LetterDefOf.NeutralEvent;
                letter.tile = data.tile;
                letter.originalName = data.name;
                letter.ruinsId = ruins.ID;
                letter.tier = data.tier;

                letter.Label = "Establish Outpost?";
                letter.Text = $"The ruins of {letter.originalName} (Tier {data.tier}) have been vacated. Volunteers are waiting to man the site.";

                Find.LetterStack.ReceiveLetter(letter);
                OutpostDataTracker.MarkFinished(ruins.Tile);
                letter.OpenLetter();
            }
        }
    }

    public class OutpostDataTracker : GameComponent
    {
        private static Dictionary<int, (int tile, string name, bool used, SettlementTier tier)> pending = new Dictionary<int, (int, string, bool, SettlementTier)>();
        public OutpostDataTracker(Game game) { }
        public static void Register(int tile, string name, SettlementTier tier) => pending[tile] = (tile, name, false, tier);
        public static bool IsPending(int tile) => pending.ContainsKey(tile) && !pending[tile].used;

        public static void MarkFinished(int tile)
        {
            if (pending.ContainsKey(tile))
            {
                var d = pending[tile];
                pending[tile] = (d.tile, d.name, true, d.tier);
            }
        }

        public static (int tile, string name, SettlementTier tier) GetData(int tile) => (pending[tile].tile, pending[tile].name, pending[tile].tier);
    }

    public class ChoiceLetter_OutpostOpportunity : ChoiceLetter
    {
        public int tile;
        public string originalName;
        public int ruinsId;
        public SettlementTier tier;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                yield return new DiaOption("Establish Outpost")
                {
                    action = () => Find.WindowStack.Add(new Dialog_OutpostSelection(tile, originalName, ruinsId, tier, this)),
                    resolveTree = true
                };
                yield return new DiaOption("Ignore")
                {
                    action = () => Find.LetterStack.RemoveLetter(this),
                    resolveTree = true
                };
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tile, "tile");
            Scribe_Values.Look(ref originalName, "originalName");
            Scribe_Values.Look(ref ruinsId, "ruinsId");
            Scribe_Values.Look(ref tier, "tier");
        }
    }
}