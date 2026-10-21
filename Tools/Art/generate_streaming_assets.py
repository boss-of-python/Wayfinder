#!/usr/bin/env python3
"""Deterministic generator for Wayfinder's 30 required 3D streaming assets (§14.2, §14.3, LEGAL-01).

Emits:
  * 30 pure-ASCII Wavefront .obj meshes under `Assets/StreamingAssets/Models/` (LF line endings,
    1 unit = 1 meter, +Z forward, +Y up, consistent placement datum, valid v/vt/vn/f indices,
    non-degenerate triangles, zero third-party geometry).
  * `Assets/Wayfinder/Content/streaming_assets.json` (canonical manifest required by
    `CampaignContent.RequiredFiles`, `Tools/Validation/validate_content.py`, `ProjectSetup`,
    `BuildCommands`, and `StreamingAssetCatalog`).
"""

import hashlib
import json
import math
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
STREAMING_MODELS_DIR = os.path.join(ROOT, "Assets", "StreamingAssets", "Models")
MANIFEST_PATH = os.path.join(ROOT, "Assets", "Wayfinder", "Content", "streaming_assets.json")


class MeshBuilder:
    """Builds a deterministic Wavefront OBJ mesh with positions, UVs, and per-face normals."""

    def __init__(self, asset_id: str, description: str):
        self.asset_id = asset_id
        self.description = description
        self.vertices = []   # list of (x, y, z)
        self.uvs = []        # list of (u, v)
        self.normals = []    # list of (nx, ny, nz)
        self.faces = []      # list of ((v1, vt1, vn1), (v2, vt2, vn2), (v3, vt3, vn3)) 1-based

    def _add_v(self, x: float, y: float, z: float) -> int:
        self.vertices.append((round(x, 5), round(y, 5), round(z, 5)))
        return len(self.vertices)

    def _add_vt(self, u: float, v: float) -> int:
        self.uvs.append((round(u, 5), round(v, 5)))
        return len(self.uvs)

    def _add_vn(self, nx: float, ny: float, nz: float) -> int:
        length = math.sqrt(nx * nx + ny * ny + nz * nz)
        if length < 1e-9:
            nx, ny, nz = 0.0, 1.0, 0.0
        else:
            nx, ny, nz = nx / length, ny / length, nz / length
        self.normals.append((round(nx, 5), round(ny, 5), round(nz, 5)))
        return len(self.normals)

    @staticmethod
    def _face_normal(p0, p1, p2):
        ux, uy, uz = p1[0] - p0[0], p1[1] - p0[1], p1[2] - p0[2]
        vx, vy, vz = p2[0] - p0[0], p2[1] - p0[1], p2[2] - p0[2]
        nx = uy * vz - uz * vy
        ny = uz * vx - ux * vz
        nz = ux * vy - uy * vx
        return nx, ny, nz

    def add_triangle(self, p0, p1, p2, uv0=(0.0, 0.0), uv1=(1.0, 0.0), uv2=(0.5, 1.0)):
        nx, ny, nz = self._face_normal(p0, p1, p2)
        if math.sqrt(nx * nx + ny * ny + nz * nz) < 1e-7:
            return
        i0 = self._add_v(*p0)
        i1 = self._add_v(*p1)
        i2 = self._add_v(*p2)
        t0 = self._add_vt(*uv0)
        t1 = self._add_vt(*uv1)
        t2 = self._add_vt(*uv2)
        n = self._add_vn(nx, ny, nz)
        self.faces.append(((i0, t0, n), (i1, t1, n), (i2, t2, n)))

    def add_quad(self, p0, p1, p2, p3):
        """Adds two CCW triangles (p0, p1, p2) and (p0, p2, p3)."""
        self.add_triangle(p0, p1, p2, (0.0, 0.0), (1.0, 0.0), (1.0, 1.0))
        self.add_triangle(p0, p2, p3, (0.0, 0.0), (1.0, 1.0), (0.0, 1.0))

    def add_box(self, min_x: float, min_y: float, min_z: float,
                max_x: float, max_y: float, max_z: float):
        """Adds an axis-aligned box with outward-pointing CCW faces."""
        p000 = (min_x, min_y, min_z)
        p100 = (max_x, min_y, min_z)
        p110 = (max_x, max_y, min_z)
        p010 = (min_x, max_y, min_z)
        p001 = (min_x, min_y, max_z)
        p101 = (max_x, min_y, max_z)
        p111 = (max_x, max_y, max_z)
        p011 = (min_x, max_y, max_z)

        # +Z (front), -Z (back), +X (right), -X (left), +Y (top), -Y (bottom)
        self.add_quad(p001, p101, p111, p011)
        self.add_quad(p100, p000, p010, p110)
        self.add_quad(p101, p100, p110, p111)
        self.add_quad(p000, p001, p011, p010)
        self.add_quad(p011, p111, p110, p010)
        self.add_quad(p000, p100, p101, p001)

    def add_frustum_y(self, cx: float, cz: float, y0: float, y1: float,
                      r0x: float, r0z: float, r1x: float, r1z: float,
                      segments: int = 8, cap_bottom: bool = True, cap_top: bool = True):
        """Adds a vertical frustum/cylinder/cone along +Y."""
        bottom_ring = []
        top_ring = []
        for i in range(segments):
            angle = 2.0 * math.pi * i / segments
            ca, sa = math.cos(angle), math.sin(angle)
            bottom_ring.append((cx + r0x * ca, y0, cz + r0z * sa))
            top_ring.append((cx + r1x * ca, y1, cz + r1z * sa))

        for i in range(segments):
            nxt = (i + 1) % segments
            b0, b1 = bottom_ring[i], bottom_ring[nxt]
            t0, t1 = top_ring[i], top_ring[nxt]
            if r1x < 1e-5 and r1z < 1e-5:
                self.add_triangle(b0, t0, b1, (0.0, 0.0), (0.5, 1.0), (1.0, 0.0))
            else:
                self.add_quad(b0, t0, t1, b1)

        if cap_bottom and (r0x > 1e-5 or r0z > 1e-5):
            center_b = (cx, y0, cz)
            for i in range(segments):
                nxt = (i + 1) % segments
                self.add_triangle(center_b, bottom_ring[i], bottom_ring[nxt],
                                  (0.5, 0.5), (0.0, 0.0), (1.0, 0.0))

        if cap_top and (r1x > 1e-5 or r1z > 1e-5):
            center_t = (cx, y1, cz)
            for i in range(segments):
                nxt = (i + 1) % segments
                self.add_triangle(center_t, top_ring[nxt], top_ring[i],
                                  (0.5, 0.5), (1.0, 1.0), (0.0, 1.0))

    def add_prism_roof_z(self, min_x: float, max_x: float, base_y: float, peak_y: float,
                         min_z: float, max_z: float):
        """Adds a gable roof with ridge running along Z."""
        mid_x = 0.5 * (min_x + max_x)
        bl0 = (min_x, base_y, min_z)
        br0 = (max_x, base_y, min_z)
        pk0 = (mid_x, peak_y, min_z)
        bl1 = (min_x, base_y, max_z)
        br1 = (max_x, base_y, max_z)
        pk1 = (mid_x, peak_y, max_z)

        # Left slope, right slope, bottom, front/back gables
        self.add_quad(bl0, bl1, pk1, pk0)
        self.add_quad(br1, br0, pk0, pk1)
        self.add_quad(bl0, br0, br1, bl1)
        self.add_triangle(bl1, br1, pk1, (0.0, 0.0), (1.0, 0.0), (0.5, 1.0))
        self.add_triangle(br0, bl0, pk0, (0.0, 0.0), (1.0, 0.0), (0.5, 1.0))

    def bounds(self):
        xs = [v[0] for v in self.vertices]
        ys = [v[1] for v in self.vertices]
        zs = [v[2] for v in self.vertices]
        return (
            [round(min(xs), 4), round(min(ys), 4), round(min(zs), 4)],
            [round(max(xs), 4), round(max(ys), 4), round(max(zs), 4)],
        )

    def to_obj(self) -> str:
        lines = [
            f"# Wayfinder 3D Streaming Asset: {self.asset_id}",
            f"# Description: {self.description}",
            "# Generator: Tools/Art/generate_streaming_assets.py (deterministic, seed 481516)",
            "# License: Project-owned generated (LEGAL-01)",
            "# Conventions: 1 unit = 1 meter, +Z forward, +Y up (§14.3)",
            f"o {self.asset_id}",
        ]
        for x, y, z in self.vertices:
            lines.append(f"v {x:.5f} {y:.5f} {z:.5f}")
        for u, v in self.uvs:
            lines.append(f"vt {u:.5f} {v:.5f}")
        for nx, ny, nz in self.normals:
            lines.append(f"vn {nx:.5f} {ny:.5f} {nz:.5f}")
        for (v0, t0, n0), (v1, t1, n1), (v2, t2, n2) in self.faces:
            lines.append(f"f {v0}/{t0}/{n0} {v1}/{t1}/{n1} {v2}/{t2}/{n2}")
        return "\n".join(lines) + "\n"


