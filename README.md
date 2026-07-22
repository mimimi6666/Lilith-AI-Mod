<div align="center">

# ♡ Lilith AI MOD ♡

### 讓莉莉絲不只待在桌面，也能真正聽見你、記得你、回應你。
### Let Lilith hear you, remember you, and answer in her own voice.

🍓 **Unofficial Community MOD · 非官方社群 MOD** 🍓

[繁體中文說明](README_繁體中文.md) · [简体中文说明](README_简体中文.md) · [日本語ガイド](README_日本語.md) · [English Guide](README_EN.md)

- **[Discord](https://discord.gg/JGAHnxjbj)**

</div>

---

## ✦ 歡迎回來 / Welcome back

這是一款為《The NOexistenceN of Lilith》桌寵製作的非官方 AI 擴充。它保留遊戲原本安靜、帶點哲學氣息的莉莉絲，並讓她能透過文字或麥克風與你自然聊天。

This unofficial AI extension for *The NOexistenceN of Lilith* keeps Lilith's quiet, philosophical charm while letting you talk to her naturally by text or microphone.

> 「今天也要和我說說話嗎？」  
> “Will you stay and talk with me today?”

## ✦ 0.1.1-RC4 更新內容 / What's new in 0.1.1-RC4

- 啟動階段改為分項隔離；單一非必要 Hook 不相容時，其餘 MOD 功能仍可繼續載入，錯誤會寫入 `BepInEx/LogOutput.log` / Startup stages are isolated so one incompatible optional hook no longer prevents the remaining MOD features from loading
- 安裝器會重試暫時性的檔案鎖定與覆寫失敗，顯示實際問題路徑，並在解壓後驗證 BepInEx 與 MOD DLL / The installer retries transient file locks, reports the exact failing path, and verifies the core installation after extraction
- 修復重啟或開啟設定後語音按鈕與實際語音不同步，以及切到日文後無法切回中文的問題；中文／日文選擇現在會正確保存 / Fixed voice-button desynchronization after restart and being unable to switch back to Chinese after selecting Japanese; both language choices now persist correctly
- 本機語音主機只啟動目前選擇的語言服務，切換中文／日文時會乾淨重啟；相容官方 Build `24275097` 的語音切換回呼 / The local voice host starts only the selected language service, restarts cleanly on language changes, and supports the voice callback in official Build `24275097`
- 千問即時語音辨識會使用設定的即時模型，HTTP 備援仍使用一般 ASR 模型 / Qwen realtime speech recognition now uses the configured realtime model while HTTP fallback keeps the regular ASR model
- 已知舊版亂碼預設值會自動遷移，且不覆蓋玩家自行修改的提示詞與設定 / Known legacy mojibake defaults are migrated without overwriting player-customized prompts or settings

## ♡ 她能陪你做什麼？ / What can she do?

| | 繁體中文 | English |
|---|---|---|
| 💬 | 使用文字或按鍵語音和莉莉絲聊天 | Chat through text or push-to-talk voice input |
| 🎙️ | 中文、日文語音輸出，並記住你的選擇 | Chinese and Japanese voice output with saved preferences |
| 🧠 | 保留有限的對話記憶與角色人格 | Keep lightweight conversation memory and character personality |
| 🌦️ | 查詢時間、天氣與網路資訊 | Check time, weather, and current web information |
| 💌 | 在重要對話後，偶爾收到她的信 | Receive occasional letters after meaningful conversations |
| 🖥️ | 經你開啟後，執行白名單內的電腦操作 | Run reviewed, allowlisted computer actions after you enable them |

<div align="center">

<img src="docs/images/settings-and-voice-language.png" alt="Lilith AI MOD settings, key bindings, and Chinese or Japanese voice selection">

<sub>設定、可自訂按鍵與中文／日文語音切換 · Settings, rebindable controls, and Chinese/Japanese voice selection</sub>

</div>

## ✦ AI 服務相容性 / AI provider compatibility

> **目前仍建議優先使用 Gemini；它是 0.1.1-RC4 測試最完整、針對性優化最多的服務。千問已完成文字對話、語音辨識、聯網搜尋與明確本機指令的實際接入測試。OpenAI 與 DeepSeek 仍是實驗性文字相容層。**

> **Gemini remains the recommended and most extensively optimized provider in 0.1.1-RC4. Qwen has been tested for chat, speech recognition, web search, and explicit local commands. OpenAI and DeepSeek remain experimental text-chat compatibility layers.**

| 功能 / Feature | Gemini | Qwen | OpenAI / DeepSeek |
|---|---|---|---|
| 文字對話 / Text chat | ✅ 已測試並完整調整 / Fully tested and tuned | ✅ `qwen3.7-plus` 已測試 / `qwen3.7-plus` tested | 🧪 基本相容，未完成端到端測試 / Basic compatibility only |
| `F6` 語音辨識 / Speech recognition | ✅ Gemini 音訊辨識 | ✅ `qwen3-asr-flash` | ⚠️ 目前仍使用 Gemini 辨識 / Currently uses Gemini transcription |
| 即時聯網搜尋 / Live web search | ✅ Google Search grounding | ✅ 千問原生聯網 / Qwen native web search | ❌ 尚未接入 / Not integrated |
| 電腦工具 / PC tools | ✅ Function calling 與本機可靠路由 | ✅ 明確指令的本機可靠路由 | ⚠️ 僅部分明確本機指令 |

千問的可用性、免費額度與計費要求依玩家的阿里雲模型服務帳戶及地區而定。OpenAI／DeepSeek 的模型名稱、回應格式、額度與地區限制尚未完整驗證。

Qwen availability, free quotas, and billing requirements depend on the player's Alibaba Cloud Model Studio account and region. OpenAI/DeepSeek model names, response formats, quotas, and regional availability have not been fully validated.

本機 GPT-SoVITS 語音合成可朗讀已成功取得的文字回覆，因此 OpenAI／DeepSeek 的文字聊天若正常回傳，仍可能播放合成語音；但這不代表該供應商的整體流程已完成測試。模型名稱、API 規格、地區限制或服務商更新也可能影響實驗性相容層。

Local GPT-SoVITS can speak a successfully returned text reply, so OpenAI/DeepSeek responses may still produce synthesized speech. This does not mean their complete workflows have been validated. Model names, API behavior, regional availability, or provider updates may also affect the experimental compatibility layer.

## 🍓 一鍵安裝 / One-click setup

### 推薦下載 / Recommended download

- **[Google Drive 完整包鏡像 / Full package mirror](https://drive.google.com/file/d/1UxynMsGJrl0nuA5b3JS2YFTsfRVafIG4/view?usp=sharing)**
- **[百度網盤完整包 / Baidu full package mirror](https://pan.baidu.com/s/1oYcX5PYBxKLvvMdi0cE8Uw?pwd=2u2c)** — 提取碼 / Code: `2u2c`

以上皆為 RC4 完整安裝包。壓縮檔解壓密碼為 `I love you, Lilith.`。完整解壓縮後，直接執行資料夾內的 `LilithAI-Mod-Setup.exe`。  
Both links provide the complete RC4 package. The archive password is `I love you, Lilith.`. Extract all files, then run `LilithAI-Mod-Setup.exe` from the extracted folder.

> ⚠️ **請勿使用首頁的 `Code → Download ZIP` 安裝 MOD；那裡只有原始碼，不是可安裝的發布包。**  
> ⚠️ **Do not install the MOD through `Code → Download ZIP`; it contains source code, not the installable release.**

### GitHub Release 手動下載 / Manual GitHub Release download

[GitHub Release](https://github.com/mimimi6666/Lilith-AI-Mod/releases) 的 Assets 會平鋪顯示，但 RC4 安裝器的本機套件必須放在 `packages` 子資料夾。若要手動下載所有附件，請整理成以下結構：

GitHub Release Assets are displayed as a flat list, while the RC4 installer expects local packages inside a `packages` subfolder. If you download the assets manually, arrange them like this:

```text
Lilith-AI-Mod-0.1.1-RC4
├─ LilithAI-Mod-Setup.exe
├─ release-manifest.json
├─ SHA256SUMS.txt
└─ packages
   ├─ core.zip
   ├─ voice-pack.zip
   └─ voice-runtime.zip
```

只下載 EXE 時，安裝器會自動取得 RC4 清單與缺少的套件。未變更的補充語音包沿用 RC1，更新後的語音執行環境則由 RC4 Release 下載。若網路、地區限制或大型檔案下載失敗，請改用上方的 Google Drive 或百度網盤完整包。

If only the EXE is downloaded, the installer retrieves the RC4 manifest and missing packages automatically. The unchanged supplemental voice pack is reused from RC1, while the updated voice runtime is downloaded from the RC4 release. If networking, regional availability, or large-file downloads fail, use the complete Google Drive or Baidu package above.

1. 完整解壓縮或整理好上述資料夾後，再執行安裝程式；它會自動尋找 Steam 遊戲路徑。  
   Extract the complete package or arrange the folders above before running the installer; it automatically searches your Steam libraries.
2. 開啟遊戲後，在左下角莉莉絲圖示按右鍵，選擇 AI 服務並輸入自己的 API Key。  
   Launch the game, right-click Lilith's tray icon, choose an AI provider, and enter your own API key.

<div align="center">

<img src="docs/images/api-provider-menu.png" alt="Choose Gemini, OpenAI, or DeepSeek from the Lilith tray menu">

<sub>從工作列選單加入自己的 API Key · Add your own API key and choose a provider from the tray menu</sub>

</div>

> ✧ 安裝包不包含作者的 API Key、聊天記錄、玩家名稱或私人資料。  
> ✧ The package contains no author API key, chat history, player name, or private data.

## ✦ 預設操作 / Default controls

- `F7`：開啟文字輸入氣泡 / Open the text input bubble
- 按住 `F6`：錄音；放開後辨識並送出 / Hold to record; release to transcribe and send
- 兩個按鍵皆可在遊戲設定中重新綁定 / Both keys can be reassigned in game settings

## 🖥️ 電腦操作 / PC controls

莉莉絲的電腦操作分成兩層。日常、低風險的功能可以直接使用；可能影響目前桌面狀態的功能，必須由玩家在遊戲設定中手動開啟「進階電腦操作」。這個開關**不會授予 Windows 系統管理員權限**。

Lilith's PC controls have two levels. Routine, low-risk actions are available normally. Actions that can affect the current desktop require the player to manually enable **Advanced Computer Controls** in the game settings. This switch **does not grant Windows administrator privileges**.

### ♡ 一般電腦操作 / Standard controls

不需要開啟進階功能：

Available without the advanced toggle:

- 依照常用名稱開啟或切回已安裝的應用程式與遊戲，例如記事本、計算機、瀏覽器、Steam、Spotify、Discord、VALORANT 等；不接受任意路徑或指令  
  Open or focus recognized applications and games by common name, such as Notepad, Calculator, a browser, Steam, Spotify, Discord, or VALORANT; arbitrary paths and commands are not accepted
- 播放／暫停、上一首、下一首、停止、靜音及調整系統音量  
  Play/pause, previous/next track, stop, mute, and system volume controls
- 查看非個人的本機狀態：電池、記憶體、系統磁碟可用空間與網路連線  
  Report non-personal local status: battery, memory, system-drive free space, and network availability

### ✦ 進階電腦操作 / Advanced controls

只有玩家主動開啟設定後才可使用：

Available only after the player explicitly enables the setting:

| 功能 / Feature | 可以做什麼 / What it can do |
|---|---|
| 📁 常用資料夾 / Known folders | 開啟下載、桌面、文件、圖片、音樂、影片、截圖、MOD 資料夾或資源回收筒；不接受任意檔案路徑 / Open Downloads, Desktop, Documents, Pictures, Music, Videos, Screenshots, the MOD folder, or Recycle Bin; arbitrary paths are not accepted |
| 🪟 視窗 / Windows | 顯示桌面、工作檢視、切換上一個視窗、最小化、最大化、還原，以及靠左／靠右排列 / Show Desktop, Task View, switch to the previous window, minimize, maximize, restore, or snap left/right |
| 📷 截圖 / Screenshots | 擷取所有螢幕並只存到 `圖片\Lilith Screenshots`；圖片不會回傳或上傳給模型 / Capture all monitors to `Pictures\Lilith Screenshots`; the image is not returned or uploaded to the model |
| 📋 複製文字 / Copy text | 只把玩家明確指定、非敏感的文字寫入剪貼簿；無法讀取剪貼簿 / Write only player-specified, non-sensitive text to the clipboard; clipboard reading is unavailable |
| 🔎 瀏覽器搜尋 / Browser search | 只有玩家明確要求時，才用預設瀏覽器開啟 Google 搜尋 / Open a Google search in the default browser only when explicitly requested |
| ⌨️ 安全快捷鍵 / Safe shortcuts | 復原、重做、儲存、全選、尋找、重新整理、全螢幕與 Escape；不支援任意按鍵或任意輸入 / Undo, redo, save, select all, find, refresh, fullscreen, and Escape; arbitrary keys and typing are unavailable |
| ⏲️ 計時器 / Timers | 建立或取消最長 24 小時的本機計時器，時間到由莉莉絲提醒 / Create or cancel local timers up to 24 hours, announced by Lilith |
| 🔒 鎖定與睡眠 / Lock & sleep | 只有在玩家明確要求「鎖定電腦」或「讓電腦睡眠」時才執行，並可在等待期間取消 / Run only after an explicit request to lock or sleep the PC, with cancellation available during the pending period |

## 🖤 溫柔也需要邊界 / Gentle, with boundaries

「進階電腦操作」預設關閉。即使開啟，MOD 也只執行經過檢查的白名單功能，不提供檔案刪除、清空資源回收筒、關機／重新啟動、關閉或強制結束程式、任意 PowerShell／CMD、系統管理員提權、密碼／API Key／OTP 讀取、剪貼簿讀取、任意打字或任意快捷鍵。

“Advanced Computer Controls” are disabled by default. When enabled, only reviewed allowlisted actions are available—never file deletion, emptying the Recycle Bin, shutdown/restart, closing or terminating apps, arbitrary PowerShell/CMD, privilege elevation, password/API key/OTP access, clipboard reading, arbitrary typing, or arbitrary shortcuts.

AI 對話與語音辨識會依玩家選擇傳送至相應服務商；GPT-SoVITS 語音合成則在本機 `127.0.0.1` 運行。

AI chat and speech recognition are sent to the provider selected by the player. GPT-SoVITS voice synthesis runs locally on `127.0.0.1`.

## ✧ 系統需求 / Requirements

- Windows 10/11 x64
- AI 對話與語音辨識需要網路及玩家自己的 API Key  
  AI chat and speech recognition require internet access and the player's own API key
- 動態語音建議 NVIDIA GPU 8 GB VRAM 與 16 GB RAM；沒有相容顯示卡時可使用較慢的 CPU 模式  
  An NVIDIA GPU with 8 GB VRAM and 16 GB RAM is recommended; a slower CPU mode is available

## ♡ 完整說明 / Full guides

- [繁體中文](README_繁體中文.md)
- [简体中文](README_简体中文.md)
- [日本語](README_日本語.md)
- [English](README_EN.md)
- [Third-party licenses](THIRD_PARTY_NOTICES.md)

## ⚠ 非官方聲明 / Unofficial project notice

本 MOD 是玩家獨立製作、免費且非商業的社群作品，未獲遊戲開發商、發行商、角色權利人或原配音員授權、認可或贊助。遊戲、角色、美術、原始台詞與錄音的相關權利均屬其合法權利人。請勿將 AI 生成內容冒充官方內容或原配音員的新錄音。

This is a free, non-commercial fan project. It is not authorized, endorsed, sponsored by, or affiliated with the developer, publisher, character rights holders, or original voice actors. Rights to the game, characters, artwork, original dialogue, and recordings remain with their lawful owners. Do not present AI-generated material as official content or new recordings by the original performers.

---

<div align="center">

### ✦ 「如果你願意，我就再陪你一會。」 ✦

**Version 0.1.1-RC4 · Publisher: MIMI**

</div>

