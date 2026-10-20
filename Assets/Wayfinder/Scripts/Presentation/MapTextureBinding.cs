using System;
using UnityEngine;
using Wayfinder.Campaign;

namespace Wayfinder.Presentation
{
    /// <summary>
    /// Uploads the engine-free <see cref="MapRaster"/> into a Texture2D (RGBA32, no mips, sRGB).
    /// The raster's rows are already bottom-up, so the bytes go to the GPU verbatim. Uploads only
    /// when <see cref="MapRaster.Version"/> changed.
    /// Status: implemented, uncompiled/unverified in Unity (syntax-checked only).
    /// </summary>
    public sealed class MapTextureBinding : IDisposable
    {
        readonly MapRaster _raster;
        int _uploadedVersion = int.MinValue;

        public Texture2D Texture { get; private set; }

        public MapTextureBinding(MapRaster raster)
        {
            _raster = raster ?? throw new ArgumentNullException(nameof(raster));
            Texture = new Texture2D(raster.Width, raster.Height, TextureFormat.RGBA32, false, false)
            {
                name = "Exploration Map",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Sync();
        }

        public bool Sync()
        {
            if (Texture == null || _raster.Version == _uploadedVersion) return false;
            Texture.LoadRawTextureData(_raster.Pixels);
            Texture.Apply(false, false);
            _uploadedVersion = _raster.Version;
            _raster.ClearDirty();
            return true;
        }

        public void Dispose()
        {
            if (Texture != null) UnityEngine.Object.Destroy(Texture);
            Texture = null;
        }
    }

    /// <summary>
    /// One exploration map shared by the HUD minimap and the full map screen: refreshes the raster
    /// within a per-frame cell budget (a vista reveal spreads over a few frames), uploads at most
    /// every <see cref="UploadIntervalSeconds"/> while cells are still queued, and rebuilds markers
    /// at 4 Hz. Uses unscaled time because the map screen pauses the simulation (timeScale 0).
    /// Status: implemented, uncompiled/unverified in Unity (syntax-checked only).
    /// </summary>
    public sealed class MapPresenter : IDisposable
    {
        public const int CellsPerFrame = 24;
        public const float UploadIntervalSeconds = 0.25f;
        public const float MarkerIntervalSeconds = 0.25f;

        readonly CampaignSession _session;
        float _sinceUpload, _sinceMarkers;
        bool _uploadPending;
        int _seenMapRevision;

        public ExplorationMap Map { get; }
        public MapTextureBinding Binding { get; }
        public MapLayers Layers { get; private set; } = MapLayers.All;
        /// <summary>Raised after markers were rebuilt (views re-layout their icons).</summary>
        public event Action MarkersChanged;

        public MapPresenter(CampaignSession session)
        {
            _session = session;
            Map = new ExplorationMap(session);
            Binding = new MapTextureBinding(Map.Raster);
            _seenMapRevision = session != null ? session.MapRevision : 0;
        }

        public void SetLayers(MapLayers layers)
        {
            Layers = layers;
            RebuildMarkersNow();
        }

        public void RebuildMarkersNow()
        {
            Map.RebuildMarkers(Layers);
            _sinceMarkers = 0f;
            _seenMapRevision = _session != null ? _session.MapRevision : 0;
            MarkersChanged?.Invoke();
        }

        public void Tick(float unscaledDeltaSeconds)
        {
            if (Map.Refresh(CellsPerFrame)) _uploadPending = true;
            _sinceUpload += unscaledDeltaSeconds;
            if (_uploadPending && (Map.PendingCells == 0 || _sinceUpload >= UploadIntervalSeconds))
            {
                Binding.Sync();
                _uploadPending = false;
                _sinceUpload = 0f;
            }
            // A committed map change (marker placed/removed, route set, cells revealed, save
            // loaded) rebuilds on this frame: the domain counter is the dirty flag (§16.3,
            // SYS-WRD-002). The 0.25 s cadence remains only because the player arrow itself moves
            // every frame; it no longer decides when a marker change becomes visible.
            if (_session != null && _session.MapRevision != _seenMapRevision)
            {
                RebuildMarkersNow();
                return;
            }
            _sinceMarkers += unscaledDeltaSeconds;
            if (_sinceMarkers >= MarkerIntervalSeconds) RebuildMarkersNow();
        }

        public void Dispose() => Binding.Dispose();
    }
}
