using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Wayfinder.Core;
using Wayfinder.Progression;
using Wayfinder.Simulation;
using Wayfinder.World;

namespace Wayfinder.Campaign
{
    /// <summary>Engine-free world position (metres, Unity axes: Y up, X east, Z north).</summary>
    public readonly struct WorldPoint
    {
        public readonly float X, Y, Z;

        public WorldPoint(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float DistanceXZ(WorldPoint other)
        {
            float dx = X - other.X, dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public override string ToString() => $"({X:0.#}, {Y:0.#}, {Z:0.#})";
    }

    /// <summary>Position plus heading; saved as PoseDto. Velocity is never persisted (§17.4).</summary>
    public readonly struct Pose
    {
        public readonly WorldPoint Position;
        public readonly float YawDegrees;

        public Pose(WorldPoint position, float yawDegrees)
        {
            Position = position;
            YawDegrees = yawDegrees;
        }

        public Pose(float x, float y, float z, float yawDegrees) : this(new WorldPoint(x, y, z), yawDegrees) { }

        /// <summary>The world origin is open ocean, so an all-zero pose means "never recorded".</summary>
        public bool IsSet => Position.X != 0f || Position.Y != 0f || Position.Z != 0f;
    }

    public sealed class CacheDefinition
    {
        public string Id;
        public string Biome;
        public string Kind;
        public WorldPoint Position;
        public int SalvageUnits;
        public readonly List<(string itemId, int quantity)> Materials = new List<(string, int)>();
        public string RewardKey;
    }

    public sealed class LandmarkDefinition
    {
        public string Id;
        public string Biome;
        public string Name;
        public string NameKey;
        public WorldPoint Position;
        public int DiscoveryRewardSalvage;
        public string RewardKey;
        public bool HeroSite;
        public bool Vista;
        public string CapabilityBand;
    }

    public sealed class GarageDefinition
    {
        public string Id;
        public string Biome;
        public WorldPoint Position;
        public bool IsHome;
        /// <summary>Services this garage offers (§10.4), e.g. "research", "crafting", "hubTravel".</summary>
        public readonly List<string> Services = new List<string>();
    }

    public sealed class CampDefinition
    {
        public string Id;
        public string Biome;
        public WorldPoint Position;
        public float StorageCapacityKg;
        public string RequiresItem;
        /// <summary>Functions this camp offers (§10.4), e.g. "save", "deposit", "hubTravel".</summary>
        public readonly List<string> Functions = new List<string>();
    }

    /// <summary>
    /// A named relic (§8.3). Relics live in a protected mission/relic ledger rather than ordinary
    /// cargo: they have no weight and cannot be sold or lost. Five sets, one per biome, five each;
    /// a completed set unlocks cosmetic memorabilia and journal content — never traction or fuel.
    /// </summary>
    public sealed class RelicDefinition
    {
        public string Id;
        public string Biome;
        public string Name;
        public string NameKey;
        public WorldPoint Position;
        public string RewardKey;
        public bool ProtectedLedger = true;
        public bool Sellable;
    }

    /// <summary>Parsed vehicle catalog row from Content/vehicles.json (§10.1, KI-046).</summary>
    public sealed class VehicleCatalogEntry
    {
        public string Id;
        public string NameKey;
        public string Role;
        public string RequiredMissionCode = string.Empty;
        public int UnlockSalvageCost;
        public VehicleSpec Spec;
    }

    /// <summary>A circular site the vehicle must reach (camp socket, inspect target, relay, quest item, delivery, visit, contract).</summary>
    public sealed class SliceSite
    {
        public string Id;
        public string Kind = string.Empty;
        public string Biome = string.Empty;
        public string MissionCode = "";
        public string ItemId = "";
        public string UnlocksInfrastructure = "";
        public WorldPoint Position;
        public float RadiusMeters;
    }

    public sealed class SliceLandmark
    {
        public string Id;
        public float ObservationRadiusMeters;
        public bool Vista;
    }

    public sealed class SliceCheckpointRoute
    {
        public string Id;
        public string MissionCode = "";
        public float RadiusMeters;
        public readonly List<WorldPoint> Checkpoints = new List<WorldPoint>();
    }

    /// <summary>One row of Content/contracts.json (§9.3 optional work).</summary>
    public sealed class ContractDefinition
    {
        public string Id;
        public string Biome;
        public string Pattern;
        public int RegionIndex;
        public int RewardSalvage;
        public int RewardMin = 150;
        public int RewardMax = 350;
        public int TargetCount;
        public string RewardKey;
        public bool OneTime;
        public readonly List<string> Targets = new List<string>();
    }

    public sealed class GreyboxFeature
    {
        public string Kind;          // water | mud | hill
        public WorldPoint Center;
        public WorldPoint Size;      // x/z footprint, y = depth (water/mud) or height (hill)
    }

    /// <summary>
    /// Vertical-slice manifest (§21.3, Content/vertical_slice.json): which canonical content the
    /// slice uses and where its authored mission sites are. Positions are world metres.
    /// </summary>
    public sealed class VerticalSliceManifest
    {
        public string Id;
        public float AreaMinX, AreaMinZ, AreaSizeMeters;
        public string VehicleId;
        public string GarageId;
        public Pose GarageSpawn;
        public WorldPoint ArrivalCenter;
        public float ArrivalRadiusMeters;
        public float InteractionRadiusMeters = 6f;
        public int ResearchTierCount;
        public readonly List<string> MissionCodes = new List<string>();
        public readonly List<SliceLandmark> Landmarks = new List<SliceLandmark>();
        public readonly List<string> CacheIds = new List<string>();
        public readonly List<SliceSite> CampSockets = new List<SliceSite>();
        public readonly List<SliceSite> InspectTargets = new List<SliceSite>();
        public readonly List<SliceCheckpointRoute> CheckpointRoutes = new List<SliceCheckpointRoute>();
        public readonly List<SliceSite> QuestItemSites = new List<SliceSite>();
        public readonly List<SliceSite> Relays = new List<SliceSite>();
        public readonly List<GreyboxFeature> GreyboxFeatures = new List<GreyboxFeature>();

        public bool Contains(WorldPoint p) =>
            p.X >= AreaMinX && p.X < AreaMinX + AreaSizeMeters && p.Z >= AreaMinZ && p.Z < AreaMinZ + AreaSizeMeters;

        public SliceSite FindSite(List<SliceSite> sites, string id)
        {
            foreach (var s in sites)
                if (s.Id == id) return s;
            return null;
        }
    }

    /// <summary>
    /// Runtime content catalog for the campaign layer, parsed from the canonical JSON in
    /// Assets/Wayfinder/Content (KI-046 single-source loader). Hydrates <see cref="ItemCatalog"/>,
    /// <see cref="CraftingService"/>, <see cref="Vehicles"/>, and <see cref="MissionDefinitions"/>
    /// directly from JSON so authoring edits take effect without modifying C#.
    /// </summary>
    public sealed class CampaignContent : IPickupCatalog
    {
        public static readonly string[] RequiredFiles =
        {
            "items.json", "recipes.json", "vehicles.json",
            "caches.json", "landmarks.json", "garages.json", "camps.json", "missions.json", "upgrades.json",
            "vertical_slice.json", "sites.json", "contracts.json", "relics.json", "streaming_assets.json"
        };

        readonly Dictionary<string, CacheDefinition> _caches = new Dictionary<string, CacheDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, LandmarkDefinition> _landmarks = new Dictionary<string, LandmarkDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, GarageDefinition> _garages = new Dictionary<string, GarageDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, CampDefinition> _camps = new Dictionary<string, CampDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, RelicDefinition> _relics = new Dictionary<string, RelicDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> _relicSets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, PickupContents> _pickups = new Dictionary<string, PickupContents>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _missionNames = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<MissionDefinition> _missionDefinitions = new List<MissionDefinition>();
        readonly Dictionary<string, string> _upgradeResults = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, SliceSite> _sites = new Dictionary<string, SliceSite>(StringComparer.Ordinal);
        readonly Dictionary<string, SliceCheckpointRoute> _routes =
            new Dictionary<string, SliceCheckpointRoute>(StringComparer.Ordinal);
        readonly Dictionary<string, ContractDefinition> _contracts =
            new Dictionary<string, ContractDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, VehicleCatalogEntry> _vehicles =
            new Dictionary<string, VehicleCatalogEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, StreamingAssetDefinition> _streamingAssets =
            new Dictionary<string, StreamingAssetDefinition>(StringComparer.Ordinal);

        public VerticalSliceManifest Slice { get; private set; }
        public IReadOnlyDictionary<string, CacheDefinition> Caches => _caches;
        public IReadOnlyDictionary<string, LandmarkDefinition> Landmarks => _landmarks;
        public IReadOnlyDictionary<string, GarageDefinition> Garages => _garages;
        public IReadOnlyDictionary<string, CampDefinition> Camps => _camps;
        public IReadOnlyDictionary<string, RelicDefinition> Relics => _relics;
        public IReadOnlyDictionary<string, List<string>> RelicSets => _relicSets;
        public IReadOnlyDictionary<string, SliceSite> Sites => _sites;
        public IReadOnlyDictionary<string, SliceCheckpointRoute> Routes => _routes;
        public IReadOnlyDictionary<string, ContractDefinition> Contracts => _contracts;
        public IReadOnlyDictionary<string, VehicleCatalogEntry> Vehicles => _vehicles;
        public IReadOnlyList<MissionDefinition> MissionDefinitions => _missionDefinitions;
        /// <summary>Required 3D streaming mesh assets (§14.2, §14.3, LEGAL-01) — 20 assets in Assets/StreamingAssets/Models/.</summary>
        public IReadOnlyDictionary<string, StreamingAssetDefinition> StreamingAssets => _streamingAssets;

        CampaignContent() { }

        /// <summary>Loads from a content directory (headless tests, editor tools).</summary>
        public static CampaignContent FromDirectory(string contentDirectory)
        {
            if (string.IsNullOrEmpty(contentDirectory)) throw new ArgumentException("content directory required");
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in RequiredFiles)
                files[name] = File.ReadAllText(Path.Combine(contentDirectory, name));
            return FromJson(files);
        }

        /// <summary>Loads from file name → JSON text (Unity runtime: Resources TextAssets).</summary>
        public static CampaignContent FromJson(IReadOnlyDictionary<string, string> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            foreach (var name in RequiredFiles)
                if (!files.ContainsKey(name) || string.IsNullOrEmpty(files[name]))
                    throw new InvalidDataException($"campaign content file missing: {name}");

            // 1. Single-source item & crafting catalogs first so cache/quest item checks validate against JSON (KI-046).
            ItemCatalog.LoadDefinitions(ParseItems(JArray.Parse(files["items.json"])));
            CraftingService.LoadRecipes(ParseRecipes(JArray.Parse(files["recipes.json"])));

            var content = new CampaignContent();
            foreach (var entry in ParseVehicles(JArray.Parse(files["vehicles.json"])))
                content._vehicles[entry.Id] = entry;

            foreach (JObject c in JArray.Parse(files["caches.json"]))
            {
                var def = new CacheDefinition
                {
                    Id = Str(c, "id"),
                    Biome = Str(c, "biome"),
                    Kind = Str(c, "kind"),
                    Position = Point(c["positionHint"]),
                    SalvageUnits = c.Value<int?>("salvageUnits") ?? 0,
                    RewardKey = Str(c, "rewardKey")
                };
                if (c["materials"] is JArray mats)
                    foreach (JObject m in mats)
                        def.Materials.Add((Str(m, "itemId"), m.Value<int?>("quantity") ?? 0));
                foreach (var (itemId, _) in def.Materials)
                    if (!ItemCatalog.TryGet(itemId, out _))
                        throw new InvalidDataException($"{def.Id}: unknown material '{itemId}'");
                content._caches[def.Id] = def;
                content._pickups[def.Id] = new PickupContents(def.Id, def.SalvageUnits, def.Materials);
            }

            foreach (JObject l in JArray.Parse(files["landmarks.json"]))
            {
                var def = new LandmarkDefinition
                {
                    Id = Str(l, "id"),
                    Biome = Str(l, "biome"),
                    Name = Str(l, "name"),
                    NameKey = Str(l, "nameKey"),
                    Position = Point(l["positionHint"]),
                    DiscoveryRewardSalvage = l.Value<int?>("discoveryRewardSalvage") ?? DiscoveryService.LandmarkDiscoveryRewardSalvage,
                    RewardKey = Str(l, "rewardKey"),
                    HeroSite = l.Value<bool?>("heroSite") ?? false,
                    Vista = l.Value<bool?>("vista") ?? false,
                    CapabilityBand = Str(l, "capabilityBand")
                };
                content._landmarks[def.Id] = def;
            }

            foreach (JObject g in JArray.Parse(files["garages.json"]))
            {
                var def = new GarageDefinition
                {
                    Id = Str(g, "id"),
                    Biome = Str(g, "biome"),
                    Position = Point(g["positionHint"]),
                    IsHome = g.Value<bool?>("isHome") ?? false
                };
                Strings(g, "services", def.Services);
                content._garages[def.Id] = def;
            }

            foreach (JObject c in JArray.Parse(files["camps.json"]))
            {
                var def = new CampDefinition
                {
                    Id = Str(c, "id"),
                    Biome = Str(c, "biome"),
                    Position = Point(c["positionHint"]),
                    StorageCapacityKg = c.Value<float?>("storageCapacityKg") ?? CampService.CampStorageCapacityKg,
                    RequiresItem = Str(c, "requiresItem")
                };
                Strings(c, "functions", def.Functions);
                content._camps[def.Id] = def;
            }

            foreach (JObject r in JArray.Parse(files["relics.json"]))
            {
                var def = new RelicDefinition
                {
                    Id = Str(r, "id"),
                    Biome = Str(r, "biome"),
                    Name = Str(r, "name"),
                    NameKey = Str(r, "nameKey"),
                    Position = Point(r["positionHint"]),
                    RewardKey = Str(r, "rewardKey"),
                    ProtectedLedger = r.Value<bool?>("protectedLedger") ?? true,
                    Sellable = r.Value<bool?>("sellable") ?? false
                };
                content._relics[def.Id] = def;
                if (!content._relicSets.TryGetValue(def.Biome, out var set))
                    content._relicSets[def.Biome] = set = new List<string>();
                set.Add(def.Id);
            }
            foreach (var set in content._relicSets.Values) set.Sort(StringComparer.Ordinal);

            foreach (JObject m in JArray.Parse(files["missions.json"]))
            {
                string code = Str(m, "displayCode");
                content._missionNames[code] = Str(m, "name");
                content._missionDefinitions.Add(ParseMission(m));
            }
            foreach (JObject u in JArray.Parse(files["upgrades.json"]))
                content._upgradeResults[Str(u, "id")] = Str(u, "result");

            foreach (JObject c in JArray.Parse(files["contracts.json"]))
            {
                var def = new ContractDefinition
                {
                    Id = Str(c, "id"),
                    Biome = Str(c, "biome"),
                    Pattern = Str(c, "pattern"),
                    RegionIndex = c.Value<int?>("regionIndex") ?? 0,
                    RewardSalvage = c.Value<int?>("rewardSalvage") ?? 0,
                    RewardKey = Str(c, "rewardKey"),
                    OneTime = c.Value<bool?>("oneTime") ?? true
                };
                if (c["targets"] is JArray targets)
                    foreach (var t in targets) def.Targets.Add((string)t);
                def.TargetCount = def.Targets.Count > 0 ? def.Targets.Count : (c["targets"]?.Value<int?>() ?? 1);
                if (c["rewardRange"] is JArray range && range.Count >= 2)
                {
                    def.RewardMin = range[0].Value<int>();
                    def.RewardMax = range[1].Value<int>();
                }
                if (string.IsNullOrEmpty(def.RewardKey)) def.RewardKey = $"reward.contract.{def.Id}";
                content._contracts[def.Id] = def;
            }

            foreach (JObject sa in JArray.Parse(files["streaming_assets.json"]))
            {
                var bmin = sa["boundsMin"] as JArray;
                var bmax = sa["boundsMax"] as JArray;
                var def = new StreamingAssetDefinition
                {
                    Id = Str(sa, "id"),
                    Name = Str(sa, "name"),
                    Category = Str(sa, "category"),
                    RelativePath = Str(sa, "relativePath"),
                    Format = Str(sa, "format"),
                    Required = sa.Value<bool?>("required") ?? false,
                    PivotConvention = Str(sa, "pivotConvention"),
                    VertexCount = sa.Value<int?>("vertexCount") ?? 0,
                    UvCount = sa.Value<int?>("uvCount") ?? 0,
                    NormalCount = sa.Value<int?>("normalCount") ?? 0,
                    TriangleCount = sa.Value<int?>("triangleCount") ?? 0,
                    MaxTrianglesLod0 = sa.Value<int?>("maxTrianglesLod0") ?? 0,
                    BoundsMin = bmin != null && bmin.Count == 3
                        ? ((float)bmin[0], (float)bmin[1], (float)bmin[2])
                        : (0f, 0f, 0f),
                    BoundsMax = bmax != null && bmax.Count == 3
                        ? ((float)bmax[0], (float)bmax[1], (float)bmax[2])
                        : (0f, 0f, 0f),
                    Sha256 = Str(sa, "sha256"),
                    License = Str(sa, "license"),
                    Generator = Str(sa, "generator")
                };
                if (string.IsNullOrEmpty(def.Id))
                    throw new InvalidDataException("streaming_assets.json: entry missing id");
                if (!def.Required)
                    throw new InvalidDataException($"streaming_assets.json: {def.Id} must be marked required=true");
                if (content._streamingAssets.ContainsKey(def.Id))
                    throw new InvalidDataException($"streaming_assets.json: duplicate id '{def.Id}'");
                content._streamingAssets[def.Id] = def;
            }
            if (content._streamingAssets.Count != StreamingAssetCatalog.RequiredAssetCount)
                throw new InvalidDataException(
                    $"streaming_assets.json: expected {StreamingAssetCatalog.RequiredAssetCount} required 3D streaming assets, found {content._streamingAssets.Count}");
            foreach (var reqId in StreamingAssetCatalog.RequiredAssetIds)
                if (!content._streamingAssets.ContainsKey(reqId))
                    throw new InvalidDataException($"streaming_assets.json: missing required 3D streaming asset '{reqId}'");

            content.Slice = ParseSlice(JObject.Parse(files["vertical_slice.json"]));
            content.ParseWorldSites(JArray.Parse(files["sites.json"]));
            content.ValidateSliceReferences();
            return content;
        }

        static List<ItemDefinition> ParseItems(JArray array)
        {
            var list = new List<ItemDefinition>(array.Count);
            foreach (JObject o in array)
            {
                list.Add(new ItemDefinition
                {
                    Id = new ContentId(Str(o, "id")),
                    NameKey = Str(o, "nameKey"),
                    MaxStack = o.Value<int?>("maxStack") ?? 1,
                    UnitMassKg = o.Value<float?>("unitMassKg") ?? 0f,
                    ProtectedLedger = o.Value<bool?>("protectedLedger") ?? false,
                    Sellable = o.Value<bool?>("sellable") ?? false,
                    PurchasePriceSalvage = o.Value<int?>("purchasePriceSalvage") ?? 0
                });
            }
            return list;
        }

        static List<CraftingService.Recipe> ParseRecipes(JArray array)
        {
            var list = new List<CraftingService.Recipe>(array.Count);
            foreach (JObject o in array)
            {
                var output = (JObject)o["output"];
                var r = new CraftingService.Recipe
                {
                    Id = new ContentId(Str(o, "id")),
                    OutputItemId = Str(output, "itemId"),
                    OutputQuantity = output.Value<int?>("quantity") ?? 1,
                    SalvageFee = o.Value<int?>("salvageFee") ?? 0
                };
                if (o["inputs"] is JArray inputs)
                    foreach (JObject inp in inputs)
                        r.With(Str(inp, "itemId"), inp.Value<int?>("quantity") ?? 1);
                list.Add(r);
            }
            return list;
        }

        static List<VehicleCatalogEntry> ParseVehicles(JArray array)
        {
            var list = new List<VehicleCatalogEntry>(array.Count);
            foreach (JObject v in array)
            {
                string id = Str(v, "id");
                var spec = new VehicleSpec
                {
                    Id = new ContentId(id),
                    DisplayNameKey = Str(v, "nameKey"),
                    DryMassKg = v.Value<float>("dryMassKg"),
                    PayloadKg = v.Value<float>("payloadKg"),
                    FuelCapacityLiters = v.Value<float>("fuelCapacityLiters"),
                    PeakTorqueNm = v.Value<float>("peakTorqueNm"),
                    GovernedSpeedKph = v.Value<float>("governedSpeedKph"),
                    CargoSlots = v.Value<int>("cargoSlots"),
                    WheelbaseM = v.Value<float>("wheelbaseM"),
                    TrackM = v.Value<float>("trackM"),
                    WheelRadiusM = v.Value<float>("wheelRadiusM"),
                    GroundClearanceM = v.Value<float>("groundClearanceM"),
                    ComHeightM = v.Value<float>("comHeightM"),
                    FrontAxleLoadFraction = v.Value<float>("frontAxleLoadFraction"),
                    ForwardGears = v["forwardGears"].Select(g => g.Value<float>()).ToArray(),
                    ReverseGear = v.Value<float>("reverseGear"),
                    FinalDrive = v.Value<float>("finalDrive"),
                    DrivetrainEfficiency = v.Value<float>("drivetrainEfficiency"),
                    BrakeTorqueFrontNmPerWheel = v.Value<float>("brakeTorqueFrontNmPerWheel"),
                    BrakeTorqueRearNmPerWheel = v.Value<float>("brakeTorqueRearNmPerWheel"),
                    SuspensionTravelM = v.Value<float>("suspensionTravelM"),
                    SpringRateFrontNm = v.Value<float>("springRateFrontNm"),
                    SpringRateRearNm = v.Value<float>("springRateRearNm"),
                    DamperFrontNsm = v.Value<float>("damperFrontNsm"),
                    DamperRearNsm = v.Value<float>("damperRearNsm"),
                    AntiRollCouplingNm = v.Value<float>("antiRollCouplingNm"),
                    TorqueCurve = v["torqueCurve"].Select(pair => new[] { pair[0].Value<float>(), pair[1].Value<float>() }).ToArray(),
                    IdleRpm = v.Value<float>("idleRpm"),
                    LimiterRpm = v.Value<float>("limiterRpm"),
                    CdA = v.Value<float>("cdA")
                };
                ParseUnlockGate(Str(v, "unlock"), out string missionReq, out int salvageCost);
                list.Add(new VehicleCatalogEntry
                {
                    Id = id,
                    NameKey = spec.DisplayNameKey,
                    Role = Str(v, "role"),
                    RequiredMissionCode = missionReq,
                    UnlockSalvageCost = salvageCost,
                    Spec = spec
                });
            }
            return list;
        }

        static void ParseUnlockGate(string unlock, out string missionCode, out int salvageCost)
        {
            missionCode = string.Empty;
            salvageCost = 0;
            if (string.IsNullOrEmpty(unlock) || string.Equals(unlock, "Start", StringComparison.OrdinalIgnoreCase))
                return;
            foreach (var part in unlock.Split('+'))
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("Q", StringComparison.Ordinal))
                    missionCode = trimmed;
                else
                {
                    var digits = trimmed.Split(' ')[0];
                    if (int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cost))
                        salvageCost = cost;
                }
            }
        }

        static MissionDefinition ParseMission(JObject m)
        {
            string code = Str(m, "displayCode");
            var reward = m["reward"] as JObject;
            var def = new MissionDefinition
            {
                Id = new ContentId(Str(m, "id")),
                DisplayCode = code,
                NameKey = Str(m, "nameKey"),
                TurnInHub = new ContentId(Str(m, "turnInHub")),
                RewardSalvage = reward?.Value<int?>("salvage") ?? 0,
                RewardKey = reward != null ? Str(reward, "rewardKey") : ("reward.story." + code)
            };
            if (m["prerequisites"] is JArray prereqs)
                foreach (var p in prereqs) def.Prerequisites.Add(new ContentId((string)p));
            if (m["onAcceptGrant"] is JArray grants)
                foreach (JObject g in grants)
                    def.OnAcceptGrant.Add((Str(g, "itemId"), g.Value<int?>("quantity") ?? 1, Str(g, "destination")));
            if (m["objectives"] is JArray objs)
                foreach (JObject o in objs)
                {
                    if (!Enum.TryParse(Str(o, "type"), out MissionConditionType type))
                        throw new InvalidDataException($"unknown mission objective type '{Str(o, "type")}' on {code}");
                    def.Objectives.Add(new MissionObjective
                    {
                        Type = type,
                        TargetId = new ContentId(Str(o, "targetId")),
                        Count = o.Value<int?>("count") ?? 1
                    });
                }
            return def;
        }

        static VerticalSliceManifest ParseSlice(JObject o)
        {
            var area = (JObject)o["area"];
            var garage = (JObject)o["garage"];
            var spawn = (JObject)garage["spawn"];
            var slice = new VerticalSliceManifest
            {
                Id = Str(o, "id"),
                AreaMinX = area.Value<float>("minX"),
                AreaMinZ = area.Value<float>("minZ"),
                AreaSizeMeters = area.Value<float>("sizeMeters"),
                VehicleId = Str(o, "vehicleId"),
                GarageId = Str(garage, "id"),
                GarageSpawn = new Pose(Point(spawn), spawn.Value<float?>("yawDegrees") ?? 0f),
                ArrivalCenter = Point(garage["arrivalCenter"]),
                ArrivalRadiusMeters = garage.Value<float>("arrivalRadiusMeters"),
                InteractionRadiusMeters = o.Value<float?>("interactionRadiusMeters") ?? 6f,
                ResearchTierCount = o.Value<int?>("researchTierCount") ?? 0
            };
            foreach (var code in (JArray)o["missions"]) slice.MissionCodes.Add((string)code);
            foreach (JObject l in (JArray)o["landmarks"])
                slice.Landmarks.Add(new SliceLandmark
                {
                    Id = Str(l, "id"),
                    ObservationRadiusMeters = l.Value<float?>("observationRadiusMeters") ?? 30f,
                    Vista = l.Value<bool?>("vista") ?? false
                });
            foreach (var id in (JArray)o["caches"]) slice.CacheIds.Add((string)id);
            ParseSites(o["campSockets"], slice.CampSockets);
            ParseSites(o["inspectTargets"], slice.InspectTargets);
            ParseSites(o["questItemSites"], slice.QuestItemSites);
            ParseSites(o["relays"], slice.Relays);
            if (o["checkpointRoutes"] is JArray routes)
                foreach (JObject r in routes)
                {
                    var route = new SliceCheckpointRoute
                    {
                        Id = Str(r, "id"),
                        MissionCode = Str(r, "missionCode"),
                        RadiusMeters = r.Value<float?>("radiusMeters") ?? 8f
                    };
                    foreach (var cp in (JArray)r["checkpoints"]) route.Checkpoints.Add(Point(cp));
                    slice.CheckpointRoutes.Add(route);
                }
            if (o["greyboxFeatures"] is JArray features)
                foreach (JObject f in features)
                    slice.GreyboxFeatures.Add(new GreyboxFeature
                    {
                        Kind = Str(f, "kind"),
                        Center = Point(f["center"]),
                        Size = Point(f["size"])
                    });
            return slice;
        }

        /// <summary>Parses Content/sites.json — world mission, relay, delivery, visit, and contract sites.</summary>
        void ParseWorldSites(JArray array)
        {
            foreach (JObject s in array)
            {
                var site = new SliceSite
                {
                    Id = Str(s, "id"),
                    Kind = Str(s, "kind"),
                    Biome = Str(s, "biome"),
                    MissionCode = Str(s, "missionCode"),
                    ItemId = Str(s, "itemId"),
                    UnlocksInfrastructure = Str(s, "unlocksInfrastructure"),
                    Position = Point(s["positionHint"]),
                    RadiusMeters = s.Value<float?>("radiusMeters") ?? 12f
                };
                if (string.IsNullOrEmpty(site.MissionCode))
                {
                    var codes = (JArray)s["missionCodes"];
                    if (codes != null && codes.Count > 0) site.MissionCode = (string)codes[0];
                }
                if (site.RadiusMeters <= 0f) throw new InvalidDataException($"site '{site.Id}': radius must be positive");
                if (!WorldConstants.InBounds(site.Position.X, site.Position.Z))
                    throw new InvalidDataException($"site '{site.Id}' lies outside the world");
                if (_sites.ContainsKey(site.Id)) throw new InvalidDataException($"duplicate site id '{site.Id}'");
                _sites[site.Id] = site;

                if (s["checkpoints"] is JArray checkpoints)
                {
                    var route = new SliceCheckpointRoute
                    {
                        Id = site.Id,
                        MissionCode = site.MissionCode,
                        RadiusMeters = site.RadiusMeters
                    };
                    foreach (var cp in checkpoints) route.Checkpoints.Add(Point(cp));
                    _routes[route.Id] = route;
                }
            }
        }

        public SliceSite FindSite(List<SliceSite> sliceSites, string id)
        {
            var site = Slice.FindSite(sliceSites, id);
            if (site != null) return site;
            return string.IsNullOrEmpty(id) ? null : (_sites.TryGetValue(id, out var world) ? world : null);
        }

        static void ParseSites(JToken token, List<SliceSite> into)
        {
            if (!(token is JArray array)) return;
            foreach (JObject s in array)
                into.Add(new SliceSite
                {
                    Id = Str(s, "id"),
                    MissionCode = Str(s, "missionCode"),
                    ItemId = s["itemId"] != null ? Str(s, "itemId") : Str(s, "requiresItem"),
                    Position = Point(s["position"]),
                    RadiusMeters = s.Value<float?>("radiusMeters") ?? 6f
                });
        }

        void ValidateSliceReferences()
        {
            var s = Slice;
            if (!_garages.ContainsKey(s.GarageId)) throw new InvalidDataException($"slice garage '{s.GarageId}' unknown");
            foreach (var l in s.Landmarks)
                if (!_landmarks.ContainsKey(l.Id)) throw new InvalidDataException($"slice landmark '{l.Id}' unknown");
            foreach (var c in s.CacheIds)
                if (!_caches.ContainsKey(c)) throw new InvalidDataException($"slice cache '{c}' unknown");
            foreach (var camp in s.CampSockets)
                if (!_camps.ContainsKey(camp.Id)) throw new InvalidDataException($"slice camp '{camp.Id}' unknown");
            foreach (var site in s.QuestItemSites)
                if (!ItemCatalog.TryGet(site.ItemId, out _)) throw new InvalidDataException($"quest site item '{site.ItemId}' unknown");
        }

        static string Str(JObject o, string key) => o.Value<string>(key) ?? string.Empty;

        static void Strings(JObject o, string key, List<string> into)
        {
            if (!(o[key] is JArray array)) return;
            foreach (JToken t in array)
            {
                string value = t.Value<string>();
                if (!string.IsNullOrEmpty(value) && !into.Contains(value)) into.Add(value);
            }
        }

        static WorldPoint Point(JToken token)
        {
            if (!(token is JObject o)) return default;
            return new WorldPoint(o.Value<float?>("x") ?? 0f, o.Value<float?>("y") ?? 0f, o.Value<float?>("z") ?? 0f);
        }

        public bool TryGetContents(string pickupId, out PickupContents contents) =>
            _pickups.TryGetValue(pickupId ?? string.Empty, out contents);

        public string MissionName(string code) =>
            _missionNames.TryGetValue(code ?? string.Empty, out var name) && !string.IsNullOrEmpty(name) ? name : code;

        public string UpgradeResult(string upgradeId) =>
            _upgradeResults.TryGetValue(upgradeId ?? string.Empty, out var text) ? text : string.Empty;

        public string LandmarkName(string landmarkId) =>
            _landmarks.TryGetValue(landmarkId ?? string.Empty, out var l) && !string.IsNullOrEmpty(l.Name) ? l.Name : landmarkId;

        public const string HubTravelService = "hubTravel";

        public bool OffersHubTravel(string hubId)
        {
            if (string.IsNullOrEmpty(hubId)) return false;
            if (_garages.TryGetValue(hubId, out var garage)) return garage.Services.Contains(HubTravelService);
            if (_camps.TryGetValue(hubId, out var camp)) return camp.Functions.Contains(HubTravelService);
            return false;
        }

        public WorldPoint GaragePosition(string garageId) =>
            _garages.TryGetValue(garageId ?? string.Empty, out var g) ? g.Position : Slice.GarageSpawn.Position;

        public VehicleSpec VehicleSpecById(string vehicleId) =>
            _vehicles.TryGetValue(vehicleId ?? string.Empty, out var entry) ? entry.Spec : VehicleSpec.ById(vehicleId);
    }
}
