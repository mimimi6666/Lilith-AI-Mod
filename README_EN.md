# Lilith AI MOD 0.1.0

This is an unofficial community MOD for the desktop companion game *The NOexistenceN of Lilith*. It adds AI conversations, text and voice input, Chinese and Japanese voice output, voice lines for previously unvoiced dialogue, weather features, and reviewed local computer controls.

## One-Click Installation

1. Run `LilithAI-Mod-Setup.exe`.
2. The installer will automatically locate the game using Steam registry information and all Steam Library folders. If the game cannot be found, click **Browse** and select the folder containing `Lilith.exe`.
3. Keep the required components selected, then click **Install / Update**.
4. The first installation of AI dynamic voice generation will download a separate inference environment. This may take several minutes. It will not use or modify any Python installation already present on the player’s computer.
5. After launching the game, right-click the Lilith icon in the lower-left corner, select **Add API Key** and a model provider, then enter the player’s own API key.

The installer does not include the author’s API key, chat history, player name, logs, screenshots, training datasets, or development computer paths.

Updates will not overwrite existing API keys, chat memory, key bindings, or player settings.

## Controls

* Default `F7`: Open the text input bubble.
* Hold default `F6`: Record voice input. Release the key to transcribe and send it.
* Both keys can be reassigned in the game settings.
* Game voice output can be switched between Chinese and Japanese. The selected language will be remembered the next time the game is launched.
* **Advanced Computer Controls** are disabled by default. Only after the player enables this option may the AI use approved whitelist functions such as launching applications, media controls, window controls, screenshots, timers, locking the computer, and sleep mode.

## Hardware and Network Requirements

* Core MOD: Windows 10/11 x64, with no additional hardware requirements.
* AI conversations and speech recognition: Require an internet connection and the player’s own API key.
* AI dynamic voice generation: An NVIDIA graphics card is recommended, preferably with at least 8 GB of VRAM. If no compatible NVIDIA graphics card is available, the CPU version will be installed instead. At least 16 GB of RAM is recommended, and voice generation will be slower.
* The complete supplementary voice-line package is approximately 301 MB.
* The dynamic voice model package is approximately 1.98 GB. Additional disk space is required during installation to create the inference environment.

## Privacy

* Conversation text and speech-recognition recordings are sent to the AI provider selected by the player and are subject to that provider’s terms of service and privacy policy.
* GPT-SoVITS voice synthesis runs locally on `127.0.0.1` and does not accept external network connections.
* Automatic weather location uses the approximate city location derived from the player’s public IP address. The MOD does not store the IP address.
* Screenshots are stored only in the player’s `Pictures\Lilith Screenshots` folder and are not automatically uploaded to any model.
* The MOD does not provide access to unrestricted PowerShell or CMD commands, file deletion, password retrieval, or clipboard contents.

## Updating, Repairing, and Removing

* Run the same installer or a newer version again and select **Install / Update** to repair missing files or upgrade the MOD.
* **Remove MOD** only removes files managed by this MOD. API keys, chat memory, and settings are preserved by default.
* To completely remove personal data, manually delete the following files after uninstalling:

  * `BepInEx\config\community.lilith.textinjector.cfg`
  * `BepInEx\data\LilithTextInjector\memory.json`
  * `BepInEx\data\LilithTextInjector\ai-note-state.json`

## Troubleshooting

* MOD log: `BepInEx\LogOutput.log`
* Voice host log: `BepInEx\data\LilithTextInjector\voice-runtime\logs\voice-host.log`
* Voice installation log: `BepInEx\data\LilithTextInjector\voice-runtime\voice-runtime-install.log`
* If the MOD stops loading after a Steam game update, run the installer again first. If the problem continues, include the relevant logs when reporting the issue.

## Distribution Notice

## Unofficial AI Voice MOD Disclaimer

This MOD is an unofficial, non-commercial game modification independently created by a player. It is provided solely for game-related community use and personal entertainment.

This MOD is not official game content and is not affiliated with, operated by, authorized by, sponsored by, endorsed by, or otherwise associated with the game’s developer, publisher, character rights holders, sound recording producers, original voice actors, Google, OpenAI, DeepSeek, BepInEx, GPT-SoVITS, or any other related technology, software, or service provider.

This MOD does not represent the views or positions of any of the individuals, organizations, or companies listed above.

The supplementary dialogue and additional voice content included in this MOD were generated or synthesized using artificial intelligence technology and were not newly recorded by the original voice actors.

Do not misrepresent, edit, redistribute, or promote such content as statements made by the original voice actors, official recordings, official additional content, or any form of officially authorized work.

The supplementary dialogue audio, voice models, and generated results used by this MOD may involve derivative use of original game audio, character voice characteristics, performance features, or other related materials.

This disclaimer is provided only to explain the source, production method, and unofficial nature of the content. It does not grant any license or authorization and does not indicate that any relevant rights holder has approved, endorsed, or permitted such use.

All rights relating to the game title, character names, character designs, original dialogue, audio, performances, and other related materials belong to their respective lawful rights holders.

The creator of this MOD does not claim ownership of any copyright, trademark, performer’s rights, sound recording producer’s rights, or other proprietary rights relating to the original work.

This MOD is provided free of charge. It may not be sold, redistributed for payment, distributed behind a paywall, or used for commercial advertising, voice endorsements, political promotion, fraud, impersonation, insults, defamation, adult content, or any other purpose that may harm the lawful rights and interests of the original voice actors or other relevant rights holders.

Without permission, users may not extract, resell, retrain, or redistribute any AI-generated voice content or voice models included in this MOD.

Such content must not be used to impersonate the original voice actors or to create material that may cause public confusion, misunderstanding, or misrepresentation.

Users are responsible for complying with the laws and regulations applicable in their jurisdiction, as well as the game’s user agreement, MOD policies, and the rules of the distribution platform.

If a relevant rights holder believes that this MOD affects or infringes upon their rights or interests, they may contact the creator through the following channel:

Contact: **[mimimi5206666@gmail.com]**

Upon receiving a specific and verifiable rights-related notice, the creator will promptly review the matter and, where appropriate, suspend distribution, remove, or modify the relevant content.

Publisher: MIMI
Version: 0.1.0-RC1
Release Date: July 18, 2026
