using UnityEngine;
using UnityEngine.InputSystem;

// Reads the player's controls from the project's Input Actions asset
// (Assets/Settings/BasketballControls) and exposes plain values that
// gameplay scripts can use. Gameplay code never asks about specific buttons,
// only about actions like "Move" and "Sprint".
public class PlayerInputReader : MonoBehaviour
{
    [Tooltip("Which action map to read: Offense or Defense.")]
    [SerializeField] private string _actionMapName = "Offense";

    private InputAction _moveAction;
    private InputAction _sprintAction;

    // Left stick (or WASD), length 0..1. A light push gives a short vector.
    public Vector2 Move { get; private set; }

    // How hard R2 (or Left Shift) is pressed, 0..1.
    public float Sprint { get; private set; }

    private void OnEnable()
    {
        // InputSystem.actions is the project-wide actions asset set in
        // Project Settings > Input System Package.
        InputActionMap map = InputSystem.actions.FindActionMap(_actionMapName, throwIfNotFound: true);
        _moveAction = map.FindAction("Move", throwIfNotFound: true);
        _sprintAction = map.FindAction("Sprint", throwIfNotFound: true);
        map.Enable();
    }

    // Input is read every rendered frame in Update. Physics scripts pick up
    // the latest values in FixedUpdate.
    private void Update()
    {
        // Diagonal keyboard input (W + D) would otherwise be longer than 1.
        Move = Vector2.ClampMagnitude(_moveAction.ReadValue<Vector2>(), 1f);
        Sprint = _sprintAction.ReadValue<float>();
    }
}