def build_scout_chassis() -> MeshBuilder:
    m = MeshBuilder("veh.scout.s1.chassis", "Scout S1 4x4 utility chassis, cab, roof rack and cargo bed (§4.2, §14.2)")
    # Lower frame + rock sliders (datum Y=0 at lower chassis base, total 1.70m x 1.45m x 3.90m)
    m.add_box(-0.85, 0.00, -1.95, 0.85, 0.48, 1.95)
    # Front hood / engine bay (+Z forward)
    m.add_box(-0.80, 0.48, 0.65, 0.80, 0.88, 1.85)
    # Front bumper + winch fairlead housing
    m.add_box(-0.85, 0.12, 1.85, 0.85, 0.42, 1.95)
    # Enclosed cab
    m.add_box(-0.82, 0.48, -0.55, 0.82, 1.32, 0.65)
    # Roof utility rack
    m.add_box(-0.72, 1.32, -0.50, 0.72, 1.45, 0.55)
    # Rear flatbed side rails (left, right, headboard)
    m.add_box(-0.85, 0.48, -1.95, -0.72, 0.78, -0.55)
    m.add_box(0.72, 0.48, -1.95, 0.85, 0.78, -0.55)
    m.add_box(-0.72, 0.48, -1.95, 0.72, 0.72, -1.82)
    return m


def build_scout_wheel() -> MeshBuilder:
    m = MeshBuilder("veh.scout.s1.wheel", "Scout S1 off-road tire and wheel rim, 0.36 m radius, axle-centered (§4.2, §14.3)")
    segments = 12
    radius = 0.36
    hub_r = 0.20
    half_w = 0.13
    ring_l = []
    ring_r = []
    hub_l = []
    hub_r_pts = []
    for i in range(segments):
        a = 2.0 * math.pi * i / segments
        y = radius * math.cos(a)
        z = radius * math.sin(a)
        hy = hub_r * math.cos(a)
        hz = hub_r * math.sin(a)
        ring_l.append((-half_w, y, z))
        ring_r.append((half_w, y, z))
        hub_l.append((-half_w * 0.85, hy, hz))
        hub_r_pts.append((half_w * 0.85, hy, hz))

    for i in range(segments):
        nxt = (i + 1) % segments
        # Outer tread band
        m.add_quad(ring_l[i], ring_r[i], ring_r[nxt], ring_l[nxt])
        # Left sidewall + hub cap
        m.add_quad(hub_l[i], ring_l[i], ring_l[nxt], hub_l[nxt])
        m.add_triangle((-half_w * 0.85, 0.0, 0.0), hub_l[i], hub_l[nxt])
        # Right sidewall + hub cap
        m.add_quad(ring_r[i], hub_r_pts[i], hub_r_pts[nxt], ring_r[nxt])
        m.add_triangle((half_w * 0.85, 0.0, 0.0), hub_r_pts[nxt], hub_r_pts[i])
    return m


def build_home_garage() -> MeshBuilder:
    m = MeshBuilder("env.hub.home_garage", "Forest home garage bay with pitched roof and workshop annex (§12.1, §14.2)")
    # Concrete foundation slab (12m x 10m)
    m.add_box(-6.00, 0.00, -5.00, 6.00, 0.25, 5.00)
    # Left wall, right wall, rear wall (leaving front drive-in bay open)
    m.add_box(-5.80, 0.25, -4.80, -4.80, 3.80, 4.80)
    m.add_box(2.80, 0.25, -4.80, 5.80, 3.80, 4.80)
    m.add_box(-4.80, 0.25, -4.80, 2.80, 3.80, -3.80)
    # Overhead lintel beam above service bay
    m.add_box(-4.80, 3.20, 3.80, 2.80, 3.80, 4.80)
    # Pitched roof over entire structure
    m.add_prism_roof_z(-6.00, 6.00, 3.80, 5.20, -5.00, 5.00)
    return m


