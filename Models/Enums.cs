namespace DiceChaosDisplay.Models;

public enum DmToolMode
{
    PanSelect,
    FogBrush,
    FogBox,
    FogPolygon,
    RoomPrep,
    GridAlign3x3,
    Pencil,
    Ping,
    Ruler,
    PlaceAoe,
    PlaceToken,
    PlaceDmPin
}

public enum FogPaintOperation
{
    Reveal,
    Hide
}

public enum GridType
{
    Square,
    HexFlatTop,
    HexPointyTop
}

public enum DiagonalRule
{
    Standard5e_5_5_5,
    Variant_5_10_5,
    Euclidean
}

public enum AoeShape
{
    Sphere,
    Cone,
    Cube,
    Line
}

public enum TokenMarkerKind
{
    ConditionRing,
    NumberedToken
}

public enum TvSyncMode
{
    PhysicalOneInch,
    MirrorDm,
    IndependentTv
}

public enum SceneMediaType
{
    StaticImage,
    UvttBundle,
    VideoMap
}
