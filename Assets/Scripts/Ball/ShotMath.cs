using UnityEngine;

// Math for launching a ball along an arc. Shared by shooting and the ball test tool.
public static class ShotMath
{
    // Finds the launch velocity that, at the chosen angle, makes the ball's
    // path pass through the target. From the trajectory equation
    //   h = d*tan(a) - g*d^2 / (2*v^2*cos^2(a))
    // solved for v:
    //   v^2 = g*d^2 / (2*cos^2(a) * (d*tan(a) - h))
    // where d = flat distance, h = height difference, a = launch angle, g = gravity.
    // Returns false if the angle is too flat to ever reach the target.
    public static bool TryGetLaunchVelocity(Vector3 from, Vector3 to, float launchAngleDegrees, out Vector3 velocity)
    {
        Vector3 flat = to - from;
        float height = flat.y;
        flat.y = 0f;
        float distance = flat.magnitude;

        float angle = launchAngleDegrees * Mathf.Deg2Rad;
        float gravity = -Physics.gravity.y;
        float denominator = 2f * Mathf.Cos(angle) * Mathf.Cos(angle) * (distance * Mathf.Tan(angle) - height);
        if (denominator <= 0f)
        {
            velocity = Vector3.zero;
            return false;
        }

        float speed = Mathf.Sqrt(gravity * distance * distance / denominator);
        Vector3 direction = flat.normalized * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
        velocity = direction * speed;

        // The formula above is for a perfectly smooth curve. Unity moves the ball
        // in small steps (Fixed Timestep), and each step it adds gravity to the
        // speed first and then moves the ball. That makes the ball fly half a
        // step of gravity lower than the smooth curve, which grows to 4-12 cm by
        // the time it reaches the rim: enough to clip the front rim. Adding half
        // a step of gravity back makes Unity's stepped path land on the target.
        velocity -= Physics.gravity * (Time.fixedDeltaTime * 0.5f);
        return true;
    }

    // Backspin: the top of the ball spins back toward the shooter. For a ball
    // flying forward, that's a rotation around the axis pointing to the shooter's left.
    // Returns angular velocity in radians per second.
    public static Vector3 GetBackspin(Vector3 travel, float revolutionsPerSecond)
    {
        travel.y = 0f;
        Vector3 axis = Vector3.Cross(travel.normalized, Vector3.up);
        return axis * (revolutionsPerSecond * 2f * Mathf.PI);
    }
}