def build_field_camp() -> MeshBuilder:
    m = MeshBuilder("env.hub.field_camp", "Expedition field camp shelter, timber pad, supply crate and beacon mast (§12.2, §14.2)")
    # Timber deck pad (5m x 5m)
    m.add_box(-2.50, 0.00, -2.50, 2.50, 0.18, 2.50)
    # Canvas tent walls + pitched ridge roof
    m.add_box(-1.80, 0.18, -1.60, 0.60, 1.35, 1.40)
    m.add_prism_roof_z(-1.90, 0.70, 1.35, 2.25, -1.70, 1.50)
    # Camp storage locker crate
    m.add_box(1.00, 0.18, -1.20, 2.10, 0.95, 0.20)
    # Signal lantern post
    m.add_box(1.55, 0.18, 1.35, 1.85, 2.80, 1.65)
    return m


def build_landmark_watchtower() -> MeshBuilder:
    m = MeshBuilder("env.landmark.watchtower", "Forest lookout watchtower with braced legs, deck and peaked roof (§8.2, §14.2)")
    # 4 corner foundation piers + timber legs
    for sx in (-1.60, 1.20):
        for sz in (-1.60, 1.20):
            m.add_box(sx, 0.00, sz, sx + 0.40, 8.20, sz + 0.40)
    # Mid-height cross-brace collar
    m.add_box(-1.70, 4.00, -1.70, 1.70, 4.35, 1.70)
    # Observation platform deck
    m.add_box(-2.10, 8.20, -2.10, 2.10, 8.60, 2.10)
    # Lookout cabin
    m.add_box(-1.65, 8.60, -1.65, 1.65, 10.60, 1.65)
    # Pyramidal roof
    m.add_frustum_y(0.0, 0.0, 10.60, 12.00, 2.10, 2.10, 0.0, 0.0, segments=4, cap_bottom=True, cap_top=False)
    return m


def build_relay_tower() -> MeshBuilder:
    m = MeshBuilder("env.prop.relay_tower", "Regional radio relay lattice tower, base cabinet and dish (§6.1, §14.2)")
    # Concrete anchor pad
    m.add_box(-1.20, 0.00, -1.20, 1.20, 0.50, 1.20)
    # Base transceiver cabinet
    m.add_box(0.45, 0.50, -0.60, 1.10, 1.90, 0.60)
    # 3-stage tapered mast sections
    m.add_frustum_y(0.0, 0.0, 0.50, 6.50, 0.55, 0.55, 0.38, 0.38, segments=6)
    m.add_frustum_y(0.0, 0.0, 6.50, 13.00, 0.38, 0.38, 0.22, 0.22, segments=6)
    m.add_frustum_y(0.0, 0.0, 13.00, 18.00, 0.22, 0.22, 0.08, 0.08, segments=6)
    # Crossbars / dish mount near top
    m.add_box(-0.85, 15.20, -0.15, 0.85, 15.55, 0.15)
    return m


def build_salvage_cache() -> MeshBuilder:
    m = MeshBuilder("env.prop.salvage_cache", "Weatherproof expedition salvage cache chest with skids and bevelled lid (§10.3, §14.2)")
    # Bottom runner skids
    m.add_box(-0.60, 0.00, -0.45, -0.42, 0.10, 0.45)
    m.add_box(0.42, 0.00, -0.45, 0.60, 0.10, 0.45)
    # Main crate body
    m.add_box(-0.58, 0.10, -0.42, 0.58, 0.62, 0.42)
    # Bevelled top lid
    m.add_frustum_y(0.0, 0.0, 0.62, 0.80, 0.60, 0.45, 0.50, 0.36, segments=4)
    return m


def build_creek_gauge() -> MeshBuilder:
    m = MeshBuilder("env.prop.creek_gauge", "Hydrological ford depth gauge post with base collar and high-water fin (§5.3, §14.2)")
    # Concrete base collar
    m.add_box(-0.20, 0.00, -0.20, 0.20, 0.25, 0.20)
    # Vertical graduated post
    m.add_box(-0.08, 0.25, -0.08, 0.08, 2.40, 0.08)
    # 0.55 m baseline safe-wading indicator tick + 0.85 m snorkel tick + top marker fin
    m.add_box(-0.16, 0.52, -0.04, 0.16, 0.58, 0.04)
    m.add_box(-0.16, 0.82, -0.04, 0.16, 0.88, 0.04)
    m.add_box(-0.18, 2.10, -0.04, 0.18, 2.35, 0.04)
    return m


def build_forest_pine() -> MeshBuilder:
    m = MeshBuilder("env.tree.forest_pine", "Conifer pine tree with tapered trunk and 3 layered foliage cones (§7.6, §14.2)")
    # Tapered trunk
    m.add_frustum_y(0.0, 0.0, 0.00, 2.60, 0.28, 0.28, 0.18, 0.18, segments=8)
    # 3 tiered foliage cones
    m.add_frustum_y(0.0, 0.0, 2.00, 4.80, 1.60, 1.60, 0.35, 0.35, segments=8)
    m.add_frustum_y(0.0, 0.0, 4.10, 6.80, 1.25, 1.25, 0.25, 0.25, segments=8)
    m.add_frustum_y(0.0, 0.0, 6.10, 8.50, 0.85, 0.85, 0.00, 0.00, segments=8, cap_top=False)
    return m


def build_granite_outcrop() -> MeshBuilder:
    m = MeshBuilder("env.rock.granite_outcrop", "Faceted granite boulder outcrop with shoulder slab (§7.6, §14.2)")
    # Main faceted boulder body
    m.add_frustum_y(-0.15, 0.05, 0.00, 1.15, 1.25, 1.15, 0.95, 0.85, segments=7)
    m.add_frustum_y(-0.10, 0.05, 1.15, 1.80, 0.95, 0.85, 0.32, 0.28, segments=7)
    # Secondary low shoulder boulder
    m.add_frustum_y(0.65, -0.25, 0.00, 0.85, 0.75, 0.70, 0.35, 0.30, segments=6)
    return m


