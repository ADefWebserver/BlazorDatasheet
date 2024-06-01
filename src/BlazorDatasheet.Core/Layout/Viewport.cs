﻿using BlazorDatasheet.DataStructures.Geometry;

namespace BlazorDatasheet.Core.Layout;

/// <summary>
/// Provides information about the current "View" of the datasheet.
/// </summary>
public class Viewport
{
    /// <summary>
    /// The visible region, including the cells that are shown off-screen for performance improvements.
    /// </summary>
    public Region VisibleRegion { get; set; } = new(-1, -1, -1, -1);

    /// <summary>
    /// The left (x) position of the first cell in the visual region.
    /// </summary>
    public double Left { get; set; }

    /// <summary>
    /// The top (y) position of the first cell in the visual region
    /// </summary>
    public double Top { get; set; }

    /// <summary>
    /// The distance from the right edge of the viewport to the end of the sheet
    /// </summary>
    public double DistanceRight { get; set; }

    /// <summary>
    /// The distance from the bottom edge of the viewport to the end of the sheet.
    /// </summary>
    public double DistanceBottom { get; set; }

    /// <summary>
    /// The width (in px) of the rendered area - includes overflow
    /// </summary>
    public double VisibleWidth { get; set; }

    /// <summary>
    /// The height (in px) of the rendered area - includes overflow
    /// </summary>
    public double VisibleHeight { get; set; }
}