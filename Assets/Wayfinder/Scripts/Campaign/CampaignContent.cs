using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Wayfinder.Progression;

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
        public string CapabilityBand;
    }

    public sealed class GarageDefinition
    {
        public string Id;
        public string Biome;
        public WorldPoint Position;
        public bool IsHome;
    }

    public sealed class CampDefinition
    {
        public string Id;
        public string Biome;
        public float StorageCapacityKg;
        public string RequiresItem;
    }

    /// <summary>A circular site the vehicle must reach (camp socket, inspect target, relay, quest item).</summary>
    public sealed class SliceSite
    {
        public string Id;
        public string MissionCode = "";
        public string ItemId = "";
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
    /// Assets/Wayfinder/Content (headless tests read the folder; the Unity runtime passes the
    /// TextAssets that ProjectSetup copies into Resources/Content). Also serves as the
    /// <see cref="IPickupCatalog"/> so cache collection grants authored contents (KI-020).
    /// </summary>
    public sealed class CampaignContent : IPickupCatalog
    {
        public static readonly string[] RequiredFiles =
        {
            "caches.json", "landmarks.json", "garages.json", "camps.json", "missions.json", "upgrades.json",
            "vertical_slice.json"
        };

        readonly Dictionary<string, CacheDefinition> _caches = new Dictionary<string, CacheDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, LandmarkDefinition> _landmarks = new Dictionary<string, LandmarkDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, GarageDefinition> _garages = new Dictionary<string, GarageDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, CampDefinition> _camps = new Dictionary<string, CampDefinition>(StringComparer.Ordinal);
        readonly Dictionary<string, PickupContents> _pickups = new Dictionary<string, PickupContents>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _missionNames = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _upgradeResults = new Dictionary<string, string>(StringComparer.Ordinal);

        public VerticalSliceManifest Slice { get; private set; }
        public IReadOnlyDictionary<string, CacheDefinition> Caches => _caches;
        public IReadOnlyDictionary<string, LandmarkDefinition> Landmarks => _landmarks;
        public IReadOnlyDictionary<string, GarageDefinition> Garages => _garages;
        public IReadOnlyDictionary<string, CampDefinition> Camps => _camps;

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

            var content = new CampaignContent();
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
                content._garages[def.Id] = def;
            }

            foreach (JObject c in JArray.Parse(files["camps.json"]))
            {
                var def = new CampDefinition
                {
                    Id = Str(c, "id"),
                    Biome = Str(c, "biome"),
                    StorageCapacityKg = c.Value<float?>("storageCapacityKg") ?? CampService.CampStorageCapacityKg,
                    RequiresItem = Str(c, "requiresItem")
                };
                content._camps[def.Id] = def;
            }

            foreach (JObject m in JArray.Parse(files["missions.json"]))
                content._missionNames[Str(m, "displayCode")] = Str(m, "name");
            foreach (JObject u in JArray.Parse(files["upgrades.json"]))
                content._upgradeResults[Str(u, "id")] = Str(u, "result");

            content.Slice = ParseSlice(JObject.Parse(files["vertical_slice.json"]));
            content.ValidateSliceReferences();
            return content;
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

        static WorldPoint Point(JToken token)
        {
            if (!(token is JObject o)) return default;
            return new WorldPoint(o.Value<float?>("x") ?? 0f, o.Value<float?>("y") ?? 0f, o.Value<float?>("z") ?? 0f);
        }

        public bool TryGetContents(string pickupId, out PickupContents contents) =>
            _pickups.TryGetValue(pickupId ?? string.Empty, out contents);

        /// <summary>Mission display name from missions.json (English placeholder until string tables exist, KI-012).</summary>
        public string MissionName(string code) =>
            _missionNames.TryGetValue(code ?? string.Empty, out var name) && !string.IsNullOrEmpty(name) ? name : code;

        /// <summary>Exact per-tier change text from upgrades.json (§13.3 "exact change per tier").</summary>
        public string UpgradeResult(string upgradeId) =>
            _upgradeResults.TryGetValue(upgradeId ?? string.Empty, out var text) ? text : string.Empty;

        public string LandmarkName(string landmarkId) =>
            _landmarks.TryGetValue(landmarkId ?? string.Empty, out var l) && !string.IsNullOrEmpty(l.Name) ? l.Name : landmarkId;

        /// <summary>World position of a garage (home falls back to the slice spawn when unknown).</summary>
        public WorldPoint GaragePosition(string garageId) =>
            _garages.TryGetValue(garageId ?? string.Empty, out var g) ? g.Position : Slice.GarageSpawn.Position;
    }
}