def build_mule_chassis() -> MeshBuilder:
    m = MeshBuilder("veh.mule.m2.chassis", "Mule M2 4x4 heavy cargo utility truck chassis and staked flatbed (§4.2, §10.1)")
    # Heavy ladder frame + side steps (datum Y=0 at lower frame rail, 1.96m wide x 4.60m long)
    m.add_box(-0.98, 0.00, -2.30, 0.98, 0.52, 2.30)
    # Forward engine nose & radiator grille
    m.add_box(-0.92, 0.52, 1.05, 0.92, 1.02, 2.22)
    # Heavy steel front bumper
    m.add_box(-0.98, 0.14, 2.20, 0.98, 0.46, 2.35)
    # Forward crew cab
    m.add_box(-0.94, 0.52, -0.15, 0.94, 1.52, 1.05)
    # Headache rack behind cab
    m.add_box(-0.96, 0.52, -0.28, 0.96, 1.56, -0.15)
    # Long cargo flatbed deck + left/right stake sideboards + tailgate
    m.add_box(-0.98, 0.52, -2.30, -0.84, 0.96, -0.28)
    m.add_box(0.84, 0.52, -2.30, 0.98, 0.96, -0.28)
    m.add_box(-0.84, 0.52, -2.30, 0.84, 0.88, -2.16)
    return m


def build_lynx_chassis() -> MeshBuilder:
    m = MeshBuilder("veh.lynx.l3.chassis", "Lynx L3 agile technical rock crawler with tubular roll cage (§4.2, §10.1)")
    # High-clearance boat-sided tub (datum Y=0 at skidplate bottom, 1.84m wide x 3.60m long)
    m.add_box(-0.92, 0.00, -1.80, 0.92, 0.46, 1.80)
    # Tapered front hood
    m.add_box(-0.76, 0.46, 0.55, 0.76, 0.84, 1.72)
    # Stubby rock-crawler winch bumper + stinger bar
    m.add_box(-0.68, 0.18, 1.72, 0.68, 0.48, 1.86)
    # Cockpit lower tub
    m.add_box(-0.86, 0.46, -0.65, 0.86, 0.92, 0.55)
    # Left & right roll-cage side rails + overhead canopy bar
    m.add_box(-0.86, 0.92, -0.65, -0.72, 1.38, 0.45)
    m.add_box(0.72, 0.92, -0.65, 0.86, 1.38, 0.45)
    m.add_box(-0.86, 1.28, -0.65, 0.86, 1.40, 0.45)
    # Rear spare-tire carrier deck
    m.add_box(-0.78, 0.46, -1.80, 0.78, 0.78, -0.65)
    return m


def build_atlas_chassis() -> MeshBuilder:
    m = MeshBuilder("veh.atlas.a4.chassis", "Atlas A4 long-range expedition rig with cab-over nose and service canopy (§4.2, §10.1)")
    # Deep reinforced expedition chassis frame (datum Y=0, 2.20m wide x 5.20m long)
    m.add_box(-1.10, 0.00, -2.60, 1.10, 0.58, 2.60)
    # Cab-over forward control cabin
    m.add_box(-1.06, 0.58, 0.85, 1.06, 1.88, 2.52)
    # Heavy expedition bullbar + winch housing
    m.add_box(-1.10, 0.16, 2.52, 1.10, 0.62, 2.68)
    # Twin side saddle fuel tanks
    m.add_box(-1.14, 0.18, -0.35, -0.92, 0.54, 0.75)
    m.add_box(0.92, 0.18, -0.35, 1.14, 0.54, 0.75)
    # Rear enclosed expedition module / service canopy with bevelled roof
    m.add_box(-1.08, 0.58, -2.55, 1.08, 1.76, 0.70)
    m.add_prism_roof_z(-1.08, 1.08, 1.76, 2.02, -2.55, 0.70)
    # Roof beacon & sensor bar above cab
    m.add_box(-0.85, 1.88, 1.15, 0.85, 2.04, 2.15)
    return m


def build_timber_bridge_span() -> MeshBuilder:
    m = MeshBuilder("env.landmark.bridge_span", "Timber creek crossing bridge with stone abutments, stringers and wheel curbs (§6.2, §8.2)")
    # Near and far stone abutments (-Z and +Z ends, 10m total span)
    m.add_box(-2.40, 0.00, -5.00, 2.40, 1.10, -3.80)
    m.add_box(-2.40, 0.00, 3.80, 2.40, 1.10, 5.00)
    # Center creek pier
    m.add_box(-2.10, 0.00, -0.45, 2.10, 1.10, 0.45)
    # Timber plank deck
    m.add_box(-2.30, 1.10, -4.80, 2.30, 1.32, 4.80)
    # Left & right heavy timber wheel guard curbs
    m.add_box(-2.30, 1.32, -4.80, -1.95, 1.62, 4.80)
    m.add_box(1.95, 1.32, -4.80, 2.30, 1.62, 4.80)
    # Approach ramps at -Z and +Z ends
    m.add_prism_roof_z(-2.30, 2.30, 0.00, 1.10, -5.80, -4.80)
    m.add_prism_roof_z(-2.30, 2.30, 0.00, 1.10, 4.80, 5.80)
    return m


def build_city_overpass() -> MeshBuilder:
    m = MeshBuilder("env.landmark.city_overpass", "Ruined city concrete highway overpass pier, crosshead and broken deck (§6.2, §8.2)")
    # Wide reinforced concrete footing pad
    m.add_box(-3.20, 0.00, -2.20, 3.20, 0.55, 2.20)
    # Twin octagonal concrete support columns
    m.add_frustum_y(-1.85, 0.0, 0.55, 5.40, 0.65, 0.65, 0.58, 0.58, segments=8)
    m.add_frustum_y(1.85, 0.0, 0.55, 5.40, 0.65, 0.65, 0.58, 0.58, segments=8)
    # Hammerhead pier cap / crossbeam
    m.add_box(-4.20, 5.40, -1.25, 4.20, 6.20, 1.25)
    # Cantilevered broken highway deck slab + jersey barriers
    m.add_box(-4.40, 6.20, -4.50, 4.40, 6.65, 4.50)
    m.add_box(-4.40, 6.65, -4.50, -3.95, 7.35, 4.50)
    m.add_box(3.95, 6.65, -4.50, 4.40, 7.35, 4.50)
    return m


