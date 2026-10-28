using UnityEngine;

// Test tool for the court lines, not gameplay. Watches where the player's feet
// are and logs whenever they cross a line that matters for the rules:
//   2 <-> 3       a 3 needs both feet outside the 3-point line (a foot on it is a 2)
//   paint         either foot in the lane, lines included
//   in bounds     both feet inside the outside edge of the boundary lines
// Walk along and across the lines and check the log switches right at the paint.
public class CourtLineProbe : MonoBehaviour
{
    [SerializeField] private PlayerBody _body;
    [Tooltip("Turn the logging off without removing the component.")]
    [SerializeField] private bool _log = true;

    private bool _hasState;
    private bool _wasThree;
    private bool _wasInPaint;
    private bool _wasInBounds;

    private void Update()
    {
        if (_body == null || !_log) return;

        Vector3 left = _body.GetFootOnFloor(-1);
        Vector3 right = _body.GetFootOnFloor(1);
        bool three = CourtGeometry.IsBeyondThreePointLine(left) && CourtGeometry.IsBeyondThreePointLine(right);
        bool inPaint = CourtGeometry.IsInPaint(left) || CourtGeometry.IsInPaint(right);
        bool inBounds = CourtGeometry.IsInBounds(left) && CourtGeometry.IsInBounds(right);

        if (_hasState && three == _wasThree && inPaint == _wasInPaint && inBounds == _wasInBounds) return;

        Debug.Log($"[Court] {(three ? "3" : "2")}, {(inPaint ? "in the paint" : "outside the paint")}, " +
            $"{(inBounds ? "in bounds" : "OUT OF BOUNDS")}. Feet from basket center: " +
            $"left {Describe(left)}, right {Describe(right)}");

        _hasState = true;
        _wasThree = three;
        _wasInPaint = inPaint;
        _wasInBounds = inBounds;
    }

    // Distance from the basket center along the floor, plus the position, so a
    // switch can be checked against the line (7.24 m on the arc, x = 6.71 m in the corners).
    private static string Describe(Vector3 foot)
    {
        float distance = Vector3.Distance(foot, CourtGeometry.BasketCenter);
        return $"{distance:0.00} m (x {foot.x:0.00}, z {foot.z:0.00})";
    }
}
