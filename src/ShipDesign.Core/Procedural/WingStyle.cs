namespace ShipDesign.Core.Procedural;

public enum WingStyle
{
    None,
    Swept,
    Delta,
    TwinFin,

    /// <summary>
    /// Four arms in an X, two rising outboard and two falling. Unlike every other style here the
    /// wings are not a horizontal plane, which is the whole point: the silhouette from ahead is a
    /// cross rather than a line, and that is what reads as a snubfighter rather than an aircraft.
    /// </summary>
    Cross,
}
