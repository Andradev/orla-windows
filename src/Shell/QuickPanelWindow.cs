using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Threading;
using Forms = System.Windows.Forms;

namespace Orla
{
    // Deliberately transient window: services, callbacks, and timers exist only
    // between Show and Closed. No hidden panel content remains in memory.
    internal sealed class QuickPanelWindow : Window, IDisposable
    {
        private readonly string _screenDeviceName;
        private readonly Border _surface;
        private readonly SystemStatusMonitor _statusMonitor;
        private readonly DispatcherTimer _brightnessSetTimer;
        private readonly DispatcherTimer _deactivationTimer;
        private readonly TextBlock _networkTitle;
        private readonly TextBlock _networkDetail;
        private readonly VectorIcon _networkGlyph;
        private readonly Border _networkSurface;
        private readonly Button _networkToggleButton;
        private readonly Border _bluetoothSurface;
        private readonly Button _bluetoothToggleButton;
        private readonly TextBlock _bluetoothTitle;
        private readonly TextBlock _bluetoothDetail;
        private readonly VectorIcon _bluetoothGlyph;
        private readonly Button _volumeButton;
        private readonly LightweightSlider _volumeSlider;
        private readonly VectorIcon _volumeGlyph;
        private readonly LightweightSlider _brightnessSlider;
        private readonly Border _energySaverSurface;
        private readonly TextBlock _energySaverDetail;
        private readonly VectorIcon _energySaverGlyph;
        private readonly Border _nightLightSurface;
        private readonly TextBlock _nightLightDetail;
        private readonly VectorIcon _nightLightGlyph;
        private bool _updatingVolume;
        private bool _updatingBrightness;
        private bool _audioAvailable;
        private bool _audioMuted;
        private int _radioOperation;
        private RadiosSnapshot _radios = RadiosSnapshot.Unavailable;
        private int _pendingBrightness;
        private BrightnessService _brightness;
        private bool _disposed;
        private DateTime _shownAt;

        internal bool RestorePreviousFocusOnClose { get; private set; }
        internal string ScreenDeviceName { get { return _screenDeviceName; } }

        internal QuickPanelWindow(string screenDeviceName, SystemStatusMonitor statusMonitor)
        {
            _screenDeviceName = screenDeviceName;
            _statusMonitor = statusMonitor;
            Title = Loc.QuickPanelTitle;
            // 316 px of content + 28 px of padding + 2 px of border.
            // The former 344 px width clipped the right edge at fractional DPI.
            Width = 346;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 620;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            _surface = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(28, 28, 31)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14)
            };
            StackPanel content = new StackPanel();
            _surface.Child = content;

            Grid header = new Grid { Height = 30, Margin = new Thickness(2, 0, 0, 10) };
            TextBlock heading = Ui.Text(Loc.Controls, 16, FontWeights.SemiBold);
            heading.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(heading);
            content.Children.Add(header);

