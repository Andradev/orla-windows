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
    // Janela deliberadamente transitória: serviços, callbacks e timer existem
    // somente entre Show e Closed. Nenhum conteúdo fica escondido em memória.
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
        private readonly LightweightSlider _volumeSlider;
        private readonly TextBlock _volumePercent;
        private readonly VectorIcon _volumeGlyph;
        private readonly TextBlock _muteText;
        private readonly Button _muteButton;
        private readonly LightweightSlider _brightnessSlider;
        private readonly TextBlock _brightnessDetail;
        private readonly TextBlock _batteryTitle;
        private readonly TextBlock _batteryDetail;
        private readonly VectorIcon _batteryGlyph;
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
            Width = 344;
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

            Grid header = new Grid { Margin = new Thickness(2, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel heading = new StackPanel();
            heading.Children.Add(Ui.Text(Loc.Controls, 16, FontWeights.SemiBold));
            TextBlock subtitle = Ui.Text(Loc.EssentialActions, 10.5, FontWeights.Normal);
            subtitle.Foreground = Ui.SecondaryTextBrush;
            subtitle.Margin = new Thickness(0, 2, 0, 0);
            heading.Children.Add(subtitle);
            header.Children.Add(heading);
            Button close = Ui.WrapButton(Ui.Vector(OrlaIcon.Close, Loc.Close, 15), Loc.CloseControls, 30, 30);
            Ui.EnableTopBarMotion(close);
            close.Click += delegate { RequestClose(true); };
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            content.Children.Add(header);

            Grid networkRow = CreateStatusRow(OrlaIcon.WifiMedium, 20,
                out _networkGlyph, out _networkTitle, out _networkDetail, false, null, null);
            _networkSurface = CreateSplitToggleCard(networkRow, Loc.ToggleWifi, delegate
                {
                    ToggleRadio(RadioKind.Wifi, _networkToggleButton);
                }, Loc.OpenNetworkSettings, delegate
                {
                    ShellActions.OpenUri("ms-settings:network-wifi");
                    RequestClose(false);
                }, out _networkToggleButton);
            content.Children.Add(_networkSurface);

            Grid bluetoothRow = CreateStatusRow(OrlaIcon.Bluetooth, 18,
                out _bluetoothGlyph, out _bluetoothTitle, out _bluetoothDetail, false, null, null);
            _bluetoothSurface = CreateSplitToggleCard(bluetoothRow, Loc.ToggleBluetooth, delegate
                {
                    ToggleRadio(RadioKind.Bluetooth, _bluetoothToggleButton);
                }, Loc.OpenBluetoothSettings, delegate
                {
                    ShellActions.OpenUri("ms-settings:bluetooth");
                    RequestClose(false);
                }, out _bluetoothToggleButton);
            _bluetoothSurface.Margin = new Thickness(0, 9, 0, 0);
            content.Children.Add(_bluetoothSurface);

            Border audioCard = CreateCard();
            audioCard.Margin = new Thickness(0, 9, 0, 0);
            StackPanel audioContent = new StackPanel();
            TextBlock audioTitle;
            Grid audioHeader = CreateStatusRow(OrlaIcon.VolumeHigh, 18,
                out _volumeGlyph, out audioTitle, out _volumePercent,
                false, Loc.ToggleMute, delegate { _statusMonitor.ToggleMute(); });
            audioTitle.Text = Loc.Volume;
            audioContent.Children.Add(audioHeader);

            _volumeSlider = new LightweightSlider
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = true,
                Height = 26,
                Margin = new Thickness(2, 10, 2, 5),
                ToolTip = Loc.MasterVolume
            };
            System.Windows.Automation.AutomationProperties.SetName(_volumeSlider, Loc.MasterVolume);
            _volumeSlider.ValueChanged += OnVolumeChanged;
            audioContent.Children.Add(_volumeSlider);

            StackPanel audioActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _muteText = Ui.Text(Loc.Mute, 11, FontWeights.SemiBold);
            StackPanel muteContent = new StackPanel { Orientation = Orientation.Horizontal };
            VectorIcon muteGlyph = Ui.Vector(OrlaIcon.VolumeMuted, Loc.ToggleMute, 14);
            muteGlyph.Margin = new Thickness(0, 0, 6, 0);
            muteContent.Children.Add(muteGlyph);
            muteContent.Children.Add(_muteText);
            _muteButton = Ui.WrapButton(muteContent, Loc.ToggleMute, 104, 30);
            Ui.EnableTopBarMotion(_muteButton);
            _muteButton.Click += delegate { _statusMonitor.ToggleMute(); };
            audioActions.Children.Add(_muteButton);
            audioContent.Children.Add(audioActions);
            audioCard.Child = audioContent;
            content.Children.Add(audioCard);

            Border brightnessCard = CreateCard();
            brightnessCard.Margin = new Thickness(0, 9, 0, 0);
            StackPanel brightnessContent = new StackPanel();
            VectorIcon brightnessGlyph;
            TextBlock brightnessTitle;
            Grid brightnessHeader = CreateStatusRow(OrlaIcon.BrightnessHigh, 20,
                out brightnessGlyph, out brightnessTitle, out _brightnessDetail,
                false, null, null);
            brightnessTitle.Text = Loc.Brightness;
            _brightnessDetail.Text = Loc.CheckingMonitorSupport;
            brightnessContent.Children.Add(brightnessHeader);
            _brightnessSlider = new LightweightSlider
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                IsMoveToPointEnabled = true,
                Height = 26,
                Margin = new Thickness(2, 10, 2, 1),
                ToolTip = Loc.Brightness,
                IsEnabled = false
            };
            System.Windows.Automation.AutomationProperties.SetName(_brightnessSlider, Loc.Brightness);
            _brightnessSlider.ValueChanged += OnBrightnessChanged;
            brightnessContent.Children.Add(_brightnessSlider);
            brightnessCard.Child = brightnessContent;
            content.Children.Add(brightnessCard);

            Grid nativeActions = new Grid { Margin = new Thickness(0, 9, 0, 0) };
            nativeActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(154) });
            nativeActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            nativeActions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(154) });
            Button energySaver = CreateCompactActionTile(OrlaIcon.EnergySaver, Loc.EnergySaver,
                Loc.OpenEnergySaverSettings, delegate
                {
                    ShellActions.OpenUri("ms-settings:batterysaver-settings");
                    RequestClose(false);
                });
            nativeActions.Children.Add(energySaver);
            Button nightLight = CreateCompactActionTile(OrlaIcon.NightLight, Loc.NightLight,
                Loc.OpenNightLightSettings, delegate
                {
                    ShellActions.OpenUri("ms-settings:nightlight");
                    RequestClose(false);
                });
            Grid.SetColumn(nightLight, 2);
            nativeActions.Children.Add(nightLight);
            content.Children.Add(nativeActions);

            Border batteryCard = CreateCard();
            batteryCard.Margin = new Thickness(0, 9, 0, 0);
            Grid batteryRow = CreateStatusRow(OrlaIcon.Battery, 20,
                out _batteryGlyph, out _batteryTitle, out _batteryDetail, true, null, null);
            batteryCard.Child = batteryRow;
            Button batteryAction = CreateActionCard(batteryCard, Loc.OpenPowerSettings, delegate
            {
                ShellActions.OpenUri("ms-settings:powersleep");
                RequestClose(false);
            });
            batteryAction.Margin = new Thickness(0, 9, 0, 0);
            content.Children.Add(batteryAction);

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

        private static Grid CreateStatusRow(OrlaIcon initialIcon, double iconSize,
            out VectorIcon glyph, out TextBlock title, out TextBlock detail,
            bool actionable, string iconActionName, Action iconAction)
        {
            Grid row = new Grid();
            row.MinHeight = 32;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(actionable ? 18 : 0) });
            glyph = Ui.Vector(initialIcon, Loc.Status, iconSize);

            if (iconAction != null)
            {
                Button iconButton = Ui.WrapButton(glyph, iconActionName, 30, 30);
                iconButton.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
                iconButton.Click += delegate { iconAction(); };
                iconButton.HorizontalAlignment = HorizontalAlignment.Left;
                Ui.EnableTopBarMotion(iconButton);
                row.Children.Add(iconButton);
            }
            else
            {
                Border iconBadge = new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = glyph
                };
                row.Children.Add(iconBadge);
            }

            StackPanel labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title = Ui.Text("", 12.5, FontWeights.SemiBold);
            labels.Children.Add(title);
            detail = Ui.Text("", 10.5, FontWeights.Normal);
            detail.Foreground = Ui.SecondaryTextBrush;
            detail.Margin = new Thickness(0, 2, 0, 0);
            detail.HorizontalAlignment = HorizontalAlignment.Left;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            detail.MaxWidth = 228;
            labels.Children.Add(detail);
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);

            if (actionable)
            {
                VectorIcon chevron = Ui.Vector(OrlaIcon.ChevronRight, Loc.OpenWindowsSettings, 12);
                chevron.Foreground = Ui.SecondaryTextBrush;
                chevron.HorizontalAlignment = HorizontalAlignment.Right;
                chevron.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(chevron, 2);
                row.Children.Add(chevron);
            }
            return row;
        }

        private static Button CreateActionCard(Border card, string accessibleName, Action action)
        {
            return CreateActionCard(card, accessibleName, action, 316, double.NaN);
        }

        private static Border CreateSplitToggleCard(Grid content, string toggleName, Action toggle,
            string settingsName, Action openSettings, out Button toggleButton)
        {
            Border surface = CreateCard();
            surface.Width = 316;
            surface.Height = 56;
            surface.Padding = new Thickness(0);

            Grid split = new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(271) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });

            content.Margin = new Thickness(12, 8, 5, 8);
            toggleButton = Ui.WrapButton(content, toggleName, 271, 54);
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
            Button settingsButton = Ui.WrapButton(chevron, settingsName, 43, 54);
            settingsButton.Margin = new Thickness(0);
            Ui.EnableTopBarMotion(settingsButton);
            settingsButton.Click += delegate { openSettings(); };
            Grid.SetColumn(settingsButton, 2);
            split.Children.Add(settingsButton);

            surface.Child = split;
            return surface;
        }

        private static Button CreateCompactActionTile(OrlaIcon icon, string title,
            string accessibleName, Action action)
        {
            Border card = CreateCard();
            card.Padding = new Thickness(9, 8, 9, 8);
            Grid content = new Grid { Height = 38 };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(37) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });

            Border iconBadge = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = Ui.Vector(icon, title, 18)
            };
            content.Children.Add(iconBadge);

            TextBlock label = Ui.Text(title, 11.25, FontWeights.SemiBold);
            label.TextWrapping = TextWrapping.Wrap;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.LineHeight = 14;
            label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            Grid.SetColumn(label, 1);
            content.Children.Add(label);

            VectorIcon chevron = Ui.Vector(OrlaIcon.ChevronRight, Loc.OpenWindowsSettings, 11);
            chevron.Foreground = Ui.SecondaryTextBrush;
            chevron.HorizontalAlignment = HorizontalAlignment.Right;
            chevron.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chevron, 2);
            content.Children.Add(chevron);

            card.Child = content;
            return CreateActionCard(card, accessibleName, action, 154, 56);
        }

        private static Button CreateActionCard(Border card, string accessibleName, Action action,
            double width, double height)
        {
            Color normal = Color.FromRgb(44, 44, 48);
            Color hover = Color.FromRgb(51, 51, 56);
            Color pressed = Color.FromRgb(39, 49, 62);
            Color normalBorder = Color.FromRgb(57, 57, 62);
            SolidColorBrush background = new SolidColorBrush(normal);
            SolidColorBrush border = new SolidColorBrush(normalBorder);
            card.Background = background;
            card.BorderBrush = border;
            card.Width = width;
            if (!double.IsNaN(height)) card.Height = height;
            Button button = Ui.WrapButton(card, accessibleName, width, height);
            button.MouseEnter += delegate { Ui.AnimateBrush(background, hover, 110); };
            button.MouseLeave += delegate { Ui.AnimateBrush(background, button.IsKeyboardFocusWithin ? hover : normal, 110); };
            button.PreviewMouseLeftButtonDown += delegate { Ui.AnimateBrush(background, pressed, 65); };
            button.PreviewMouseLeftButtonUp += delegate { Ui.AnimateBrush(background, button.IsMouseOver ? hover : normal, 90); };
            button.GotKeyboardFocus += delegate
            {
                Ui.AnimateBrush(background, hover, 110);
                Ui.AnimateBrush(border, Color.FromArgb(180, 10, 132, 255), 120);
            };
            button.LostKeyboardFocus += delegate
            {
                Ui.AnimateBrush(background, button.IsMouseOver ? hover : normal, 110);
                Ui.AnimateBrush(border, normalBorder, 120);
            };
            button.Click += delegate { action(); };
            return button;
        }

        private void OnLoaded(object sender, RoutedEventArgs eventArgs)
        {
            PositionOnScreen();
            _shownAt = DateTime.UtcNow;
            BeginBrightnessDetection();
            BeginRadioRefresh();
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
            // A topbar não ativa a si mesma, mas o Windows pode entregar uma
            // transição de foco logo após Show/Activate. Adie a verificação,
            // mas não descarte o evento: um clique externo rápido também deve
            // fechar o painel assim que a estabilização inicial terminar.
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
            _volumePercent.Text = _audioMuted ? Loc.Muted : percent.ToString(Loc.FormattingCulture) + "%";
            _volumeGlyph.SetIcon(StatusIcons.Volume(percent, _audioMuted));
            _statusMonitor.SetVolumePercent(eventArgs.NewValue);
        }

        private void OnBrightnessChanged(object sender, RoutedPropertyChangedEventArgs<double> eventArgs)
        {
            if (_updatingBrightness || _brightness == null || _brightness.SupportedCount == 0) return;
            _pendingBrightness = Math.Max(0, Math.Min(100, (int)Math.Round(eventArgs.NewValue)));
            _brightnessDetail.Text = _pendingBrightness.ToString(Loc.FormattingCulture) + "% • "
                + Loc.BrightnessTargets(_brightness.IntegratedCount, _brightness.DdcCount);
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
                        _brightnessDetail.Text = percent.ToString(Loc.FormattingCulture) + "% • "
                            + Loc.BrightnessTargets(service.IntegratedCount, service.DdcCount);
                    }
                    else
                    {
                        _brightnessSlider.IsEnabled = false;
                        _brightnessDetail.Text = Loc.DdcUnavailable;
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
            RefreshBattery(state.Battery);
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
                _muteButton.IsEnabled = false;
                _volumePercent.Text = Loc.Unavailable;
                _volumeGlyph.SetIcon(OrlaIcon.VolumeMuted);
                _volumeGlyph.Foreground = Ui.ErrorBrush;
                return;
            }

            _updatingVolume = true;
            _volumeSlider.Value = state.VolumePercent;
            _updatingVolume = false;
            _audioMuted = state.IsMuted;
            _volumePercent.Text = state.IsMuted ? Loc.Muted : state.VolumePercent.ToString(Loc.FormattingCulture) + "%";
            _volumeGlyph.SetIcon(StatusIcons.Volume(state.VolumePercent, state.IsMuted));
            _volumeGlyph.Foreground = state.IsMuted ? Ui.SecondaryTextBrush : Ui.PrimaryTextBrush;
            _volumeGlyph.SetAccessibleName(Loc.VolumeStatus(state.VolumePercent, state.IsMuted));
            _muteText.Text = state.IsMuted ? Loc.Unmute : Loc.Mute;
        }

        private void RefreshNetwork(NetworkSnapshot state)
        {
            if (state == null) return;
            _networkTitle.Text = state.Name;
            _networkDetail.Text = state.Detail;
            _networkGlyph.SetIcon(StatusIcons.Network(state));
            _networkGlyph.Foreground = state.IsAvailable ? Ui.SuccessBrush : Ui.ErrorBrush;
            _networkGlyph.SetAccessibleName(state.Name + " • " + state.Detail);
        }

        private void RefreshBluetooth(BluetoothSnapshot state)
        {
            try
            {
                _bluetoothSurface.Visibility = Visibility.Visible;
                _bluetoothTitle.Text = Loc.Bluetooth;
                _bluetoothDetail.Text = !state.IsEnabled ? Loc.Disabled
                    : state.IsConnected ? state.DeviceName : Loc.NoBluetoothDevice;
                _bluetoothGlyph.Foreground = state.IsConnected ? Ui.AccentBrush : Ui.SecondaryTextBrush;
                _bluetoothGlyph.SetAccessibleName(Loc.BluetoothStatus(state));
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao atualizar Bluetooth do painel: " + exception.Message);
                _bluetoothSurface.Visibility = Visibility.Visible;
                _bluetoothTitle.Text = Loc.Bluetooth;
                _bluetoothDetail.Text = Loc.TemporarilyUnavailable;
                _bluetoothGlyph.Foreground = Ui.SecondaryTextBrush;
            }
        }

        private void RefreshBattery(BatterySnapshot state)
        {
            try
            {
                _batteryTitle.Text = state.Status;
                _batteryDetail.Text = state.Detail;
                _batteryGlyph.SetBatteryState(state.Percent, state.HasBattery, state.IsCharging);
                _batteryGlyph.Foreground = !state.HasBattery ? Ui.SecondaryTextBrush
                    : state.IsCharging ? Ui.SuccessBrush
                    : state.Percent <= 20 ? Ui.ErrorBrush : Ui.PrimaryTextBrush;
                _batteryGlyph.SetAccessibleName(Loc.BatteryStatus(state));
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao consultar bateria: " + exception.Message);
                _batteryTitle.Text = Loc.Energy;
                _batteryDetail.Text = Loc.TemporarilyUnavailable;
                _batteryGlyph.Foreground = Ui.SecondaryTextBrush;
            }
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

    // Slider desenhado em uma única superfície. Mantém a semântica e o
    // AutomationPeer do Slider, sem a árvore visual do template padrão.
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
