using UnityEngine;

// The NBA half court's measurements in one place, plus the rule questions
// that depend on them (is this spot a 3? in the paint? in bounds?).
// SandboxSceneBuilder places the rim and draws the lines from these same
// numbers, so what's drawn and what the rules say can't drift apart.
//
// Layout: 1 unit = 1 meter. The half-court line is at z = 0 and the baseline
// at z = HalfCourtLength; x = 0 runs down the middle of the court.
// Questions take a point on the floor; its height is ignored.
public static class CourtGeometry
{
    // Outer size. The outside edges of the baseline, sidelines and half-court
    // line are the boundary, so the lines are drawn inside this box.
    public const float CourtWidth = 15.24f;            // 50 ft
    public const float HalfCourtLength = 14.325f;      // 47 ft, half of 28.65 m
    public const float LineWidth = 0.05f;              // 2 in

    // Backboard and rim, measured from the baseline toward half court.
    public const float BaselineToBoard = 1.22f;        // backboard face is 4 ft inside the baseline
    public const float RimToBoardGap = 0.15f;          // board face to the rim's nearest inner edge
    public const float RimInnerRadius = 0.2285f;       // 0.457 m inner diameter

    // Markings. All distances in meters (feet in the comments).
    public const float ThreePointRadius = 7.24f;       // 23 ft 9 in from the basket center, to the line's outer edge
    public const float ThreePointCornerX = 6.71f;      // 22 ft from the basket center in the corners (3 ft in from the sideline)
    public const float LaneWidth = 4.88f;              // 16 ft, outside edge to outside edge
    public const float FreeThrowToBoard = 4.57f;       // 15 ft from the board face
    public const float FreeThrowCircleRadius = 1.83f;  // 6 ft
    public const float RestrictedAreaRadius = 1.22f;   // 4 ft from the basket center
    public const float CenterCircleRadius = 1.83f;     // 6 ft
    public const float LaneMarkLength = 0.15f;         // 6 in out from the lane line
    public const float BlockWidth = 0.3f;              // the block is 1 ft along the lane line
    public const float HashMarkFromBaseline = 8.53f;   // 28 ft
    public const float HashMarkLength = 0.91f;         // 3 ft

    // Where the small marks sit along each lane line, from the baseline (7, 11 and 14 ft).
    // The block sits at 8 ft. Each distance is to the mark's edge nearest the baseline.
    public static readonly float[] LaneMarksFromBaseline = { 2.13f, 3.35f, 4.27f };
    public const float BlockFromBaseline = 2.44f;

    public static float BaselineZ => HalfCourtLength;
    public static float BoardFaceZ => HalfCourtLength - BaselineToBoard;
    public static float FreeThrowLineZ => BoardFaceZ - FreeThrowToBoard;

    // The basket center: the middle of the rim, straight down on the floor.
    // About 1.60 m (5 ft 3 in) in from the baseline.
    public static float BasketZ => BoardFaceZ - RimToBoardGap - RimInnerRadius;
    public static Vector3 BasketCenter => new Vector3(0f, 0f, BasketZ);

    // How far toward half court (from the basket center) the straight corner
    // lines meet the arc: where a circle of the 3-point radius reaches x = 22 ft.
    // About 2.72 m, so the corners run 4.3 m (14 ft) out from the baseline.
    public static float ThreePointCornerDepth =>
        Mathf.Sqrt(ThreePointRadius * ThreePointRadius - ThreePointCornerX * ThreePointCornerX);

    // True when the point is outside the 3-point line. A point on the line is
    // a 2 (the radius is measured to the line's outer edge), corners included.
    public static bool IsBeyondThreePointLine(Vector3 floorPoint)
    {
        float x = floorPoint.x;
        float towardHalfCourt = BasketZ - floorPoint.z;

        // Between the baseline and where the corners meet the arc, the line is straight.
        if (towardHalfCourt < ThreePointCornerDepth) return Mathf.Abs(x) > ThreePointCornerX;

        // Beyond that it's the arc around the basket center.
        return x * x + towardHalfCourt * towardHalfCourt > ThreePointRadius * ThreePointRadius;
    }

    // True inside the lane, lines included: from the baseline to the far
    // edge of the free-throw line, 16 ft wide.
    public static bool IsInPaint(Vector3 floorPoint)
    {
        float freeThrowFarEdge = FreeThrowLineZ - LineWidth / 2f;
        return Mathf.Abs(floorPoint.x) <= LaneWidth / 2f
            && floorPoint.z >= freeThrowFarEdge
            && floorPoint.z <= BaselineZ;
    }

    // True inside the half court, up to the outside edges of the boundary lines.
    public static bool IsInBounds(Vector3 floorPoint)
    {
        return Mathf.Abs(floorPoint.x) <= CourtWidth / 2f
            && floorPoint.z >= 0f
            && floorPoint.z <= BaselineZ;
    }
}
