using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LilithRuntime.Configuration;

namespace LilithControlCenter;

public partial class D046GameControlsPanel : UserControl
{
    private readonly D046GameControlsPanelViewModel _viewModel;
    private readonly HashSet<int> _suppressedVirtualKeys = new();
    private Window? _ownerWindow;
    private readonly DispatcherTimer _preferencesRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    public D046GameControlsPanel()
    {
        InitializeComponent();
        _viewModel = new D046GameControlsPanelViewModel(
            D046GameControlPreferencesStore.ForCurrentUser());
        DataContext = _viewModel;
        _preferencesRefreshTimer.Tick += (_, _) => _viewModel.RefreshSavedPreferences();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.Apply();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
        => _viewModel.Cancel();

    private void PushToTalkKeySelector_Click(object sender, RoutedEventArgs e)
        => BeginKeyCapture(
            D046GameControlKeyTarget.PushToTalk,
            PushToTalkKeySelector);

    private void TextChatKeySelector_Click(object sender, RoutedEventArgs e)
        => BeginKeyCapture(
            D046GameControlKeyTarget.TextChat,
            TextChatKeySelector);

    private void ConversationHistoryKeySelector_Click(
        object sender,
        RoutedEventArgs e)
        => BeginKeyCapture(
            D046GameControlKeyTarget.ConversationHistory,
            ConversationHistoryKeySelector);

    private void StopKeySelector_Click(object sender, RoutedEventArgs e)
        => BeginKeyCapture(
            D046GameControlKeyTarget.Stop,
            StopKeySelector);

    private void BeginKeyCapture(D046GameControlKeyTarget target, Button button)
    {
        if (_viewModel.BeginKeyCapture(target))
        {
            button.Focus();
            Keyboard.Focus(button);
        }
    }

    private void CaptureButton_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (!_viewModel.IsCapturingKey)
        {
            if (_suppressedVirtualKeys.Contains(virtualKey))
                e.Handled = true;
            return;
        }

        e.Handled = true;
        _suppressedVirtualKeys.Add(virtualKey);
        _viewModel.CaptureWindowsVirtualKey(
            virtualKey,
            Keyboard.Modifiers != ModifierKeys.None);
    }

    private void CaptureButton_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (_suppressedVirtualKeys.Remove(virtualKey))
        {
            e.Handled = true;
            _viewModel.CommitCapturedVirtualKey(
                virtualKey,
                _suppressedVirtualKeys.Count == 0);
        }
    }

    private void CaptureButton_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        _suppressedVirtualKeys.Clear();
        _viewModel.CancelKeyCapture(
            "已取消快捷鍵修改（按鍵控制已失去焦點）；原按鍵保持不變。");
    }

    private void D046GameControlsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        ControlCenterLocalization.Current.LanguageChanged -= _viewModel.RefreshLanguage;
        ControlCenterLocalization.Current.LanguageChanged += _viewModel.RefreshLanguage;
        _viewModel.RefreshLanguage();
        _viewModel.RefreshSavedPreferences();
        _preferencesRefreshTimer.Start();
        var ownerWindow = Window.GetWindow(this);
        if (ReferenceEquals(ownerWindow, _ownerWindow))
            return;
        DetachOwnerWindow();
        _ownerWindow = ownerWindow;
        if (_ownerWindow != null)
            _ownerWindow.Closed += OwnerWindow_Closed;
    }

    private void D046GameControlsPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        _preferencesRefreshTimer.Stop();
        ControlCenterLocalization.Current.LanguageChanged -= _viewModel.RefreshLanguage;
        ReleaseSurfaceCapture("已取消快捷鍵修改（設定頁面已關閉）。");
        DetachOwnerWindow();
    }

    private void OwnerWindow_Closed(object? sender, EventArgs e)
    {
        _preferencesRefreshTimer.Stop();
        ReleaseSurfaceCapture("已取消快捷鍵修改（控制中心已關閉）。");
    }

    private void ReleaseSurfaceCapture(string message)
    {
        _suppressedVirtualKeys.Clear();
        _viewModel.CancelKeyCapture(message);
    }

    private void DetachOwnerWindow()
    {
        if (_ownerWindow != null)
            _ownerWindow.Closed -= OwnerWindow_Closed;
        _ownerWindow = null;
    }
}