            Grid quickActions = new Grid();
            quickActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(154) });
            quickActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            quickActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(154) });
            quickActions.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
            quickActions.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            quickActions.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });

            Grid networkRow = CreateCompactStatusContent(OrlaIcon.WifiMedium, 20,
                out _networkGlyph, out _networkTitle, out _networkDetail);
            _networkSurface = CreateCompactSplitToggleCard(networkRow, Loc.ToggleWifi, delegate
                {
                    ToggleRadio(RadioKind.Wifi, _networkToggleButton);
                }, Loc.OpenNetworkSettings, delegate
                {
                    ShellActions.OpenUri("ms-settings:network-wifi");
                    RequestClose(false);
                }, out _networkToggleButton);
            quickActions.Children.Add(_networkSurface);

            Grid bluetoothRow = CreateCompactStatusContent(OrlaIcon.Bluetooth, 18,
                out _bluetoothGlyph, out _bluetoothTitle, out _bluetoothDetail);
            _bluetoothSurface = CreateCompactSplitToggleCard(bluetoothRow, Loc.ToggleBluetooth, delegate
                {
                    ToggleRadio(RadioKind.Bluetooth, _bluetoothToggleButton);
                }, Loc.OpenBluetoothSettings, delegate
                {
                    ShellActions.OpenUri("ms-settings:bluetooth");
                    RequestClose(false);
                }, out _bluetoothToggleButton);
            Grid.SetColumn(_bluetoothSurface, 2);
            quickActions.Children.Add(_bluetoothSurface);

            _energySaverSurface = CreateCompactSettingsTile(OrlaIcon.EnergySaver,
                Loc.EnergySaverShort, Loc.OpenEnergySaverSettings, delegate
                {
                    ShellActions.OpenUri("ms-settings:batterysaver-settings");
                    RequestClose(false);
                }, out _energySaverGlyph, out _energySaverDetail);
            Grid.SetRow(_energySaverSurface, 2);
            quickActions.Children.Add(_energySaverSurface);

            _nightLightSurface = CreateCompactSettingsTile(OrlaIcon.NightLight, Loc.NightLight,
                Loc.OpenNightLightSettings, delegate
                {
                    ShellActions.OpenUri("ms-settings:nightlight");
                    RequestClose(false);
                }, out _nightLightGlyph, out _nightLightDetail);
            Grid.SetColumn(_nightLightSurface, 2);
            Grid.SetRow(_nightLightSurface, 2);
            quickActions.Children.Add(_nightLightSurface);
            content.Children.Add(quickActions);

            Border audioCard = CreateCard();
            audioCard.Width = 316;
            audioCard.Height = 48;
            audioCard.Margin = new Thickness(0, 9, 0, 0);
            audioCard.Padding = new Thickness(10, 5, 10, 5);
            Grid audioContent = new Grid();
            audioContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            audioContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _volumeGlyph = Ui.Vector(OrlaIcon.VolumeHigh, Loc.ToggleMute, 18);
            _volumeButton = Ui.WrapButton(_volumeGlyph, Loc.ToggleMute, 30, 30);
            _volumeButton.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            _volumeButton.VerticalAlignment = VerticalAlignment.Center;
            _volumeButton.Click += delegate { _statusMonitor.ToggleMute(); };
            Ui.EnableTopBarMotion(_volumeButton);
            audioContent.Children.Add(_volumeButton);

            _volumeSlider = new LightweightSlider
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = true,
                Height = 26,
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc.MasterVolume
            };
            System.Windows.Automation.AutomationProperties.SetName(_volumeSlider, Loc.MasterVolume);
            _volumeSlider.ValueChanged += OnVolumeChanged;
            Grid.SetColumn(_volumeSlider, 1);
            audioContent.Children.Add(_volumeSlider);
            audioCard.Child = audioContent;
            content.Children.Add(audioCard);

            Border brightnessCard = CreateCard();
            brightnessCard.Width = 316;
            brightnessCard.Height = 48;
            brightnessCard.Margin = new Thickness(0, 8, 0, 0);
            brightnessCard.Padding = new Thickness(10, 5, 10, 5);
            Grid brightnessContent = new Grid();
            brightnessContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            brightnessContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border brightnessBadge = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Ui.Vector(OrlaIcon.BrightnessHigh, Loc.Brightness, 20)
            };
            brightnessContent.Children.Add(brightnessBadge);
            _brightnessSlider = new LightweightSlider
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = true,
                Height = 26,
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Loc.CheckingMonitorSupport,
                IsEnabled = false
            };
            System.Windows.Automation.AutomationProperties.SetName(_brightnessSlider, Loc.Brightness);
            _brightnessSlider.ValueChanged += OnBrightnessChanged;
            Grid.SetColumn(_brightnessSlider, 1);
            brightnessContent.Children.Add(_brightnessSlider);
            brightnessCard.Child = brightnessContent;
            content.Children.Add(brightnessCard);

            Content = _surface;

            _brightnessSetTimer = new DispatcherTimer(DispatcherPriority.Background);
            _brightnessSetTimer.Interval = TimeSpan.FromMilliseconds(90);
            _brightnessSetTimer.Tick += OnBrightnessSetTimerTick;

            _deactivationTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
            _deactivationTimer.Tick += OnDeactivationTimerTick;

            Loaded += OnLoaded;
            Activated += OnActivated;
            Deactivated += OnDeactivated;
            PreviewKeyDown += OnPreviewKeyDown;
            Closed += OnClosed;
            _statusMonitor.StateChanged += OnSystemStatusChanged;
            RefreshStatus(_statusMonitor.ReadSnapshot());
        }

        private static Border CreateCard()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(44, 44, 48)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(57, 57, 62)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(12, 11, 12, 11)
            };
        }

        private static Grid CreateCompactStatusContent(OrlaIcon initialIcon, double iconSize,
            out VectorIcon glyph, out TextBlock title, out TextBlock detail)
        {
            Grid row = new Grid { Margin = new Thickness(8, 7, 3, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(37) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            glyph = Ui.Vector(initialIcon, Loc.Status, iconSize);
            Border iconBadge = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = glyph
            };
            row.Children.Add(iconBadge);

            StackPanel labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title = Ui.Text("", 11.25, FontWeights.SemiBold);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            labels.Children.Add(title);
            detail = Ui.Text("", 10, FontWeights.Normal);
            detail.Foreground = Ui.SecondaryTextBrush;
            detail.Margin = new Thickness(0, 1, 0, 0);
            detail.HorizontalAlignment = HorizontalAlignment.Left;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            labels.Children.Add(detail);
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);
            return row;
        }

        private static Border CreateCompactSplitToggleCard(Grid content, string toggleName, Action toggle,
            string settingsName, Action openSettings, out Button toggleButton)
        {
            Border surface = CreateCard();
            surface.Width = 154;
            surface.Height = 58;
            surface.Padding = new Thickness(0);

            Grid split = new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(37) });

            toggleButton = Ui.WrapButton(content, toggleName, 116, 56);
            Ui.EnableTopBarMotion(toggleButton);
            toggleButton.Click += delegate { toggle(); };
            split.Children.Add(toggleButton);

            Border separator = new Border
            {
                Width = 1,
                Margin = new Thickness(0, 9, 0, 9),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
            };
            Grid.SetColumn(separator, 1);
            split.Children.Add(separator);

            VectorIcon chevron = Ui.Vector(OrlaIcon.ChevronRight, settingsName, 12);
            Button settingsButton = Ui.WrapButton(chevron, settingsName, 36, 56);
            settingsButton.Margin = new Thickness(0);
            Ui.EnableTopBarMotion(settingsButton);
            settingsButton.Click += delegate { openSettings(); };
            Grid.SetColumn(settingsButton, 2);
            split.Children.Add(settingsButton);

            surface.Child = split;
            return surface;
        }

        private static Border CreateCompactSettingsTile(OrlaIcon icon, string title,
            string accessibleName, Action action, out VectorIcon glyph, out TextBlock detail)
        {
            TextBlock label;
            Grid status = CreateCompactStatusContent(icon, 18, out glyph, out label, out detail);
            label.Text = title;
            detail.Text = Loc.Unavailable;

            Border surface = CreateCard();
            surface.Width = 154;
            surface.Height = 58;
            surface.Padding = new Thickness(0);
            Grid split = new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(37) });

            Button statusButton = Ui.WrapButton(status, accessibleName, 116, 56);
            Ui.EnableTopBarMotion(statusButton);
            statusButton.Click += delegate { action(); };
            split.Children.Add(statusButton);

            Border separator = new Border
            {
                Width = 1,
                Margin = new Thickness(0, 9, 0, 9),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
            };
            Grid.SetColumn(separator, 1);
            split.Children.Add(separator);

            VectorIcon chevron = Ui.Vector(OrlaIcon.ChevronRight, accessibleName, 11);
            Button settingsButton = Ui.WrapButton(chevron, accessibleName, 36, 56);
            Ui.EnableTopBarMotion(settingsButton);
            settingsButton.Click += delegate { action(); };
            Grid.SetColumn(settingsButton, 2);
            split.Children.Add(settingsButton);

            surface.Child = split;
            return surface;
        }

        private void OnLoaded(object sender, RoutedEventArgs eventArgs)
        {
            PositionOnScreen();
            _shownAt = DateTime.UtcNow;
            BeginBrightnessDetection();
            BeginRadioRefresh();
            BeginQuickSettingsRefresh();
            if (!SystemParameters.ClientAreaAnimation)
            {
                Opacity = 1;
                return;
            }

            Opacity = 0;
            ScaleTransform scale = new ScaleTransform(0.97, 0.97);
            _surface.RenderTransform = scale;
            _surface.RenderTransformOrigin = new Point(0.94, 0.02);
            CubicEase easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(130)) { EasingFunction = easing };
            DoubleAnimation growX = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { EasingFunction = easing };
            DoubleAnimation growY = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { EasingFunction = easing };
            BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, growX, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, growY, HandoffBehavior.SnapshotAndReplace);
        }

        private void PositionOnScreen()
        {
            Forms.Screen screen = Array.Find(Forms.Screen.AllScreens, delegate(Forms.Screen candidate)
            {
                return string.Equals(candidate.DeviceName, _screenDeviceName, StringComparison.OrdinalIgnoreCase);
            }) ?? Forms.Screen.PrimaryScreen;
            System.Drawing.Rectangle working = screen.WorkingArea;
            Left = working.Right - Width - 10;
            Top = working.Top + 8;
        }

        private void OnDeactivated(object sender, EventArgs eventArgs)
        {
            // The top bar does not activate itself, but Windows can deliver a
            // focus transition immediately after Show/Activate. Delay the check
            // without dropping it: a quick outside click must still close the
            // panel as soon as initial focus settles.
            double elapsed = (DateTime.UtcNow - _shownAt).TotalMilliseconds;
            _deactivationTimer.Stop();
            _deactivationTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, 600 - elapsed));
            _deactivationTimer.Start();
        }

        private void OnActivated(object sender, EventArgs eventArgs)
        {
            _deactivationTimer.Stop();
        }

        private void OnDeactivationTimerTick(object sender, EventArgs eventArgs)
        {
            _deactivationTimer.Stop();
            if (!_disposed && !IsActive) RequestClose(false);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs eventArgs)
        {
            if (eventArgs.Key != Key.Escape) return;
            eventArgs.Handled = true;
            RequestClose(true);
        }

        internal void RequestClose(bool restorePreviousFocus)
        {
            RestorePreviousFocusOnClose = RestorePreviousFocusOnClose || restorePreviousFocus;
            if (!_disposed) Close();
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
        {
            if (_updatingVolume || !_audioAvailable) return;
            int percent = Math.Max(0, Math.Min(100, (int)Math.Round(eventArgs.NewValue)));
            string description = Loc.VolumeStatus(percent, _audioMuted);
            string percentage = percent.ToString(Loc.FormattingCulture) + "%";
            _volumeSlider.ToolTip = percentage;
            _volumeButton.ToolTip = percentage;
            ToolTipService.SetToolTip(_volumeGlyph, percentage);
            System.Windows.Automation.AutomationProperties.SetName(_volumeSlider, description);
            System.Windows.Automation.AutomationProperties.SetName(_volumeButton, description);
            _volumeGlyph.SetIcon(StatusIcons.Volume(percent, _audioMuted));
            _statusMonitor.SetVolumePercent(eventArgs.NewValue);
        }

        private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
        {
            if (_updatingBrightness || _brightness == null || _brightness.SupportedCount == 0) return;
            _pendingBrightness = Math.Max(0, Math.Min(100, (int)Math.Round(eventArgs.NewValue)));
            string description = _pendingBrightness.ToString(Loc.FormattingCulture) + "% • "
                + Loc.BrightnessTargets(_brightness.IntegratedCount, _brightness.DdcCount);
            _brightnessSlider.ToolTip = description;
            System.Windows.Automation.AutomationProperties.SetName(_brightnessSlider,
                Loc.Brightness + " • " + description);
            _brightnessSetTimer.Stop();
            _brightnessSetTimer.Start();
        }

        private void OnBrightnessSetTimerTick(object sender, EventArgs eventArgs)
        {
            _brightnessSetTimer.Stop();
            BrightnessService service = _brightness;
            int percent = _pendingBrightness;
            if (service == null) return;
            ThreadPool.QueueUserWorkItem(delegate { service.SetPercent(percent); });
        }

        private void BeginBrightnessDetection()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                BrightnessService service = BrightnessService.Create();
                int percent;
                bool available = service.TryReadPercent(out percent);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (_disposed)
                    {
                        service.Dispose();
                        return;
                    }
                    _brightness = service;
                    _updatingBrightness = true;
                    if (available)
                    {
                        _brightnessSlider.Value = percent;
                        _brightnessSlider.IsEnabled = true;
                        string description = percent.ToString(Loc.FormattingCulture) + "% • "
                            + Loc.BrightnessTargets(service.IntegratedCount, service.DdcCount);
                        _brightnessSlider.ToolTip = description;
                        System.Windows.Automation.AutomationProperties.SetName(_brightnessSlider,
                            Loc.Brightness + " • " + description);
                    }
                    else
                    {
                        _brightnessSlider.IsEnabled = false;
                        _brightnessSlider.ToolTip = Loc.DdcUnavailable;
                        System.Windows.Automation.AutomationProperties.SetName(_brightnessSlider,
                            Loc.Brightness + " • " + Loc.DdcUnavailable);
                    }
                    _updatingBrightness = false;
                }), DispatcherPriority.Background);
            });
        }

        private void OnSystemStatusChanged(object sender, EventArgs eventArgs)
        {
            if (_disposed) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!_disposed) RefreshStatus(_statusMonitor.ReadSnapshot());
            }), DispatcherPriority.Background);
        }

        private void BeginRadioRefresh()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                RadiosSnapshot radios = RadioService.Read();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (_disposed) return;
                    _radios = radios;
                    ApplyRadioState(_statusMonitor.ReadSnapshot());
                }), DispatcherPriority.Background);
            });
        }

        private void BeginQuickSettingsRefresh()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                QuickSettingsSnapshot quickSettings = QuickSettingsSnapshot.Read();
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!_disposed) RefreshQuickSettings(quickSettings);
                }), DispatcherPriority.Background);
            });
        }

        private void ToggleRadio(RadioKind kind, Button button)
        {
            if (Interlocked.CompareExchange(ref _radioOperation, 1, 0) != 0) return;
            _networkToggleButton.IsEnabled = false;
            _bluetoothToggleButton.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    RadioService.Toggle(kind);
                    Thread.Sleep(160);
                    _radios = RadioService.Read();
                    if (kind == RadioKind.Bluetooth && _radios.Bluetooth.IsAvailable)
                        _statusMonitor.SetBluetoothRadioState(_radios.Bluetooth.IsEnabled);
                }
                finally
                {
                    Interlocked.Exchange(ref _radioOperation, 0);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (_disposed) return;
                        _networkToggleButton.IsEnabled = _radios.Wifi.IsAvailable;
                        _bluetoothToggleButton.IsEnabled = _radios.Bluetooth.IsAvailable;
                        RefreshStatus(_statusMonitor.ReadSnapshot());
                    }), DispatcherPriority.Background);
                }
            });
        }

        private void RefreshStatus(SystemStatusSnapshot state)
        {
            if (state == null || _disposed) return;
            RefreshAudio(state.Audio, state.AudioAvailable);
            RefreshNetwork(state.Network);
            RefreshBluetooth(state.Bluetooth);
            RefreshQuickSettings(state.QuickSettings);
            ApplyRadioState(state);
        }

        private void ApplyRadioState(SystemStatusSnapshot state)
        {
            if (state == null || _disposed) return;
            bool wifiKnown = _radios.Wifi.IsAvailable;
            bool wifiEnabled = wifiKnown ? _radios.Wifi.IsEnabled : state.Network.IsWifi && state.Network.IsAvailable;
            bool bluetoothKnown = _radios.Bluetooth.IsAvailable;
            bool bluetoothEnabled = bluetoothKnown ? _radios.Bluetooth.IsEnabled : state.Bluetooth.IsEnabled;

            ApplyToggleSurface(_networkSurface, wifiEnabled, wifiKnown);
            ApplyToggleSurface(_bluetoothSurface, bluetoothEnabled, bluetoothKnown);
            _networkToggleButton.IsEnabled = Volatile.Read(ref _radioOperation) == 0 && wifiKnown;
            _bluetoothToggleButton.IsEnabled = Volatile.Read(ref _radioOperation) == 0 && bluetoothKnown;

            _networkTitle.Text = Loc.Wifi;
            if (wifiKnown && wifiEnabled && !(state.Network.IsWifi && state.Network.IsAvailable))
            {
                _networkDetail.Text = Loc.Enabled;
                _networkGlyph.SetIcon(OrlaIcon.WifiMedium);
                _networkGlyph.Foreground = Ui.PrimaryTextBrush;
            }

            if (wifiKnown && !wifiEnabled)
            {
                _networkTitle.Text = Loc.Wifi;
                _networkDetail.Text = Loc.Disabled;
                _networkGlyph.SetIcon(OrlaIcon.WifiOff);
                _networkGlyph.Foreground = Ui.PrimaryTextBrush;
            }
            if (bluetoothKnown && !bluetoothEnabled)
            {
                _bluetoothDetail.Text = Loc.Disabled;
                _bluetoothGlyph.Foreground = Ui.PrimaryTextBrush;
            }
        }

        private static void ApplyToggleSurface(Border surface, bool active, bool available)
        {
            SolidColorBrush background = surface.Background as SolidColorBrush;
            SolidColorBrush border = surface.BorderBrush as SolidColorBrush;
            Color backgroundColor = active ? Color.FromRgb(0, 95, 184) : Color.FromRgb(44, 44, 48);
            Color borderColor = active ? Color.FromRgb(47, 134, 219) : Color.FromRgb(57, 57, 62);
            if (!available)
            {
                backgroundColor = Color.FromRgb(39, 39, 43);
                borderColor = Color.FromRgb(51, 51, 56);
            }
            if (background != null) Ui.AnimateBrush(background, backgroundColor, 120);
            if (border != null) Ui.AnimateBrush(border, borderColor, 120);
            surface.Opacity = available ? 1.0 : 0.72;
        }

        private void RefreshAudio(AudioStateChangedEventArgs state, bool available)
        {
            if (_disposed) return;
            _audioAvailable = available;
            if (!available)
            {
                _volumeSlider.IsEnabled = false;
                _volumeSlider.ToolTip = Loc.Unavailable;
                _volumeButton.ToolTip = Loc.Unavailable;
                ToolTipService.SetToolTip(_volumeGlyph, Loc.Unavailable);
                System.Windows.Automation.AutomationProperties.SetName(_volumeSlider,
                    Loc.MasterVolume + " • " + Loc.Unavailable);
                System.Windows.Automation.AutomationProperties.SetName(_volumeButton,
                    Loc.ToggleMute + " • " + Loc.Unavailable);
                _volumeGlyph.SetIcon(OrlaIcon.VolumeMuted);
                _volumeGlyph.Foreground = Ui.ErrorBrush;
                return;
            }

            _updatingVolume = true;
            _volumeSlider.Value = state.VolumePercent;
            _updatingVolume = false;
            _audioMuted = state.IsMuted;
            string description = Loc.VolumeStatus(state.VolumePercent, state.IsMuted);
            string percentage = state.VolumePercent.ToString(Loc.FormattingCulture) + "%";
            _volumeSlider.ToolTip = percentage;
            _volumeButton.ToolTip = percentage;
            ToolTipService.SetToolTip(_volumeGlyph, percentage);
            System.Windows.Automation.AutomationProperties.SetName(_volumeSlider, description);
            System.Windows.Automation.AutomationProperties.SetName(_volumeButton, description);
            _volumeGlyph.SetIcon(StatusIcons.Volume(state.VolumePercent, state.IsMuted));
            _volumeGlyph.Foreground = state.IsMuted ? Ui.SecondaryTextBrush : Ui.PrimaryTextBrush;
            _volumeGlyph.SetAccessibleName(Loc.VolumeStatus(state.VolumePercent, state.IsMuted));
        }

        private void RefreshNetwork(NetworkSnapshot state)
        {
            if (state == null) return;
            _networkTitle.Text = Loc.Wifi;
            _networkDetail.Text = state.IsWifi && state.IsAvailable
                && !string.IsNullOrWhiteSpace(state.ConnectionName) ? state.ConnectionName
                : state.IsWifi && state.IsAvailable && state.SignalQuality >= 0
                    ? state.SignalQuality.ToString(Loc.FormattingCulture) + "%"
                    : state.IsAvailable ? Loc.Connected : Loc.NoConnection;
            _networkGlyph.SetIcon(StatusIcons.Network(state));
            _networkGlyph.Foreground = state.IsAvailable ? Ui.PrimaryTextBrush : Ui.ErrorBrush;
            _networkGlyph.SetAccessibleName(state.Name + " • " + state.Detail);
            _networkToggleButton.ToolTip = state.Name + " • " + state.Detail;
            System.Windows.Automation.AutomationProperties.SetName(_networkToggleButton,
                state.Name + " • " + state.Detail);
        }

        private void RefreshBluetooth(BluetoothSnapshot state)
        {
            try
            {
                _bluetoothSurface.Visibility = Visibility.Visible;
                _bluetoothTitle.Text = Loc.Bluetooth;
                _bluetoothDetail.Text = !state.IsEnabled ? Loc.Disabled
                    : state.IsConnected && !string.IsNullOrWhiteSpace(state.DeviceName)
                        ? state.DeviceName : Loc.Enabled;
                _bluetoothGlyph.Foreground = Ui.PrimaryTextBrush;
                _bluetoothGlyph.SetAccessibleName(Loc.BluetoothStatus(state));
                _bluetoothToggleButton.ToolTip = Loc.BluetoothStatus(state);
                System.Windows.Automation.AutomationProperties.SetName(_bluetoothToggleButton,
                    Loc.BluetoothStatus(state));
            }
            catch (Exception exception)
            {
                Logger.Write("Could not update Bluetooth in Quick Panel: " + exception.Message);
                _bluetoothSurface.Visibility = Visibility.Visible;
                _bluetoothTitle.Text = Loc.Bluetooth;
                _bluetoothDetail.Text = Loc.TemporarilyUnavailable;
                _bluetoothGlyph.Foreground = Ui.SecondaryTextBrush;
            }
        }

        private void RefreshQuickSettings(QuickSettingsSnapshot state)
        {
            if (state == null || _disposed) return;
            ApplyToggleSurface(_energySaverSurface, state.EnergySaverEnabled,
                state.EnergySaverAvailable);
            ApplyToggleSurface(_nightLightSurface, state.NightLightEnabled,
                state.NightLightAvailable);

            _energySaverDetail.Text = !state.EnergySaverAvailable ? Loc.Unavailable
                : state.EnergySaverEnabled ? Loc.Enabled : Loc.Disabled;
            _nightLightDetail.Text = !state.NightLightAvailable ? Loc.Unavailable
                : state.NightLightEnabled ? Loc.Enabled : Loc.Disabled;
            _energySaverGlyph.Foreground = state.EnergySaverAvailable
                ? Ui.PrimaryTextBrush : Ui.SecondaryTextBrush;
            _nightLightGlyph.Foreground = state.NightLightAvailable
                ? Ui.PrimaryTextBrush : Ui.SecondaryTextBrush;
            _energySaverGlyph.SetAccessibleName(Loc.EnergySaver + " • " + _energySaverDetail.Text);
            _nightLightGlyph.SetAccessibleName(Loc.NightLight + " • " + _nightLightDetail.Text);
        }

        private void OnClosed(object sender, EventArgs eventArgs)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _brightnessSetTimer.Stop();
            _deactivationTimer.Stop();
            _brightnessSetTimer.Tick -= OnBrightnessSetTimerTick;
            _deactivationTimer.Tick -= OnDeactivationTimerTick;
            _volumeSlider.ValueChanged -= OnVolumeChanged;
            _brightnessSlider.ValueChanged -= OnBrightnessChanged;
            _statusMonitor.StateChanged -= OnSystemStatusChanged;
            if (_brightness != null) _brightness.Dispose();
            Loaded -= OnLoaded;
            Activated -= OnActivated;
            Deactivated -= OnDeactivated;
            PreviewKeyDown -= OnPreviewKeyDown;
            Closed -= OnClosed;
        }
    }

    // Slider drawn as a single surface. It retains Slider semantics and its
    // AutomationPeer without the standard template's larger visual tree.
    internal sealed class LightweightSlider : Slider
    {
        private static readonly Brush TrackBrush = FrozenBrush(Color.FromRgb(82, 82, 88));
        private static readonly Brush AccentBrush = FrozenBrush(Color.FromRgb(10, 132, 255));
        private static readonly Pen ThumbBorderPen = FrozenPen(Color.FromArgb(70, 0, 0, 0), 1);
        private bool _dragging;

        internal LightweightSlider()
        {
            Focusable = true;
            Cursor = Cursors.Hand;
            Template = CreateEmptyTemplate();
        }

        private static ControlTemplate CreateEmptyTemplate()
        {
            FrameworkElementFactory root = new FrameworkElementFactory(typeof(Border));
            root.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            ControlTemplate template = new ControlTemplate(typeof(Slider));
            template.VisualTree = root;
            return template;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            double left = 8;
            double right = Math.Max(left, ActualWidth - 8);
            double center = ActualHeight / 2;
            double range = Math.Max(0.0001, Maximum - Minimum);
            double progress = Math.Max(0, Math.Min(1, (Value - Minimum) / range));
            double thumbX = left + ((right - left) * progress);
            Brush thumb = IsEnabled ? Brushes.White : Ui.SecondaryTextBrush;
            drawingContext.DrawRoundedRectangle(TrackBrush, null,
                new Rect(left, center - 2, right - left, 4), 2, 2);
            if (progress > 0)
            {
                drawingContext.DrawRoundedRectangle(AccentBrush, null,
                    new Rect(left, center - 2, Math.Max(2, thumbX - left), 4), 2, 2);
            }
            drawingContext.DrawEllipse(thumb, ThumbBorderPen,
                new Point(thumbX, center), IsMouseOver || _dragging ? 7 : 6.5, IsMouseOver || _dragging ? 7 : 6.5);
        }

        private static Brush FrozenBrush(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Color color, double thickness)
        {
            Pen pen = new Pen(FrozenBrush(color), thickness);
            pen.Freeze();
            return pen;
        }

        protected override void OnValueChanged(double oldValue, double newValue)
        {
            base.OnValueChanged(oldValue, newValue);
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
        {
            base.OnMouseLeftButtonDown(eventArgs);
            Focus();
            _dragging = CaptureMouse();
            SetValueFromPoint(eventArgs.GetPosition(this));
            eventArgs.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs eventArgs)
        {
            base.OnMouseMove(eventArgs);
            if (_dragging && eventArgs.LeftButton == MouseButtonState.Pressed)
                SetValueFromPoint(eventArgs.GetPosition(this));
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs eventArgs)
        {
            base.OnMouseLeftButtonUp(eventArgs);
            if (_dragging)
            {
                SetValueFromPoint(eventArgs.GetPosition(this));
                ReleaseMouseCapture();
                _dragging = false;
                InvalidateVisual();
            }
            eventArgs.Handled = true;
        }

        protected override void OnMouseEnter(MouseEventArgs eventArgs)
        {
            base.OnMouseEnter(eventArgs);
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs eventArgs)
        {
            base.OnMouseLeave(eventArgs);
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs eventArgs)
        {
            if (eventArgs.Key == Key.Left || eventArgs.Key == Key.Down)
            {
                Value = Math.Max(Minimum, Value - Math.Max(1, SmallChange));
                eventArgs.Handled = true;
                return;
            }
            if (eventArgs.Key == Key.Right || eventArgs.Key == Key.Up)
            {
                Value = Math.Min(Maximum, Value + Math.Max(1, SmallChange));
                eventArgs.Handled = true;
                return;
            }
            base.OnKeyDown(eventArgs);
        }

        private void SetValueFromPoint(Point point)
        {
            double width = Math.Max(1, ActualWidth - 16);
            double progress = Math.Max(0, Math.Min(1, (point.X - 8) / width));
            Value = Minimum + ((Maximum - Minimum) * progress);
        }
    }
}
