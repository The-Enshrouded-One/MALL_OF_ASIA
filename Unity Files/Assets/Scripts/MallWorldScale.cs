/// <summary>
/// Shared physical scale for the mall floor maps and their pixel-based data (3000 x 1385 px plans).
/// The original reconstruction mapped the plan onto a 79 m footprint, which made corridors only
/// 1-3 m wide next to 5 m tall floors. The footprint is now scaled 4x horizontally (about 318 m x
/// 147 m, comparable to the real building) while storey heights stay at 5 m.
/// </summary>
public static class MallWorldScale
{
    public const float WorldUnitsPerSourceUnit = 100f;
    /// <summary>Plan pixel size in the original reconstruction (before the horizontal scale).</summary>
    public const float SourceUnitsPerPixel = 0.02645833f;
    /// <summary>Horizontal (x/z) enlargement of the mall footprint; heights are unchanged.</summary>
    public const float HorizontalScale = 4f;
    public const float WorldUnitsPerPixel = SourceUnitsPerPixel * HorizontalScale;
    public const float FloorHeight = 5f;
    public const float PlanWidthPixels = 3000f;
    public const float PlanHeightPixels = 1385f;
}
