# CustomResolution
### Basic plugin that allows you to specify a custom resolution.
Enforces a custom resolution for the game, similar to NVIDIA DSR.

Mirrored at https://gitlab.com/0x0ade/dp-customresolution in case you want to submit a merge request.
Please do ping me on Discord though (I'm in the Dalamud server) as some notifs might fly under my radar.

# Troubleshooting

<details><summary>My multi-monitor windows aren't working correctly!</summary>
Similarly to some other plugins (f.e. Umbra), multi-monitor mode in Dalamud might not work correctly.
In the case of cres, fixing this would be a huge undertaking to make ImGui handle the cursor inside and outside
the main game window differently, and to also handle window positioning correctly (spoiler: there is not one way
to handle it correctly - things are already shifting around when just resizing the game normally).
</details>

<details><summary>The game UI is really really small! I don't want to scale all my HUD manually!</summary>
Good news: You don't need to! XIV itself has got a "High Resolution UI Settings" scaling option in the system
configuration window, in the very first category ("Display Settings"), right under the resolution options.

Bad news: We're currently limited to the very options that XIV gives us. Perhaps a deeper dive into how the game
renders the HUD and decoupling that from the actual game render resolution could help, but it's on the same
scale of difficulty as fixing multi-monitor windows.
</details>

<details><summary>I'm scaling by 3x or higher, but I'm still getting jagged edges!</summary>
The plugin currently only gives FFXIV a fake resolution to work with internally, but it doesn't ensure that the
extra pixels land on your screen - those will still be used for in-game or ReShade screenshots though.

Windows should perform basic filtering with 2x scaling (it might not work on some graphics cards?), and I haven't
heard back from anyone using this on Linux yet. But an universal fix for this is possible - it just requires more
time than I have on my hands right now.
</details>

<details><summary>I'm using DLSS, and my game looks pixelated / is small!</summary>
DLSS in FFXIV is bugged. No, I'm not joking: even without any plugins or mods loaded, it can glitch out when
simply resizing the window (most notably by rapidly pressing WinKey + Down and WinKey + Up).

This issue doesn't happen with FSR and non-DLSS dynamic resolution.
</details>
