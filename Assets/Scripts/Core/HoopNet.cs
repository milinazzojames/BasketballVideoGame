using UnityEngine;

// The net under the rim. Instead of simulating every strand, a trigger volume
// the shape of the net slows down anything passing through it and nudges it
// toward the middle, the way a real net catches the ball and drops it
// straight down. Needs a trigger collider on the same GameObject.
public class HoopNet : MonoBehaviour
{
    [Tooltip("How strongly the net slows vertical speed. Higher = the ball drops out of the net slower.")]
    [SerializeField] private float _verticalDrag = 6f;
    [Tooltip("How strongly the net slows sideways speed. Higher = the ball comes out straighter.")]
    [SerializeField] private float _horizontalDrag = 12f;
    [Tooltip("How strongly the net pulls the ball toward its center line, in (m/s per second) per meter off-center.")]
    [SerializeField] private float _centering = 20f;

    // Runs every physics step while a Rigidbody is inside the net's trigger.
    private void OnTriggerStay(Collider other)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body == null || body.isKinematic) return;

        float dt = Time.fixedDeltaTime;
        Vector3 velocity = body.linearVelocity;

        // Exp(-drag * time) shrinks the speed by the same fraction each second,
        // no matter how many physics steps happen in that second.
        float vertical = velocity.y * Mathf.Exp(-_verticalDrag * dt);
        Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z) * Mathf.Exp(-_horizontalDrag * dt);

        Vector3 toCenter = transform.position - body.position;
        toCenter.y = 0f;
        horizontal += toCenter * (_centering * dt);

        body.linearVelocity = new Vector3(horizontal.x, vertical, horizontal.z);
    }
}