def build_fuel_depot_tank() -> MeshBuilder:
    m = MeshBuilder("env.prop.fuel_depot_tank", "Garage fuel reserve tank on concrete containment pad and cradles (§10.3, §12.1)")
    # Bunded concrete containment base pad
    m.add_box(-1.35, 0.00, -2.10, 1.35, 0.22, 2.10)
    # Front and rear saddle cradles
    m.add_box(-0.95, 0.22, -1.35, 0.95, 0.72, -0.95)
    m.add_box(-0.95, 0.22, 0.95, 0.95, 0.72, 1.35)
    # Elevated tank body (octagonal horizontal-style tank block + top fill turret + pump cabinet)
    m.add_box(-0.90, 0.65, -1.75, 0.90, 1.95, 1.75)
    m.add_frustum_y(0.0, 0.0, 1.95, 2.25, 0.32, 0.32, 0.28, 0.28, segments=6)
    m.add_box(-0.45, 0.22, 1.75, 0.45, 1.35, 2.05)
    return m


def build_winch_anchor_bollard() -> MeshBuilder:
    m = MeshBuilder("env.prop.winch_anchor_bollard", "Anchored steel winch recovery bollard with base flange and top cap (§5.2, §14.2)")
    # Heavy bolted steel base plate on ground datum Y=0
    m.add_box(-0.38, 0.00, -0.38, 0.38, 0.12, 0.38)
    # Main cylindrical steel bollard post
    m.add_frustum_y(0.0, 0.0, 0.12, 0.92, 0.18, 0.18, 0.16, 0.16, segments=8)
    # Flared cable-retaining crown cap + cross pin
    m.add_frustum_y(0.0, 0.0, 0.92, 1.10, 0.23, 0.23, 0.19, 0.19, segments=8)
    m.add_box(-0.28, 0.68, -0.05, 0.28, 0.78, 0.05)
    return m


def build_trail_cairn_marker() -> MeshBuilder:
    m = MeshBuilder("env.prop.trail_cairn_marker", "Stacked stone frontier route cairn with survey flag pole (§8.2, §14.2)")
    # 3 stacked tapered stone tiers from Y=0
    m.add_frustum_y(0.0, 0.0, 0.00, 0.42, 0.62, 0.58, 0.48, 0.45, segments=7)
    m.add_frustum_y(0.02, -0.01, 0.42, 0.78, 0.44, 0.42, 0.31, 0.29, segments=7)
    m.add_frustum_y(-0.01, 0.02, 0.78, 1.06, 0.27, 0.25, 0.14, 0.13, segments=6)
    # Timber survey stake & high-visibility pennant board
    m.add_box(-0.04, 1.00, -0.04, 0.04, 1.95, 0.04)
    m.add_box(0.04, 1.62, -0.02, 0.38, 1.90, 0.02)
    return m


def build_wetland_cypress() -> MeshBuilder:
    m = MeshBuilder("env.tree.wetland_cypress", "Wetlands bald cypress with fluted buttress base and wide canopy (§6.2, §7.6)")
    # Fluted swollen buttress base (Y=0) + tapered trunk
    m.add_frustum_y(0.0, 0.0, 0.00, 1.40, 0.68, 0.68, 0.32, 0.32, segments=8)
    m.add_frustum_y(0.0, 0.0, 1.40, 4.20, 0.32, 0.32, 0.20, 0.20, segments=8)
    # Broad flat-topped wetland canopy tiers
    m.add_frustum_y(0.0, 0.0, 3.60, 5.40, 2.10, 2.10, 1.55, 1.55, segments=8)
    m.add_frustum_y(0.15, -0.10, 5.10, 6.70, 1.45, 1.45, 0.65, 0.65, segments=8)
    return m


def build_canyon_hoodoo() -> MeshBuilder:
    m = MeshBuilder("env.rock.canyon_hoodoo", "Stratified red canyon sandstone hoodoo spire with overhang caprock (§6.2, §7.6)")
    # Pedestal talus base (Y=0)
    m.add_frustum_y(0.0, 0.0, 0.00, 1.10, 1.35, 1.20, 0.82, 0.75, segments=7)
    # Eroded narrow neck
    m.add_frustum_y(0.03, -0.02, 1.10, 2.35, 0.82, 0.75, 0.52, 0.48, segments=7)
    # Wider stratified caprock overhang + crown
    m.add_frustum_y(0.0, 0.02, 2.35, 3.15, 0.96, 0.88, 0.74, 0.68, segments=7)
    m.add_frustum_y(-0.02, 0.0, 3.15, 3.65, 0.74, 0.68, 0.28, 0.24, segments=7)
    return m


def _build_wheel_mesh(asset_id: str, description: str, radius: float, hub_r: float, half_w: float, segments: int = 12) -> MeshBuilder:
    m = MeshBuilder(asset_id, description)
    ring_l, ring_r, hub_l, hub_r_pts = [], [], [], []
    for i in range(segments):
        a = 2.0 * math.pi * i / segments
        y = radius * math.cos(a)
        z = radius * math.sin(a)
        hy = hub_r * math.cos(a)
        hz = hub_r * math.sin(a)
        ring_l.append((-half_w, y, z))
        ring_r.append((half_w, y, z))
        hub_l.append((-half_w * 0.85, hy, hz))
        hub_r_pts.append((half_w * 0.85, hy, hz))

    for i in range(segments):
        nxt = (i + 1) % segments
        m.add_quad(ring_l[i], ring_r[i], ring_r[nxt], ring_l[nxt])
        m.add_quad(hub_l[i], ring_l[i], ring_l[nxt], hub_l[nxt])
        m.add_triangle((-half_w * 0.85, 0.0, 0.0), hub_l[i], hub_l[nxt])
        m.add_quad(ring_r[i], hub_r_pts[i], hub_r_pts[nxt], ring_r[nxt])
        m.add_triangle((half_w * 0.85, 0.0, 0.0), hub_r_pts[nxt], hub_r_pts[i])
    return m


