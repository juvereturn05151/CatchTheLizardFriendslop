# Catch the Lizard — Playable Prototype

## Run in Unity

1. Open `Assets/CatchTheLizard/Scenes/PrototypeRoom.unity`.
2. Press Play.
3. Choose **HOST GAME**.

The generated scene and prefab can be rebuilt at any time from **Catch the Lizard > Rebuild Playable Prototype**. A Windows player can be rebuilt from **Catch the Lizard > Build Windows Test Player**.

## Test two players

1. Start the Editor in Play mode and choose **HOST GAME**.
2. Run `Builds/WindowsRelease/CatchTheLizard.exe`.
3. On the same PC, leave `127.0.0.1`; on another PC, enter the host PC's LAN IPv4 address.
4. Choose **JOIN GAME**.

Both machines must allow UDP port `7777` through their local firewall.

## Controls

- `WASD`: move
- Mouse: look
- Left Shift: sprint
- Left Ctrl or `C`: crouch
- `E`: pick up an item into the first free hand
- `Q`: drop left-hand item
- `R`: drop right-hand item
- Left mouse: use left hand
- Right mouse: use right hand
- Escape: release/capture cursor

## Loop

Use the broom to herd the lizard, hold spray on it until it is stunned, use the container close to the stunned lizard, then carry the occupied container through the green exit area outside the doorway.
