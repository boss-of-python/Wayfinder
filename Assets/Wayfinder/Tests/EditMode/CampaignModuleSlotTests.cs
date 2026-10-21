using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.Simulation;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    /// <summary>
    /// §10.2 equipment slots (KI-028 remainder, D-041): one module per named slot plus two utility
    /// slots, tire sets mutually exclusive and unlocked by research, and the garage composing exactly
    /// the modules that are fitted — so what the player reads is what the vehicle does (§11.2:741).
    /// </summary>
    public class CampaignModuleSlotTests
    {
        // Tier ids are read from the canonical table, so this fixture cannot drift from content.
        static readonly string[] TierIds = ResearchService.Global.Tiers.Select(t => t.Id.Value).ToArray();

        static string Tier(int tier) => TierIds[tier - 1];

        static List<string> Through(int tier) => TierIds.Take(tier).ToList();

        string _root;
        SaveService _saves;

        [SetUp]
        public void SetUp()
        {
            _root = NewTempRoot();
            _saves = new SaveService(_root);
        }

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        /// <summary>
        /// A game with research arranged rather than earned: payment and story gating are covered by
        /// <c>CampaignSessionTests</c> and <c>ProgressionTests</c>, slots are what this fixture tests.
        /// </summary>
        CampaignSession Arranged(int throughTier)
        {
            var s = NewGame(_saves);
            s.Research.Restore(Through(throughTier));
            return s;
        }

        /// <summary>Arranged and reloaded, so the cargo container carries the fitted rack's capacity.</summary>
        CampaignSession ArrangedAndReloaded(int throughTier)
        {
            var s = Arranged(throughTier);
            Assert.IsTrue(s.SaveNow().Success, "save the arranged loadout");
            return Reload(_root);
        }

        static float Grip(VehicleStatsResolver r, TireSet set, SurfaceType surface, bool wet = false) =>
            r.GripFor(set, surface, wet, 1f, out _);

        static VehicleStatsResolver ResolverOf(CampaignSession s)
        {
            var r = new VehicleStatsResolver();
            s.ConfigureResolver(r);
            return r;
        }

        // ================================================================== fitting and slots

        [Test]
        public void ResearchingAModule_FitsIt_AndItsStatComposes_D041()
        {
            var s = NewGame(_saves);
            PlayQ01(s);                                   // Q01 gate and the Salvage for tier 1
            var chassis = Grip(new VehicleStatsResolver(), TireSet.AllTerrain, SurfaceType.PackedDirt);

            Assert.IsFalse(s.Research.IsFitted(Tier(1)), "nothing is fitted before it is bought");
            Assert.IsEmpty(s.FittedModules);

            Ok(s.ResearchUpgrade(Tier(1)), "buy Trail Tires I");

            Assert.IsTrue(s.Research.IsFitted(Tier(1)), "buying a module fits it, so it takes effect at once");
            CollectionAssert.AreEqual(new[] { Tier(1) }, s.FittedModules);
            Assert.AreEqual(ModuleSlot.Tires, s.Research.SlotOf(Tier(1)));
            Assert.AreEqual(chassis * 1.10f, Grip(ResolverOf(s), TireSet.AllTerrain, SurfaceType.PackedDirt),
                1e-4f, "the fitted module composes into the shared resolver");
        }

        [Test]
        public void ASlotlessTier_IsAResearchUnlock_AndOccupiesNoSlot()
        {
            var s = Arranged(8);                          // tier 8 = Mud Tires (an unlock, not a module)

            Assert.IsTrue(s.Research.IsResearched(Tier(8)));
            Assert.IsFalse(s.Research.IsFitted(Tier(8)), "a tire-set unlock is not equipment (§10.2)");
            Assert.AreEqual(ModuleSlot.None, s.Research.SlotOf(Tier(8)));
            Assert.AreEqual(ResearchService.NoSlotName, ResearchService.SlotName(ModuleSlot.None));

            var refused = s.EquipModule(Tier(8));
            Assert.AreEqual(FailureReason.InvalidTarget, refused.Reason, "there is no slot to fit it into");
            Assert.IsTrue(s.Research.IsTireSetUnlocked(TireSet.Mud), "but the research still unlocks the set");
        }

        [Test]
        public void ANamedSlotHoldsOneModule_TheNewerTierReplacesTheOlder()
        {
            var s = Arranged(14);                         // Suspension I (5) and Suspension II (14)

            Assert.AreEqual(1, ResearchService.SlotCapacity(ModuleSlot.Suspension), "§10.2: one per named slot");
            Assert.AreEqual(1, s.Research.ModulesInSlot(ModuleSlot.Suspension).Count);
            Assert.AreEqual(Tier(14), s.Research.ModuleInSlot(ModuleSlot.Suspension));
            Assert.IsFalse(s.Research.IsFitted(Tier(5)), "the older package is displaced, never stacked");
            Assert.AreEqual(0.40f, s.ResolveStats().SuspensionTravelM, 1e-5f);
            Assert.AreEqual(0.30f, s.ResolveStats().GroundClearanceM, 1e-5f);

            Ok(s.EquipModule(Tier(5)), "fitting the softer package back is a legal choice");
            Assert.AreEqual(0.34f, s.ResolveStats().SuspensionTravelM, 1e-5f);
            Assert.AreEqual(0.27f, s.ResolveStats().GroundClearanceM, 1e-5f);
            Assert.IsFalse(s.Research.IsFitted(Tier(14)), "and it vacates the slot it held");
            Assert.IsTrue(s.Research.IsResearched(Tier(14)), "research is never destroyed (§17.5)");
        }

        [Test]
        public void TheTwoUtilitySlots_HoldBothUtilityModules()
        {
            var s = Arranged(7);                          // Underbody Guard (2) and Low Range (7)

            Assert.AreEqual(2, ResearchService.SlotCapacity(ModuleSlot.Utility), "§10.2: two utility slots");
            CollectionAssert.AreEqual(new[] { Tier(2), Tier(7) }, s.Research.ModulesInSlot(ModuleSlot.Utility));
            Assert.AreEqual(0.75f, s.ResolveStats().UnderbodyDamageFactor, 1e-5f, "both compose at once");
        }

        [Test]
        public void UnequippingDropsTheEffect_RefittingRestoresIt()
        {
            var s = Arranged(6);                          // Tank I = 30 L over the chassis' 24 L

            Assert.AreEqual(30f, s.ResolveStats().FuelCapacityLiters, 1e-4f);
            Assert.AreEqual(FailureReason.AlreadyApplied, s.EquipModule(Tier(6)).Reason, "it is already fitted");

            Ok(s.UnequipModule(Tier(6)), "drop the auxiliary tank");
            Assert.AreEqual(24f, s.ResolveStats().FuelCapacityLiters, 1e-4f, "the chassis tank composes again");
            Assert.IsTrue(s.Research.IsResearched(Tier(6)), "unequipping never loses progression (§17.5)");
            Assert.AreEqual(FailureReason.ItemNotFound, s.UnequipModule(Tier(6)).Reason, "it is no longer fitted");

            Ok(s.EquipModule(Tier(6)), "fit it again");
            Assert.AreEqual(30f, s.ResolveStats().FuelCapacityLiters, 1e-4f);
            Assert.AreEqual(FailureReason.ItemNotFound, s.UnequipModule(Tier(8)).Reason, "an unlock is never fitted");
        }

        [Test]
        public void DroppingToASmallerTank_ClampsFuelToTheResolvedCapacity()
        {
            var s = NewGame(_saves);
            PlayQ01(s); PlayQ02(s); PlayQ03(s); PlayQ04(s);   // gates for tiers 1-6
            s.Economy.Grant(100000);
            for (int tier = 1; tier <= 6; tier++) Ok(s.ResearchUpgrade(Tier(tier)), "buy tier " + tier);

            Assert.AreEqual(30f, s.FuelCapacityLiters, 1e-4f, "Tank I");
            Assert.AreEqual(30f, s.FuelLiters, 1e-4f, "garages refuel for free (§12.1)");

            Ok(s.UnequipModule(Tier(6)), "drop the auxiliary tank");
            Assert.AreEqual(24f, s.FuelCapacityLiters, 1e-4f);
            Assert.AreEqual(24f, s.FuelLiters, 1e-4f, "the vehicle is never left over full (§5.4)");
        }

        // ================================================================== tire sets (§10.2)

        [Test]
        public void TireSets_AreLockedUntilTheTierThatUnlocksThem()
        {
            var s = NewGame(_saves);

            Assert.AreEqual(TireSet.AllTerrain, s.SelectedTireSet, "every vehicle mounts the baseline set");
            Assert.IsTrue(s.Research.IsTireSetUnlocked(TireSet.AllTerrain));
            CollectionAssert.AreEqual(new[] { TireSet.AllTerrain }, s.Research.UnlockedTireSets());
            foreach (var set in new[] { TireSet.Mud, TireSet.Rock, TireSet.Snow })
            {
                Assert.IsFalse(s.Research.IsTireSetUnlocked(set), set + " needs research (§11.2)");
                Assert.AreEqual(FailureReason.PrerequisiteNotMet, s.SelectTireSet(set).Reason, set.ToString());
            }
            Assert.AreEqual(TireSet.AllTerrain, s.SelectedTireSet, "a refused mount changes nothing");
        }

        [Test]
        public void MountingMudTires_ChangesSurfaceGrip_WithNoSecondDirtBonus()
        {
            var chassis = Grip(new VehicleStatsResolver(), TireSet.AllTerrain, SurfaceType.PackedDirt);
            var s = Arranged(8);                          // Trail Tires I (1) + Mud Tires unlock (8)
            var r = ResolverOf(s);

            Assert.AreEqual(chassis * 1.10f, Grip(r, TireSet.AllTerrain, SurfaceType.PackedDirt), 1e-4f,
                "Trail Tires I helps the all-terrain set on dirt and gravel");

            Ok(s.SelectTireSet(TireSet.Mud), "mount the mud set");
            Assert.AreEqual(TireSet.Mud, s.SelectedTireSet);
            Assert.AreEqual(chassis, Grip(r, TireSet.Mud, SurfaceType.PackedDirt), 1e-4f,
                "a specialized set never also takes the Trail Tires bonus (§11.2:739, QA case 8)");
            Assert.AreEqual(Grip(r, TireSet.AllTerrain, SurfaceType.ShallowMud) * TireSetRules.MudMudGrip,
                Grip(r, TireSet.Mud, SurfaceType.ShallowMud), 1e-4f, "+35% mud grip (§10.2)");
            Assert.AreEqual(Grip(r, TireSet.AllTerrain, SurfaceType.Grass) * TireSetRules.MudGrassGrip,
                Grip(r, TireSet.Mud, SurfaceType.Grass), 1e-4f, "+10% grass");

            r.GripFor(TireSet.Mud, SurfaceType.IntactAsphalt, false, 1f, out float lateral);
            Assert.AreEqual(TireSetRules.MudAsphaltLateral, lateral, 1e-5f, "−10% asphalt lateral response");
            Assert.LessOrEqual(Grip(r, TireSet.Mud, SurfaceType.ShallowMud), SurfaceTable.EffectiveGripCap,
                "effective grip stays capped at μ 1.25 (§10.2)");
        }

        [Test]
        public void Tier13UnlocksTheRockSet_WhichNoEarlierTierSets()
        {
            var before = Arranged(12);
            Assert.IsFalse(before.Research.IsTireSetUnlocked(TireSet.Rock),
                "through tier 12 the rock set is unreachable — the gap this batch closes");

            var s = Arranged(13);                         // Differential Lock carries the rock unlock
            Assert.IsTrue(s.Research.IsTireSetUnlocked(TireSet.Rock));
            Ok(s.SelectTireSet(TireSet.Rock), "mount rock tires");
            Assert.AreEqual(TireSet.Rock, s.SelectedTireSet);

            var r = ResolverOf(s);
            Assert.AreEqual(Grip(r, TireSet.AllTerrain, SurfaceType.Rock) * TireSetRules.RockDryRockGrip,
                Grip(r, TireSet.Rock, SurfaceType.Rock), 1e-4f, "+20% dry rock grip (§10.2)");
            Assert.AreEqual(Grip(r, TireSet.AllTerrain, SurfaceType.DeepMud) * TireSetRules.RockDeepMudGrip,
                Grip(r, TireSet.Rock, SurfaceType.DeepMud), 1e-4f, "−10% deep mud");
            Assert.AreEqual(Grip(r, TireSet.AllTerrain, SurfaceType.Rock, wet: true),
                Grip(r, TireSet.Rock, SurfaceType.Rock, wet: true), 1e-4f, "the bonus is for DRY rock");
        }

        [Test]
        public void Tier18UnlocksTheSnowSet_AndIceStaysHazardous()
        {
            var s = Arranged(18);
            Assert.IsTrue(s.Research.IsTireSetUnlocked(TireSet.Snow));
            Ok(s.SelectTireSet(TireSet.Snow), "mount snow tires");

            var r = ResolverOf(s);
            Assert.AreEqual(Grip(r, TireSet.AllTerrain, SurfaceType.PackedSnow) * TireSetRules.SnowSnowGrip,
                Grip(r, TireSet.Snow, SurfaceType.PackedSnow), 1e-4f, "+35% snow (§10.2)");
            Assert.AreEqual(Grip(r, TireSet.AllTerrain, SurfaceType.Ice) * TireSetRules.SnowIceGrip,
                Grip(r, TireSet.Snow, SurfaceType.Ice), 1e-4f, "+20% ice");
            Assert.Less(Grip(r, TireSet.Snow, SurfaceType.Ice), Grip(r, TireSet.Snow, SurfaceType.PackedSnow),
                "ice remains the most hazardous surface even on snow tires");
            Assert.AreEqual(Grip(r, TireSet.AllTerrain, SurfaceType.IntactAsphalt) * TireSetRules.SnowAsphaltGrip,
                Grip(r, TireSet.Snow, SurfaceType.IntactAsphalt), 1e-4f, "−8% dry asphalt");
        }

        [Test]
        public void AnUnlockSurvivesUnequippingTheModuleThatCarriedIt()
        {
            var s = Arranged(13);
            Ok(s.SelectTireSet(TireSet.Rock), "mount rock");

            Ok(s.UnequipModule(Tier(13)), "drop the differential lock");
            Assert.IsFalse(s.Research.IsFitted(Tier(13)));
            Assert.IsTrue(s.Research.IsTireSetUnlocked(TireSet.Rock),
                "unlocks come from research, not from what is fitted (D-041)");
            Assert.AreEqual(TireSet.Rock, s.SelectedTireSet, "and the mounted set stays mounted");
            Assert.IsTrue(s.ResolveStats().RockTiresUnlocked,
                "the resolver agrees: tier 13 is unfitted, so only the research flag can say this");

            Ok(s.SelectTireSet(TireSet.AllTerrain), "back to all-terrain");
            Ok(s.SelectTireSet(TireSet.Rock), "rock is still mountable with the diff lock removed");
        }

        [Test]
        public void TireSetsAreMutuallyExclusive_OneMountedAtATime()
        {
            var s = Arranged(18);                         // mud, rock and snow all unlocked

            Ok(s.SelectTireSet(TireSet.Mud), "mud");
            Ok(s.SelectTireSet(TireSet.Snow), "snow replaces it");
            Assert.AreEqual(TireSet.Snow, s.SelectedTireSet);
            CollectionAssert.AreEquivalent(
                new[] { TireSet.AllTerrain, TireSet.Mud, TireSet.Rock, TireSet.Snow },
                s.Research.UnlockedTireSets(), "mounting one set never un-unlocks another");
            Assert.AreEqual(FailureReason.InvalidTarget, s.SelectTireSet((TireSet)99).Reason,
                "an undefined set is refused");
            Assert.AreEqual(TireSet.Snow, s.SelectedTireSet);
        }

        // ================================================================== composition (§11.2:739)

        [Test]
        public void Tier20CarriesTheEngineTuneTorque_WithoutStackingItsDrain()
        {
            var chassis = NewGame(_saves).ResolveStats();
            var tuned = Arranged(10).ResolveStats();      // Engine Tune I holds the engine slot

            Assert.AreEqual(chassis.PeakTorqueNm * 1.10f, tuned.PeakTorqueNm, 1e-3f, "×1.10 torque curve");
            Assert.AreEqual(1.04f, tuned.PoweredFuelDrainFactor, 1e-5f, "×1.04 powered drain");

            var s = Arranged(20);                         // Expedition Calibration takes the engine slot
            Assert.AreEqual(ModuleSlot.Engine, s.Research.SlotOf(Tier(20)));
            Assert.AreEqual(Tier(20), s.Research.ModuleInSlot(ModuleSlot.Engine));
            Assert.IsFalse(s.Research.IsFitted(Tier(10)), "so Engine Tune I no longer composes");

            var final = s.ResolveStats();
            Assert.AreEqual(tuned.PeakTorqueNm, final.PeakTorqueNm, 1e-3f,
                "the final composition keeps the ×1.10 curve (§11.2:739)");
            Assert.AreEqual(0.90f, final.PoweredFuelDrainFactor, 1e-5f,
                "and the drain is the calibration's ×0.90 alone, not ×1.04 ×0.90");
        }

        [Test]
        public void TheGarageShowsExactlyWhatTheFittedLoadoutDoes()
        {
            var s = Arranged(8);
            Ok(s.UnequipModule(Tier(4)), "run without the cargo organizer");

            // The rig's own resolver, configured the way the driver configures it on load.
            var driving = ResolverOf(s).Resolve(s.Vehicle, s.FuelLiters,
                s.Inventory.TotalMassKg(CampaignSession.Cargo), s.SelectedTireSet, SurfaceType.PackedDirt, false,
                s.Damage.EngineCondition, s.Damage.SuspensionCondition, s.Damage.TiresCondition,
                s.Research.IsTireSetUnlocked(TireSet.Mud), s.Research.IsTireSetUnlocked(TireSet.Snow),
                s.Research.IsTireSetUnlocked(TireSet.Rock));
            var garage = s.ResolveStats();

            Assert.AreEqual(12, garage.CargoSlots, "the chassis rack, not the organizer's 14");
            Assert.AreEqual(120f, garage.PayloadKg, 1e-3f, "and the chassis payload");
            Assert.AreEqual(driving.CargoSlots, garage.CargoSlots, "garage display == driving (§11.2:741)");
            Assert.AreEqual(driving.PayloadKg, garage.PayloadKg, 1e-4f);
            Assert.AreEqual(driving.FuelCapacityLiters, garage.FuelCapacityLiters, 1e-4f);
            Assert.AreEqual(driving.EffectiveGrip, garage.EffectiveGrip, 1e-6f);

            // The mounted set has to reach the same resolution (§10.2), not a hardcoded all-terrain:
            // on packed dirt the mud set loses the Trail Tires bonus, so the two are distinguishable.
            Ok(s.SelectTireSet(TireSet.Mud), "mount the mud set");
            var resolver = ResolverOf(s);
            var mounted = s.ResolveStats();
            Assert.AreEqual(Grip(resolver, TireSet.Mud, SurfaceType.PackedDirt), mounted.EffectiveGrip, 1e-6f,
                "the garage shows the mounted set's grip");
            Assert.Less(mounted.EffectiveGrip, Grip(resolver, TireSet.AllTerrain, SurfaceType.PackedDirt),
                "and it is not silently resolving all-terrain");
        }

        // ================================================================== refusal order and rollback

        [Test]
        public void LoadoutChangesAreGarageOnly()
        {
            var s = Arranged(8);
            Ok(s.Depart(), "depart");
            Assert.IsFalse(s.AtGarage);

            Assert.AreEqual(FailureReason.WrongState, s.EquipModule(Tier(4)).Reason,
                "vehicle changes are a garage service (§10.4)");
            Assert.AreEqual(FailureReason.WrongState, s.UnequipModule(Tier(1)).Reason);
            Assert.AreEqual(FailureReason.WrongState, s.SelectTireSet(TireSet.Mud).Reason);
        }

        [Test]
        public void UnknownAndUnresearchedModules_AreRefusedWithoutSideEffects()
        {
            var s = Arranged(6);
            var before = Canonical(s);

            Assert.AreEqual(FailureReason.InvalidTarget, s.EquipModule("upgrade.scout.99.nope").Reason);
            Assert.AreEqual(FailureReason.InvalidTarget, s.UnequipModule("upgrade.scout.99.nope").Reason);
            Assert.AreEqual(FailureReason.PrerequisiteNotMet, s.EquipModule(Tier(9)).Reason,
                "research the module first (§11.2)");
            Assert.AreEqual(FailureReason.ItemNotFound, s.UnequipModule(Tier(8)).Reason,
                "a slot-less unlock is never fitted, so there is nothing to remove");

            Assert.AreEqual(before, Canonical(s), "refusals leave the campaign byte-identical");
        }

        [Test]
        public void SwappingToASmallerRackWhileOverCapacity_IsRefusedAndRolledBack_QA9()
        {
            var s = ArrangedAndReloaded(17);              // Expedition Rack: 20 slots, 240 kg payload
            Assert.AreEqual(20, s.ResolveStats().CargoSlots);
            Assert.AreEqual(240f, s.ResolveStats().PayloadKg, 1e-3f);

            // The starting loadout already occupies slots (§10.4), so fill exactly up to the rack
            // instead of hardcoding a quantity that only fits an empty vehicle.
            int freeSlots = s.ResolveStats().CargoSlots - s.Inventory.UsedSlots(CampaignSession.Cargo);
            Assert.Greater(freeSlots, 0, "the rack must have room to arrange this case");
            int units = freeSlots * 10;                   // scrap_metal is 1 kg and stacks 10 high
            float loadedMass = s.Inventory.TotalMassKg(CampaignSession.Cargo) + units;

            Ok(s.Inventory.Add(CampaignSession.GarageStorage, new ItemStack(new ContentId("scrap_metal"), units)),
                $"stock {units} kg of scrap");
            Ok(s.LoadCargo("scrap_metal", units), $"load it into the last {freeSlots} slots");
            Assert.AreEqual(loadedMass, s.Inventory.TotalMassKg(CampaignSession.Cargo), 1e-3f);
            Assert.IsFalse(s.Inventory.IsOverCapacity(CampaignSession.Cargo), "the rack is exactly full");

            var refused = s.UnequipModule(Tier(17));
            Assert.AreEqual(FailureReason.OverCapacity, refused.Reason,
                "the loaded cargo would no longer fit the chassis rack (QA case 9)");
            Assert.IsTrue(s.Research.IsFitted(Tier(17)), "the refused swap rolled back (§17.4)");
            Assert.AreEqual(20, s.ResolveStats().CargoSlots);
            Assert.AreEqual(loadedMass, s.Inventory.TotalMassKg(CampaignSession.Cargo), 1e-3f, "cargo untouched");

            Assert.AreEqual(FailureReason.OverCapacity, s.EquipModule(Tier(11)).Reason,
                "swapping down to the 16-slot rack is refused for the same reason");
            Assert.IsFalse(s.Research.IsFitted(Tier(11)));

            Ok(s.UnloadCargo("scrap_metal", units / 2), "unload half");
            Ok(s.UnequipModule(Tier(17)), "now the same swap is legal");
            Assert.AreEqual(12, s.ResolveStats().CargoSlots, "an empty rack slot composes the chassis rack");
            Ok(s.EquipModule(Tier(11)), "fit the Reinforced Rack");
            Assert.AreEqual(16, s.ResolveStats().CargoSlots);
        }

        [Test]
        public void LoadoutChangesAnnounceThemselves()
        {
            var s = Arranged(8);
            s.DrainNotices();

            Ok(s.UnequipModule(Tier(4)), "drop the organizer");
            Assert.IsTrue(s.DrainNotices().Any(n => n.Kind == CampaignNoticeKind.ModuleRemoved && n.Id == Tier(4)),
                "a committed loadout change announces itself (§13.2)");

            Ok(s.EquipModule(Tier(4)), "refit it");
            Assert.IsTrue(s.DrainNotices().Any(n => n.Kind == CampaignNoticeKind.ModuleFitted && n.Id == Tier(4)));

            Ok(s.SelectTireSet(TireSet.Mud), "mount mud");
            Assert.IsTrue(s.DrainNotices().Any(n => n.Kind == CampaignNoticeKind.TireSetMounted
                && n.Id == TireSet.Mud.ToString()));
        }

        // ================================================================== persistence

        [Test]
        public void TheFittedLoadoutAndMountedSet_SurviveSaveAndReload()
        {
            var s = Arranged(8);
            Ok(s.UnequipModule(Tier(4)), "run without the cargo organizer");
            Ok(s.SelectTireSet(TireSet.Mud), "mount the mud set");
            Assert.IsTrue(s.SaveNow().Success, "save");
            var expected = Canonical(s);

            var reloaded = Reload(_root);
            Assert.AreEqual(expected, Canonical(reloaded), "reload restores the exact loadout");
            CollectionAssert.AreEqual(s.FittedModules, reloaded.FittedModules);
            Assert.AreEqual(TireSet.Mud, reloaded.SelectedTireSet);
            Assert.IsFalse(reloaded.Research.IsFitted(Tier(4)), "the organizer stays out of the rack slot");
            Assert.IsTrue(reloaded.Research.IsResearched(Tier(4)), "but stays researched");
            Assert.AreEqual(12, reloaded.ResolveStats().CargoSlots);
            CollectionAssert.AreEquivalent(new[] { TireSet.AllTerrain, TireSet.Mud },
                reloaded.Research.UnlockedTireSets());
        }

        [Test]
        public void APreSlotSave_ReconcilesToTheHighestTierPerSlot_D041()
        {
            var s = Arranged(17);
            var payload = s.CaptureSnapshot();

            // Rewrite it the way a save from before §10.2 looked: every researched module "equipped",
            // and no tire-set field at all.
            foreach (var v in payload.vehicles)
            {
                v.equippedModuleIds = new List<string>(payload.researchedUpgradeIds);
                v.equippedTireSet = null;
            }
            Assert.IsTrue(_saves.Save(new SaveSlotId(0), payload, s.Revision + 1000).Success, "save the legacy shape");

            var legacy = Reload(_root);
            Assert.AreEqual(17, legacy.Research.Tiers.Count(t => legacy.Research.IsResearched(t.Id.Value)),
                "no research is lost (§17.5)");
            Assert.AreEqual(Tier(17), legacy.Research.ModuleInSlot(ModuleSlot.CargoRack), "highest tier per slot wins");
            Assert.AreEqual(Tier(14), legacy.Research.ModuleInSlot(ModuleSlot.Suspension));
            Assert.AreEqual(Tier(15), legacy.Research.ModuleInSlot(ModuleSlot.Winch));
            Assert.AreEqual(Tier(16), legacy.Research.ModuleInSlot(ModuleSlot.Tank));
            Assert.AreEqual(Tier(13), legacy.Research.ModuleInSlot(ModuleSlot.Differential));
            Assert.AreEqual(Tier(12), legacy.Research.ModuleInSlot(ModuleSlot.Intake));
            Assert.AreEqual(Tier(10), legacy.Research.ModuleInSlot(ModuleSlot.Engine));
            Assert.AreEqual(Tier(9), legacy.Research.ModuleInSlot(ModuleSlot.Sensor));
            Assert.AreEqual(Tier(1), legacy.Research.ModuleInSlot(ModuleSlot.Tires));
            CollectionAssert.AreEqual(new[] { Tier(2), Tier(7) },
                legacy.Research.ModulesInSlot(ModuleSlot.Utility), "both fit the two utility slots");
            Assert.IsFalse(legacy.Research.IsFitted(Tier(8)), "tier 8 is an unlock, so it is never 'fitted'");
            Assert.AreEqual(TireSet.AllTerrain, legacy.SelectedTireSet, "no tire set in the save → all-terrain");

            // The reconciled state is exactly what a modern save of the same research holds.
            var modern = Arranged(17);
            CollectionAssert.AreEqual(modern.FittedModules, legacy.FittedModules);
            Assert.AreEqual(modern.SelectedTireSet, legacy.SelectedTireSet);
            Assert.AreEqual(modern.ResolveStats().CargoSlots, legacy.ResolveStats().CargoSlots);
            Assert.AreEqual(modern.ResolveStats().PeakTorqueNm, legacy.ResolveStats().PeakTorqueNm, 1e-3f);
        }

        // ================================================================== garage panel (§13.3)

        [Test]
        public void TheGaragePanel_ShowsSlotsFitStateAndTireSets()
        {
            var s = Arranged(14);                         // Suspension II fitted, Suspension I only owned
            var model = GarageModel.Build(s);

            Assert.AreEqual(20, model.Research.Count);
            var tires = model.Research.First(r => r.UpgradeId == Tier(1));
            Assert.AreEqual(EquipmentSlots.Tires, tires.SlotName, "labels use the shared §10.2 vocabulary");
            Assert.IsTrue(tires.IsFitted && tires.CanUnfit && !tires.CanFit, "fitted modules offer removal");

            var unlock = model.Research.First(r => r.UpgradeId == Tier(8));
            Assert.AreEqual(ResearchService.NoSlotName, unlock.SlotName, "an unlock occupies no slot");
            Assert.IsFalse(unlock.CanFit || unlock.CanUnfit, "so there is nothing to fit or remove");

            var newer = model.Research.First(r => r.UpgradeId == Tier(14));
            Assert.IsTrue(newer.IsFitted && newer.CanUnfit);
            var older = model.Research.First(r => r.UpgradeId == Tier(5));
            Assert.IsFalse(older.IsFitted);
            Assert.IsTrue(older.CanFit, "owned with a slot to take → the garage offers to fit it");
            Assert.AreEqual(Tier(14), older.ReplacesId, "and says which module it would displace");
            var locked = model.Research.First(r => r.UpgradeId == Tier(17));
            Assert.IsFalse(locked.CanFit || locked.CanUnfit, "not researched yet, so nothing to fit");

            Assert.AreEqual("AllTerrain", model.SelectedTireSet);
            Assert.AreEqual(4, model.TireSets.Count);
            Assert.IsTrue(model.TireSets.First(t => t.TireSetId == TireSet.AllTerrain.ToString()).Mounted);
            var mud = model.TireSets.First(t => t.TireSetId == TireSet.Mud.ToString());
            Assert.IsTrue(mud.Unlocked && mud.CanMount && !mud.Mounted, "tier 8 unlocked it");
            StringAssert.StartsWith("Mud", mud.UnlockName, "the row names the tier that unlocked it");
            var rock = model.TireSets.First(t => t.TireSetId == TireSet.Rock.ToString());
            Assert.IsTrue(rock.Unlocked && rock.CanMount, "tier 13 unlocked it");
            var snow = model.TireSets.First(t => t.TireSetId == TireSet.Snow.ToString());
            Assert.IsFalse(snow.Unlocked || snow.CanMount, "snow needs tier 18");
            Assert.IsNotEmpty(snow.UnlockName, "so the panel can say what is missing");

            // After the change the panel reflects it, because Act() rebuilds from the same session.
            Ok(s.EquipModule(Tier(5)), "fit the softer suspension package");
            var after = GarageModel.Build(s);
            Assert.IsTrue(after.Research.First(r => r.UpgradeId == Tier(5)).IsFitted);
            Assert.IsFalse(after.Research.First(r => r.UpgradeId == Tier(14)).IsFitted, "one per named slot");
            Assert.AreEqual(0.34f, s.ResolveStats().SuspensionTravelM, 1e-5f, "and the numbers follow");
        }

        [Test]
        public void SlotNames_AreTheSharedEquipmentVocabulary()
        {
            Assert.AreEqual(EquipmentSlots.Tires, ResearchService.SlotName(ModuleSlot.Tires));
            Assert.AreEqual(EquipmentSlots.Suspension, ResearchService.SlotName(ModuleSlot.Suspension));
            Assert.AreEqual(EquipmentSlots.EngineTune, ResearchService.SlotName(ModuleSlot.Engine));
            Assert.AreEqual(EquipmentSlots.Tank, ResearchService.SlotName(ModuleSlot.Tank));
            Assert.AreEqual(EquipmentSlots.Intake, ResearchService.SlotName(ModuleSlot.Intake));
            Assert.AreEqual(EquipmentSlots.Differential, ResearchService.SlotName(ModuleSlot.Differential));
            Assert.AreEqual(EquipmentSlots.CargoRack, ResearchService.SlotName(ModuleSlot.CargoRack));
            Assert.AreEqual(EquipmentSlots.Sensor, ResearchService.SlotName(ModuleSlot.Sensor));
            Assert.AreEqual(EquipmentSlots.Winch, ResearchService.SlotName(ModuleSlot.Winch));

            foreach (ModuleSlot slot in Enum.GetValues(typeof(ModuleSlot)))
            {
                string name = ResearchService.SlotName(slot);
                Assert.IsTrue(ResearchService.TryParseSlot(name, out var parsed), name);
                Assert.AreEqual(slot, parsed, name);
            }
            Assert.IsFalse(ResearchService.TryParseSlot("hyperdrive", out _), "an unknown slot is refused");
            Assert.IsFalse(ResearchService.TryParseSlot(null, out _));
            Assert.IsFalse(ResearchService.TryParseSlot(string.Empty, out _));
        }
    }
}