def build_mule_wheel() -> MeshBuilder:
    return _build_wheel_mesh(
        "veh.mule.m2.wheel",
        "Mule M2 heavy-duty cargo truck tire and steel rim, 0.42 m radius, axle-centered (§4.2, §14.3)",
        radius=0.42,
        hub_r=0.23,
        half_w=0.16,
        segments=12,
    )


def build_lynx_wheel() -> MeshBuilder:
    return _build_wheel_mesh(
        "veh.lynx.l3.wheel",
        "Lynx L3 beadlock crawler tire and rim, 0.40 m radius, axle-centered (§4.2, §14.3)",
        radius=0.40,
        hub_r=0.21,
        half_w=0.15,
        segments=12,
    )


def build_atlas_wheel() -> MeshBuilder:
    return _build_wheel_mesh(
        "veh.atlas.a4.wheel",
        "Atlas A4 deep-lug expedition tire and planetary hub, 0.48 m radius, axle-centered (§4.2, §14.3)",
        radius=0.48,
        hub_r=0.26,
        half_w=0.19,
        segments=12,
    )


def build_outpost_garage() -> MeshBuilder:
    m = MeshBuilder("env.hub.outpost_garage", "Regional biome outpost garage bay with Quonset-style arched roof and loading dock (§12.1, §14.2)")
    # Raised reinforced concrete apron slab (10m x 9m)
    m.add_box(-5.00, 0.00, -4.50, 5.00, 0.24, 4.50)
    # Left tool bay wall, right logistics wall, rear bulkhead
    m.add_box(-4.80, 0.24, -4.30, -3.70, 3.40, 4.30)
    m.add_box(2.40, 0.24, -4.30, 4.80, 3.40, 4.30)
    m.add_box(-3.70, 0.24, -4.30, 2.40, 3.40, -3.40)
    # Side cargo loading dock platform + awning
    m.add_box(4.80, 0.00, -2.20, 6.10, 0.85, 2.20)
    # Bevelled industrial roof + ridge vent
    m.add_prism_roof_z(-5.10, 5.10, 3.40, 4.55, -4.50, 4.50)
    m.add_box(-0.55, 4.40, -2.50, 0.55, 4.85, 2.50)
    return m


def build_highland_observatory() -> MeshBuilder:
    m = MeshBuilder("env.landmark.highland_observatory", "Highland ridge weather observatory dome, stone plinth and anemometer mast (§6.2, §8.2)")
    # Octagonal stone foundation plinth (Y=0)
    m.add_frustum_y(0.0, 0.0, 0.00, 1.20, 3.40, 3.40, 3.10, 3.10, segments=8)
    # Cylindrical instrument drum wall
    m.add_frustum_y(0.0, 0.0, 1.20, 3.80, 2.65, 2.65, 2.65, 2.65, segments=8)
    # Two-stage geodesic-style weather dome
    m.add_frustum_y(0.0, 0.0, 3.80, 5.30, 2.70, 2.70, 1.75, 1.75, segments=8)
    m.add_frustum_y(0.0, 0.0, 5.30, 6.20, 1.75, 1.75, 0.35, 0.35, segments=8)
    # Entry vestibule + anemometer mast
    m.add_box(-1.10, 0.00, 2.40, 1.10, 2.70, 3.80)
    m.add_box(2.20, 1.20, -0.25, 2.50, 7.10, 0.25)
    return m


def build_wetland_pump_station() -> MeshBuilder:
    m = MeshBuilder("env.landmark.wetland_pump_station", "Wetlands flood-control sluice gate, pump house and intake manifolds (§6.2, §8.2)")
    # Concrete sluice headwall & basin deck (Y=0)
    m.add_box(-3.60, 0.00, -2.80, 3.60, 0.90, 2.80)
    # Elevated pump control room
    m.add_box(-2.40, 0.90, -2.20, 1.60, 3.50, 1.40)
    m.add_prism_roof_z(-2.55, 1.75, 3.50, 4.30, -2.35, 1.55)
    # Twin vertical intake standpipes + discharge header
    m.add_frustum_y(2.35, -0.95, 0.90, 3.80, 0.48, 0.48, 0.48, 0.48, segments=8)
    m.add_frustum_y(2.35, 0.95, 0.90, 3.80, 0.48, 0.48, 0.48, 0.48, segments=8)
    m.add_box(1.60, 2.60, -1.25, 2.95, 3.20, 1.25)
    return m


def build_solar_beacon_pod() -> MeshBuilder:
    m = MeshBuilder("env.prop.solar_beacon_pod", "Autonomous field telemetry pod with tripod base, battery box and solar wing (§8.2, §9.1)")
    # Weighted hexagonal tripod footpad (Y=0)
    m.add_frustum_y(0.0, 0.0, 0.00, 0.22, 0.72, 0.72, 0.52, 0.52, segments=6)
    # Insulated battery & logger enclosure
    m.add_box(-0.36, 0.22, -0.32, 0.36, 0.86, 0.32)
    # Sensor mast
    m.add_box(-0.07, 0.86, -0.07, 0.07, 2.45, 0.07)
    # Tilted solar array panel wing + top strobe head
    m.add_box(-0.68, 1.55, -0.42, 0.68, 1.68, 0.42)
    m.add_frustum_y(0.0, 0.0, 2.45, 2.72, 0.14, 0.14, 0.08, 0.08, segments=6)
    return m


def build_cargo_pallet_stack() -> MeshBuilder:
    m = MeshBuilder("env.prop.cargo_pallet_stack", "Timber logistics pallet with strapped supply cases and fuel jerrycans (§10.3, §12.3)")
    # Standard 1.4m x 1.2m timber forklift pallet runners & deck (Y=0)
    m.add_box(-0.70, 0.00, -0.60, -0.52, 0.10, 0.60)
    m.add_box(-0.09, 0.00, -0.60, 0.09, 0.10, 0.60)
    m.add_box(0.52, 0.00, -0.60, 0.70, 0.10, 0.60)
    m.add_box(-0.70, 0.10, -0.60, 0.70, 0.16, 0.60)
    # Lower heavy equipment case + upper ration/parts crates + jerrycan pair
    m.add_box(-0.64, 0.16, -0.54, 0.18, 0.74, 0.54)
    m.add_box(-0.58, 0.74, -0.48, 0.12, 1.18, 0.18)
    m.add_box(0.22, 0.16, -0.50, 0.64, 0.68, -0.04)
    m.add_box(0.22, 0.16, 0.04, 0.64, 0.68, 0.50)
    return m


