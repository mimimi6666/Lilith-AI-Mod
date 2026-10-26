# Lilith game integration

This is the Beta.4 BepInEx IL2CPP plugin for the Lilith game. It provides the
game-side conversation bridge, voice and dialogue presentation, character
interaction, and the Old Maid and chess minigames. AI service requests are
handled through the local Runtime and Hermes integration.

Default shortcuts are F6 for voice input, F7 for text input, F10 for conversation
history and F8 to stop; players can change them in Control Center. Match events
and the latest position can accompany ordinary and proactive conversation.

This project is not a standalone application. Building requires matching game
interop and BepInEx assemblies supplied by the developer's own game installation.
See the public [build guide](../../BUILDING.md).
