using System;
using System.IO;
using UnityEngine;
using Wayfinder.Core;

namespace Wayfinder.Platform
{
    /// <summary>
    /// Platform adapter (§16.1): persistent-data paths, atomic file replace semantics,
    /// pause/focus signals. No gameplay monetization, no network.
    /// </summary>
    public static class PlatformAdapter
    {
        /// <summary>Save root: platform persistent-data directory — never asset folders (§17.4).</summary>
        public static string SaveRootDirectory
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "Saves");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsFilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static string DiagnosticsDirectory
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, "Diagnostics");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// Safest supported replace on the same filesystem. On platforms where File.Replace is
        /// unsupported, falls back to move-with-backup. Never leaves 'current' half-written.
        /// </summary>
        public static void AtomicReplace(string tempFile, string destinationFile, string backupFile)
        {
            try
            {
                if (File.Exists(destinationFile))
                    File.Replace(tempFile, destinationFile, backupFile);
                else
                    File.Move(tempFile, destinationFile);
            }
            catch (PlatformNotSupportedException)
            {
                FallbackReplace(tempFile, destinationFile, backupFile);
            }
            catch (IOException)
            {
                FallbackReplace(tempFile, destinationFile, backupFile);
            }
        }

        static void FallbackReplace(string tempFile, string destinationFile, string backupFile)
        {
            if (File.Exists(destinationFile))
            {
                if (!string.IsNullOrEmpty(backupFile))
                    File.Copy(destinationFile, backupFile, overwrite: true);
                File.Delete(destinationFile);
            }
            File.Move(tempFile, destinationFile);
        }
    }

    /// <summary>Quality tiers (§18.5). Dynamic quality must never change physics or collectible visibility.</summary>
    public enum QualityTier
    {
        BatteryLow = 0,  // 30 FPS
        Balanced = 1,    // 60 FPS
        High = 2         // 60 FPS
    }

    public static class QualityTierService
    {
        public const int FpsBattery = 30;
        public const int FpsBalanced = 60;
        public const int FpsHigh = 60;

        public static void Apply(QualityTier tier)
        {
            switch (tier)
            {
                case QualityTier.BatteryLow:
                    QualitySettings.SetQualityLevel(0, true);
                    Application.targetFrameRate = FpsBattery;
                    QualitySettings.shadowDistance = 35f;
                    QualitySettings.shadowCascades = 1;
                    QualitySettings.antiAliasing = 0;
                    break;
                case QualityTier.Balanced:
                    QualitySettings.SetQualityLevel(1, true);
                    Application.targetFrameRate = FpsBalanced;
                    QualitySettings.shadowDistance = 60f;
                    QualitySettings.shadowCascades = 2;
                    QualitySettings.antiAliasing = 2;
                    break;
                case QualityTier.High:
                    QualitySettings.SetQualityLevel(2, true);
                    Application.targetFrameRate = FpsHigh;
                    QualitySettings.shadowDistance = 85f;
                    QualitySettings.shadowCascades = 2;
                    QualitySettings.antiAliasing = 4;
                    break;
            }
        }

        /// <summary>
        /// Dynamic degradation (§18.5): reduce render scale and nonessential detail only.
        /// Never physics, hazards, collectibles or collision.
        /// </summary>
        public static float AdjustRenderScale(float currentScale, float medianFrameTimeMs)
        {
            const float targetMs = 16.67f;
            if (medianFrameTimeMs > targetMs * 1.15f)
                return Mathf.Max(0.70f, currentScale - 0.05f);
            if (medianFrameTimeMs < targetMs * 0.85f)
                return Mathf.Min(1.00f, currentScale + 0.05f);
            return currentScale;
        }
    }

    /// <summary>
    /// Rolling local diagnostic log (§19.4). No remote telemetry. User-initiated export only.
    /// No personal text, location, advertising IDs or device accounts.
    /// </summary>
    public sealed class LocalDiagnostics
    {
        const int MaxLines = 2000;
        readonly System.Collections.Generic.Queue<string> _lines =
            new System.Collections.Generic.Queue<string>(MaxLines);

        public void Log(string category, string message)
        {
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] [{category}] {message}";
            _lines.Enqueue(line);
            while (_lines.Count > MaxLines) _lines.Dequeue();
        }

        /// <summary>User-initiated export after review (§19.4).</summary>
        public string Export()
        {
            string path = Path.Combine(PlatformAdapter.DiagnosticsDirectory,
                $"diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
            File.WriteAllLines(path, _lines);
            return path;
        }
    }
}