def build_highland_larch() -> MeshBuilder:
    m = MeshBuilder("env.tree.highland_larch", "Windswept subalpine highland larch with gnarly trunk and asymmetric crown (§6.2, §7.6)")
    # Root flare & leaning subalpine trunk (Y=0)
    m.add_frustum_y(0.0, 0.0, 0.00, 1.80, 0.34, 0.34, 0.21, 0.21, segments=7)
    m.add_frustum_y(0.10, 0.05, 1.80, 3.90, 0.21, 0.21, 0.13, 0.13, segments=7)
    # Windswept needle clusters shifted slightly leeward (+X/+Z)
    m.add_frustum_y(0.18, 0.08, 2.40, 4.60, 1.35, 1.15, 0.42, 0.36, segments=7)
    m.add_frustum_y(0.26, 0.12, 4.10, 6.10, 1.05, 0.90, 0.28, 0.24, segments=7)
    m.add_frustum_y(0.32, 0.15, 5.60, 7.20, 0.68, 0.58, 0.00, 0.00, segments=7, cap_top=False)
    return m


def build_basalt_column_cluster() -> MeshBuilder:
    m = MeshBuilder("env.rock.basalt_column_cluster", "Hexagonal highland volcanic basalt column cluster (§6.2, §7.6)")
    # Central tall hexagonal prism + 3 stepped surrounding hexagonal columns (all rooted at Y=0)
    m.add_frustum_y(0.00, 0.00, 0.00, 2.45, 0.62, 0.62, 0.58, 0.58, segments=6)
    m.add_frustum_y(0.78, 0.32, 0.00, 1.75, 0.52, 0.52, 0.49, 0.49, segments=6)
    m.add_frustum_y(-0.68, 0.45, 0.00, 1.35, 0.48, 0.48, 0.45, 0.45, segments=6)
    m.add_frustum_y(-0.25, -0.72, 0.00, 0.95, 0.55, 0.55, 0.52, 0.52, segments=6)
    return m


