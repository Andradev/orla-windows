using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Orla
{
    // Janela deliberadamente transitória: serviços, callbacks e timer existem
    // somente entre Show e Closed. Nenhum conteúdo fica escondido em memória.
    internal sealed class QuickPanelWindow : Window, IDisposable
    {
        private readonly string _screenDeviceName;
        private readonly Border _surface;
        private readonly AudioService _audio;
        private readonly NetworkStatusService _network;
        private readonly DispatcherTimer _batteryTimer;
        private readonly DispatcherTimer _deactivationTimer;
        private readonly TextBlock _networkTitle;
        private readonly TextBlock _networkDetail;
        private readonly TextBlock _networkGlyph;
        private readonly Button _bluetoothCard;
        private readonly TextBlock _bluetoothTitle;
        private readonly TextBlock _bluetoothDetail;
        private readonly TextBlock _bluetoothGlyph;
        private readonly LightweightSlider _volumeSlider;
        private readonly TextBlock _volumePercent;
        private readonly TextBlock _volumeGlyph;
        private readonly TextBlock _muteText;
        private readonly Button _muteButton;
        private readonly TextBlock _batteryTitle;
        private readonly TextBlock _batteryDetail;
        private readonly TextBlock _batteryGlyph;
        private bool _updatingVolume;
        private bool _audioMuted;
        private bool _disposed;
        private DateTime _shownAt;

        internal bool RestorePreviousFocusOnClose { get; private set; }
        internal string ScreenDeviceName { get { return _screenDeviceName; } }

        internal QuickPanelWindow(string screenDeviceName)
        {
            _screenDeviceName = screenDeviceName;
            Title = Loc.QuickPanelTitle;
            Width = 344;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 520;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = true;
            WindowStartupLocation = WindowStartupLocation.Manual;

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
            Button close = Ui.WrapButton(Ui.Glyph("\uE711", Loc.Close), Loc.CloseControls, 30, 30);
            Ui.EnableTopBarMotion(close);
            close.Click += delegate { RequestClose(true); };
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            content.Children.Add(header);

            Border networkCard = CreateCard();
            Grid networkRow = CreateStatusRow(out _networkGlyph, out _networkTitle, out _networkDetail,
                true, null, null);
            _networkGlyph.Text = "\uE701";
            networkCard.Child = networkRow;
            Button networkAction = CreateActionCard(networkCard, Loc.OpenNetworkSettings, delegate
            {
                NetworkSnapshot state = _network.ReadSnapshot();
                ShellActions.OpenUri(state.IsWifi ? "ms-settings:network-wifi" : "ms-settings:network-status");
                RequestClose(false);
            });
            content.Children.Add(networkAction);

            Border bluetoothSurface = CreateCard();
            Grid bluetoothRow = CreateStatusRow(out _bluetoothGlyph, out _bluetoothTitle, out _bluetoothDetail,
                true, null, null);
            _bluetoothGlyph.Text = "\uE702";
            bluetoothSurface.Child = bluetoothRow;
            _bluetoothCard = CreateActionCard(bluetoothSurface, Loc.OpenBluetoothSettings, delegate
            {
                ShellActions.OpenUri("ms-settings:bluetooth");
                RequestClose(false);
            });
            _bluetoothCard.Margin = new Thickness(0, 9, 0, 0);
            content.Children.Add(_bluetoothCard);

            Border audioCard = CreateCard();
            audioCard.Margin = new Thickness(0, 9, 0, 0);
            StackPanel audioContent = new StackPanel();
            TextBlock audioTitle;
            Grid audioHeader = CreateStatusRow(out _volumeGlyph, out audioTitle, out _volumePercent,
                false, Loc.ToggleMute, delegate { _audio.ToggleMute(); });
            _volumeGlyph.Text = "\uE767";
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
            TextBlock muteGlyph = Ui.Glyph("\uE74F", Loc.ToggleMute);
            muteGlyph.Margin = new Thickness(0, 0, 6, 0);
            muteContent.Children.Add(muteGlyph);
            muteContent.Children.Add(_muteText);
            _muteButton = Ui.WrapButton(muteContent, Loc.ToggleMute, 104, 30);
            Ui.EnableTopBarMotion(_muteButton);
            _muteButton.Click += delegate { _audio.ToggleMute(); };
            audioActions.Children.Add(_muteButton);
            audioContent.Children.Add(audioActions);
            audioCard.Child = audioContent;
            content.Children.Add(audioCard);

            Border batteryCard = CreateCard();
            batteryCard.Margin = new Thickness(0, 9, 0, 0);
            Grid batteryRow = CreateStatusRow(out _batteryGlyph, out _batteryTitle, out _batteryDetail,
                true, null, null);
            _batteryGlyph.Text = "\uEBA0";
            batteryCard.Child = batteryRow;
            Button batteryAction = CreateActionCard(batteryCard, Loc.OpenPowerSettings, delegate
            {
                ShellActions.OpenUri("ms-settings:powersleep");
                RequestClose(false);
            });
            batteryAction.Margin = new Thickness(0, 9, 0, 0);
            content.Children.Add(batteryAction);

            Content = _surface;

            _audio = new AudioService();
            _network = new NetworkStatusService();
            _audio.StateChanged += OnAudioStateChanged;
            _network.StateChanged += OnNetworkStateChanged;

            _batteryTimer = new DispatcherTimer(DispatcherPriority.Background);
            _batteryTimer.Interval = TimeSpan.FromSeconds(15);
            _batteryTimer.Tick += delegate
            {
                RefreshAudio(_audio.ReadState());
                RefreshNetwork();
                RefreshBattery();
                RefreshBluetooth();
            };

            _deactivationTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
            _deactivationTimer.Tick += OnDeactivationTimerTick;

            Loaded += OnLoaded;
            Activated += OnActivated;
            Deactivated += OnDeactivated;
            PreviewKeyDown += OnPreviewKeyDown;
            Closed += OnClosed;
            RefreshAudio(_audio.ReadState());
            RefreshNetwork();
            RefreshBattery();
            RefreshBluetooth();
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

        private static Grid CreateStatusRow(out TextBlock glyph, out TextBlock title, out TextBlock detail,
            bool actionable, string iconActionName, Action iconAction)
        {
            Grid row = new Grid();
            row.MinHeight = 32;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(actionable ? 18 : 0) });
            glyph = Ui.Glyph("", Loc.Status);
            Ui.ConfigureStatusGlyph(glyph, 15);

            if (iconAction != null)
            {
                Button iconButton = Ui.WrapButton(glyph, iconActionName, 30, 30);
                iconButton.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
                iconButton.Click += delegate { iconAction(); };
                iconButton.HorizontalAlignment = HorizontalAlignment.Left;
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
                TextBlock chevron = Ui.Glyph("\uE76C", Loc.OpenWindowsSettings);
                chevron.FontSize = 10;
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
            SolidColorBrush normal = new SolidColorBrush(Color.FromRgb(44, 44, 48));
            SolidColorBrush hover = new SolidColorBrush(Color.FromRgb(51, 51, 56));
            SolidColorBrush pressed = new SolidColorBrush(Color.FromRgb(39, 49, 62));
            card.Background = normal;
            card.Width = 316;
            Button button = Ui.WrapButton(card, accessibleName, 316, double.NaN);
            button.MouseEnter += delegate { card.Background = hover; };
            button.MouseLeave += delegate { card.Background = normal; };
            button.PreviewMouseLeftButtonDown += delegate { card.Background = pressed; };
            button.PreviewMouseLeftButtonUp += delegate { card.Background = button.IsMouseOver ? hover : normal; };
            button.Click += delegate { action(); };
            return button;
        }

        private void OnLoaded(object sender, RoutedEventArgs eventArgs)
        {
            PositionOnScreen();
            _shownAt = DateTime.UtcNow;
            _batteryTimer.Start();
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
            if (_updatingVolume || !_audio.IsAvailable) return;
            int percent = Math.Max(0, Math.Min(100, (int)Math.Round(eventArgs.NewValue)));
            _volumePercent.Text = _audioMuted ? Loc.Muted : percent.ToString(Loc.FormattingCulture) + "%";
            _volumeGlyph.Text = StatusGlyphs.Volume(percent, _audioMuted);
            _audio.SetVolumePercent(eventArgs.NewValue);
        }

        private void OnAudioStateChanged(object sender, AudioStateChangedEventArgs eventArgs)
        {
            if (_disposed) return;
            Dispatcher.BeginInvoke(new Action(delegate { RefreshAudio(eventArgs); }), DispatcherPriority.Background);
        }

        private void RefreshAudio(AudioStateChangedEventArgs state)
        {
            if (_disposed) return;
            if (!_audio.IsAvailable)
            {
                _volumeSlider.IsEnabled = false;
                _muteButton.IsEnabled = false;
                _volumePercent.Text = Loc.Unavailable;
                _volumeGlyph.Foreground = Ui.ErrorBrush;
                return;
            }

            _updatingVolume = true;
            _volumeSlider.Value = state.VolumePercent;
            _updatingVolume = false;
            _audioMuted = state.IsMuted;
            _volumePercent.Text = state.IsMuted ? Loc.Muted : state.VolumePercent.ToString(Loc.FormattingCulture) + "%";
            _volumeGlyph.Text = StatusGlyphs.Volume(state.VolumePercent, state.IsMuted);
            _volumeGlyph.Foreground = state.IsMuted ? Ui.SecondaryTextBrush : Ui.PrimaryTextBrush;
            _muteText.Text = state.IsMuted ? Loc.Unmute : Loc.Mute;
        }

        private void OnNetworkStateChanged(object sender, EventArgs eventArgs)
        {
            if (_disposed) return;
            Dispatcher.BeginInvoke(new Action(RefreshNetwork), DispatcherPriority.Background);
        }

        private void RefreshNetwork()
        {
            NetworkSnapshot state = _network.ReadSnapshot();
            _networkTitle.Text = state.Name;
            _networkDetail.Text = state.Detail;
            _networkGlyph.Text = StatusGlyphs.Network(state);
            _networkGlyph.Foreground = state.IsAvailable ? Ui.SuccessBrush : Ui.ErrorBrush;
        }

        private void RefreshBluetooth()
        {
            try
            {
                BluetoothSnapshot state = BluetoothSnapshot.Read();
                _bluetoothCard.Visibility = Visibility.Visible;
                _bluetoothTitle.Text = Loc.Bluetooth;
                _bluetoothDetail.Text = !state.IsEnabled ? Loc.Disabled
                    : state.IsConnected ? state.DeviceName : Loc.NoBluetoothDevice;
                _bluetoothGlyph.Foreground = state.IsConnected ? Ui.AccentBrush : Ui.SecondaryTextBrush;
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao atualizar Bluetooth do painel: " + exception.Message);
                _bluetoothCard.Visibility = Visibility.Visible;
                _bluetoothTitle.Text = Loc.Bluetooth;
                _bluetoothDetail.Text = Loc.TemporarilyUnavailable;
                _bluetoothGlyph.Foreground = Ui.SecondaryTextBrush;
            }
        }

        private void RefreshBattery()
        {
            try
            {
                BatterySnapshot state = BatterySnapshot.Read();
                _batteryTitle.Text = state.Status;
                _batteryDetail.Text = state.Detail;
                _batteryGlyph.Text = StatusGlyphs.Battery(state);
                _batteryGlyph.Foreground = !state.HasBattery ? Ui.SecondaryTextBrush
                    : state.IsCharging ? Ui.SuccessBrush
                    : state.Percent <= 20 ? Ui.ErrorBrush : Ui.PrimaryTextBrush;
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
            _batteryTimer.Stop();
            _deactivationTimer.Stop();
            _volumeSlider.ValueChanged -= OnVolumeChanged;
            _audio.StateChanged -= OnAudioStateChanged;
            _network.StateChanged -= OnNetworkStateChanged;
            _network.Dispose();
            _audio.Dispose();
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
