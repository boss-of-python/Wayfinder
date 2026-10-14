namespace Wayfinder.Core
{
    /// <summary>
    /// Gameplay surface labels (§4.4). One shared enum used by the physics surface table
    /// (<c>Wayfinder.Simulation.SurfaceTable</c>), the world route graph / baked surface maps
    /// (<c>Wayfinder.World</c>) and scene labels. Lives in Core so that World does not have to
    /// depend on Simulation (asmdef graph: World → Core, Simulation → Core).
    /// Values are persisted in baked chunk surface maps — never renumber.
    /// </summary>
    public enum SurfaceType
    {
        IntactAsphalt = 0,
        BrokenAsphalt = 1,
        PackedDirt = 2,
        Gravel = 3,
        Grass = 4,
        Rock = 5,
        ShallowMud = 6,
        DeepMud = 7,
        Sand = 8,
        PackedSnow = 9,
        Ice = 10
    }
}
