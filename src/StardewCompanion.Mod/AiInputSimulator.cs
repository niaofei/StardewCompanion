using StardewValley.Util;

namespace StardewCompanion.Mod;

/// <summary>Uses the game's own input seam. Every frame replaces physical controls and releases unused directions.</summary>
internal sealed class AiInputSimulator : IInputSimulator
{
    public string Direction { get; set; } = "";
    public bool Tool { get; set; }
    public bool Interact { get; set; }
    public bool ManualControl { get; set; }
    private string _previousDirection = "";
    private bool _previousTool;
    private bool _previousInteract;

    public void Clear() { Direction = ""; Tool = false; Interact = false; }

    public void SimulateInput(ref bool actionButtonPressed, ref bool switchToolButtonPressed,
        ref bool useToolButtonPressed, ref bool useToolButtonReleased, ref bool addItemToInventoryButtonPressed,
        ref bool cancelButtonPressed, ref bool moveUpPressed, ref bool moveRightPressed, ref bool moveLeftPressed,
        ref bool moveDownPressed, ref bool moveUpReleased, ref bool moveRightReleased, ref bool moveLeftReleased,
        ref bool moveDownReleased, ref bool moveUpHeld, ref bool moveRightHeld, ref bool moveLeftHeld, ref bool moveDownHeld)
    {
        if(ManualControl){
            moveUpReleased|=_previousDirection=="up"&&!moveUpHeld;
            moveRightReleased|=_previousDirection=="right"&&!moveRightHeld;
            moveLeftReleased|=_previousDirection=="left"&&!moveLeftHeld;
            moveDownReleased|=_previousDirection=="down"&&!moveDownHeld;
            useToolButtonReleased|=_previousTool&&!useToolButtonPressed;
            _previousDirection="";_previousTool=false;_previousInteract=false;
            return;
        }
        actionButtonPressed = Interact && !_previousInteract;
        useToolButtonPressed = Tool && !_previousTool;
        useToolButtonReleased = !Tool && _previousTool;
        switchToolButtonPressed = addItemToInventoryButtonPressed = cancelButtonPressed = false;
        moveUpHeld = Direction == "up";
        moveRightHeld = Direction == "right";
        moveLeftHeld = Direction == "left";
        moveDownHeld = Direction == "down";
        moveUpPressed = moveUpHeld && _previousDirection != "up";
        moveRightPressed = moveRightHeld && _previousDirection != "right";
        moveLeftPressed = moveLeftHeld && _previousDirection != "left";
        moveDownPressed = moveDownHeld && _previousDirection != "down";
        moveUpReleased = !moveUpHeld;
        moveRightReleased = !moveRightHeld;
        moveLeftReleased = !moveLeftHeld;
        moveDownReleased = !moveDownHeld;
        _previousDirection = Direction;
        _previousTool = Tool;
        _previousInteract = Interact;
    }
}
