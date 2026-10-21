using System;
using System.IO;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;

namespace Wayfinder.Tests
{
    /// <summary>Thrown from SaveService.StepHook to model the process dying at a persistence step.</summary>
    public sealed class SimulatedCrashException : Exception
    {
        public SimulatedCrashException(SaveStep step) : base("simulated process death after " + step) { }
    }

    /// <summary>Shared fixtures for the campaign suites: canonical content, temp save roots, playthrough steps.</summary>
    public static class CampaignTestKit
    {
        static CampaignContent _content;
        static string _contentDir;

        public const string Home = "garage.home";
        public const string Ford = "route.forest.ford";

        /// <summary>Canonical content (immutable after load, so one instance is shared by all tests).</summary>
        public static CampaignContent Content => _content ??= CampaignContent.FromDirectory(ContentDir);

        public static string ContentDir => _contentDir ??= LocateContentDir();

        public static string StreamingAssetsDir =>
            Path.GetFullPath(Path.Combine(ContentDir, "..", "..", "StreamingAssets"));

        static string LocateContentDir()
        {
#if UNITY_5_3_OR_NEWER
            return Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "Wayfinder", "Content"));
#else
            string overrideDir = Environment.GetEnvironmentVariable("WAYFINDER_CONTENT_DIR");
            if (!string.IsNullOrEmpty(overrideDir) && Directory.Exists(overrideDir))
                return Path.GetFullPath(overrideDir);
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Assets", "Wayfinder", "Content");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException("Assets/Wayfinder/Content not found; set WAYFINDER_CONTENT_DIR.");
#endif
        }

        public static string NewTempRoot() =>
            Path.Combine(Path.GetTempPath(), "wayfinder-campaign-tests", Guid.NewGuid().ToString("N"));

        public static void DeleteRoot(string root)
        {
            try
            {
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static CampaignSession NewGame(SaveService saves, int slot = 0, GameMode mode = GameMode.Expedition)
        {
            var result = CampaignSession.StartNew(saves, Content, new SaveSlotId(slot), mode);
            Assert.AreEqual(CampaignOpenStatus.NewGameSaved, result.Status, result.Detail);
            Assert.IsTrue(result.Playable);
            return result.Session;
        }

        public static CampaignSession Reload(string root, int slot = 0)
        {
            var result = CampaignSession.Continue(new SaveService(root), Content, new SaveSlotId(slot));
            Assert.IsTrue(result.Playable, $"{result.Status}: {result.Detail}");
            return result.Session;
        }

        public static string Canonical(CampaignSession session) => CanonicalJson.Serialize(session.CaptureSnapshot());

        public static void Ok(CommandResult result, string what) => Assert.IsTrue(result.Success, $"{what}: {result}");

        public static ExpeditionSummary Return(CampaignSession s)
        {
            var summary = s.EndExpedition(Home);
            Assert.IsTrue(summary.Success, "end expedition: " + summary.Result);
            return summary;
        }

        /// <summary>Q01 First Tracks: cache → Ranger Rise → home (the onboarding loop, §2.3).</summary>
        public static ExpeditionSummary PlayQ01(CampaignSession s)
        {
            Ok(s.Depart(), "depart");
            Ok(s.CollectCache("cache.forest.001"), "collect cache.forest.001");
            Ok(s.DiscoverLandmark("landmark.forest.01", 8f, 2.5f), "discover Ranger Rise");
            return Return(s);
        }

        /// <summary>Q02 The Creek Gauge: inspect the gauge, cross both ford checkpoints in order.</summary>
        public static ExpeditionSummary PlayQ02(CampaignSession s)
        {
            Ok(s.AcceptMission("Q02"), "accept Q02");
            Ok(s.Depart(), "depart");
            Ok(s.InspectObject("landmark.forest.04"), "inspect gauge");
            Ok(s.PassCheckpoint(Ford, 0), "ford checkpoint 1");
            Ok(s.PassCheckpoint(Ford, 1), "ford checkpoint 2");
            return Return(s);
        }

        /// <summary>Q03 A Place to Stay: kit granted to storage → load → establish camp.forest.a.</summary>
        public static ExpeditionSummary PlayQ03(CampaignSession s)
        {
            Ok(s.AcceptMission("Q03"), "accept Q03");
            Ok(s.LoadCargo("camp_kit", 1), "load camp kit");
            Ok(s.Depart(), "depart");
            Ok(s.EstablishCamp("camp.forest.a"), "establish camp");
            return Return(s);
        }

        /// <summary>Q04 A Signal Through the Trees: collect the fuse, restore the relay.</summary>
        public static ExpeditionSummary PlayQ04(CampaignSession s)
        {
            Ok(s.AcceptMission("Q04"), "accept Q04");
            Ok(s.Depart(), "depart");
            Ok(s.CollectQuestItem("questsite.forest.relay_fuse"), "collect fuse");
            Ok(s.ActivateRelay("relay.forest"), "activate relay");
            return Return(s);
        }
    }
}
