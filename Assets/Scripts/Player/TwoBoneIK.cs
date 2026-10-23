using UnityEngine;

// Two-bone IK ("inverse kinematics"): we know where a limb starts (a shoulder
// or a hip), how long its two bones are (upper arm and forearm, or thigh and
// shin), and where we want its end to be (a wrist or an ankle). This works out
// where the joint in the middle (the elbow or knee) has to go.
//
// The three points form a triangle whose three side lengths we know:
//   upper bone, lower bone, and the distance from the root to the target.
// The law of cosines gives the angle at the root, and a "hint" direction
// picks which way the middle joint bends (elbows out and back, knees forward).
public static class TwoBoneIK
{
    // root   = shoulder or hip
    // target = where the wrist or ankle should go
    // hint   = a direction the elbow or knee should point toward
    // middle = the elbow or knee (output)
    // end    = the wrist or ankle (output). It's the target, unless the target is
    //          out of reach: too far (the limb points straight at it, fully
    //          stretched) or too close (fully folded).
    public static void Solve(Vector3 root, Vector3 target, Vector3 hint, float upperLength, float lowerLength,
        out Vector3 middle, out Vector3 end)
    {
        Vector3 toTarget = target - root;
        float distance = toTarget.magnitude;
        Vector3 direction = distance > 0.0001f ? toTarget / distance : Vector3.down;

        // A limb can't reach farther than both bones end to end, or closer than
        // their difference (fully folded). A hair short of full length keeps the
        // math below from dividing by zero.
        float maxReach = (upperLength + lowerLength) * 0.9999f;
        float minReach = Mathf.Abs(upperLength - lowerLength) + 0.001f;
        distance = Mathf.Clamp(distance, minReach, maxReach);
        end = root + direction * distance;

        // Law of cosines, for the angle at the root between the line to the
        // target and the upper bone:
        //   lower^2 = upper^2 + distance^2 - 2 * upper * distance * cos(angle)
        float cosAngle = (upperLength * upperLength + distance * distance - lowerLength * lowerLength)
            / (2f * upperLength * distance);
        float angle = Mathf.Acos(Mathf.Clamp(cosAngle, -1f, 1f));

        // The bend direction is the hint with its part along the target line
        // removed, so it's square to the limb. If the hint points straight along
        // the limb, any square direction will do.
        Vector3 bend = Vector3.ProjectOnPlane(hint, direction);
        if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(Vector3.forward, direction);
        if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(Vector3.right, direction);
        bend.Normalize();

        // Turn away from the target line by that angle, toward the bend direction.
        middle = root + (direction * Mathf.Cos(angle) + bend * Mathf.Sin(angle)) * upperLength;
    }
}