ASSET_SPECS = [
    {
        "id": "veh.scout.s1.chassis",
        "name": "Scout S1 Chassis & Cab",
        "category": "vehicle",
        "filename": "scout_s1_chassis.obj",
        "maxTrianglesLod0": 45000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_scout_chassis,
    },
    {
        "id": "veh.scout.s1.wheel",
        "name": "Scout S1 Off-Road Wheel",
        "category": "vehicle_wheel",
        "filename": "scout_s1_wheel.obj",
        "maxTrianglesLod0": 4500,
        "pivotConvention": "axle_center",
        "builder": build_scout_wheel,
    },
    {
        "id": "env.hub.home_garage",
        "name": "Forest Home Garage Bay",
        "category": "building",
        "filename": "home_garage_bay.obj",
        "maxTrianglesLod0": 25000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_home_garage,
    },
    {
        "id": "env.hub.field_camp",
        "name": "Expedition Field Camp Shelter",
        "category": "camp",
        "filename": "field_camp_shelter.obj",
        "maxTrianglesLod0": 15000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_field_camp,
    },
    {
        "id": "env.landmark.watchtower",
        "name": "Forest Lookout Watchtower",
        "category": "landmark",
        "filename": "landmark_watchtower.obj",
        "maxTrianglesLod0": 25000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_landmark_watchtower,
    },
    {
        "id": "env.prop.relay_tower",
        "name": "Regional Radio Relay Mast",
        "category": "landmark",
        "filename": "relay_mast_tower.obj",
        "maxTrianglesLod0": 25000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_relay_tower,
    },
    {
        "id": "env.prop.salvage_cache",
        "name": "Weatherproof Salvage Cache Crate",
        "category": "prop",
        "filename": "salvage_cache_crate.obj",
        "maxTrianglesLod0": 1500,
        "pivotConvention": "ground_datum_y0",
        "builder": build_salvage_cache,
    },
    {
        "id": "env.prop.creek_gauge",
        "name": "Hydrological Creek Depth Gauge",
        "category": "prop",
        "filename": "creek_depth_gauge.obj",
        "maxTrianglesLod0": 1500,
        "pivotConvention": "ground_datum_y0",
        "builder": build_creek_gauge,
    },
    {
        "id": "env.tree.forest_pine",
        "name": "Frontier Conifer Pine Tree",
        "category": "tree",
        "filename": "forest_pine_tree.obj",
        "maxTrianglesLod0": 4000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_forest_pine,
    },
    {
        "id": "env.rock.granite_outcrop",
        "name": "Granite Outcrop Boulder",
        "category": "rock",
        "filename": "granite_boulder_outcrop.obj",
        "maxTrianglesLod0": 2000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_granite_outcrop,
    },
    {
        "id": "veh.mule.m2.chassis",
        "name": "Mule M2 Cargo Hauler Chassis",
        "category": "vehicle",
        "filename": "mule_m2_chassis.obj",
        "maxTrianglesLod0": 45000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_mule_chassis,
    },
    {
        "id": "veh.lynx.l3.chassis",
        "name": "Lynx L3 Crawler Chassis & Roll Cage",
        "category": "vehicle",
        "filename": "lynx_l3_chassis.obj",
        "maxTrianglesLod0": 45000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_lynx_chassis,
    },
    {
        "id": "veh.atlas.a4.chassis",
        "name": "Atlas A4 Expedition Rig Chassis",
        "category": "vehicle",
        "filename": "atlas_a4_chassis.obj",
        "maxTrianglesLod0": 45000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_atlas_chassis,
    },
    {
        "id": "env.landmark.bridge_span",
        "name": "Timber Creek Bridge Span",
        "category": "landmark",
        "filename": "timber_bridge_span.obj",
        "maxTrianglesLod0": 25000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_timber_bridge_span,
    },
    {
        "id": "env.landmark.city_overpass",
        "name": "Ruined City Concrete Overpass",
        "category": "landmark",
        "filename": "ruined_city_overpass.obj",
        "maxTrianglesLod0": 25000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_city_overpass,
    },
    {
        "id": "env.prop.fuel_depot_tank",
        "name": "Garage Fuel Reserve Tank",
        "category": "prop",
        "filename": "fuel_depot_tank.obj",
        "maxTrianglesLod0": 1500,
        "pivotConvention": "ground_datum_y0",
        "builder": build_fuel_depot_tank,
    },
    {
        "id": "env.prop.winch_anchor_bollard",
        "name": "Steel Winch Recovery Bollard",
        "category": "prop",
        "filename": "winch_anchor_bollard.obj",
        "maxTrianglesLod0": 1500,
        "pivotConvention": "ground_datum_y0",
        "builder": build_winch_anchor_bollard,
    },
    {
        "id": "env.prop.trail_cairn_marker",
        "name": "Frontier Stacked Stone Cairn",
        "category": "prop",
        "filename": "trail_cairn_marker.obj",
        "maxTrianglesLod0": 1500,
        "pivotConvention": "ground_datum_y0",
        "builder": build_trail_cairn_marker,
    },
    {
        "id": "env.tree.wetland_cypress",
        "name": "Wetlands Buttressed Cypress Tree",
        "category": "tree",
        "filename": "wetland_cypress_tree.obj",
        "maxTrianglesLod0": 4000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_wetland_cypress,
    },
    {
        "id": "env.rock.canyon_hoodoo",
        "name": "Red Canyon Sandstone Hoodoo Spire",
        "category": "rock",
        "filename": "canyon_sandstone_hoodoo.obj",
        "maxTrianglesLod0": 2000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_canyon_hoodoo,
    },
    {
        "id": "veh.mule.m2.wheel",
        "name": "Mule M2 Heavy Cargo Wheel",
        "category": "vehicle_wheel",
        "filename": "mule_m2_wheel.obj",
        "maxTrianglesLod0": 4500,
        "pivotConvention": "axle_center",
        "builder": build_mule_wheel,
    },
    {
        "id": "veh.lynx.l3.wheel",
        "name": "Lynx L3 Beadlock Crawler Wheel",
        "category": "vehicle_wheel",
        "filename": "lynx_l3_wheel.obj",
        "maxTrianglesLod0": 4500,
        "pivotConvention": "axle_center",
        "builder": build_lynx_wheel,
    },
    {
        "id": "veh.atlas.a4.wheel",
        "name": "Atlas A4 Expedition Planetary Wheel",
        "category": "vehicle_wheel",
        "filename": "atlas_a4_wheel.obj",
        "maxTrianglesLod0": 4500,
        "pivotConvention": "axle_center",
        "builder": build_atlas_wheel,
    },
    {
        "id": "env.hub.outpost_garage",
        "name": "Regional Outpost Garage & Dock",
        "category": "building",
        "filename": "outpost_garage_bay.obj",
        "maxTrianglesLod0": 25000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_outpost_garage,
    },
    {
        "id": "env.landmark.highland_observatory",
        "name": "Highland Weather Observatory Dome",
        "category": "landmark",
        "filename": "highland_weather_dome.obj",
        "maxTrianglesLod0": 25000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_highland_observatory,
    },
    {
        "id": "env.landmark.wetland_pump_station",
        "name": "Wetlands Sluice & Pump Station",
        "category": "landmark",
        "filename": "wetland_pump_station.obj",
        "maxTrianglesLod0": 25000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_wetland_pump_station,
    },
    {
        "id": "env.prop.solar_beacon_pod",
        "name": "Autonomous Solar Telemetry Pod",
        "category": "prop",
        "filename": "solar_telemetry_pod.obj",
        "maxTrianglesLod0": 1500,
        "pivotConvention": "ground_datum_y0",
        "builder": build_solar_beacon_pod,
    },
    {
        "id": "env.prop.cargo_pallet_stack",
        "name": "Strapped Logistics Cargo Pallet",
        "category": "prop",
        "filename": "cargo_pallet_stack.obj",
        "maxTrianglesLod0": 1500,
        "pivotConvention": "ground_datum_y0",
        "builder": build_cargo_pallet_stack,
    },
    {
        "id": "env.tree.highland_larch",
        "name": "Windswept Subalpine Highland Larch",
        "category": "tree",
        "filename": "highland_windswept_larch.obj",
        "maxTrianglesLod0": 4000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_highland_larch,
    },
    {
        "id": "env.rock.basalt_column_cluster",
        "name": "Hexagonal Basalt Column Cluster",
        "category": "rock",
        "filename": "highland_basalt_columns.obj",
        "maxTrianglesLod0": 2000,
        "pivotConvention": "ground_datum_y0",
        "builder": build_basalt_column_cluster,
    },
]


def main():
    os.makedirs(STREAMING_MODELS_DIR, exist_ok=True)
    manifest_rows = []

    for spec in ASSET_SPECS:
        mesh = spec["builder"]()
        obj_text = mesh.to_obj()
        obj_bytes = obj_text.encode("utf-8")
        sha256_hex = hashlib.sha256(obj_bytes).hexdigest()
        bmin, bmax = mesh.bounds()

        out_path = os.path.join(STREAMING_MODELS_DIR, spec["filename"])
        with open(out_path, "wb") as f:
            f.write(obj_bytes)

        manifest_rows.append({
            "id": spec["id"],
            "name": spec["name"],
            "category": spec["category"],
            "relativePath": f"Models/{spec['filename']}",
            "format": "obj",
            "required": True,
            "pivotConvention": spec["pivotConvention"],
            "vertexCount": len(mesh.vertices),
            "uvCount": len(mesh.uvs),
            "normalCount": len(mesh.normals),
            "triangleCount": len(mesh.faces),
            "maxTrianglesLod0": spec["maxTrianglesLod0"],
            "boundsMin": bmin,
            "boundsMax": bmax,
            "sha256": sha256_hex,
            "license": "Project-owned generated",
            "generator": "Tools/Art/generate_streaming_assets.py",
        })

    manifest_json = json.dumps(manifest_rows, indent=2) + "\n"
    with open(MANIFEST_PATH, "wb") as f:
        f.write(manifest_json.encode("utf-8"))

    print(f"Generated {len(manifest_rows)} 3D streaming assets in {STREAMING_MODELS_DIR}")
    for row in manifest_rows:
        print(f"  {row['id']:28s} -> {row['relativePath']:32s} "
              f"tris={row['triangleCount']:3d}/{row['maxTrianglesLod0']:5d} sha256={row['sha256'][:12]}…")


if __name__ == "__main__":
    main()
