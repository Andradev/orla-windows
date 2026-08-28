using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using DrawingIcon = System.Drawing.Icon;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using WpfEllipse = System.Windows.Shapes.Ellipse;

[assembly: AssemblyTitle("Orla")]
[assembly: AssemblyDescription("Topbar e dock leves para Windows")]
[assembly: AssemblyCompany("Orla contributors")]
[assembly: AssemblyProduct("Orla")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("1.2.8.0")]
[assembly: AssemblyFileVersion("1.2.8.0")]

namespace Orla
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Any(delegate(string value) { return string.Equals(value, "--restore", StringComparison.OrdinalIgnoreCase); }))
            {
                TaskbarController.RestoreAll();
                return;
            }

            if (RedirectExternalCopyToInstallation(args)) return;

            bool automaticStartup = args.Any(delegate(string value)
            {
                return string.Equals(value, "--startup", StringComparison.OrdinalIgnoreCase);
            });

            bool created;
            _mutex = new Mutex(true, "Orla.SingleInstance", out created);
            if (!created)
            {
                return;
            }

            if (automaticStartup) Logger.Write("Inicialização automática do usuário confirmada.");

            // O renderizador padrão reserva centenas de MB na GPU integrada.
            // SoftwareOnly mantém a memória previsível neste PC.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs eventArgs)
            {
                Logger.Write("Falha não tratada: " + eventArgs.ExceptionObject);
                TaskbarController.RestoreAll();
            };

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ShellController controller = new ShellController(app);
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs eventArgs)
            {
                Logger.Write("Falha da interface: " + eventArgs.Exception);
                eventArgs.Handled = true;
                controller.Exit(true);
            };

            controller.Start();
            app.Run();
        }

        private static bool RedirectExternalCopyToInstallation(string[] args)
        {
            if (args.Any(delegate(string value)
            {
                return string.Equals(value, "--portable", StringComparison.OrdinalIgnoreCase);
            })) return false;

            try
            {
                string currentPath = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                string installedPath = Path.GetFullPath(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Orla", "Orla.exe"));
                if (string.Equals(currentPath, installedPath, StringComparison.OrdinalIgnoreCase)
                    || !File.Exists(installedPath)) return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = installedPath,
                    Arguments = args.Any(delegate(string value)
                    {
                        return string.Equals(value, "--startup", StringComparison.OrdinalIgnoreCase);
                    }) ? "--startup" : string.Empty,
                    UseShellExecute = true
                });
                Logger.Write("Cópia externa redirecionada para a instalação oficial: " + currentPath);
                return true;
            }
            catch (Exception exception)
            {
                Logger.Write("Não foi possível redirecionar para a instalação oficial: " + exception.Message);
                return false;
            }
        }
    }

    internal sealed class ShellController : IDisposable
    {
        private readonly Application _application;
        private readonly ShellSettings _settings;
        private readonly List<TopBarWindow> _topBars = new List<TopBarWindow>();
        private readonly List<DockWindow> _docks = new List<DockWindow>();
        private readonly uint _currentProcessId = (uint)Process.GetCurrentProcess().Id;
        private BareWindowsKeyHook _windowsKeyHook;
        private ForegroundWindowTracker _foregroundTracker;
        private QuickPanelWindow _quickPanel;
        private IntPtr _quickPanelPreviousForeground;
        private IntPtr _lastExternalForeground;
        private DateTime _lastQuickPanelClosedAt = DateTime.MinValue;
        private string _lastQuickPanelClosedScreen;
        private DispatcherTimer _taskbarTimer;
        private bool _exiting;
        private readonly object _fluentQueueSync = new object();
        private readonly Queue<FluentSearchRequest> _fluentQueue = new Queue<FluentSearchRequest>();
        private bool _fluentWorkerRunning;
        private SystemStatusMonitor _statusMonitor;

        private sealed class FluentSearchRequest
        {
            internal readonly string Path;
            internal readonly string TargetScreen;

            internal FluentSearchRequest(string path, string targetScreen)
            {
                Path = path;
                TargetScreen = targetScreen;
            }
        }

        internal ShellController(Application application)
        {
            _application = application;
            _settings = ShellSettings.Load();
        }

        internal void Start()
        {
            Logger.Write("Iniciando Orla.");
            IntPtr initialForeground = NativeMethods.GetForegroundWindow();
            uint initialProcessId;
            NativeMethods.GetWindowThreadProcessId(initialForeground, out initialProcessId);
            if (initialProcessId != 0 && initialProcessId != _currentProcessId)
                _lastExternalForeground = initialForeground;
            TaskbarController.HideAll();

            _statusMonitor = new SystemStatusMonitor();
            CreateBars();

            _foregroundTracker = new ForegroundWindowTracker(delegate(IntPtr foreground)
            {
                _application.Dispatcher.BeginInvoke(new Action(delegate
                {
                    uint processId;
                    NativeMethods.GetWindowThreadProcessId(foreground, out processId);
                    // Flyouts da própria Orla não apagam o título nem o indicador
                    // do aplicativo que continuava ativo antes de abrir o painel.
                    if (processId == _currentProcessId) return;
                    _lastExternalForeground = foreground;
                    foreach (DockWindow dock in _docks) dock.ForegroundChanged(foreground);
                    foreach (TopBarWindow topBar in _topBars) topBar.ForegroundChanged(foreground);
                }), DispatcherPriority.Background);
            });
            _foregroundTracker.Start();

            // O Explorer pode recriar a taskbar secundária após uma mudança de área útil.
            // Esta verificação barata garante que ela continue oculta nos dois monitores.
            _taskbarTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
            _taskbarTimer.Interval = TimeSpan.FromSeconds(5);
            _taskbarTimer.Tick += delegate { TaskbarController.HideAll(false); };
            _taskbarTimer.Start();

            if (_settings.BareWindowsKeyOpensFluent && File.Exists(_settings.FluentSearchPath))
            {
                _windowsKeyHook = new BareWindowsKeyHook(LaunchFluentSearch);
                _windowsKeyHook.Start();
            }
            else if (_settings.BareWindowsKeyOpensFluent)
            {
                Logger.Write("Fluent Search ausente; tecla Windows nativa preservada.");
            }

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _application.SessionEnding += OnSessionEnding;
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs eventArgs)
        {
            if (_exiting)
            {
                return;
            }

            _application.Dispatcher.BeginInvoke(new Action(delegate
            {
                RecreateBars();
            }));
        }

        private void CreateBars()
        {
            Forms.Screen[] screens = Forms.Screen.AllScreens
                .OrderBy(delegate(Forms.Screen screen) { return screen.Primary ? 0 : 1; })
                .ThenBy(delegate(Forms.Screen screen) { return screen.Bounds.Left; })
                .ToArray();

            foreach (Forms.Screen screen in screens)
            {
                TopBarWindow topBar = new TopBarWindow(_settings, this, _statusMonitor, screen.DeviceName);
                DockWindow dock = new DockWindow(_settings, this, screen.DeviceName);
                _topBars.Add(topBar);
                _docks.Add(dock);
                topBar.Show();
                dock.Show();
            }
            Logger.Write("Topbars e docks criados em " + screens.Length.ToString(CultureInfo.InvariantCulture) + " monitor(es).");
        }

        private void RecreateBars()
        {
            DisposeBars();
            CreateBars();
            TaskbarController.HideAll(false);
        }

        private void DisposeBars()
        {
            CloseQuickPanel(false);
            foreach (DockWindow dock in _docks.ToArray()) dock.Dispose();
            foreach (TopBarWindow topBar in _topBars.ToArray()) topBar.Dispose();
            _docks.Clear();
            _topBars.Clear();
        }

        internal void ToggleQuickPanel(string targetScreenDeviceName)
        {
            string targetScreen = ResolveFluentTargetScreen(targetScreenDeviceName);
            if (_quickPanel != null)
            {
                bool sameScreen = string.Equals(_quickPanel.ScreenDeviceName, targetScreen, StringComparison.OrdinalIgnoreCase);
                CloseQuickPanel(sameScreen);
                if (sameScreen) return;
            }

            // Ao clicar novamente na topbar, WPF pode entregar Deactivated ao
            // painel alguns milissegundos antes do Click do botão. Nesse caso a
            // janela já fechou; não a reabra na mesma ação.
            if ((DateTime.UtcNow - _lastQuickPanelClosedAt).TotalMilliseconds < 350
                && string.Equals(_lastQuickPanelClosedScreen, targetScreen, StringComparison.OrdinalIgnoreCase))
            {
                if (_lastExternalForeground != IntPtr.Zero && NativeMethods.IsWindow(_lastExternalForeground))
                    ShellActions.ActivateWindowWithRetry(_lastExternalForeground);
                return;
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            uint processId;
            NativeMethods.GetWindowThreadProcessId(foreground, out processId);
            _quickPanelPreviousForeground = processId != 0 && processId != _currentProcessId
                ? foreground
                : _lastExternalForeground;

            QuickPanelWindow panel = new QuickPanelWindow(targetScreen, _statusMonitor);
            _quickPanel = panel;
            panel.Closed += delegate
            {
                if (!ReferenceEquals(_quickPanel, panel)) return;
                bool restore = panel.RestorePreviousFocusOnClose;
                _lastQuickPanelClosedAt = DateTime.UtcNow;
                _lastQuickPanelClosedScreen = panel.ScreenDeviceName;
                _quickPanel = null;
                RestoreQuickPanelFocus(restore);
            };
            panel.Show();
            panel.Activate();
        }

        private void CloseQuickPanel(bool restorePreviousFocus)
        {
            QuickPanelWindow panel = _quickPanel;
            if (panel == null) return;
            panel.RequestClose(restorePreviousFocus);
        }

        private void RestoreQuickPanelFocus(bool requested)
        {
            IntPtr previous = _quickPanelPreviousForeground;
            _quickPanelPreviousForeground = IntPtr.Zero;
            if (!requested || previous == IntPtr.Zero || !NativeMethods.IsWindow(previous)) return;

            IntPtr current = NativeMethods.GetForegroundWindow();
            uint currentProcessId;
            NativeMethods.GetWindowThreadProcessId(current, out currentProcessId);
            if (current == IntPtr.Zero || currentProcessId == _currentProcessId)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    ShellActions.ActivateWindowWithRetry(previous);
                });
            }
        }

        internal bool IsPinned(string path)
        {
            return _settings.PinnedApplications.Any(delegate(PinnedApplication app)
            {
                return string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase);
            });
        }

        internal void PinApplication(string name, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || IsPinned(path)) return;
            _settings.PinnedApplications.Add(new PinnedApplication
            {
                Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(path) : name,
                Path = path
            });
            _settings.SavePinnedApplications();
            RefreshDocks();
        }

        internal void UnpinApplication(string path)
        {
            _settings.PinnedApplications.RemoveAll(delegate(PinnedApplication app)
            {
                return string.Equals(app.Path, path, StringComparison.OrdinalIgnoreCase);
            });
            _settings.SavePinnedApplications();
            RefreshDocks();
        }

        internal void MoveApplication(string sourceKey, string targetKey, IEnumerable<string> visibleOrder, bool insertAfter)
        {
            if (string.IsNullOrWhiteSpace(sourceKey) || string.IsNullOrWhiteSpace(targetKey)
                || string.Equals(sourceKey, targetKey, StringComparison.OrdinalIgnoreCase)) return;

            List<string> order = visibleOrder
                .Where(delegate(string key) { return !string.IsNullOrWhiteSpace(key); })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            int sourceIndex = order.FindIndex(delegate(string key) { return string.Equals(key, sourceKey, StringComparison.OrdinalIgnoreCase); });
            if (sourceIndex < 0) return;
            order.RemoveAt(sourceIndex);
            int targetIndex = order.FindIndex(delegate(string key) { return string.Equals(key, targetKey, StringComparison.OrdinalIgnoreCase); });
            if (targetIndex < 0) return;
            if (insertAfter) targetIndex++;
            order.Insert(Math.Min(targetIndex, order.Count), sourceKey);

            _settings.ApplicationOrder = order;
            _settings.SaveApplicationOrder();
            RefreshDocks();
        }

        internal void SynchronizeForeground(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero) return;
            foreach (DockWindow dock in _docks) dock.ForegroundChanged(foreground);
            foreach (TopBarWindow topBar in _topBars) topBar.ForegroundChanged(foreground);
        }

        private void RefreshDocks()
        {
            foreach (DockWindow dock in _docks) dock.RefreshNow();
        }

        private void OnSessionEnding(object sender, SessionEndingCancelEventArgs eventArgs)
        {
            Exit(false);
        }

        internal void LaunchFluentSearch()
        {
            LaunchFluentSearch(null);
        }

        internal void LaunchFluentSearch(string targetScreenDeviceName)
        {
            try
            {
                string path = _settings.FluentSearchPath;
                if (!File.Exists(path))
                {
                    Logger.Write("Fluent Search não encontrado em: " + path);
                    return;
                }

                string targetScreen = ResolveFluentTargetScreen(targetScreenDeviceName);
                Logger.Write("Fluent Search solicitado para " + targetScreen + ".");
                bool startWorker = false;
                lock (_fluentQueueSync)
                {
                    _fluentQueue.Enqueue(new FluentSearchRequest(path, targetScreen));
                    if (!_fluentWorkerRunning)
                    {
                        _fluentWorkerRunning = true;
                        startWorker = true;
                    }
                }
                if (startWorker) ThreadPool.QueueUserWorkItem(delegate { ProcessFluentQueue(); });
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao abrir Fluent Search: " + exception);
            }
        }

        private void ProcessFluentQueue()
        {
            while (true)
            {
                FluentSearchRequest request;
                lock (_fluentQueueSync)
                {
                    if (_fluentQueue.Count == 0)
                    {
                        _fluentWorkerRunning = false;
                        return;
                    }
                    request = _fluentQueue.Dequeue();
                }
                ActivateFluentSearch(request.Path, request.TargetScreen);
            }
        }

        private static string ResolveFluentTargetScreen(string requestedScreenDeviceName)
        {
            if (!string.IsNullOrWhiteSpace(requestedScreenDeviceName)
                && Forms.Screen.AllScreens.Any(delegate(Forms.Screen screen)
                {
                    return string.Equals(screen.DeviceName, requestedScreenDeviceName, StringComparison.OrdinalIgnoreCase);
                }))
            {
                return requestedScreenDeviceName;
            }

            NativeMethods.ScreenPoint cursor;
            if (NativeMethods.GetCursorPos(out cursor))
                return Forms.Screen.FromPoint(new DrawingPoint(cursor.X, cursor.Y)).DeviceName;
            return Forms.Screen.PrimaryScreen.DeviceName;
        }

        private static bool RequestFluentSearchWindow(int timeoutMilliseconds, string targetScreenDeviceName)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            string pipeName = "FluentSearch" + Environment.UserName;
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                try
                {
                    using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                    {
                        pipe.Connect(250);
                        // Transfere a janela ainda oculta para o monitor solicitado.
                        // Depois de visível, o próprio Fluent controla tamanho e
                        // posição para não haver um segundo salto vertical.
                        IntPtr hiddenWindow = WindowCatalog.FindProcessWindow("FluentSearch", "Fluent Search", false);
                        MoveFluentSearchToScreen(hiddenWindow, targetScreenDeviceName);
                        using (StreamWriter writer = new StreamWriter(pipe, new UTF8Encoding(false)))
                        {
                            writer.AutoFlush = true;
                            writer.WriteLine("{\"MessageType\":0}");
                        }
                    }
                    return true;
                }
                catch (TimeoutException) { }
                catch (IOException) { }
                Thread.Sleep(100);
            }
            return false;
        }

        private static bool WaitForFluentSearchVisibility(bool visible, int timeoutMilliseconds)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
            {
                IntPtr handle = WindowCatalog.FindProcessWindow("FluentSearch", "Fluent Search", true);
                if ((handle != IntPtr.Zero) == visible) return true;
                Thread.Sleep(5);
            }
            return false;
        }

        private static bool MoveFluentSearchToScreen(IntPtr handle, string targetScreenDeviceName)
        {
            if (handle == IntPtr.Zero) return false;
            Forms.Screen screen = Forms.Screen.AllScreens.FirstOrDefault(delegate(Forms.Screen candidate)
            {
                return string.Equals(candidate.DeviceName, targetScreenDeviceName, StringComparison.OrdinalIgnoreCase);
            }) ?? Forms.Screen.PrimaryScreen;

            NativeMethods.Rect current;
            if (!NativeMethods.GetWindowRect(handle, out current)) return false;
            DrawingRectangle working = screen.WorkingArea;
            int width = Math.Min(Math.Max(1, current.right - current.left), working.Width);
            int height = Math.Min(Math.Max(1, current.bottom - current.top), working.Height);
            int left = working.Left + Math.Max(0, (working.Width - width) / 2);
            int top = working.Top + Math.Max(0, (working.Height - height) / 2);

            return NativeMethods.SetWindowPos(
                handle,
                IntPtr.Zero,
                left,
                top,
                0,
                0,
                NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        }

        private void ActivateFluentSearch(string path, string targetScreenDeviceName)
        {
            try
            {
                Process[] running = Process.GetProcessesByName("FluentSearch");
                if (running.Length == 0)
                {
                    Process.Start(new ProcessStartInfo(path, "-forceShow") { UseShellExecute = true });
                }
                foreach (Process process in running) process.Dispose();

                IntPtr visibleWindow = WindowCatalog.FindProcessWindow("FluentSearch", "Fluent Search", true);
                bool wasVisible = visibleWindow != IntPtr.Zero;
                if (wasVisible)
                {
                    // O canal RequestOpen de algumas versões apenas reafirma a
                    // janela quando ela já está visível. WM_CLOSE passa pelo
                    // ciclo normal do Fluent Search: ele oculta a janela e
                    // preserva o processo/índice, sem forçar ShowWindow.
                    NativeMethods.PostMessage(visibleWindow, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);
                    if (WaitForFluentSearchVisibility(false, 2000))
                    {
                        Logger.Write("Fluent Search ocultado pelo fechamento cooperativo.");
                        return;
                    }
                    Logger.Write("Fluent Search não respondeu ao fechamento cooperativo; tentando o canal nativo.");
                }
                if (RequestFluentSearchWindow(4000, targetScreenDeviceName))
                {
                    // A animação de saída do Fluent mantém WS_VISIBLE por mais
                    // de 500 ms em algumas GPUs. Espere o estado real antes de
                    // liberar o próximo pedido da fila; assim um terceiro toque
                    // não interpreta por engano que a busca ainda está aberta.
                    bool reachedExpectedState = WaitForFluentSearchVisibility(!wasVisible, wasVisible ? 2000 : 2500);
                    if (reachedExpectedState)
                    {
                        Logger.Write(wasVisible
                            ? "Fluent Search ocultado pelo toggle nativo."
                            : "Fluent Search aberto pelo canal nativo em " + targetScreenDeviceName + ".");
                    }
                    else
                    {
                        Logger.Write("Fluent Search recebeu o toggle, mas não mudou de visibilidade no tempo esperado.");
                    }
                }
                else
                    Logger.Write("O canal nativo do Fluent Search não respondeu em " + targetScreenDeviceName + ".");
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao recuperar ativação do Fluent Search: " + exception.Message);
            }
        }

        internal void Exit(bool fromFailure)
        {
            if (_exiting)
            {
                return;
            }

            _exiting = true;
            Logger.Write(fromFailure ? "Saindo após falha; restaurando barra nativa." : "Saindo; restaurando barra nativa.");
            Dispose();
            TaskbarController.RestoreAll();
            _application.Shutdown(fromFailure ? 1 : 0);
        }

        public void Dispose()
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _application.SessionEnding -= OnSessionEnding;

            if (_windowsKeyHook != null)
            {
                _windowsKeyHook.Dispose();
                _windowsKeyHook = null;
            }

            if (_foregroundTracker != null)
            {
                _foregroundTracker.Dispose();
                _foregroundTracker = null;
            }

            if (_taskbarTimer != null)
            {
                _taskbarTimer.Stop();
                _taskbarTimer = null;
            }

            DisposeBars();
            if (_statusMonitor != null)
            {
                _statusMonitor.Dispose();
                _statusMonitor = null;
            }
        }
    }

    internal sealed class PinnedApplication
    {
        internal string Name;
        internal string Path;
    }

    internal sealed class DockApplication
    {
        internal string Name;
        internal string Path;
        internal string Key;
        internal List<WindowItem> Windows;
        internal bool Pinned;
        internal int DefaultOrder;
    }

    internal sealed class DockIndicatorState
    {
        internal HashSet<IntPtr> Handles;
        internal WpfEllipse Indicator;
    }

    internal sealed class ShellSettings
    {
        internal string FluentSearchPath;
        internal bool BareWindowsKeyOpensFluent;
        internal int TopBarHeight;
        internal int DockReservedHeight;
        internal List<PinnedApplication> PinnedApplications;
        internal List<string> ApplicationOrder;

        internal static ShellSettings Load()
        {
            ShellSettings settings = new ShellSettings();
            settings.FluentSearchPath = @"C:\Program Files\Fluent Search\FluentSearch.exe";
            settings.BareWindowsKeyOpensFluent = true;
            settings.TopBarHeight = 29;
            settings.DockReservedHeight = 61;
            settings.PinnedApplications = new List<PinnedApplication>();
            settings.ApplicationOrder = new List<string>();

            try
            {
                string path = Path.Combine(Paths.DataDirectory, "settings.ini");
                string legacyPath = Path.Combine(Paths.LegacyDataDirectory, "settings.ini");
                if (!File.Exists(path) && File.Exists(legacyPath))
                {
                    Directory.CreateDirectory(Paths.DataDirectory);
                    File.Copy(legacyPath, path, false);
                    string[] migratedLines = File.ReadAllLines(path, Encoding.UTF8);
                    if (migratedLines.Length > 0 && migratedLines[0].StartsWith("# Victor Shell", StringComparison.OrdinalIgnoreCase))
                        migratedLines[0] = "# Orla - configuração simples e reversível";
                    File.WriteAllLines(path, migratedLines, Encoding.UTF8);
                    Logger.Write("Configuração migrada da instalação anterior para Orla.");
                }
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Paths.DataDirectory);
                    settings.PinnedApplications = PinnedCatalog.GetDefaultPinnedApplications();
                    List<string> initialLines = new List<string>
                    {
                        "# Orla - configuração simples e reversível",
                        "FluentSearchPath=" + settings.FluentSearchPath,
                        "BareWindowsKeyOpensFluent=true",
                        "SettingsFormat=2",
                        "NativePinnedImportedV5=true",
                        "TopBarHeight=29",
                        "DockReservedHeight=61",
                        "# Uma topbar e um dock aparecem em cada monitor conectado."
                    };
                    initialLines.AddRange(settings.PinnedApplications.Select(delegate(PinnedApplication app)
                    {
                        return "PinnedApp=" + app.Name + "|" + app.Path;
                    }));
                    File.WriteAllLines(path, initialLines, Encoding.UTF8);
                    return settings;
                }

                bool pinnedConfigured = false;
                bool nativePinnedImported = false;
                int settingsFormat = 0;
                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (string.Equals(key, "FluentSearchPath", StringComparison.OrdinalIgnoreCase)) settings.FluentSearchPath = value;
                    if (string.Equals(key, "BareWindowsKeyOpensFluent", StringComparison.OrdinalIgnoreCase)) settings.BareWindowsKeyOpensFluent = ParseBool(value, true);
                    if (string.Equals(key, "TopBarHeight", StringComparison.OrdinalIgnoreCase)) settings.TopBarHeight = ParseInt(value, 29, 24, 42);
                    if (string.Equals(key, "DockReservedHeight", StringComparison.OrdinalIgnoreCase)) settings.DockReservedHeight = ParseInt(value, 61, 48, 82);
                    if (string.Equals(key, "NativePinnedImportedV5", StringComparison.OrdinalIgnoreCase)) nativePinnedImported = ParseBool(value, false);
                    if (string.Equals(key, "SettingsFormat", StringComparison.OrdinalIgnoreCase)) settingsFormat = ParseInt(value, 0, 0, 99);
                    if (string.Equals(key, "PinnedApp", StringComparison.OrdinalIgnoreCase))
                    {
                        pinnedConfigured = true;
                        PinnedApplication app = ParsePinnedApplication(value);
                        if (app != null && !settings.PinnedApplications.Any(delegate(PinnedApplication existing)
                        {
                            return string.Equals(existing.Path, app.Path, StringComparison.OrdinalIgnoreCase);
                        })) settings.PinnedApplications.Add(app);
                    }
                    if (string.Equals(key, "AppOrder", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(value)
                        && !settings.ApplicationOrder.Contains(value, StringComparer.OrdinalIgnoreCase))
                        settings.ApplicationOrder.Add(value);
                }
                if (!pinnedConfigured)
                {
                    settings.PinnedApplications = PinnedCatalog.GetDefaultPinnedApplications();
                    settings.SavePinnedApplications();
                }
                else if (!nativePinnedImported)
                {
                    settings.PinnedApplications.RemoveAll(delegate(PinnedApplication app)
                    {
                        return PinnedCatalog.IsExpandedDefault(app.Path);
                    });
                    foreach (PinnedApplication app in PinnedCatalog.GetDefaultPinnedApplications())
                    {
                        if (!settings.PinnedApplications.Any(delegate(PinnedApplication existing)
                        {
                            return string.Equals(existing.Path, app.Path, StringComparison.OrdinalIgnoreCase);
                        })) settings.PinnedApplications.Add(app);
                    }
                    settings.SavePinnedApplications();
                }
                else if (settingsFormat < 2)
                {
                    settings.SavePinnedApplications();
                }
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao ler configuração; usando padrão: " + exception.Message);
            }

            return settings;
        }

        internal void SavePinnedApplications()
        {
            try
            {
                string path = Path.Combine(Paths.DataDirectory, "settings.ini");
                List<string> lines = File.Exists(path)
                    ? File.ReadAllLines(path, Encoding.UTF8).Where(delegate(string line)
                    {
                        string trimmed = line.TrimStart();
                        return !trimmed.StartsWith("PinnedApp=", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("FluentSearchHotkey=", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("SettingsFormat=", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("NativePinnedImported=", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("NativePinnedImportedV2=", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("NativePinnedImportedV3=", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("NativePinnedImportedV4=", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("NativePinnedImportedV5=", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("# Favoritos do dock", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("# As barras aparecem", StringComparison.OrdinalIgnoreCase)
                            && !trimmed.StartsWith("# Uma topbar e um dock", StringComparison.OrdinalIgnoreCase);
                    }).ToList()
                    : new List<string>();
                lines.Add("SettingsFormat=2");
                lines.Add("NativePinnedImportedV5=true");
                lines.Add("# Uma topbar e um dock aparecem em cada monitor conectado.");
                lines.Add("# Favoritos do dock; use o menu de contexto para fixar ou desafixar.");
                if (PinnedApplications.Count == 0)
                {
                    // Diferencia "nenhum favorito" de uma configuração antiga ou
                    // ausente, que recebe a lista padrão na primeira inicialização.
                    lines.Add("PinnedApp=");
                }
                else
                {
                    lines.AddRange(PinnedApplications.Select(delegate(PinnedApplication app)
                    {
                        return "PinnedApp=" + app.Name.Replace("|", "-") + "|" + app.Path;
                    }));
                }
                File.WriteAllLines(path, lines, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao salvar favoritos: " + exception.Message);
            }
        }

        internal void SaveApplicationOrder()
        {
            try
            {
                string path = Path.Combine(Paths.DataDirectory, "settings.ini");
                List<string> lines = File.Exists(path)
                    ? File.ReadAllLines(path, Encoding.UTF8).Where(delegate(string line)
                    {
                        return !line.TrimStart().StartsWith("AppOrder=", StringComparison.OrdinalIgnoreCase)
                            && !line.TrimStart().StartsWith("# Ordem dos aplicativos", StringComparison.OrdinalIgnoreCase);
                    }).ToList()
                    : new List<string>();
                lines.Add("# Ordem dos aplicativos; arraste no dock. Isto não fixa aplicativos fechados.");
                lines.AddRange(ApplicationOrder.Select(delegate(string key) { return "AppOrder=" + key; }));
                File.WriteAllLines(path, lines, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao salvar ordem do dock: " + exception.Message);
            }
        }

        private static PinnedApplication ParsePinnedApplication(string value)
        {
            int separator = value.IndexOf('|');
            if (separator <= 0 || separator >= value.Length - 1) return null;
            string name = value.Substring(0, separator).Trim();
            string path = value.Substring(separator + 1).Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return null;
            return new PinnedApplication { Name = name, Path = path };
        }

        private static bool ParseBool(string value, bool fallback)
        {
            bool parsed;
            return bool.TryParse(value, out parsed) ? parsed : fallback;
        }

        private static int ParseInt(string value, int fallback, int minimum, int maximum)
        {
            int parsed;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) return fallback;
            return Math.Max(minimum, Math.Min(maximum, parsed));
        }

    }

    internal static class PinnedCatalog
    {
        internal static List<PinnedApplication> GetDefaultPinnedApplications()
        {
            // O dock começa somente com os aplicativos realmente abertos.
            // Favoritos são sempre uma escolha explícita do usuário.
            return new List<PinnedApplication>();
        }

        internal static bool IsExpandedDefault(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string[] expanded =
            {
                Path.Combine(programFiles, @"Zen Browser\zen.exe"),
                Path.Combine(local, @"Programs\Microsoft VS Code\Code.exe"),
                Path.Combine(local, @"Microsoft\WindowsApps\wt.exe"),
                Path.Combine(local, @"Microsoft\WindowsApps\ms-teams.exe")
            };
            return expanded.Any(delegate(string value) { return string.Equals(value, path, StringComparison.OrdinalIgnoreCase); });
        }

    }

    internal static class Paths
    {
        internal static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orla");
        internal static readonly string LegacyDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VictorShell");
        internal static readonly string LogPath = Path.Combine(DataDirectory, "Orla.log");
    }

    internal static class Logger
    {
        private static readonly object Sync = new object();

        internal static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Paths.DataDirectory);
                    File.AppendAllText(Paths.LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { }
        }
    }

    internal abstract class AppBarWindow : Window, IDisposable
    {
        private const int WmAppBar = 0x8001;
        private bool _registered;
        private bool _disposed;
        private bool _hasPosition;
        private NativeMethods.Rect _lastPosition;
        private IntPtr _handle;
        private HwndSource _source;

        protected AppBarWindow(int edge, int thickness, bool allowsTransparency, string screenDeviceName)
        {
            Edge = edge;
            ThicknessPixels = thickness;
            ScreenDeviceName = screenDeviceName;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            AllowsTransparency = allowsTransparency;
            Background = allowsTransparency ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(24, 26, 32));
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            SourceInitialized += OnSourceInitialized;
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs eventArgs)
            {
                if (!_disposed) eventArgs.Cancel = true;
            };
        }

        protected int Edge { get; private set; }
        protected int ThicknessPixels { get; private set; }
        protected string ScreenDeviceName { get; private set; }

        protected Forms.Screen ResolveScreen()
        {
            return Forms.Screen.AllScreens.FirstOrDefault(delegate(Forms.Screen screen)
            {
                return string.Equals(screen.DeviceName, ScreenDeviceName, StringComparison.OrdinalIgnoreCase);
            }) ?? Forms.Screen.PrimaryScreen;
        }

        private void OnSourceInitialized(object sender, EventArgs eventArgs)
        {
            _handle = new WindowInteropHelper(this).Handle;
            int extendedStyle = NativeMethods.GetWindowLong(_handle, NativeMethods.GwlExStyle);
            NativeMethods.SetWindowLong(_handle, NativeMethods.GwlExStyle, extendedStyle | NativeMethods.WsExNoActivate);
            _source = HwndSource.FromHwnd(_handle);
            if (_source != null) _source.AddHook(WndProc);
            RegisterAppBar();
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmAppBar && wParam.ToInt32() == NativeMethods.AbnPosChanged)
            {
                Reposition();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void RegisterAppBar()
        {
            if (_registered || _handle == IntPtr.Zero) return;
            NativeMethods.AppBarData data = NativeMethods.CreateAppBarData(_handle);
            data.uCallbackMessage = WmAppBar;
            NativeMethods.SHAppBarMessage(NativeMethods.AbmNew, ref data);
            _registered = true;
            Reposition();
        }

        internal void Reposition()
        {
            if (!_registered || _handle == IntPtr.Zero) return;

            DrawingRectangle bounds = ResolveScreen().Bounds;
            NativeMethods.AppBarData data = NativeMethods.CreateAppBarData(_handle);
            data.uEdge = (uint)Edge;
            data.rc.left = bounds.Left;
            data.rc.right = bounds.Right;
            data.rc.top = bounds.Top;
            data.rc.bottom = bounds.Bottom;

            if (Edge == NativeMethods.AbeTop) data.rc.bottom = data.rc.top + ThicknessPixels;
            else data.rc.top = data.rc.bottom - ThicknessPixels;

            NativeMethods.Rect desiredPosition = data.rc;
            Left = desiredPosition.left;
            Top = desiredPosition.top;
            Width = desiredPosition.right - desiredPosition.left;
            Height = desiredPosition.bottom - desiredPosition.top;
            if (_hasPosition && NativeMethods.RectEquals(_lastPosition, desiredPosition))
            {
                return;
            }

            // O Shell pode devolver o próximo espaço livre, já descontando a própria
            // reserva do appbar. A janela deve ocupar o retângulo pedido, enquanto o
            // Shell usa ABM_SETPOS apenas para calcular a área útil dos aplicativos.
            _lastPosition = desiredPosition;
            _hasPosition = true;
            NativeMethods.SHAppBarMessage(NativeMethods.AbmSetPos, ref data);

            NativeMethods.SetWindowPos(
                _handle,
                NativeMethods.HwndTopmost,
                desiredPosition.left,
                desiredPosition.top,
                desiredPosition.right - desiredPosition.left,
                desiredPosition.bottom - desiredPosition.top,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_registered && _handle != IntPtr.Zero)
            {
                NativeMethods.AppBarData data = NativeMethods.CreateAppBarData(_handle);
                NativeMethods.SHAppBarMessage(NativeMethods.AbmRemove, ref data);
                _registered = false;
            }

            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }

            Close();
        }
    }

    internal sealed class TopBarWindow : AppBarWindow
    {
        private readonly ShellSettings _settings;
        private readonly ShellController _controller;
        private readonly SystemStatusMonitor _statusMonitor;
        private readonly TextBlock _activeTitle;
        private readonly TextBlock _clock;
        private readonly VectorIcon _network;
        private readonly VectorIcon _volume;
        private readonly VectorIcon _batteryGlyph;
        private readonly TextBlock _batteryPercent;
        private readonly Button _statusButton;
        private readonly RotateTransform _trayOverflowRotation;
        private readonly DispatcherTimer _timer;

        internal TopBarWindow(ShellSettings settings, ShellController controller,
            SystemStatusMonitor statusMonitor, string screenDeviceName)
            : base(NativeMethods.AbeTop, settings.TopBarHeight, false, screenDeviceName)
        {
            _settings = settings;
            _controller = controller;
            _statusMonitor = statusMonitor;

            Border rootBorder = new Border();
            rootBorder.Background = new SolidColorBrush(Color.FromArgb(245, 28, 28, 31));
            rootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            rootBorder.BorderThickness = new Thickness(0, 0, 0, 1);

            Grid grid = new Grid();
            grid.Margin = new Thickness(7, 0, 7, 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            VectorIcon searchGlyph = Ui.Vector(OrlaIcon.Search, Loc.OpenFluentSearch, 15);
            searchGlyph.Foreground = new SolidColorBrush(Color.FromRgb(10, 132, 255));
            Button search = Ui.WrapButton(searchGlyph, Loc.OpenFluentSearchHint, 28, 24);
            Ui.EnableTopBarMotion(search);
            search.Click += delegate { _controller.LaunchFluentSearch(ScreenDeviceName); };
            left.Children.Add(search);

            _activeTitle = Ui.Text(WindowCatalog.GetActiveWindowTitle(), 11.5, FontWeights.SemiBold);
            _activeTitle.Margin = new Thickness(5, 0, 0, 0);
            _activeTitle.MaxWidth = 360;
            _activeTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            left.Children.Add(_activeTitle);
            Grid.SetColumn(left, 0);
            grid.Children.Add(left);

            _clock = Ui.Text("", 11.5, FontWeights.SemiBold);
            _clock.HorizontalAlignment = HorizontalAlignment.Center;
            Button clockButton = Ui.WrapButton(_clock, Loc.DateAndTime, 210, 25);
            Ui.EnableTopBarMotion(clockButton);
            clockButton.Click += delegate { ShellActions.OpenUri("ms-settings:dateandtime"); };
            Grid.SetColumn(clockButton, 1);
            grid.Children.Add(clockButton);

            StackPanel right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            StackPanel indicators = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            const double statusCellWidth = 28;
            _network = Ui.Vector(OrlaIcon.WifiMedium, Loc.NetworkAndInternet, 19);
            System.Windows.Controls.ToolTipService.SetToolTip(_network, null);
            Grid networkSlot = new Grid { Width = statusCellWidth, Height = 25 };
            networkSlot.Children.Add(_network);
            indicators.Children.Add(networkSlot);

            _volume = Ui.Vector(OrlaIcon.VolumeHigh, Loc.Sound, 18);
            System.Windows.Controls.ToolTipService.SetToolTip(_volume, null);
            Grid volumeSlot = new Grid { Width = statusCellWidth, Height = 25 };
            volumeSlot.Children.Add(_volume);
            indicators.Children.Add(volumeSlot);

            Grid batteryPanel = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                Width = 60,
                Height = 20
            };
            batteryPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(statusCellWidth) });
            batteryPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            batteryPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) });
            _batteryGlyph = Ui.Vector(OrlaIcon.Battery, Loc.PowerAndBattery, 20);
            System.Windows.Controls.ToolTipService.SetToolTip(_batteryGlyph, null);
            _batteryPercent = Ui.Text("", 10.5, FontWeights.SemiBold);
            _batteryPercent.Width = 29;
            _batteryPercent.Height = 18;
            _batteryPercent.LineHeight = 18;
            _batteryPercent.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            _batteryPercent.TextAlignment = TextAlignment.Left;
            _batteryPercent.HorizontalAlignment = HorizontalAlignment.Left;
            _batteryPercent.VerticalAlignment = VerticalAlignment.Center;
            _batteryPercent.RenderTransform = new TranslateTransform(0, -1);
            TextOptions.SetTextFormattingMode(_batteryPercent, TextFormattingMode.Display);
            batteryPanel.Children.Add(_batteryGlyph);
            Grid.SetColumn(_batteryPercent, 2);
            batteryPanel.Children.Add(_batteryPercent);
            indicators.Children.Add(batteryPanel);

            VectorIcon trayOverflowGlyph = Ui.Vector(OrlaIcon.ChevronUp, Loc.HiddenTrayIcons, 13);
            _trayOverflowRotation = new RotateTransform(0);
            trayOverflowGlyph.RenderTransform = _trayOverflowRotation;
            trayOverflowGlyph.RenderTransformOrigin = new Point(0.5, 0.5);
            Button trayOverflowButton = Ui.WrapButton(trayOverflowGlyph, Loc.HiddenTrayIcons, 25, 25);
            trayOverflowButton.Margin = new Thickness(0, 0, 3, 0);
            Ui.EnableTopBarMotion(trayOverflowButton);
            trayOverflowButton.Click += delegate
            {
                ShellActions.ToggleTrayOverflow(ScreenDeviceName, _settings.TopBarHeight);
            };
            right.Children.Add(trayOverflowButton);

            _statusButton = Ui.WrapButton(indicators, Loc.QuickPanelTitle, double.NaN, 25);
            _statusButton.Padding = new Thickness(3, 0, 3, 0);
            Ui.EnableTopBarMotion(_statusButton);
            _statusButton.Click += delegate { _controller.ToggleQuickPanel(ScreenDeviceName); };
            right.Children.Add(_statusButton);

            VectorIcon quickGlyph = Ui.Vector(OrlaIcon.Settings, Loc.Settings, 16);
            Button quickButton = Ui.WrapButton(quickGlyph, Loc.Settings, 27, 25);
            quickButton.Margin = new Thickness(5, 0, 0, 0);
            Ui.EnableTopBarMotion(quickButton);
            quickButton.Click += delegate { ShellActions.OpenUri("ms-settings:"); };
            right.Children.Add(quickButton);

            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            rootBorder.Child = grid;
            Content = rootBorder;
            ContextMenu = Ui.CreateExitMenu(_controller);

            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromSeconds(15);
            _timer.Tick += delegate { RefreshStatus(); };
            _timer.Start();
            _statusMonitor.StateChanged += OnSystemStatusChanged;
            ShellActions.TrayOverflowStateChanged += OnTrayOverflowStateChanged;
            RefreshStatus();
        }

        private void OnTrayOverflowStateChanged(bool isOpen)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                double angle = isOpen ? 180 : 0;
                if (!SystemParameters.ClientAreaAnimation)
                {
                    _trayOverflowRotation.Angle = angle;
                    return;
                }
                CubicEase easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                DoubleAnimation animation = new DoubleAnimation(angle,
                    TimeSpan.FromMilliseconds(135)) { EasingFunction = easing };
                _trayOverflowRotation.BeginAnimation(RotateTransform.AngleProperty, animation,
                    HandoffBehavior.SnapshotAndReplace);
            }), DispatcherPriority.Background);
        }

        private void RefreshStatus()
        {
            DateTime now = DateTime.Now;
            string monthDayPattern = Loc.FormattingCulture.DateTimeFormat.MonthDayPattern
                .Replace("MMMM", "MMM");
            string clockText = now.ToString("ddd", Loc.FormattingCulture) + ", "
                + now.ToString(monthDayPattern, Loc.FormattingCulture) + "  "
                + now.ToString("t", Loc.FormattingCulture);
            if (_clock.Text != clockText) _clock.Text = clockText;
            ApplySystemStatus(_statusMonitor.ReadSnapshot());
        }

        private void OnSystemStatusChanged(object sender, EventArgs eventArgs)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (IsVisible) ApplySystemStatus(_statusMonitor.ReadSnapshot());
            }), DispatcherPriority.Background);
        }

        private void ApplySystemStatus(SystemStatusSnapshot state)
        {
            if (state == null) return;

            _network.SetIcon(StatusIcons.Network(state.Network));
            _network.Foreground = state.Network.IsAvailable ? Ui.PrimaryTextBrush : Ui.ErrorBrush;
            string networkDescription = state.Network.Name + " • " + state.Network.Detail;

            string volumeDescription;
            if (!state.AudioAvailable)
            {
                _volume.SetIcon(OrlaIcon.VolumeMuted);
                _volume.Foreground = Ui.ErrorBrush;
                volumeDescription = Loc.Sound + " • " + Loc.TemporarilyUnavailable;
            }
            else
            {
                int volume = state.Audio.VolumePercent;
                _volume.SetIcon(StatusIcons.Volume(volume, state.Audio.IsMuted));
                _volume.Foreground = state.Audio.IsMuted ? Ui.SecondaryTextBrush : Ui.PrimaryTextBrush;
                volumeDescription = Loc.VolumeStatus(volume, state.Audio.IsMuted);
            }

            BatterySnapshot battery = state.Battery;
            if (!battery.HasBattery)
            {
                _batteryGlyph.SetBatteryState(0, false, false);
                _batteryGlyph.Foreground = Ui.SecondaryTextBrush;
                _batteryPercent.Text = "AC";
            }
            else
            {
                _batteryGlyph.SetBatteryState(battery.Percent, true, battery.IsCharging);
                _batteryGlyph.Foreground = battery.IsCharging ? Ui.SuccessBrush
                    : battery.Percent <= 20 ? Ui.ErrorBrush : Ui.PrimaryTextBrush;
                _batteryPercent.Text = battery.Percent.ToString(Loc.FormattingCulture) + "%";
            }
            string batteryDescription = Loc.BatteryStatus(battery);

            List<string> descriptions = new List<string> { networkDescription, volumeDescription, batteryDescription };
            SetButtonDescription(_statusButton, string.Join(" • ", descriptions.ToArray()));
        }

        private static void SetButtonDescription(Button button, string description)
        {
            button.ToolTip = description;
            System.Windows.Automation.AutomationProperties.SetName(button, description);
        }

        internal void ForegroundChanged(IntPtr foreground)
        {
            string title = WindowCatalog.GetWindowTitle(foreground);
            if (string.IsNullOrWhiteSpace(title)) title = Loc.Desktop;
            if (_activeTitle.Text != title) _activeTitle.Text = title;
        }

        public new void Dispose()
        {
            _timer.Stop();
            _statusMonitor.StateChanged -= OnSystemStatusChanged;
            ShellActions.TrayOverflowStateChanged -= OnTrayOverflowStateChanged;
            base.Dispose();
        }
    }

    internal sealed class DockWindow : Window, IDisposable
    {
        private static readonly List<string> DynamicApplicationOrder = new List<string>();
        private readonly ShellSettings _settings;
        private readonly ShellController _controller;
        private readonly string _screenDeviceName;
        private readonly StackPanel _items;
        private readonly Border _dock;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _autoHideTimer;
        private readonly Dictionary<IntPtr, string> _lastWindows = new Dictionary<IntPtr, string>();
        private readonly List<DockIndicatorState> _indicators = new List<DockIndicatorState>();
        private List<string> _currentApplicationKeys = new List<string>();
        private readonly uint _currentProcessId = (uint)Process.GetCurrentProcess().Id;
        private IntPtr _handle;
        private IntPtr _lastForeground;
        private IntPtr _lastExternalForeground;
        private IntPtr _previousExternalForeground;
        private bool _hidden;
        private bool _disposed;
        private DateTime _hideRequestedAt = DateTime.MinValue;
        private DateTime _showRequestedAt = DateTime.MinValue;
        private DateTime _lastOverlapCheckAt = DateTime.MinValue;
        private bool _cachedOverlap;
        private bool _recycleBinStateInitialized;
        private bool _recycleBinFull;
        private System.Windows.Point _dragStart;
        private DateTime _dragPressedAt = DateTime.MinValue;

        internal DockWindow(ShellSettings settings, ShellController controller, string screenDeviceName)
        {
            _settings = settings;
            _controller = controller;
            _screenDeviceName = screenDeviceName;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.Width;
            Height = settings.DockReservedHeight;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            SourceInitialized += delegate
            {
                _handle = new WindowInteropHelper(this).Handle;
                HwndSource source = HwndSource.FromHwnd(_handle);
                if (source != null && source.CompositionTarget != null)
                    source.CompositionTarget.RenderMode = RenderMode.SoftwareOnly;
                int extendedStyle = NativeMethods.GetWindowLong(_handle, NativeMethods.GwlExStyle);
                NativeMethods.SetWindowLong(_handle, NativeMethods.GwlExStyle, extendedStyle | NativeMethods.WsExNoActivate);
            };
            Loaded += delegate { Reposition(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs eventArgs)
            {
                if (!_disposed) eventArgs.Cancel = true;
            };

            Grid canvas = new Grid { Height = settings.DockReservedHeight };
            _dock = new Border();
            _dock.HorizontalAlignment = HorizontalAlignment.Center;
            _dock.VerticalAlignment = VerticalAlignment.Center;
            _dock.Height = 51;
            _dock.CornerRadius = new CornerRadius(18);
            _dock.Background = new SolidColorBrush(Color.FromArgb(240, 30, 30, 34));
            _dock.BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255));
            _dock.BorderThickness = new Thickness(1);
            _dock.Padding = new Thickness(7, 5, 7, 5);
            // Sombra por pixel em janela transparente força recomposição contínua
            // em software. A borda translúcida preserva a separação visual sem custo.

            _items = new StackPanel { Orientation = Orientation.Horizontal };
            _dock.Child = _items;
            canvas.Children.Add(_dock);
            Content = canvas;
            ContextMenu = Ui.CreateExitMenu(_controller);

            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += delegate { RefreshDock(); };
            _timer.Start();

            _autoHideTimer = new DispatcherTimer(DispatcherPriority.Background);
            _autoHideTimer.Interval = TimeSpan.FromMilliseconds(500);
            _autoHideTimer.Tick += delegate { UpdateAutoHide(); };
            _autoHideTimer.Start();
            RefreshDock();
        }

        internal void Reposition()
        {
            DrawingRectangle bounds = ResolveScreen().Bounds;
            UpdateLayout();
            double width = ActualWidth > 0 ? ActualWidth : 360;
            Left = bounds.Left + (bounds.Width - width) / 2.0;
            Top = _hidden ? bounds.Bottom - 3 : bounds.Bottom - _settings.DockReservedHeight;
        }

        private void UpdateAutoHide()
        {
            CaptureExternalForeground();
            DrawingRectangle bounds = ResolveScreen().Bounds;
            NativeMethods.ScreenPoint cursor;
            if (!NativeMethods.GetCursorPos(out cursor)) return;
            double windowWidth = ActualWidth > 0 ? ActualWidth : 360;
            double windowLeft = bounds.Left + (bounds.Width - windowWidth) / 2.0;
            bool nearRevealEdge = cursor.X >= bounds.Left && cursor.X < bounds.Right
                && cursor.Y >= bounds.Bottom - 2 && cursor.Y <= bounds.Bottom + 1;
            bool overDock = cursor.X >= Left && cursor.X <= Left + windowWidth
                && cursor.Y >= Top && cursor.Y <= Top + Height;

            DateTime overlapNow = DateTime.Now;
            if ((overlapNow - _lastOverlapCheckAt).TotalMilliseconds >= 600)
            {
                _cachedOverlap = AnyWindowOverlapsDock(bounds);
                _lastOverlapCheckAt = overlapNow;
            }
            bool overlaps = _cachedOverlap;
            bool shouldHide = overlaps && !nearRevealEdge && !overDock;
            DateTime now = DateTime.Now;
            if (shouldHide)
            {
                _showRequestedAt = DateTime.MinValue;
                if (_hideRequestedAt == DateTime.MinValue) _hideRequestedAt = now;
                if ((now - _hideRequestedAt).TotalMilliseconds >= 800) SetHidden(true);
            }
            else
            {
                _hideRequestedAt = DateTime.MinValue;
                if (_showRequestedAt == DateTime.MinValue) _showRequestedAt = now;
                if (!_hidden || (now - _showRequestedAt).TotalMilliseconds >= 100) SetHidden(false);
            }
        }

        private void CaptureExternalForeground()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return;
            RecordExternalForeground(foreground);
            if (foreground != _lastForeground)
            {
                _lastForeground = foreground;
                UpdateActiveIndicators(foreground);
            }
        }

        internal void ForegroundChanged(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero) return;
            RecordExternalForeground(foreground);
            if (foreground == _lastForeground) return;
            _lastForeground = foreground;
            UpdateActiveIndicators(foreground);
        }

        private void RecordExternalForeground(IntPtr foreground)
        {
            uint processId;
            NativeMethods.GetWindowThreadProcessId(foreground, out processId);
            if (processId == 0 || processId == _currentProcessId || foreground == _lastExternalForeground) return;

            uint lastProcessId = 0;
            if (_lastExternalForeground != IntPtr.Zero)
                NativeMethods.GetWindowThreadProcessId(_lastExternalForeground, out lastProcessId);

            // Janelas auxiliares do mesmo aplicativo não substituem o histórico
            // do app anterior (comum no Teams e em aplicativos WebView).
            if (lastProcessId != 0 && lastProcessId == processId)
            {
                _lastExternalForeground = foreground;
                return;
            }

            _previousExternalForeground = _lastExternalForeground;
            _lastExternalForeground = foreground;
        }

        private IntPtr ResolvePreviousExternalForeground(List<WindowItem> currentApplicationWindows)
        {
            if (_previousExternalForeground == IntPtr.Zero) return IntPtr.Zero;
            if (currentApplicationWindows.Any(delegate(WindowItem item) { return item.Handle == _previousExternalForeground; }))
                return IntPtr.Zero;

            return WindowCatalog.GetVisibleWindows().Any(delegate(WindowItem item)
            {
                return item.Handle == _previousExternalForeground
                    && !item.IsOrla
                    && !NativeMethods.IsIconic(item.Handle);
            }) ? _previousExternalForeground : IntPtr.Zero;
        }

        private void UpdateActiveIndicators(IntPtr foreground)
        {
            foreach (DockIndicatorState state in _indicators)
            {
                state.Indicator.Fill = state.Handles.Contains(foreground)
                    ? new SolidColorBrush(Color.FromRgb(10, 132, 255))
                    : new SolidColorBrush(Color.FromRgb(174, 174, 178));
            }
        }

        private bool AnyWindowOverlapsDock(DrawingRectangle primaryBounds)
        {
            bool overlap = false;
            NativeMethods.EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                if (!NativeMethods.IsWindowVisible(window)) return true;
                if (NativeMethods.GetWindow(window, NativeMethods.GwOwner) != IntPtr.Zero) return true;
                if ((NativeMethods.GetWindowLong(window, NativeMethods.GwlExStyle) & NativeMethods.WsExToolWindow) != 0) return true;
                uint processId;
                NativeMethods.GetWindowThreadProcessId(window, out processId);
                if (processId == _currentProcessId) return true;

                StringBuilder className = new StringBuilder(128);
                NativeMethods.GetClassName(window, className, className.Capacity);
                string value = className.ToString();
                if (value == "Shell_TrayWnd" || value == "Shell_SecondaryTrayWnd" || value == "Progman" || value == "WorkerW") return true;

                NativeMethods.Rect rect;
                if (!NativeMethods.GetWindowRect(window, out rect)) return true;
                bool onPrimary = rect.right > primaryBounds.Left && rect.left < primaryBounds.Right
                    && rect.bottom > primaryBounds.Top && rect.top < primaryBounds.Bottom;
                bool reachesDock = rect.bottom > primaryBounds.Bottom - _settings.DockReservedHeight + 8;
                if (onPrimary && reachesDock)
                {
                    overlap = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return overlap;
        }

        private void SetHidden(bool hidden)
        {
            if (_hidden == hidden) return;
            _hidden = hidden;
            DrawingRectangle bounds = ResolveScreen().Bounds;
            double targetTop = hidden ? bounds.Bottom - 3 : bounds.Bottom - _settings.DockReservedHeight;
            DoubleAnimation movement = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(200));
            movement.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut };
            BeginAnimation(Window.TopProperty, movement, HandoffBehavior.SnapshotAndReplace);
            DoubleAnimation fade = new DoubleAnimation(hidden ? 0.12 : 1.0, TimeSpan.FromMilliseconds(160));
            BeginAnimation(Window.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        }

        private void RefreshDock()
        {
            // O perfil anterior do Seelen usava TemporalItemsVisibility=All e
            // SplitWindows=false: os dois docks mostram os mesmos apps, agrupados.
            List<WindowItem> windows = WindowCatalog.GetVisibleWindows();
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            RecordExternalForeground(foreground);
            bool recycleBinFull = ShellActions.IsRecycleBinFull();
            bool recycleBinChanged = !_recycleBinStateInitialized || recycleBinFull != _recycleBinFull;
            Dictionary<IntPtr, string> snapshot = windows.ToDictionary(delegate(WindowItem item) { return item.Handle; }, delegate(WindowItem item) { return item.Title; });
            if (!recycleBinChanged && foreground == _lastForeground && snapshot.Count == _lastWindows.Count && snapshot.All(delegate(KeyValuePair<IntPtr, string> pair)
            {
                string oldTitle;
                return _lastWindows.TryGetValue(pair.Key, out oldTitle) && oldTitle == pair.Value;
            }))
            {
                return;
            }

            _lastForeground = foreground;
            _recycleBinStateInitialized = true;
            _recycleBinFull = recycleBinFull;
            _lastWindows.Clear();
            foreach (KeyValuePair<IntPtr, string> pair in snapshot) _lastWindows[pair.Key] = pair.Value;

            _items.Children.Clear();
            _indicators.Clear();
            _items.Children.Add(CreateShowDesktopButton());
            _items.Children.Add(CreateGroupSpacer());

            List<DockApplication> applications = new List<DockApplication>();
            int defaultOrder = 0;
            foreach (PinnedApplication pinned in _settings.PinnedApplications)
            {
                List<WindowItem> matchingWindows = windows.Where(delegate(WindowItem item)
                {
                    return MatchesPinnedApplication(item, pinned.Path);
                }).ToList();
                applications.Add(new DockApplication
                {
                    Name = pinned.Name,
                    Path = pinned.Path,
                    Key = pinned.Path,
                    Windows = matchingWindows,
                    Pinned = true,
                    DefaultOrder = defaultOrder++
                });
            }

            List<IGrouping<string, WindowItem>> dynamicGroups = windows
                .Where(delegate(WindowItem item)
                {
                    return !item.IsOrla
                        && !string.IsNullOrWhiteSpace(ApplicationKey(item))
                        && !string.Equals(item.ProcessName, "FluentSearch", StringComparison.OrdinalIgnoreCase)
                        && !_settings.PinnedApplications.Any(delegate(PinnedApplication pinned)
                        {
                            return MatchesPinnedApplication(item, pinned.Path);
                        });
                })
                .GroupBy(ApplicationKey, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            HashSet<string> currentKeys = new HashSet<string>(dynamicGroups.Select(delegate(IGrouping<string, WindowItem> group) { return group.Key; }), StringComparer.OrdinalIgnoreCase);
            DynamicApplicationOrder.RemoveAll(delegate(string key) { return !currentKeys.Contains(key); });
            foreach (IGrouping<string, WindowItem> group in dynamicGroups)
            {
                if (!DynamicApplicationOrder.Contains(group.Key, StringComparer.OrdinalIgnoreCase)) DynamicApplicationOrder.Add(group.Key);
            }
            dynamicGroups = dynamicGroups.OrderBy(delegate(IGrouping<string, WindowItem> group)
            {
                return DynamicApplicationOrder.FindIndex(delegate(string key) { return string.Equals(key, group.Key, StringComparison.OrdinalIgnoreCase); });
            }).ToList();
            foreach (IGrouping<string, WindowItem> group in dynamicGroups)
            {
                List<WindowItem> groupWindows = group.ToList();
                WindowItem first = groupWindows[0];
                applications.Add(new DockApplication
                {
                    Name = first.ApplicationName,
                    Path = first.ExecutablePath,
                    Key = group.Key,
                    Windows = groupWindows,
                    Pinned = false,
                    DefaultOrder = defaultOrder++
                });
            }

            applications = applications.OrderBy(delegate(DockApplication app)
            {
                int savedIndex = _settings.ApplicationOrder.FindIndex(delegate(string key)
                {
                    return string.Equals(key, app.Key, StringComparison.OrdinalIgnoreCase);
                });
                return savedIndex >= 0 ? savedIndex : 10000 + app.DefaultOrder;
            }).ToList();
            _currentApplicationKeys = applications.Select(delegate(DockApplication app) { return app.Key; }).ToList();
            foreach (DockApplication app in applications)
                _items.Children.Add(CreateApplicationButton(app.Name, app.Path, app.Key, app.Windows, app.Pinned));
            _items.Children.Add(CreateGroupSpacer());
            _items.Children.Add(CreateRecycleBinButton(recycleBinFull));
            Dispatcher.BeginInvoke(new Action(Reposition), DispatcherPriority.Loaded);
        }

        internal void RefreshNow()
        {
            _lastForeground = IntPtr.Zero;
            _lastWindows.Clear();
            RefreshDock();
        }

        private static string ApplicationKey(WindowItem item)
        {
            return !string.IsNullOrWhiteSpace(item.ExecutablePath) ? item.ExecutablePath : item.ProcessName;
        }

        private static bool MatchesPinnedApplication(WindowItem item, string pinnedPath)
        {
            if (string.Equals(item.ExecutablePath, pinnedPath, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.IsNullOrWhiteSpace(pinnedPath) || string.IsNullOrWhiteSpace(item.ProcessName)) return false;
            string pinnedName = Path.GetFileNameWithoutExtension(pinnedPath);
            if (string.Equals(pinnedName, item.ProcessName, StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(pinnedName, "wt", StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.ProcessName, "WindowsTerminal", StringComparison.OrdinalIgnoreCase);
        }

        private Forms.Screen ResolveScreen()
        {
            return Forms.Screen.AllScreens.FirstOrDefault(delegate(Forms.Screen screen)
            {
                return string.Equals(screen.DeviceName, _screenDeviceName, StringComparison.OrdinalIgnoreCase);
            }) ?? Forms.Screen.PrimaryScreen;
        }

        private UIElement CreatePinnedButton(string path, string tooltip, Action action, string fallbackGlyph, bool running)
        {
            FrameworkElement icon = Ui.TryCreateIcon(path, 24) ?? Ui.Glyph(fallbackGlyph, tooltip);
            FrameworkElement content = CreateDockIcon(icon, running, false);
            Button button = Ui.WrapButton(content, tooltip, 39, 39);
            Ui.EnableDockMotion(button);
            button.Margin = new Thickness(2, 0, 2, 0);
            button.Click += delegate
            {
                Ui.PlayDockBounce(button);
                action();
            };
            return button;
        }

        private UIElement CreateApplicationButton(string name, string path, string applicationKey, List<WindowItem> windows, bool pinned)
        {
            string iconPath = !string.IsNullOrWhiteSpace(path) ? path : windows.Select(delegate(WindowItem item) { return item.ExecutablePath; }).FirstOrDefault(delegate(string value) { return !string.IsNullOrWhiteSpace(value); });
            bool running = windows.Count > 0;
            bool active = windows.Any(delegate(WindowItem item) { return item.Handle == _lastForeground; });
            FrameworkElement icon = Ui.TryCreateIcon(iconPath, 24) ?? Ui.Glyph("\uE8A7", name);
            FrameworkElement content = CreateDockIcon(icon, running, active);
            Grid iconGrid = content as Grid;
            WpfEllipse indicator = iconGrid == null ? null : iconGrid.Children.OfType<WpfEllipse>().FirstOrDefault();
            if (indicator != null)
            {
                _indicators.Add(new DockIndicatorState
                {
                    Handles = new HashSet<IntPtr>(windows.Select(delegate(WindowItem item) { return item.Handle; })),
                    Indicator = indicator
                });
            }
            string tooltip = windows.Count > 1
                ? name + " — " + windows.Count.ToString(CultureInfo.InvariantCulture) + " janelas"
                : name;
            Button button = Ui.WrapButton(content, tooltip, 39, 39);
            Ui.EnableDockMotion(button);
            button.Margin = new Thickness(2, 0, 2, 0);
            EnableApplicationDrag(button, applicationKey);
            button.Click += delegate
            {
                // Aplicativos Chromium/WebView podem trocar o HWND principal sem
                // trocar o processo. Resolva novamente no clique para nunca usar
                // a fotografia de até dois segundos atrás que desenhou o botão.
                List<WindowItem> currentWindows = ResolveCurrentApplicationWindows(applicationKey, path, pinned);
                if (currentWindows.Count == 0)
                {
                    Ui.PlayDockBounce(button);
                    ShellActions.Start(path);
                    return;
                }
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                WindowItem focused = currentWindows.FirstOrDefault(delegate(WindowItem item) { return item.Handle == foreground; });
                if (focused == null)
                    focused = currentWindows.FirstOrDefault(delegate(WindowItem item)
                    {
                        return item.Handle == _lastExternalForeground && !NativeMethods.IsIconic(item.Handle);
                    });
                bool wasActive = focused != null;
                IntPtr fallback = wasActive ? ResolvePreviousExternalForeground(currentWindows) : IntPtr.Zero;
                ShellActions.ActivateOrMinimize((focused ?? currentWindows[0]).Handle, wasActive, fallback);
                if (wasActive && fallback != IntPtr.Zero) _controller.SynchronizeForeground(fallback);
            };
            button.PreviewMouseDown += delegate(object sender, MouseButtonEventArgs eventArgs)
            {
                if (eventArgs.ChangedButton != MouseButton.Middle) return;
                Ui.PlayDockBounce(button);
                ShellActions.Start(path);
                eventArgs.Handled = true;
            };
            button.ContextMenu = CreateApplicationMenu(name, path, windows, pinned);
            return button;
        }

        private List<WindowItem> ResolveCurrentApplicationWindows(string applicationKey, string path, bool pinned)
        {
            return WindowCatalog.GetVisibleWindows().Where(delegate(WindowItem item)
            {
                if (item.IsOrla) return false;
                return pinned
                    ? MatchesPinnedApplication(item, path)
                    : string.Equals(ApplicationKey(item), applicationKey, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        private void EnableApplicationDrag(Button button, string applicationKey)
        {
            if (string.IsNullOrWhiteSpace(applicationKey)) return;
            button.AllowDrop = true;
            button.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs eventArgs)
            {
                // Use coordenadas do painel, que não mudam durante a animação de
                // escala do botão. Assim um clique normal nunca vira arrasto.
                _dragStart = eventArgs.GetPosition(_items);
                _dragPressedAt = DateTime.UtcNow;
            };
            button.PreviewMouseMove += delegate(object sender, MouseEventArgs eventArgs)
            {
                if (eventArgs.LeftButton != MouseButtonState.Pressed) return;
                if ((DateTime.UtcNow - _dragPressedAt).TotalMilliseconds < 160) return;
                System.Windows.Point current = eventArgs.GetPosition(_items);
                double horizontalThreshold = Math.Max(8.0, SystemParameters.MinimumHorizontalDragDistance * 1.5);
                double verticalThreshold = Math.Max(8.0, SystemParameters.MinimumVerticalDragDistance * 1.5);
                if (Math.Abs(current.X - _dragStart.X) < horizontalThreshold
                    && Math.Abs(current.Y - _dragStart.Y) < verticalThreshold) return;
                DataObject data = new DataObject("Orla.ApplicationKey", applicationKey);
                DragDrop.DoDragDrop(button, data, DragDropEffects.Move);
                eventArgs.Handled = true;
            };
            button.DragOver += delegate(object sender, DragEventArgs eventArgs)
            {
                eventArgs.Effects = eventArgs.Data.GetDataPresent("Orla.ApplicationKey")
                    ? DragDropEffects.Move
                    : DragDropEffects.None;
                eventArgs.Handled = true;
            };
            button.DragEnter += delegate { button.Opacity = 0.68; };
            button.DragLeave += delegate { button.Opacity = 1.0; };
            button.Drop += delegate(object sender, DragEventArgs eventArgs)
            {
                button.Opacity = 1.0;
                if (!eventArgs.Data.GetDataPresent("Orla.ApplicationKey")) return;
                string sourceKey = eventArgs.Data.GetData("Orla.ApplicationKey") as string;
                bool insertAfter = eventArgs.GetPosition(button).X > button.ActualWidth / 2.0;
                _controller.MoveApplication(sourceKey, applicationKey, _currentApplicationKeys, insertAfter);
                eventArgs.Handled = true;
            };
        }

        private ContextMenu CreateApplicationMenu(string name, string path, List<WindowItem> windows, bool pinned)
        {
            ContextMenu menu = new ContextMenu();
            foreach (WindowItem window in windows.Take(8))
            {
                WindowItem captured = window;
                MenuItem windowItem = new MenuItem
                {
                    Header = string.IsNullOrWhiteSpace(window.Title) ? name : window.Title,
                    IsChecked = window.Handle == _lastForeground
                };
                windowItem.Click += delegate { ShellActions.ActivateOrMinimize(captured.Handle); };
                menu.Items.Add(windowItem);
            }
            if (windows.Count > 0) menu.Items.Add(new Separator());

            MenuItem openNew = new MenuItem { Header = Loc.OpenNewWindow };
            openNew.Click += delegate { ShellActions.Start(path); };
            menu.Items.Add(openNew);

            MenuItem pin = new MenuItem { Header = pinned ? Loc.UnpinFromDock : Loc.PinToDock };
            pin.Click += delegate
            {
                if (pinned) _controller.UnpinApplication(path);
                else _controller.PinApplication(name, path);
            };
            menu.Items.Add(pin);

            if (windows.Count > 0)
            {
                menu.Items.Add(new Separator());
                MenuItem close = new MenuItem { Header = windows.Count > 1 ? Loc.CloseAllWindows : Loc.CloseWindow };
                close.Click += delegate
                {
                    foreach (WindowItem window in windows) ShellActions.CloseWindow(window.Handle);
                };
                menu.Items.Add(close);
            }
            return menu;
        }

        private UIElement CreateShowDesktopButton()
        {
            FrameworkElement content = CreateDockIcon(Ui.DesktopGlyph(), false, false);
            Button button = Ui.WrapButton(content, Loc.ShowDesktop, 39, 39);
            Ui.EnableDockMotion(button);
            button.Margin = new Thickness(2, 0, 2, 0);
            button.Click += delegate { ShellActions.ShowDesktop(); };
            return button;
        }

        private UIElement CreateRecycleBinButton(bool full)
        {
            FrameworkElement icon = Ui.TryCreateStockIcon(
                full ? NativeMethods.SiidRecyclerFull : NativeMethods.SiidRecycler, 24)
                ?? Ui.TrashGlyph();
            FrameworkElement content = CreateDockIcon(icon, false, false);
            Button button = Ui.WrapButton(content, Loc.RecycleBin, 39, 39);
            Ui.EnableDockMotion(button);
            button.Margin = new Thickness(2, 0, 2, 0);
            button.Click += delegate { ShellActions.OpenRecycleBin(); };
            return button;
        }

        private UIElement CreateGroupSpacer()
        {
            return new Border { Width = 6, Height = 1, Background = Brushes.Transparent };
        }

        private FrameworkElement CreateDockIcon(FrameworkElement icon, bool running, bool active)
        {
            Grid grid = new Grid { Width = 28, Height = 34 };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Top;
            grid.Children.Add(icon);
            if (running)
            {
                WpfEllipse dot = new WpfEllipse
                {
                    Width = 4,
                    Height = 4,
                    Fill = active
                        ? new SolidColorBrush(Color.FromRgb(10, 132, 255))
                        : new SolidColorBrush(Color.FromRgb(174, 174, 178)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 1)
                };
                grid.Children.Add(dot);
            }
            return grid;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _autoHideTimer.Stop();
            Close();
        }
    }

    internal static class Ui
    {
        internal static readonly System.Windows.Media.Brush PrimaryTextBrush = new SolidColorBrush(Color.FromRgb(242, 242, 247));
        internal static readonly System.Windows.Media.Brush SecondaryTextBrush = new SolidColorBrush(Color.FromRgb(174, 174, 178));
        internal static readonly System.Windows.Media.Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(255, 69, 58));
        internal static readonly System.Windows.Media.Brush SuccessBrush = new SolidColorBrush(Color.FromRgb(48, 209, 88));
        internal static readonly System.Windows.Media.Brush AccentBrush = new SolidColorBrush(Color.FromRgb(10, 132, 255));

        internal static TextBlock Text(string value, double size, FontWeight weight)
        {
            return new TextBlock
            {
                Text = value,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = size,
                FontWeight = weight,
                Foreground = PrimaryTextBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        internal static TextBlock Glyph(string glyph, string accessibleName)
        {
            TextBlock text = Text(glyph, 14, FontWeights.Normal);
            text.FontFamily = new FontFamily("Segoe Fluent Icons");
            text.ToolTip = accessibleName;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            return text;
        }

        internal static VectorIcon Vector(OrlaIcon icon, string accessibleName, double size)
        {
            VectorIcon vector = new VectorIcon(icon, size, PrimaryTextBrush, accessibleName);
            System.Windows.Controls.ToolTipService.SetToolTip(vector, accessibleName);
            return vector;
        }

        internal static void AnimateBrush(SolidColorBrush brush, Color target, int durationMs)
        {
            if (brush == null) return;
            if (!SystemParameters.ClientAreaAnimation)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                brush.Color = target;
                return;
            }

            ColorAnimation animation = new ColorAnimation(brush.Color, target, TimeSpan.FromMilliseconds(durationMs));
            animation.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        internal static FrameworkElement DesktopGlyph()
        {
            Canvas canvas = new Canvas { Width = 24, Height = 24 };
            Border display = new Border
            {
                Width = 20,
                Height = 14,
                CornerRadius = new CornerRadius(2.5),
                BorderThickness = new Thickness(1.4),
                BorderBrush = PrimaryTextBrush,
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255))
            };
            Canvas.SetLeft(display, 2);
            Canvas.SetTop(display, 2.5);
            canvas.Children.Add(display);

            Border stand = new Border { Width = 2, Height = 4, Background = PrimaryTextBrush };
            Canvas.SetLeft(stand, 11);
            Canvas.SetTop(stand, 16.5);
            canvas.Children.Add(stand);

            Border baseLine = new Border
            {
                Width = 10,
                Height = 1.5,
                CornerRadius = new CornerRadius(1),
                Background = PrimaryTextBrush
            };
            Canvas.SetLeft(baseLine, 7);
            Canvas.SetTop(baseLine, 20);
            canvas.Children.Add(baseLine);
            return canvas;
        }

        internal static FrameworkElement TrashGlyph()
        {
            Canvas canvas = new Canvas { Width = 24, Height = 24 };
            Border body = new Border
            {
                Width = 15,
                Height = 16,
                CornerRadius = new CornerRadius(2, 2, 3, 3),
                BorderThickness = new Thickness(1.4),
                BorderBrush = PrimaryTextBrush,
                Background = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255))
            };
            Canvas.SetLeft(body, 4.5);
            Canvas.SetTop(body, 6);
            canvas.Children.Add(body);

            Border lid = new Border { Width = 19, Height = 1.5, CornerRadius = new CornerRadius(1), Background = PrimaryTextBrush };
            Canvas.SetLeft(lid, 2.5);
            Canvas.SetTop(lid, 4.5);
            canvas.Children.Add(lid);

            Border handle = new Border
            {
                Width = 7,
                Height = 3,
                CornerRadius = new CornerRadius(2, 2, 0, 0),
                BorderThickness = new Thickness(1.2, 1.2, 1.2, 0),
                BorderBrush = PrimaryTextBrush
            };
            Canvas.SetLeft(handle, 8.5);
            Canvas.SetTop(handle, 1.5);
            canvas.Children.Add(handle);

            for (int index = 0; index < 3; index++)
            {
                Border line = new Border { Width = 1, Height = 10, Background = SecondaryTextBrush };
                Canvas.SetLeft(line, 8 + index * 4);
                Canvas.SetTop(line, 9);
                canvas.Children.Add(line);
            }
            return canvas;
        }

        internal static Button Button(string text, string tooltip, double width, double height)
        {
            return WrapButton(Text(text, 13, FontWeights.SemiBold), tooltip, width, height);
        }

        internal static Button WrapButton(FrameworkElement content, string tooltip, double width, double height)
        {
            Button button = new Button();
            button.Content = content;
            button.ToolTip = tooltip;
            button.Width = width;
            button.Height = height;
            button.Padding = new Thickness(0);
            button.BorderThickness = new Thickness(0);
            button.Background = Brushes.Transparent;
            button.Foreground = PrimaryTextBrush;
            button.Cursor = Cursors.Hand;
            button.FocusVisualStyle = null;
            button.Template = CreateButtonTemplate();
            System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
            return button;
        }

        internal static void EnableDockMotion(Button button)
        {
            if (!SystemParameters.ClientAreaAnimation) return;

            ScaleTransform scale = new ScaleTransform(1.0, 1.0);
            TranslateTransform translate = new TranslateTransform(0, 0);
            TransformGroup transforms = new TransformGroup();
            transforms.Children.Add(scale);
            transforms.Children.Add(translate);
            button.RenderTransform = transforms;
            button.RenderTransformOrigin = new System.Windows.Point(0.5, 0.72);

            button.MouseEnter += delegate
            {
                Panel.SetZIndex(button, 10);
                AnimateDockButton(scale, translate, 1.10, -2.0, 170);
            };
            button.MouseLeave += delegate
            {
                Panel.SetZIndex(button, 0);
                AnimateDockButton(scale, translate, 1.0, 0.0, 150);
            };
            button.PreviewMouseLeftButtonDown += delegate
            {
                AnimateDockButton(scale, translate, 0.96, 1.0, 80);
            };
            button.PreviewMouseLeftButtonUp += delegate
            {
                AnimateDockButton(scale, translate, button.IsMouseOver ? 1.10 : 1.0, button.IsMouseOver ? -2.0 : 0.0, 120);
            };
        }

        internal static void EnableTopBarMotion(Button button)
        {
            // Botões de barras e flyouts usam o hover sutil do Windows 11,
            // mantendo o realce mais forte do dock somente nos itens do dock.
            button.Template = CreateButtonTemplate(15, 10);
            if (!SystemParameters.ClientAreaAnimation) return;
            ScaleTransform scale = new ScaleTransform(1.0, 1.0);
            button.RenderTransform = scale;
            button.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            // No hover o conteúdo permanece imóvel; o template fornece o
            // realce de fundo. A escala existe somente como feedback de clique.
            button.MouseLeave += delegate { AnimateScale(scale, 1.0, 85); };
            button.PreviewMouseLeftButtonDown += delegate { AnimateScale(scale, 0.985, 40); };
            button.PreviewMouseLeftButtonUp += delegate { AnimateScale(scale, 1.0, 55); };
            button.LostMouseCapture += delegate { AnimateScale(scale, 1.0, 55); };
        }

        internal static void PlayDockBounce(Button button)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            TransformGroup group = button.RenderTransform as TransformGroup;
            if (group == null || group.Children.Count < 2) return;
            TranslateTransform translate = group.Children[1] as TranslateTransform;
            if (translate == null) return;

            double resting = button.IsMouseOver ? -2.0 : 0.0;
            CubicEase easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimationUsingKeyFrames bounce = new DoubleAnimationUsingKeyFrames();
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(resting, KeyTime.FromTimeSpan(TimeSpan.Zero), easing));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(-7.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(85)), easing));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(resting, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(175)), easing));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(-4.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(245)), easing));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(resting, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(330)), easing));
            translate.BeginAnimation(TranslateTransform.YProperty, bounce, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimateScale(ScaleTransform scale, double target, int durationMs)
        {
            CubicEase easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation x = new DoubleAnimation(target, TimeSpan.FromMilliseconds(durationMs));
            DoubleAnimation y = new DoubleAnimation(target, TimeSpan.FromMilliseconds(durationMs));
            x.EasingFunction = easing;
            y.EasingFunction = easing;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, x, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, y, HandoffBehavior.SnapshotAndReplace);
        }

        private static void AnimateDockButton(ScaleTransform scale, TranslateTransform translate, double targetScale, double targetY, int durationMs)
        {
            CubicEase easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            DoubleAnimation scaleX = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(durationMs));
            DoubleAnimation scaleY = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(durationMs));
            DoubleAnimation moveY = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(durationMs));
            scaleX.EasingFunction = easing;
            scaleY.EasingFunction = easing;
            moveY.EasingFunction = easing;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX, HandoffBehavior.SnapshotAndReplace);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY, HandoffBehavior.SnapshotAndReplace);
            translate.BeginAnimation(TranslateTransform.YProperty, moveY, HandoffBehavior.SnapshotAndReplace);
        }

        private static ControlTemplate CreateButtonTemplate()
        {
            // O dock conserva somente sua animação de flutuação original.
            // Topbar e flyouts optam explicitamente pelo realce sutil.
            return CreateButtonTemplate(0, 0);
        }

        private static ControlTemplate CreateButtonTemplate(byte hoverAlpha, byte pressedAlpha)
        {
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ButtonChrome";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(System.Windows.Controls.Button.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(System.Windows.Controls.Button.PaddingProperty));

            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);

            ControlTemplate template = new ControlTemplate(typeof(System.Windows.Controls.Button));
            template.VisualTree = border;
            Trigger over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            // Altere o chrome do template diretamente. Um Setter em
            // Button.Background perderia para o valor local transparente.
            over.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(hoverAlpha, 255, 255, 255)), "ButtonChrome"));
            template.Triggers.Add(over);
            Trigger pressed = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty,
                new SolidColorBrush(Color.FromArgb(pressedAlpha, 255, 255, 255)), "ButtonChrome"));
            template.Triggers.Add(pressed);
            return template;
        }

        internal static ContextMenu CreateExitMenu(ShellController controller)
        {
            ContextMenu menu = new ContextMenu();
            MenuItem openSearch = new MenuItem { Header = Loc.OpenFluentSearch };
            openSearch.Click += delegate { controller.LaunchFluentSearch(); };
            menu.Items.Add(openSearch);
            menu.Items.Add(new Separator());
            MenuItem exit = new MenuItem { Header = Loc.RestoreTaskbarAndExit };
            exit.Click += delegate { controller.Exit(false); };
            menu.Items.Add(exit);
            return menu;
        }

        internal static FrameworkElement TryCreateIcon(string path, int size)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try
            {
                using (DrawingIcon icon = DrawingIcon.ExtractAssociatedIcon(path))
                {
                    if (icon == null) return null;
                    BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(size, size));
                    source.Freeze();
                    return new System.Windows.Controls.Image { Source = source, Width = size, Height = size };
                }
            }
            catch
            {
                return null;
            }
        }

        internal static FrameworkElement TryCreateStockIcon(int stockIconId, int size)
        {
            NativeMethods.StockIconInfo info = new NativeMethods.StockIconInfo();
            info.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.StockIconInfo));
            try
            {
                int result = NativeMethods.SHGetStockIconInfo(stockIconId, NativeMethods.ShgsiIcon, ref info);
                if (result < 0 || info.hIcon == IntPtr.Zero) return null;
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(size, size));
                source.Freeze();
                return new System.Windows.Controls.Image { Source = source, Width = size, Height = size };
            }
            catch
            {
                return null;
            }
            finally
            {
                if (info.hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(info.hIcon);
            }
        }
    }

    internal sealed class WindowItem
    {
        internal IntPtr Handle;
        internal string Title;
        internal string ExecutablePath;
        internal string ProcessName;
        internal string ApplicationName;
        internal bool IsOrla;
        internal bool IsPinnedApplication;
    }

    internal static class WindowCatalog
    {
        private static readonly Dictionary<string, string> ApplicationNameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static string GetActiveWindowTitle()
        {
            IntPtr handle = NativeMethods.GetForegroundWindow();
            string title = GetWindowTitle(handle);
            return string.IsNullOrWhiteSpace(title) ? Loc.Desktop : title;
        }

        internal static IntPtr FindVisibleWindow(string processName, string expectedTitle)
        {
            return FindProcessWindow(processName, expectedTitle, true);
        }

        internal static IntPtr FindProcessWindow(string processName, string expectedTitle, bool visibleOnly)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            HashSet<uint> processIds = new HashSet<uint>(processes.Select(delegate(Process process) { return (uint)process.Id; }));
            foreach (Process process in processes) process.Dispose();
            IntPtr found = IntPtr.Zero;
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                if (visibleOnly && !NativeMethods.IsWindowVisible(handle)) return true;
                uint processId;
                NativeMethods.GetWindowThreadProcessId(handle, out processId);
                if (!processIds.Contains(processId)) return true;
                string title = GetWindowTitle(handle);
                if (!string.IsNullOrWhiteSpace(expectedTitle) && !string.Equals(title, expectedTitle, StringComparison.OrdinalIgnoreCase)) return true;
                found = handle;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        internal static List<WindowItem> GetVisibleWindows()
        {
            return GetVisibleWindows(null);
        }

        internal static List<WindowItem> GetVisibleWindows(string screenDeviceName)
        {
            List<WindowItem> result = new List<WindowItem>();
            int currentProcess = Process.GetCurrentProcess().Id;
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                if (!NativeMethods.IsWindowVisible(handle)) return true;
                if (NativeMethods.GetWindow(handle, NativeMethods.GwOwner) != IntPtr.Zero) return true;
                int extendedStyle = NativeMethods.GetWindowLong(handle, NativeMethods.GwlExStyle);
                if ((extendedStyle & NativeMethods.WsExToolWindow) != 0) return true;

                string title = GetWindowTitle(handle);
                if (string.IsNullOrWhiteSpace(title)) return true;

                uint processId;
                NativeMethods.GetWindowThreadProcessId(handle, out processId);
                string path = null;
                string processName = null;
                try
                {
                    using (Process process = Process.GetProcessById((int)processId))
                    {
                        processName = process.ProcessName;
                        try { path = process.MainModule.FileName; } catch { }
                    }
                }
                catch { }

                if (string.Equals(processName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(processName, "TextInputHost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(processName, "SearchHost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(processName, "StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(processName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(processName, "LockApp", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(title, "Windows Input Experience", StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrWhiteSpace(screenDeviceName))
                {
                    Forms.Screen windowScreen = Forms.Screen.FromHandle(handle);
                    if (!string.Equals(windowScreen.DeviceName, screenDeviceName, StringComparison.OrdinalIgnoreCase)) return true;
                }
                bool pinned = string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(processName, "FluentSearch", StringComparison.OrdinalIgnoreCase);
                result.Add(new WindowItem
                {
                    Handle = handle,
                    Title = title,
                    ExecutablePath = path,
                    ProcessName = processName,
                    ApplicationName = GetApplicationName(processName, path, title),
                    IsOrla = processId == currentProcess,
                    IsPinnedApplication = pinned
                });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static string GetApplicationName(string processName, string path, string title)
        {
            string key = !string.IsNullOrWhiteSpace(path) ? path : processName;
            if (!string.IsNullOrWhiteSpace(key))
            {
                string cached;
                if (ApplicationNameCache.TryGetValue(key, out cached)) return cached;
            }

            string name = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
                    name = !string.IsNullOrWhiteSpace(version.FileDescription) ? version.FileDescription : version.ProductName;
                }
            }
            catch { }

            if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase)) name = Loc.FileExplorer;
            else if (string.Equals(processName, "ms-teams", StringComparison.OrdinalIgnoreCase)) name = "Microsoft Teams";
            if (string.IsNullOrWhiteSpace(name)) name = !string.IsNullOrWhiteSpace(processName) ? processName : title;
            name = name.Trim();
            if (!string.IsNullOrWhiteSpace(key)) ApplicationNameCache[key] = name;
            return name;
        }

        internal static string GetWindowTitle(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return string.Empty;
            int length = NativeMethods.GetWindowTextLength(handle);
            if (length <= 0) return string.Empty;
            StringBuilder builder = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString().Trim();
        }
    }

    internal static class ShellActions
    {
        private static readonly object RecycleBinSync = new object();
        private static DateTime _lastRecycleBinQueryAt = DateTime.MinValue;
        private static bool _lastRecycleBinFull;
        private static int _trayOverflowOperation;
        private static int _trayOverflowCloseRequested;
        private static int _trayOverflowKnownOpen;
        private static int _trayOverflowLastClosedTick;
        internal static event Action<bool> TrayOverflowStateChanged;

        // EVENT_OBJECT_CREATE/SHOW chega antes do polling enxergar o flyout.
        // Mantemos este hook somente durante a abertura para esconder a janela
        // da composição e posicioná-la antes do primeiro quadro visível.
        private sealed class TrayOverflowPrepositioner : IDisposable
        {
            private readonly DrawingRectangle _targetBounds;
            private readonly int _topBarHeight;
            private readonly NativeMethods.WinEventProc _callback;
            private readonly ManualResetEventSlim _captured;
            private readonly ManualResetEventSlim _ready;
            private readonly Thread _thread;
            private IntPtr _hook;
            private IntPtr _popup;
            private uint _threadId;
            private int _armed;

            internal TrayOverflowPrepositioner(DrawingRectangle targetBounds, int topBarHeight)
            {
                _targetBounds = targetBounds;
                _topBarHeight = topBarHeight;
                _captured = new ManualResetEventSlim(false);
                _ready = new ManualResetEventSlim(false);
                _callback = OnWinEvent;
                _armed = 1;
                _thread = new Thread(HookThreadMain);
                _thread.IsBackground = true;
                _thread.Name = "Orla.TrayOverflowPrepositioner";
                _thread.Priority = ThreadPriority.Highest;
                _thread.Start();
            }

            internal void WaitUntilReady(int timeoutMilliseconds)
            {
                _ready.Wait(timeoutMilliseconds);
            }

            internal IntPtr WaitForPopup(int timeoutMilliseconds)
            {
                if (Volatile.Read(ref _armed) == 0) return IntPtr.Zero;
                _captured.Wait(timeoutMilliseconds);
                return Interlocked.CompareExchange(ref _popup, IntPtr.Zero, IntPtr.Zero);
            }

            internal void Disarm()
            {
                Interlocked.Exchange(ref _armed, 0);
            }

            private void OnWinEvent(IntPtr hook, uint eventType, IntPtr window,
                int objectId, int childId, uint eventThread, uint eventTime)
            {
                if ((eventType != NativeMethods.EventObjectCreate
                    && eventType != NativeMethods.EventObjectShow
                    && eventType != NativeMethods.EventObjectLocationChange)
                    || Volatile.Read(ref _armed) == 0 || objectId != NativeMethods.ObjidWindow
                    || !IsTrayOverflowWindow(window)) return;

                int width;
                int height;
                if (!TryPrepareTrayOverflow(window, _targetBounds, _topBarHeight,
                    out width, out height)) return;
                Interlocked.Exchange(ref _popup, window);
                _captured.Set();
            }

            private void HookThreadMain()
            {
                try
                {
                    _threadId = NativeMethods.GetCurrentThreadId();
                    NativeMethods.NativeMessage unused;
                    NativeMethods.PeekMessage(out unused, IntPtr.Zero, 0, 0, NativeMethods.PmNoRemove);
                    _hook = NativeMethods.SetWinEventHook(
                        NativeMethods.EventObjectCreate,
                        NativeMethods.EventObjectLocationChange,
                        IntPtr.Zero,
                        _callback,
                        0,
                        0,
                        NativeMethods.WinEventOutOfContext);
                    _ready.Set();
                    if (_hook == IntPtr.Zero) return;

                    NativeMethods.NativeMessage message;
                    while (NativeMethods.GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
                    {
                        NativeMethods.TranslateMessage(ref message);
                        NativeMethods.DispatchMessage(ref message);
                    }
                }
                catch (Exception exception)
                {
                    Logger.Write("Falha ao preparar eventos da bandeja: " + exception.Message);
                }
                finally
                {
                    if (!_ready.IsSet) _ready.Set();
                    IntPtr hook = Interlocked.Exchange(ref _hook, IntPtr.Zero);
                    if (hook != IntPtr.Zero) NativeMethods.UnhookWinEvent(hook);
                    GC.KeepAlive(_callback);
                }
            }

            public void Dispose()
            {
                Disarm();
                uint threadId = _threadId;
                if (threadId != 0)
                    NativeMethods.PostThreadMessage(threadId, NativeMethods.WmQuit,
                        IntPtr.Zero, IntPtr.Zero);
                if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(350);
                _captured.Dispose();
                _ready.Dispose();
            }
        }

        internal static void Start(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(path) { UseShellExecute = true };
                if (string.Equals(Path.GetFileName(path), "explorer.exe", StringComparison.OrdinalIgnoreCase))
                    info.Arguments = "shell:Home";
                Process.Start(info);
            }
            catch (Exception exception) { Logger.Write("Falha ao abrir " + path + ": " + exception.Message); }
        }

        internal static void OpenUri(string uri)
        {
            Start(uri);
        }

        internal static bool ShouldKeepTaskbarForTrayOverflow()
        {
            return Volatile.Read(ref _trayOverflowOperation) != 0 || IsTrayOverflowVisible();
        }

        internal static void ToggleTrayOverflow(string screenDeviceName, int topBarHeight)
        {
            IntPtr visiblePopup = FindTrayOverflow();
            bool popupVisible = visiblePopup != IntPtr.Zero && NativeMethods.IsWindowVisible(visiblePopup);
            if (Volatile.Read(ref _trayOverflowOperation) != 0
                || Volatile.Read(ref _trayOverflowKnownOpen) != 0 || popupVisible)
            {
                Volatile.Write(ref _trayOverflowCloseRequested, 1);
                RaiseTrayOverflowState(false);
                if (popupVisible) CloseTrayOverflowWindow(visiblePopup);
                return;
            }

            // Um clique real no chevron já conta como clique externo para o
            // flyout. Se o Shell o fechou entre MouseDown e Click, não reabra
            // imediatamente a mesma janela.
            int sinceClosed = unchecked(Environment.TickCount
                - Volatile.Read(ref _trayOverflowLastClosedTick));
            if (sinceClosed >= 0 && sinceClosed < 350) return;
            if (Interlocked.CompareExchange(ref _trayOverflowOperation, 1, 0) != 0) return;
            Volatile.Write(ref _trayOverflowCloseRequested, 0);

            Forms.Screen targetScreen = Forms.Screen.AllScreens.FirstOrDefault(delegate(Forms.Screen item)
            {
                return string.Equals(item.DeviceName, screenDeviceName, StringComparison.OrdinalIgnoreCase);
            }) ?? Forms.Screen.PrimaryScreen;
            DrawingRectangle targetBounds = targetScreen.Bounds;
            TrayOverflowPrepositioner prepositioner = new TrayOverflowPrepositioner(
                targetBounds, topBarHeight);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    prepositioner.WaitUntilReady(300);
                    IntPtr existing = FindTrayOverflow();
                    if (existing != IntPtr.Zero && NativeMethods.IsWindowVisible(existing))
                    {
                        RaiseTrayOverflowState(false);
                        CloseTrayOverflowWindow(existing);
                        Thread.Sleep(90);
                        return;
                    }

                    if (existing != IntPtr.Zero)
                    {
                        int dormantWidth;
                        int dormantHeight;
                        TryPrepareTrayOverflow(existing, targetBounds, topBarHeight,
                            out dormantWidth, out dormantHeight);
                    }

                    NativeMethods.Input[] openTray =
                    {
                        CreateInjectedKey((ushort)NativeMethods.VkLwin, false),
                        CreateInjectedKey(NativeMethods.VkB, false),
                        CreateInjectedKey(NativeMethods.VkB, true),
                        CreateInjectedKey((ushort)NativeMethods.VkLwin, true)
                    };
                    if (NativeMethods.SendInput((uint)openTray.Length, openTray,
                        Marshal.SizeOf(typeof(NativeMethods.Input))) != openTray.Length) return;
                    Thread.Sleep(150);
                    NativeMethods.Input[] confirm =
                    {
                        CreateInjectedKey(NativeMethods.VkReturn, false),
                        CreateInjectedKey(NativeMethods.VkReturn, true)
                    };
                    NativeMethods.SendInput((uint)confirm.Length, confirm,
                        Marshal.SizeOf(typeof(NativeMethods.Input)));

                    IntPtr popup = prepositioner.WaitForPopup(180);
                    for (int attempt = 0; attempt < 180 && popup == IntPtr.Zero; attempt++)
                    {
                        Thread.Sleep(2);
                        IntPtr candidate = FindTrayOverflow();
                        if (candidate == IntPtr.Zero) continue;
                        if (Volatile.Read(ref _trayOverflowCloseRequested) != 0)
                        {
                            CloseTrayOverflowWindow(candidate);
                            return;
                        }
                        int candidateWidth;
                        int candidateHeight;
                        if (TryPrepareTrayOverflow(candidate, targetBounds, topBarHeight,
                            out candidateWidth, out candidateHeight)) popup = candidate;
                    }
                    if (popup == IntPtr.Zero) return;
                    prepositioner.Disarm();

                    int width = 0;
                    int height = 0;
                    if (!TryPrepareTrayOverflow(popup, targetBounds, topBarHeight,
                        out width, out height)) return;

                    // A abertura por teclado é a única API estável oferecida
                    // pelo shell. Ocultamos o indicador de foco do primeiro
                    // item para o flyout se comportar como uma abertura por
                    // mouse, sem parecer que um aplicativo foi selecionado.
                    NativeMethods.SendMessage(popup, NativeMethods.WmChangeUiState,
                        new IntPtr(NativeMethods.UisSet | (NativeMethods.UisfHideFocus << 16)), IntPtr.Zero);

                    if (width > 0 && height > 0)
                    {
                        // O flyout XAML insiste em desenhar foco no primeiro
                        // ícone quando foi aberto via Win+B. Um clique sintético
                        // no padding inferior pertence ao próprio contêiner e
                        // limpa essa seleção sem acionar nenhum aplicativo.
                        int neutralX = Math.Max(1, width / 2);
                        int neutralY = Math.Max(1, height - 3);
                        IntPtr neutralPoint = new IntPtr((neutralY << 16) | (neutralX & 0xFFFF));
                        NativeMethods.PostMessage(popup, NativeMethods.WmLButtonDown,
                            new IntPtr(1), neutralPoint);
                        NativeMethods.PostMessage(popup, NativeMethods.WmLButtonUp,
                            IntPtr.Zero, neutralPoint);
                    }

                    if (Volatile.Read(ref _trayOverflowCloseRequested) != 0)
                    {
                        CloseTrayOverflowWindow(popup);
                        return;
                    }

                    // A janela permanece cloaked enquanto recebe posição,
                    // estado visual e tamanho. SWP_SHOWWINDOW torna o HWND
                    // logicamente visível já no topo; só depois retiramos o
                    // cloak do DWM para não existir um quadro na parte baixa.
                    NativeMethods.SetWindowPos(popup, NativeMethods.HwndTopmost,
                        targetBounds.Right - width - 8,
                        targetBounds.Top + topBarHeight + 6,
                        width, height,
                        NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
                    SetTrayOverflowRegionHidden(popup, false);
                    SetTrayOverflowCloaked(popup, false);

                    uint popupProcessId;
                    uint popupThread = NativeMethods.GetWindowThreadProcessId(popup, out popupProcessId);
                    uint currentThread = NativeMethods.GetCurrentThreadId();
                    bool attached = popupThread != 0 && popupThread != currentThread
                        && NativeMethods.AttachThreadInput(currentThread, popupThread, true);
                    try { NativeMethods.SetFocus(popup); }
                    finally { if (attached) NativeMethods.AttachThreadInput(currentThread, popupThread, false); }

                    RaiseTrayOverflowState(true);
                    while (NativeMethods.IsWindowVisible(popup))
                    {
                        if (Volatile.Read(ref _trayOverflowCloseRequested) != 0)
                        {
                            CloseTrayOverflowWindow(popup);
                            Thread.Sleep(90);
                            if (NativeMethods.IsWindowVisible(popup))
                                NativeMethods.ShowWindowAsync(popup, NativeMethods.SwHide);
                            break;
                        }
                        Thread.Sleep(80);
                    }
                }
                catch (Exception exception)
                {
                    Logger.Write("Falha ao abrir ícones ocultos: " + exception.Message);
                }
                finally
                {
                    prepositioner.Dispose();
                    RaiseTrayOverflowState(false);
                    Volatile.Write(ref _trayOverflowCloseRequested, 0);
                    Interlocked.Exchange(ref _trayOverflowOperation, 0);
                    TaskbarController.HideAll(false);
                }
            });
        }

        private static bool TryPrepareTrayOverflow(IntPtr popup, DrawingRectangle targetBounds,
            int topBarHeight, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (!IsTrayOverflowWindow(popup)) return false;

            NativeMethods.Rect rectangle;
            if (!NativeMethods.GetWindowRect(popup, out rectangle)) return false;
            width = rectangle.right - rectangle.left;
            height = rectangle.bottom - rectangle.top;
            if (width <= 0 || height <= 0) return false;

            // O Shell pode retirar seu próprio cloak durante a animação. Além
            // do bloqueio do DWM, aplicamos uma região de desenho vazia antes
            // da abertura e escondemos o HWND de forma síncrona no evento SHOW.
            // A região permanece vazia mesmo quando o Shell remove o cloak.
            SetTrayOverflowRegionHidden(popup, true);
            SetTrayOverflowCloaked(popup, true);
            NativeMethods.ShowWindow(popup, NativeMethods.SwHide);
            NativeMethods.SetWindowPos(popup, NativeMethods.HwndTopmost,
                targetBounds.Right - width - 8,
                targetBounds.Top + topBarHeight + 6,
                width, height, NativeMethods.SwpNoActivate);
            return true;
        }

        private static bool SetTrayOverflowCloaked(IntPtr popup, bool cloaked)
        {
            if (popup == IntPtr.Zero || !NativeMethods.IsWindow(popup)) return false;
            try
            {
                int value = cloaked ? 1 : 0;
                return NativeMethods.DwmSetWindowAttribute(popup, NativeMethods.DwmwaCloak,
                    ref value, Marshal.SizeOf(typeof(int))) >= 0;
            }
            catch { return false; }
        }

        private static bool SetTrayOverflowRegionHidden(IntPtr popup, bool hidden)
        {
            if (popup == IntPtr.Zero || !NativeMethods.IsWindow(popup)) return false;
            if (!hidden) return NativeMethods.SetWindowRgn(popup, IntPtr.Zero, true) != 0;

            IntPtr emptyRegion = NativeMethods.CreateRectRgn(0, 0, 0, 0);
            if (emptyRegion == IntPtr.Zero) return false;
            if (NativeMethods.SetWindowRgn(popup, emptyRegion, true) != 0) return true;
            NativeMethods.DeleteObject(emptyRegion);
            return false;
        }

        private static bool IsTrayOverflowWindow(IntPtr popup)
        {
            if (popup == IntPtr.Zero || !NativeMethods.IsWindow(popup)) return false;
            StringBuilder className = new StringBuilder(64);
            if (NativeMethods.GetClassName(popup, className, className.Capacity) <= 0) return false;
            return string.Equals(className.ToString(),
                "TopLevelWindowForOverflowXamlIsland", StringComparison.Ordinal);
        }

        private static void CloseTrayOverflowWindow(IntPtr popup)
        {
            if (popup == IntPtr.Zero || !NativeMethods.IsWindow(popup)) return;
            SetTrayOverflowRegionHidden(popup, false);
            SetTrayOverflowCloaked(popup, false);
            NativeMethods.PostMessage(popup, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);
            NativeMethods.Input[] escape =
            {
                CreateInjectedKey(NativeMethods.VkEscape, false),
                CreateInjectedKey(NativeMethods.VkEscape, true)
            };
            NativeMethods.SendInput((uint)escape.Length, escape,
                Marshal.SizeOf(typeof(NativeMethods.Input)));
        }

        private static void RaiseTrayOverflowState(bool isOpen)
        {
            if (isOpen)
            {
                Volatile.Write(ref _trayOverflowKnownOpen, 1);
            }
            else if (Interlocked.Exchange(ref _trayOverflowKnownOpen, 0) != 0)
            {
                Volatile.Write(ref _trayOverflowLastClosedTick, Environment.TickCount);
            }
            Action<bool> handler = TrayOverflowStateChanged;
            if (handler == null) return;
            try { handler(isOpen); }
            catch (Exception exception) { Logger.Write("Falha ao atualizar chevron da bandeja: " + exception.Message); }
        }

        private static IntPtr FindTrayOverflow()
        {
            return NativeMethods.FindWindow("TopLevelWindowForOverflowXamlIsland", null);
        }

        private static bool IsTrayOverflowVisible()
        {
            IntPtr popup = FindTrayOverflow();
            return popup != IntPtr.Zero && NativeMethods.IsWindowVisible(popup);
        }

        private static NativeMethods.Input CreateInjectedKey(ushort virtualKey, bool keyUp)
        {
            NativeMethods.Input input = new NativeMethods.Input();
            input.type = NativeMethods.InputKeyboard;
            input.union.keyboard = new NativeMethods.KeyboardInput();
            input.union.keyboard.wVk = virtualKey;
            input.union.keyboard.dwFlags = keyUp ? NativeMethods.KeyeventfKeyup : 0u;
            return input;
        }

        internal static bool IsRecycleBinFull()
        {
            lock (RecycleBinSync)
            {
                if ((DateTime.UtcNow - _lastRecycleBinQueryAt).TotalMilliseconds < 1500)
                    return _lastRecycleBinFull;
                _lastRecycleBinQueryAt = DateTime.UtcNow;
                try
                {
                    NativeMethods.QueryRecycleBinInfo info = new NativeMethods.QueryRecycleBinInfo();
                    info.cbSize = Marshal.SizeOf(typeof(NativeMethods.QueryRecycleBinInfo));
                    if (NativeMethods.SHQueryRecycleBin(null, ref info) >= 0)
                        _lastRecycleBinFull = info.ItemCount > 0;
                }
                catch { }
                return _lastRecycleBinFull;
            }
        }

        internal static void OpenRecycleBin()
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao abrir a Lixeira: " + exception.Message);
            }
        }

        internal static void ShowDesktop()
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(shellType);
                shellType.InvokeMember("MinimizeAll", BindingFlags.InvokeMethod, null, shell, null);
                Marshal.FinalReleaseComObject(shell);
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao mostrar área de trabalho: " + exception.Message);
            }
        }

        internal static void ActivateOrMinimize(IntPtr handle)
        {
            ActivateOrMinimize(handle, NativeMethods.GetForegroundWindow() == handle);
        }

        internal static void ActivateOrMinimize(IntPtr handle, bool wasActive)
        {
            ActivateOrMinimize(handle, wasActive, IntPtr.Zero);
        }

        internal static void ActivateOrMinimize(IntPtr handle, bool wasActive, IntPtr fallbackHandle)
        {
            if (handle == IntPtr.Zero) return;
            if (wasActive)
            {
                if (fallbackHandle != IntPtr.Zero) TryActivateWindow(fallbackHandle);
                NativeMethods.ShowWindowAsync(handle, NativeMethods.SwMinimize);
                if (fallbackHandle != IntPtr.Zero && NativeMethods.GetForegroundWindow() != fallbackHandle)
                    ActivateWindowWithRetry(fallbackHandle);
                return;
            }

            ActivateWindowWithRetry(handle);
        }

        internal static void ActivateWindowWithRetry(IntPtr handle)
        {
            if (TryActivateWindow(handle)) return;

            ThreadPool.QueueUserWorkItem(delegate
            {
                int[] delays = { 35, 85, 160, 300 };
                foreach (int delay in delays)
                {
                    Thread.Sleep(delay);
                    if (!NativeMethods.IsWindow(handle)) return;
                    if (TryActivateWindow(handle)) return;
                }
                Logger.Write("Windows recusou ativação da janela HWND " + handle.ToInt64().ToString(CultureInfo.InvariantCulture) + " após novas tentativas.");
            });
        }

        private static bool TryActivateWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle)) return false;

            if (NativeMethods.IsIconic(handle)) NativeMethods.ShowWindowAsync(handle, NativeMethods.SwRestore);
            NativeMethods.ShowWindowAsync(handle, NativeMethods.SwShow);

            IntPtr currentForeground = NativeMethods.GetForegroundWindow();
            uint foregroundProcessId;
            uint foregroundThread = NativeMethods.GetWindowThreadProcessId(currentForeground, out foregroundProcessId);
            uint targetProcessId;
            uint targetThread = NativeMethods.GetWindowThreadProcessId(handle, out targetProcessId);
            uint currentThread = NativeMethods.GetCurrentThreadId();
            bool attachedForeground = foregroundThread != 0 && foregroundThread != currentThread
                && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
            bool attachedTarget = targetThread != 0 && targetThread != currentThread && targetThread != foregroundThread
                && NativeMethods.AttachThreadInput(currentThread, targetThread, true);
            try
            {
                NativeMethods.BringWindowToTop(handle);
                NativeMethods.SetForegroundWindow(handle);
            }
            finally
            {
                if (attachedTarget) NativeMethods.AttachThreadInput(currentThread, targetThread, false);
                if (attachedForeground) NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
            return NativeMethods.GetForegroundWindow() == handle;
        }

        internal static void CloseWindow(IntPtr handle)
        {
            if (handle != IntPtr.Zero) NativeMethods.PostMessage(handle, NativeMethods.WmClose, IntPtr.Zero, IntPtr.Zero);
        }
    }

    internal static class TaskbarController
    {
        private static bool _stateCaptured;
        private static uint _originalState;
        private static readonly string StatePath = Path.Combine(Paths.DataDirectory, "taskbar-state.txt");

        internal static void HideAll()
        {
            HideAll(true);
        }

        internal static void HideAll(bool writeLog)
        {
            if (ShellActions.ShouldKeepTaskbarForTrayOverflow()) return;
            EnsureAutoHideState();
            foreach (IntPtr handle in FindTaskbars())
            {
                NativeMethods.ShowWindow(handle, NativeMethods.SwHide);
            }
            if (writeLog) Logger.Write("Barras nativas ocultadas de forma reversível.");
        }

        internal static void RestoreAll()
        {
            foreach (IntPtr handle in FindTaskbars())
            {
                NativeMethods.ShowWindow(handle, NativeMethods.SwShow);
            }
            RestoreTaskbarState();
            Logger.Write("Barras nativas restauradas.");
        }

        private static void EnsureAutoHideState()
        {
            if (!_stateCaptured)
            {
                NativeMethods.AppBarData getData = NativeMethods.CreateAppBarData(IntPtr.Zero);
                _originalState = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref getData).ToUInt32();
                _stateCaptured = true;
                try
                {
                    Directory.CreateDirectory(Paths.DataDirectory);
                    File.WriteAllText(StatePath, _originalState.ToString(CultureInfo.InvariantCulture), Encoding.ASCII);
                }
                catch (Exception exception)
                {
                    Logger.Write("Não foi possível salvar estado da taskbar: " + exception.Message);
                }
            }

            NativeMethods.AppBarData setData = NativeMethods.CreateAppBarData(IntPtr.Zero);
            setData.lParam = new IntPtr((long)(_originalState | NativeMethods.AbsAutoHide));
            NativeMethods.SHAppBarMessage(NativeMethods.AbmSetState, ref setData);
        }

        private static void RestoreTaskbarState()
        {
            uint state = _originalState;
            bool haveState = _stateCaptured;
            if (!haveState)
            {
                try
                {
                    uint parsed;
                    if (File.Exists(StatePath) && uint.TryParse(File.ReadAllText(StatePath).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                    {
                        state = parsed;
                        haveState = true;
                    }
                }
                catch { }
            }

            if (haveState)
            {
                NativeMethods.AppBarData setData = NativeMethods.CreateAppBarData(IntPtr.Zero);
                setData.lParam = new IntPtr((long)state);
                NativeMethods.SHAppBarMessage(NativeMethods.AbmSetState, ref setData);
            }

            try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { }
            _stateCaptured = false;
        }

        private static List<IntPtr> FindTaskbars()
        {
            List<IntPtr> result = new List<IntPtr>();
            IntPtr primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero) result.Add(primary);
            NativeMethods.EnumWindows(delegate(IntPtr handle, IntPtr parameter)
            {
                StringBuilder className = new StringBuilder(256);
                NativeMethods.GetClassName(handle, className, className.Capacity);
                if (string.Equals(className.ToString(), "Shell_SecondaryTrayWnd", StringComparison.Ordinal)) result.Add(handle);
                return true;
            }, IntPtr.Zero);
            return result.Distinct().ToList();
        }
    }

    internal sealed class ForegroundWindowTracker : IDisposable
    {
        private readonly Action<IntPtr> _onForegroundChanged;
        private readonly NativeMethods.WinEventProc _callback;
        private IntPtr _hook;

        internal ForegroundWindowTracker(Action<IntPtr> onForegroundChanged)
        {
            _onForegroundChanged = onForegroundChanged;
            _callback = OnWinEvent;
        }

        internal void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _hook = NativeMethods.SetWinEventHook(
                NativeMethods.EventSystemForeground,
                NativeMethods.EventSystemForeground,
                IntPtr.Zero,
                _callback,
                0,
                0,
                NativeMethods.WinEventOutOfContext);
            if (_hook == IntPtr.Zero) Logger.Write("Não foi possível ativar o rastreamento nativo de foco.");
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime)
        {
            if (window != IntPtr.Zero) _onForegroundChanged(window);
        }

        public void Dispose()
        {
            if (_hook == IntPtr.Zero) return;
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    internal sealed class BareWindowsKeyHook : IDisposable
    {
        private readonly Action _onBareWindowsKey;
        private readonly NativeMethods.LowLevelKeyboardProc _callback;
        private IntPtr _hook;
        private bool _physicalWindowsDown;
        private bool _combinationMode;
        private ushort _windowsVirtualKey;

        internal BareWindowsKeyHook(Action onBareWindowsKey)
        {
            _onBareWindowsKey = onBareWindowsKey;
            _callback = HookProcedure;
        }

        internal void Start()
        {
            if (_hook != IntPtr.Zero) return;
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                IntPtr moduleHandle = NativeMethods.GetModuleHandle(module.ModuleName);
                _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _callback, moduleHandle, 0);
            }
            if (_hook == IntPtr.Zero) Logger.Write("Não foi possível ativar integração da tecla Windows.");
            else Logger.Write("Integração da tecla Windows ativada.");
        }

        private IntPtr HookProcedure(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code < 0) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            NativeMethods.KeyboardHookData data = (NativeMethods.KeyboardHookData)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KeyboardHookData));
            if ((data.flags & NativeMethods.LlkhfInjected) != 0) return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);

            int message = wParam.ToInt32();
            bool down = message == NativeMethods.WmKeyDown || message == NativeMethods.WmSysKeyDown;
            bool up = message == NativeMethods.WmKeyUp || message == NativeMethods.WmSysKeyUp;
            bool windowsKey = data.vkCode == NativeMethods.VkLwin || data.vkCode == NativeMethods.VkRwin;

            if (down && windowsKey)
            {
                if (!_physicalWindowsDown)
                {
                    _physicalWindowsDown = true;
                    _combinationMode = false;
                    _windowsVirtualKey = (ushort)data.vkCode;
                }
                return new IntPtr(1);
            }

            if (down && _physicalWindowsDown && !_combinationMode)
            {
                _combinationMode = true;
                SendWindowsKey(true);
                return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
            }

            if (up && windowsKey && _physicalWindowsDown)
            {
                bool wasCombination = _combinationMode;
                _physicalWindowsDown = false;
                _combinationMode = false;
                if (wasCombination)
                {
                    SendWindowsKey(false);
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(_onBareWindowsKey);
                }
                return new IntPtr(1);
            }

            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        private void SendWindowsKey(bool down)
        {
            NativeMethods.Input input = new NativeMethods.Input();
            input.type = NativeMethods.InputKeyboard;
            input.union.keyboard = new NativeMethods.KeyboardInput();
            input.union.keyboard.wVk = _windowsVirtualKey == 0 ? (ushort)NativeMethods.VkLwin : _windowsVirtualKey;
            input.union.keyboard.dwFlags = down ? 0u : NativeMethods.KeyeventfKeyup;
            uint sent = NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf(typeof(NativeMethods.Input)));
            if (sent != 1) Logger.Write("Falha ao repassar tecla Windows para atalho combinado. INPUT=" + Marshal.SizeOf(typeof(NativeMethods.Input)));
        }

        public void Dispose()
        {
            if (_physicalWindowsDown && _combinationMode) SendWindowsKey(false);
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            _physicalWindowsDown = false;
            _combinationMode = false;
        }
    }

    internal static class NativeMethods
    {
        internal const int AbeLeft = 0;
        internal const int AbeTop = 1;
        internal const int AbeRight = 2;
        internal const int AbeBottom = 3;
        internal const uint AbmNew = 0x00000000;
        internal const uint AbmRemove = 0x00000001;
        internal const uint AbmQueryPos = 0x00000002;
        internal const uint AbmSetPos = 0x00000003;
        internal const uint AbmGetState = 0x00000004;
        internal const uint AbmSetState = 0x0000000A;
        internal const uint AbsAutoHide = 0x00000001;
        internal const int AbnPosChanged = 0x00000001;
        internal const uint SwpNoActivate = 0x0010;
        internal const uint SwpNoSize = 0x0001;
        internal const uint SwpNoZOrder = 0x0004;
        internal const uint SwpShowWindow = 0x0040;
        internal static readonly IntPtr HwndTopmost = new IntPtr(-1);
        internal const int SwHide = 0;
        internal const int SwShow = 5;
        internal const int SwMinimize = 6;
        internal const int SwRestore = 9;
        internal const uint GwOwner = 4;
        internal const int GwlExStyle = -20;
        internal const int WsExToolWindow = 0x00000080;
        internal const int WsExNoActivate = 0x08000000;
        internal const int WhKeyboardLl = 13;
        internal const int WmKeyDown = 0x0100;
        internal const int WmKeyUp = 0x0101;
        internal const int WmSysKeyDown = 0x0104;
        internal const int WmSysKeyUp = 0x0105;
        internal const int WmClose = 0x0010;
        internal const uint WmQuit = 0x0012;
        internal const int WmChangeUiState = 0x0127;
        internal const int WmLButtonDown = 0x0201;
        internal const int WmLButtonUp = 0x0202;
        internal const int UisSet = 1;
        internal const int UisfHideFocus = 1;
        internal const uint VkLwin = 0x5B;
        internal const uint VkRwin = 0x5C;
        internal const ushort VkB = 0x42;
        internal const ushort VkReturn = 0x0D;
        internal const ushort VkEscape = 0x1B;
        internal const uint LlkhfInjected = 0x10;
        internal const uint InputKeyboard = 1;
        internal const uint KeyeventfKeyup = 0x0002;
        internal const uint EventSystemForeground = 0x0003;
        internal const uint EventObjectCreate = 0x8000;
        internal const uint EventObjectShow = 0x8002;
        internal const uint EventObjectLocationChange = 0x800B;
        internal const int ObjidWindow = 0;
        internal const uint WinEventOutOfContext = 0x0000;
        internal const int DwmwaCloak = 13;
        internal const uint PmNoRemove = 0x0000;
        internal const uint MonitorDefaultToNearest = 0x00000002;
        internal const uint ShgsiIcon = 0x00000100;
        internal const int SiidRecycler = 31;
        internal const int SiidRecyclerFull = 32;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int left;
            internal int top;
            internal int right;
            internal int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ScreenPoint
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeMessage
        {
            internal IntPtr Window;
            internal uint Message;
            internal IntPtr WParam;
            internal IntPtr LParam;
            internal uint Time;
            internal ScreenPoint Point;
            internal uint Private;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AppBarData
        {
            internal int cbSize;
            internal IntPtr hWnd;
            internal uint uCallbackMessage;
            internal uint uEdge;
            internal Rect rc;
            internal IntPtr lParam;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StockIconInfo
        {
            internal uint cbSize;
            internal IntPtr hIcon;
            internal int SystemImageIndex;
            internal int IconIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Path;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct QueryRecycleBinInfo
        {
            internal int cbSize;
            internal long Size;
            internal long ItemCount;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PhysicalMonitor
        {
            internal IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
        }

        internal static AppBarData CreateAppBarData(IntPtr handle)
        {
            AppBarData data = new AppBarData();
            data.cbSize = Marshal.SizeOf(typeof(AppBarData));
            data.hWnd = handle;
            return data;
        }

        internal static Input CreateKeyboardInput(ushort virtualKey, bool keyUp)
        {
            Input input = new Input();
            input.type = InputKeyboard;
            input.union.keyboard = new KeyboardInput();
            input.union.keyboard.wVk = virtualKey;
            input.union.keyboard.dwFlags = keyUp ? KeyeventfKeyup : 0u;
            return input;
        }

        internal static bool RectEquals(Rect left, Rect right)
        {
            return left.left == right.left
                && left.top == right.top
                && left.right == right.right
                && left.bottom == right.bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardHookData
        {
            internal uint vkCode;
            internal uint scanCode;
            internal uint flags;
            internal uint time;
            internal IntPtr extraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput
        {
            internal ushort wVk;
            internal ushort wScan;
            internal uint dwFlags;
            internal uint time;
            internal IntPtr dwExtraInfo;
        }

        // Em Windows x64, a união INPUT ocupa 32 bytes (MOUSEINPUT é o maior
        // membro) e a estrutura completa ocupa 40. Sem o tamanho explícito,
        // SendInput retorna ERROR_INVALID_PARAMETER.
        [StructLayout(LayoutKind.Explicit, Size = 32)]
        internal struct InputUnion
        {
            [FieldOffset(0)] internal KeyboardInput keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input
        {
            internal uint type;
            internal InputUnion union;
        }

        internal delegate bool EnumWindowsProc(IntPtr handle, IntPtr parameter);
        internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
        internal delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint eventTime);

        [DllImport("shell32.dll", SetLastError = true)]
        internal static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHGetStockIconInfo(int stockIconId, uint flags, ref StockIconInfo info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        internal static extern int SHQueryRecycleBin(string rootPath, ref QueryRecycleBinInfo info);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr icon);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr objectHandle);

        [DllImport("user32.dll")]
        internal static extern int SetWindowRgn(IntPtr handle, IntPtr region, bool redraw);

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(ScreenPoint point, uint flags);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

        [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count,
            [Out] PhysicalMonitor[] physicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] physicalMonitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum,
            out uint current, out uint maximum);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr handle, StringBuilder className, int maximumCount);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr handle, int command);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindowAsync(IntPtr handle, int command);

        [DllImport("user32.dll")]
        internal static extern bool BringWindowToTop(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetFocus(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool IsIconic(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern bool GetWindowRect(IntPtr handle, out Rect rectangle);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr handle, uint command);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        internal static extern int GetWindowLong(IntPtr handle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        internal static extern int SetWindowLong(IntPtr handle, int index, int value);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr handle, StringBuilder text, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out ScreenPoint point);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr handle);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int hook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetWinEventHook(uint eventMinimum, uint eventMaximum, IntPtr module, WinEventProc callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        internal static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr handle, int attribute,
            ref int attributeValue, int attributeSize);

        [DllImport("user32.dll")]
        internal static extern bool PeekMessage(out NativeMessage message, IntPtr window,
            uint filterMinimum, uint filterMaximum, uint removeMessage);

        [DllImport("user32.dll")]
        internal static extern int GetMessage(out NativeMessage message, IntPtr window,
            uint filterMinimum, uint filterMaximum);

        [DllImport("user32.dll")]
        internal static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        internal static extern IntPtr DispatchMessage(ref NativeMessage message);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool PostThreadMessage(uint threadId, uint message,
            IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, Input[] inputs, int size);

    }
}
