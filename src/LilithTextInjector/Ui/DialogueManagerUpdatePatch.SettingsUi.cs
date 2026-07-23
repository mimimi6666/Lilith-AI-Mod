using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using Microsoft.Win32;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using TMPro;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;
using UI.Common;
using UI.TraySetting;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private static void EnsureAdvancedComputerActionsToggle()
    {
        SyncInjectedSettingsVisibility();
        if (Time.unscaledTime < _nextAdvancedActionsUiScanAt)
            return;
        _nextAdvancedActionsUiScanAt = Time.unscaledTime + 0.75f;
        try
        {
            if (_advancedActionsRow != null && _advancedActionsToggle != null)
            {
                SetAdvancedActionsLabel(_advancedActionsRow.transform);
                if (_advancedActionsToggle.IsOn != Plugin.AdvancedComputerActionsEnabled.Value)
                    _advancedActionsToggle.SetValue(Plugin.AdvancedComputerActionsEnabled.Value, false);
                SyncInjectedSettingsVisibility();
                return;
            }

            TraySettingView? view = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TraySettingView>())
            {
                if (candidate != null && candidate.gameObject != null && candidate._crossScreenDragToggle != null)
                {
                    view = candidate;
                    break;
                }
            }
            if (view == null)
                return;

            var templateToggle = view._crossScreenDragToggle;
            var templateRow = FindSettingRow(templateToggle.transform, view.transform);
            if (templateRow == null || templateRow.parent == null)
                return;

            _settingsView = view;
            _settingsVisibilityTemplateRow = templateRow.gameObject;

            var clone = UnityEngine.Object.Instantiate(templateRow.gameObject, templateRow.parent);
            clone.name = "LilithAdvancedComputerActions";
            clone.SetActive(true);
            var clonedToggle = FindButtonToggle(clone.transform);
            if (clonedToggle == null)
            {
                UnityEngine.Object.Destroy(clone);
                return;
            }

            // A cloned ButtonToggle also clones the official row's managed callback.
            // Replace it so this control cannot accidentally change cross-screen dragging.
            clonedToggle.OnValueChanged = null;
            _advancedActionsChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(
                new System.Action<bool>(enabled =>
                {
                    Plugin.AdvancedComputerActionsEnabled.Value = enabled;
                    Plugin.PluginLog.LogInfo($"Advanced computer actions {(enabled ? "enabled" : "disabled")} by the player.");
                }));
            clonedToggle.OnValueChanged = _advancedActionsChanged;
            clonedToggle.SetValue(Plugin.AdvancedComputerActionsEnabled.Value, false);
            SetAdvancedActionsLabel(clone.transform);
            PlaceAdvancedActionsRow(templateRow, clone.transform);

            _advancedActionsRow = clone;
            _advancedActionsToggle = clonedToggle;
            SyncInjectedSettingsVisibility();
            Plugin.PluginLog.LogInfo($"Added the native-style advanced computer actions toggle at {GetTransformPath(clone.transform)} (default={Plugin.AdvancedComputerActionsEnabled.Value}).");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not add the advanced computer actions setting: {exception}");
            _advancedActionsRow = null;
            _advancedActionsToggle = null;
            _advancedActionsChanged = null;
        }
    }

    private static void UpdateSettingsUiSafely()
    {
        try
        {
            EnsureJapaneseVoiceOptionVisible();
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not update the game voice setting: {exception}");
        }

        try
        {
            EnsureAdvancedComputerActionsToggle();
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not update the advanced actions setting: {exception}");
        }

        try
        {
            EnsureKeyBindingSettings();
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not update key binding settings: {exception}");
            ResetKeyBindingUiReferences();
        }
    }

    private static Transform? FindSettingRow(Transform start, Transform viewRoot)
    {
        var current = start;
        for (var depth = 0; current != null && current != viewRoot && depth < 6; depth++)
        {
            if (FindFirstText(current) != null)
                return current;
            current = current.parent;
        }
        return start.parent;
    }

    private static TMP_Text? FindFirstText(Transform root)
    {
        var own = root.gameObject.GetComponent<TMP_Text>();
        if (own != null)
            return own;
        for (var index = 0; index < root.childCount; index++)
        {
            var found = FindFirstText(root.GetChild(index));
            if (found != null)
                return found;
        }
        return null;
    }

    private static ButtonToggle? FindButtonToggle(Transform root)
    {
        var own = root.gameObject.GetComponent<ButtonToggle>();
        if (own != null)
            return own;
        for (var index = 0; index < root.childCount; index++)
        {
            var found = FindButtonToggle(root.GetChild(index));
            if (found != null)
                return found;
        }
        return null;
    }

    private static void SetAdvancedActionsLabel(Transform row)
    {
        var label = FindFirstText(row);
        if (label == null)
            return;
        label.text = ApiKeyText("進階電腦操作", "高级电脑操作", "高度なPC操作", "Advanced PC controls");
    }

    private static void PlaceAdvancedActionsRow(Transform templateRow, Transform clonedRow)
    {
        var parent = templateRow.parent;
        var cloneRect = clonedRow.gameObject.GetComponent<RectTransform>();
        var templateRect = templateRow.gameObject.GetComponent<RectTransform>();
        if (parent == null || cloneRect == null || templateRect == null)
            return;

        // The lower half of the settings panel is already full. Keep the cloned
        // row out of the vertical layout and use the empty right side instead.
        var layoutElement = clonedRow.gameObject.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = clonedRow.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;
        clonedRow.SetSiblingIndex(parent.childCount - 1);
        cloneRect.anchoredPosition = templateRect.anchoredPosition + new Vector2(300f, 0f);
    }

    private static void EnsureKeyBindingSettings()
    {
        var controlsVisible = SyncInjectedSettingsVisibility();
        UpdateKeyBindingTexts();
        if (controlsVisible && ProcessKeyBindingInteraction())
            return;
        if (_textInputKeyRow != null && _voiceInputKeyRow != null
            && _textInputKeyButtonRect != null && _voiceInputKeyButtonRect != null)
            return;
        if (Time.unscaledTime < _nextKeyBindingsUiScanAt)
            return;
        _nextKeyBindingsUiScanAt = Time.unscaledTime + 0.75f;

        try
        {
            TraySettingView? view = null;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<TraySettingView>())
            {
                if (candidate != null && candidate.gameObject != null && candidate._crossScreenDragToggle != null)
                {
                    view = candidate;
                    break;
                }
            }
            if (view == null)
                return;

            var templateRow = FindSettingRow(view._crossScreenDragToggle.transform, view.transform);
            if (templateRow == null || templateRow.parent == null)
                return;

            _settingsView = view;
            _settingsVisibilityTemplateRow = templateRow.gameObject;

            if (!CreateKeyBindingRow(templateRow, "LilithTextInputKey", 82f,
                    out _textInputKeyRow, out _textInputKeyButton, out _textInputKeyButtonRect, out _textInputKeyValue)
                || !CreateKeyBindingRow(templateRow, "LilithVoiceInputKey", 41f,
                    out _voiceInputKeyRow, out _voiceInputKeyButton, out _voiceInputKeyButtonRect, out _voiceInputKeyValue))
            {
                if (_textInputKeyRow != null) UnityEngine.Object.Destroy(_textInputKeyRow);
                if (_voiceInputKeyRow != null) UnityEngine.Object.Destroy(_voiceInputKeyRow);
                ResetKeyBindingUiReferences();
                return;
            }

            UpdateKeyBindingTexts();
            SyncInjectedSettingsVisibility();
            Plugin.PluginLog.LogInfo("Added native-style text input and push-to-talk key binding controls.");
        }
        catch (Exception exception)
        {
            Plugin.PluginLog.LogWarning($"Could not add key binding settings: {exception}");
            ResetKeyBindingUiReferences();
        }
    }

    private static bool CreateKeyBindingRow(
        Transform templateRow,
        string objectName,
        float verticalOffset,
        out GameObject? row,
        out ButtonToggle? button,
        out RectTransform? buttonRect,
        out TMP_Text? valueText)
    {
        row = null;
        button = null;
        buttonRect = null;
        valueText = null;
        if (templateRow.parent == null)
            return false;

        var clone = UnityEngine.Object.Instantiate(templateRow.gameObject, templateRow.parent);
        clone.name = objectName;
        clone.SetActive(true);
        var clonedButton = FindButtonToggle(clone.transform);
        var label = FindFirstText(clone.transform);
        if (clonedButton == null || label == null)
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }

        clonedButton.OnValueChanged = null;
        clonedButton.SetValue(false, false);
        var toggleRect = clonedButton.gameObject.GetComponent<RectTransform>();
        if (toggleRect == null)
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }
        toggleRect.sizeDelta = new Vector2(Math.Max(82f, toggleRect.sizeDelta.x), Math.Max(28f, toggleRect.sizeDelta.y));

        // Keep the native rounded frame on the ButtonToggle root, but hide its
        // child state marker. A key binding is a button, not an on/off switch.
        for (var index = 0; index < clonedButton.transform.childCount; index++)
            clonedButton.transform.GetChild(index).gameObject.SetActive(false);

        var valueObject = UnityEngine.Object.Instantiate(label.gameObject, clonedButton.transform);
        valueObject.name = "LilithKeyValue";
        valueObject.SetActive(true);
        var clonedValue = valueObject.GetComponent<TMP_Text>();
        var valueRect = valueObject.GetComponent<RectTransform>();
        if (clonedValue == null || valueRect == null)
        {
            UnityEngine.Object.Destroy(clone);
            return false;
        }
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = Vector2.one;
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = Vector2.zero;
        valueRect.anchoredPosition = Vector2.zero;
        clonedValue.alignment = TextAlignmentOptions.Center;
        clonedValue.raycastTarget = false;
        clonedValue.enableWordWrapping = false;

        PlaceKeyBindingRow(templateRow, clone.transform, verticalOffset);
        row = clone;
        button = clonedButton;
        buttonRect = toggleRect;
        valueText = clonedValue;
        return true;
    }

    private static void PlaceKeyBindingRow(Transform templateRow, Transform clonedRow, float verticalOffset)
    {
        var parent = templateRow.parent;
        var cloneRect = clonedRow.gameObject.GetComponent<RectTransform>();
        var templateRect = templateRow.gameObject.GetComponent<RectTransform>();
        if (parent == null || cloneRect == null || templateRect == null)
            return;
        var layoutElement = clonedRow.gameObject.GetComponent<LayoutElement>();
        if (layoutElement == null)
            layoutElement = clonedRow.gameObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;
        clonedRow.SetSiblingIndex(parent.childCount - 1);
        cloneRect.anchoredPosition = templateRect.anchoredPosition + new Vector2(300f, verticalOffset);
    }

    private static bool ProcessKeyBindingInteraction()
    {
        if (_keyBindingTarget != 0)
        {
            if (_keyBindingStartedAt >= 0f && Time.unscaledTime - _keyBindingStartedAt >= 15f)
            {
                _keyBindingTarget = 0;
                _keyBindingStartedAt = -1f;
                RebindingHeldVirtualKeys.Clear();
                UpdateKeyBindingTexts();
                Plugin.PluginLog.LogInfo("Key rebinding timed out and was cancelled.");
                return true;
            }
            // Read the Windows keyboard state directly. The updated settings
            // window no longer forwards keyboard events to Unity's legacy Input
            // API while it is focused.
            for (var code = (int)KeyCode.Backspace; code < (int)KeyCode.Mouse0; code++)
            {
                var key = (KeyCode)code;
                if (!TryGetWindowsVirtualKey(key, out var virtualKey))
                    continue;
                var down = IsVirtualKeyDown(virtualKey);
                if (!down)
                {
                    RebindingHeldVirtualKeys.Remove(virtualKey);
                    continue;
                }
                if (!RebindingHeldVirtualKeys.Add(virtualKey))
                    continue;
                if (key == KeyCode.Escape)
                {
                    _keyBindingTarget = 0;
                    _keyBindingStartedAt = -1f;
                    RebindingHeldVirtualKeys.Clear();
                    UpdateKeyBindingTexts();
                    Plugin.PluginLog.LogInfo("Key rebinding cancelled.");
                    return true;
                }
                ApplyKeyBinding(key);
                return true;
            }
            return true;
        }

        if (!Input.GetMouseButtonDown(0))
            return false;
        if (_textInputKeyButtonRect != null && _textInputKeyRow != null && _textInputKeyRow.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(_textInputKeyButtonRect, Input.mousePosition, null))
        {
            _keyBindingTarget = 1;
            _keyBindingStartedAt = Time.unscaledTime;
            CaptureHeldRebindingKeys();
            UpdateKeyBindingTexts();
            Plugin.PluginLog.LogInfo("Waiting for a new text input hotkey.");
            return true;
        }
        if (_voiceInputKeyButtonRect != null && _voiceInputKeyRow != null && _voiceInputKeyRow.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(_voiceInputKeyButtonRect, Input.mousePosition, null))
        {
            _keyBindingTarget = 2;
            _keyBindingStartedAt = Time.unscaledTime;
            CaptureHeldRebindingKeys();
            UpdateKeyBindingTexts();
            Plugin.PluginLog.LogInfo("Waiting for a new push-to-talk hotkey.");
            return true;
        }
        return false;
    }

    private static bool SyncInjectedSettingsVisibility()
    {
        var controlsVisible = false;
        try
        {
            controlsVisible = _settingsView != null
                && _settingsView.gameObject != null
                && _settingsView.gameObject.activeInHierarchy
                && _settingsView._currentTab == TraySettingView.TabControls;
        }
        catch
        {
            // Older game builds do not expose tabs. Retain the legacy behavior
            // there, while current builds use the official selected-tab state.
            controlsVisible = _settingsVisibilityTemplateRow != null
                && _settingsVisibilityTemplateRow.activeInHierarchy;
        }

        SetInjectedSettingsRowActive(_advancedActionsRow, controlsVisible);
        SetInjectedSettingsRowActive(_textInputKeyRow, controlsVisible);
        SetInjectedSettingsRowActive(_voiceInputKeyRow, controlsVisible);

        if (!controlsVisible && _keyBindingTarget != 0)
        {
            _keyBindingTarget = 0;
            _keyBindingStartedAt = -1f;
            RebindingHeldVirtualKeys.Clear();
            UpdateKeyBindingTexts();
            Plugin.PluginLog.LogInfo("Key rebinding cancelled because the Controls settings tab was closed.");
        }
        return controlsVisible;
    }

    private static void SetInjectedSettingsRowActive(GameObject? row, bool active)
    {
        if (row != null && row.activeSelf != active)
            row.SetActive(active);
    }

}
