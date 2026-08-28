using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Orla
{
    internal sealed class NetworkSnapshot
    {
        internal readonly bool IsAvailable;
        internal readonly bool IsWifi;
        internal readonly int SignalQuality;
        internal readonly string Name;
        internal readonly string Detail;
        internal readonly string ConnectionName;

        internal NetworkSnapshot(bool isAvailable, bool isWifi, int signalQuality, string name,
            string detail, string connectionName)
        {
            IsAvailable = isAvailable;
            IsWifi = isWifi;
            SignalQuality = signalQuality;
            Name = name;
            Detail = detail;
            ConnectionName = connectionName;
        }
    }

    // Uma única tabela semântica mantém topbar e painel no mesmo sistema
    // vetorial, independentemente de fonte instalada ou escala de tela.
    internal static class StatusIcons
    {
        internal static OrlaIcon Network(NetworkSnapshot state)
        {
            if (state == null || !state.IsAvailable) return OrlaIcon.WifiOff;
            if (!state.IsWifi) return OrlaIcon.Ethernet;
            if (state.SignalQuality < 0) return OrlaIcon.WifiMedium;
            if (state.SignalQuality <= 25) return OrlaIcon.WifiLow;
            if (state.SignalQuality <= 60) return OrlaIcon.WifiMedium;
            return OrlaIcon.WifiHigh;
        }

        internal static OrlaIcon Volume(int percent, bool muted)
        {
            if (muted) return OrlaIcon.VolumeMuted;
            if (percent <= 0) return OrlaIcon.VolumeZero;
            if (percent <= 50) return OrlaIcon.VolumeLow;
            return OrlaIcon.VolumeHigh;
        }
    }

    // As inscrições existem apenas durante a vida do Quick Panel e são sempre
    // removidas no Dispose para não manter a janela viva por eventos estáticos.
    internal sealed class NetworkStatusService : IDisposable
    {
        private bool _disposed;

        internal event EventHandler StateChanged;

        internal NetworkStatusService()
        {
            NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += OnAddressChanged;
        }

        internal NetworkSnapshot ReadSnapshot()
        {
            try
            {
                NetworkInterface active = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(delegate(NetworkInterface item)
                    {
                        return item.OperationalStatus == OperationalStatus.Up
                            && item.NetworkInterfaceType != NetworkInterfaceType.Loopback
                            && item.NetworkInterfaceType != NetworkInterfaceType.Tunnel;
                    })
                    .OrderByDescending(HasGateway)
                    .ThenBy(NetworkPriority)
                    .FirstOrDefault();

                if (active == null || !NetworkInterface.GetIsNetworkAvailable())
                    return new NetworkSnapshot(false, false, -1, Loc.NoConnection, Loc.CheckNetwork,
                        string.Empty);

                bool isWifi = active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                string kind;
                if (isWifi) kind = Loc.WifiConnected;
                else if (active.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                    || active.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet) kind = Loc.EthernetConnected;
                else kind = Loc.NetworkConnected;

                string detail = string.IsNullOrWhiteSpace(active.Name) ? active.Description : active.Name;
                if (string.IsNullOrWhiteSpace(detail)) detail = Loc.InternetAvailable;
                if (string.Equals(detail, "Wi-Fi", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(detail, "Ethernet", StringComparison.OrdinalIgnoreCase))
                    detail = Loc.InternetAvailable;
                WifiConnectionSnapshot wifi = isWifi ? NativeWifiSignal.Read()
                    : WifiConnectionSnapshot.Unavailable;
                int signalQuality = wifi.SignalQuality;
                string connectionName = wifi.Name;
                if (isWifi && !string.IsNullOrWhiteSpace(connectionName)) detail = connectionName;
                if (isWifi && signalQuality >= 0)
                    detail = detail + " • " + Loc.WifiSignal(signalQuality);
                return new NetworkSnapshot(true, isWifi, signalQuality, kind, detail, connectionName);
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao consultar rede: " + exception.Message);
                return new NetworkSnapshot(false, false, -1, Loc.NetworkStatus,
                    Loc.TemporarilyUnavailable, string.Empty);
            }
        }

        private static bool HasGateway(NetworkInterface item)
        {
            try
            {
                return item.GetIPProperties().GatewayAddresses.Any(delegate(GatewayIPAddressInformation gateway)
                {
                    return gateway.Address != null && !gateway.Address.Equals(System.Net.IPAddress.Any)
                        && !gateway.Address.Equals(System.Net.IPAddress.IPv6Any);
                });
            }
            catch { return false; }
        }

        private static int NetworkPriority(NetworkInterface item)
        {
            if (item.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) return 0;
            if (item.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                || item.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet) return 1;
            return 2;
        }

        private void OnAvailabilityChanged(object sender, NetworkAvailabilityEventArgs eventArgs)
        {
            RaiseStateChanged();
        }

        private void OnAddressChanged(object sender, EventArgs eventArgs)
        {
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            if (_disposed) return;
            EventHandler handler = StateChanged;
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception) { Logger.Write("Falha ao entregar evento de rede: " + exception.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= OnAddressChanged;
            StateChanged = null;
        }
    }

    internal sealed class BatterySnapshot
    {
        internal readonly bool HasBattery;
        internal readonly bool IsCharging;
        internal readonly bool IsPluggedIn;
        internal readonly int Percent;
        internal readonly string Status;
        internal readonly string Detail;

        internal BatterySnapshot(bool hasBattery, bool isCharging, bool isPluggedIn,
            int percent, string status, string detail)
        {
            HasBattery = hasBattery;
            IsCharging = isCharging;
            IsPluggedIn = isPluggedIn;
            Percent = percent;
            Status = status;
            Detail = detail;
        }

        internal static BatterySnapshot Read()
        {
            Forms.PowerStatus power = Forms.SystemInformation.PowerStatus;
            if ((power.BatteryChargeStatus & Forms.BatteryChargeStatus.NoSystemBattery) != 0)
                return new BatterySnapshot(false, false, true, 100, Loc.ExternalPower, Loc.NoBatteryReported);

            int percent = Math.Max(0, Math.Min(100, (int)Math.Round(power.BatteryLifePercent * 100)));
            bool plugged = power.PowerLineStatus == Forms.PowerLineStatus.Online;
            bool charging = (power.BatteryChargeStatus & Forms.BatteryChargeStatus.Charging) != 0;
            string status = charging ? Loc.Charging : plugged ? Loc.PluggedIn : Loc.OnBattery;
            string detail = percent.ToString(Loc.FormattingCulture) + "%";
            if (!plugged && power.BatteryLifeRemaining > 0)
            {
                TimeSpan remaining = TimeSpan.FromSeconds(power.BatteryLifeRemaining);
                detail += " • ";
                if (remaining.TotalHours >= 1) detail += ((int)remaining.TotalHours).ToString() + " h ";
                detail += Loc.RemainingMinutes(remaining.Minutes);
            }
            return new BatterySnapshot(true, charging, plugged, percent, status, detail);
        }
    }

    internal sealed class BluetoothSnapshot
    {
        internal readonly bool IsEnabled;
        internal readonly bool IsConnected;
        internal readonly string DeviceName;

        internal BluetoothSnapshot(bool isEnabled, bool isConnected, string deviceName)
        {
            IsEnabled = isEnabled;
            IsConnected = isConnected;
            DeviceName = deviceName;
        }

        internal static BluetoothSnapshot Read()
        {
            IntPtr radioFindHandle = IntPtr.Zero;
            IntPtr radioHandle = IntPtr.Zero;
            IntPtr findHandle = IntPtr.Zero;
            try
            {
                BluetoothFindRadioParams radioSearch = new BluetoothFindRadioParams();
                radioSearch.Size = Marshal.SizeOf(typeof(BluetoothFindRadioParams));
                radioFindHandle = BluetoothNative.BluetoothFindFirstRadio(ref radioSearch, out radioHandle);
                if (radioFindHandle == IntPtr.Zero || radioHandle == IntPtr.Zero)
                    return new BluetoothSnapshot(false, false, string.Empty);

                BluetoothDeviceSearchParams search = new BluetoothDeviceSearchParams();
                search.Size = Marshal.SizeOf(typeof(BluetoothDeviceSearchParams));
                search.ReturnConnected = true;
                search.TimeoutMultiplier = 1;
                search.RadioHandle = radioHandle;
                BluetoothDeviceInfo device = new BluetoothDeviceInfo();
                device.Size = Marshal.SizeOf(typeof(BluetoothDeviceInfo));
                findHandle = BluetoothNative.BluetoothFindFirstDevice(ref search, ref device);
                if (findHandle == IntPtr.Zero || !device.Connected)
                    return new BluetoothSnapshot(true, false, string.Empty);
                string name = string.IsNullOrWhiteSpace(device.Name) ? Loc.BluetoothDevice : device.Name.Trim();
                return new BluetoothSnapshot(true, true, name);
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao consultar Bluetooth: " + exception.Message);
                return new BluetoothSnapshot(false, false, string.Empty);
            }
            finally
            {
                if (findHandle != IntPtr.Zero) BluetoothNative.BluetoothFindDeviceClose(findHandle);
                if (radioHandle != IntPtr.Zero) BluetoothNative.CloseHandle(radioHandle);
                if (radioFindHandle != IntPtr.Zero) BluetoothNative.BluetoothFindRadioClose(radioFindHandle);
            }
        }
    }

    internal sealed class QuickSettingsSnapshot
    {
        private const string NightLightStatePath =
            @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

        internal readonly bool EnergySaverAvailable;
        internal readonly bool EnergySaverEnabled;
        internal readonly bool NightLightAvailable;
        internal readonly bool NightLightEnabled;

        internal QuickSettingsSnapshot(bool energySaverAvailable, bool energySaverEnabled,
            bool nightLightAvailable, bool nightLightEnabled)
        {
            EnergySaverAvailable = energySaverAvailable;
            EnergySaverEnabled = energySaverEnabled;
            NightLightAvailable = nightLightAvailable;
            NightLightEnabled = nightLightEnabled;
        }

        internal static QuickSettingsSnapshot Read()
        {
            bool energySaverEnabled;
            bool nightLightEnabled;
            bool energySaverAvailable = TryReadEnergySaver(out energySaverEnabled);
            bool nightLightAvailable = TryReadNightLight(out nightLightEnabled);
            return new QuickSettingsSnapshot(energySaverAvailable, energySaverEnabled,
                nightLightAvailable, nightLightEnabled);
        }

        private static bool TryReadEnergySaver(out bool enabled)
        {
            enabled = false;
            try
            {
                Type powerManager = Type.GetType(
                    "Windows.System.Power.PowerManager, Windows, ContentType=WindowsRuntime", false);
                if (powerManager == null) return false;
                PropertyInfo statusProperty = powerManager.GetProperty("EnergySaverStatus",
                    BindingFlags.Public | BindingFlags.Static);
                if (statusProperty == null) return false;
                string status = Convert.ToString(statusProperty.GetValue(null, null));
                if (string.IsNullOrWhiteSpace(status)) return false;
                enabled = string.Equals(status, "On", StringComparison.OrdinalIgnoreCase);
                return string.Equals(status, "On", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "Off", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "Disabled", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool TryReadNightLight(out bool enabled)
        {
            enabled = false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(NightLightStatePath))
                {
                    byte[] data = key == null ? null : key.GetValue("Data") as byte[];
                    if (data == null || data.Length < 12) return false;

                    // CloudStore contém dois envelopes Bond CompactBinary. No
                    // segundo, o campo Int32 de id 0 existe somente quando a
                    // Luz noturna está ativa. A leitura é estritamente passiva;
                    // formatos desconhecidos retornam indisponível.
                    int envelopes = 0;
                    for (int index = 0; index <= data.Length - 5; index++)
                    {
                        if (data[index] != 0x43 || data[index + 1] != 0x42
                            || data[index + 2] != 0x01 || data[index + 3] != 0x00) continue;
                        envelopes++;
                        if (envelopes != 2) continue;
                        byte firstField = data[index + 4];
                        if (firstField == 0x10)
                        {
                            enabled = true;
                            return true;
                        }
                        if (firstField == 0xD0)
                        {
                            enabled = false;
                            return true;
                        }
                        return false;
                    }
                }
            }
            catch { }
            return false;
        }
    }

    internal sealed class SystemStatusSnapshot
    {
        internal readonly NetworkSnapshot Network;
        internal readonly AudioStateChangedEventArgs Audio;
        internal readonly BatterySnapshot Battery;
        internal readonly BluetoothSnapshot Bluetooth;
        internal readonly QuickSettingsSnapshot QuickSettings;
        internal readonly bool AudioAvailable;

        internal SystemStatusSnapshot(NetworkSnapshot network, AudioStateChangedEventArgs audio,
            BatterySnapshot battery, BluetoothSnapshot bluetooth, QuickSettingsSnapshot quickSettings,
            bool audioAvailable)
        {
            Network = network;
            Audio = audio;
            Battery = battery;
            Bluetooth = bluetooth;
            QuickSettings = quickSettings;
            AudioAvailable = audioAvailable;
        }
    }

    // Uma única instância atende todas as topbars. Rede e volume usam eventos;
    // bateria, Bluetooth, ações rápidas e intensidade do Wi-Fi recebem uma
    // leitura barata a cada 10 segundos para acompanhar mudanças sem evento.
    internal sealed class SystemStatusMonitor : IDisposable
    {
        private readonly object _sync = new object();
        private readonly AudioService _audio;
        private readonly NetworkStatusService _network;
        private readonly Timer _slowTimer;
        private SystemStatusSnapshot _snapshot;
        private bool _disposed;

        internal event EventHandler StateChanged;

        internal SystemStatusMonitor()
        {
            _audio = new AudioService();
            _network = new NetworkStatusService();
            _snapshot = new SystemStatusSnapshot(
                _network.ReadSnapshot(), _audio.ReadState(), BatterySnapshot.Read(), BluetoothSnapshot.Read(),
                QuickSettingsSnapshot.Read(), _audio.IsAvailable);
            _audio.StateChanged += OnAudioStateChanged;
            _network.StateChanged += OnNetworkStateChanged;
            _slowTimer = new Timer(OnSlowTimer, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        internal SystemStatusSnapshot ReadSnapshot()
        {
            lock (_sync) return _snapshot;
        }

        internal void SetVolumePercent(double value)
        {
            _audio.SetVolumePercent(value);
        }

        internal void ToggleMute()
        {
            _audio.ToggleMute();
        }

        internal void SetBluetoothRadioState(bool enabled)
        {
            BluetoothSnapshot bluetooth;
            if (!enabled)
            {
                bluetooth = new BluetoothSnapshot(false, false, string.Empty);
            }
            else
            {
                BluetoothSnapshot detected = BluetoothSnapshot.Read();
                bluetooth = new BluetoothSnapshot(true, detected.IsConnected, detected.DeviceName);
            }
            lock (_sync)
            {
                if (_disposed) return;
                _snapshot = new SystemStatusSnapshot(_snapshot.Network, _snapshot.Audio,
                    _snapshot.Battery, bluetooth, _snapshot.QuickSettings, _snapshot.AudioAvailable);
            }
            RaiseStateChanged();
        }

        private void OnAudioStateChanged(object sender, AudioStateChangedEventArgs state)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _snapshot = new SystemStatusSnapshot(_snapshot.Network, state, _snapshot.Battery,
                    _snapshot.Bluetooth, _snapshot.QuickSettings, _audio.IsAvailable);
            }
            RaiseStateChanged();
        }

        private void OnNetworkStateChanged(object sender, EventArgs eventArgs)
        {
            NetworkSnapshot network = _network.ReadSnapshot();
            lock (_sync)
            {
                if (_disposed) return;
                _snapshot = new SystemStatusSnapshot(network, _snapshot.Audio, _snapshot.Battery,
                    _snapshot.Bluetooth, _snapshot.QuickSettings, _snapshot.AudioAvailable);
            }
            RaiseStateChanged();
        }

        private void OnSlowTimer(object state)
        {
            NetworkSnapshot network;
            AudioStateChangedEventArgs audio;
            BatterySnapshot battery;
            BluetoothSnapshot bluetooth;
            QuickSettingsSnapshot quickSettings;
            try
            {
                network = _network.ReadSnapshot();
                audio = _audio.ReadState();
                battery = BatterySnapshot.Read();
                bluetooth = BluetoothSnapshot.Read();
                quickSettings = QuickSettingsSnapshot.Read();
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao atualizar indicadores do sistema: " + exception.Message);
                return;
            }
            lock (_sync)
            {
                if (_disposed) return;
                _snapshot = new SystemStatusSnapshot(network, audio, battery, bluetooth, quickSettings,
                    _audio.IsAvailable);
            }
            RaiseStateChanged();
        }

        private void RaiseStateChanged()
        {
            EventHandler handler = StateChanged;
            if (handler == null || _disposed) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception) { Logger.Write("Falha ao entregar estado do sistema: " + exception.Message); }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _slowTimer.Dispose();
            _audio.StateChanged -= OnAudioStateChanged;
            _network.StateChanged -= OnNetworkStateChanged;
            _network.Dispose();
            _audio.Dispose();
            StateChanged = null;
        }
    }

    internal sealed class WifiConnectionSnapshot
    {
        internal readonly int SignalQuality;
        internal readonly string Name;

        internal WifiConnectionSnapshot(int signalQuality, string name)
        {
            SignalQuality = signalQuality;
            Name = name ?? string.Empty;
        }

        internal static WifiConnectionSnapshot Unavailable
        {
            get { return new WifiConnectionSnapshot(-1, string.Empty); }
        }
    }

    // Consulta RSSI e, quando a política de privacidade permitir, o nome do
    // perfil conectado. Falhas de acesso ao nome preservam o sinal e não
    // solicitam permissões adicionais ao usuário.
    internal static class NativeWifiSignal
    {
        private const int WlanInterfaceStateConnected = 1;
        private const int WlanIntfOpcodeCurrentConnection = 7;
        private const int WlanIntfOpcodeRssi = unchecked((int)0x10000102);

        internal static WifiConnectionSnapshot Read()
        {
            IntPtr clientHandle = IntPtr.Zero;
            IntPtr interfaceList = IntPtr.Zero;
            try
            {
                uint negotiatedVersion;
                if (WlanNative.WlanOpenHandle(2, IntPtr.Zero, out negotiatedVersion, out clientHandle) != 0
                    || clientHandle == IntPtr.Zero) return WifiConnectionSnapshot.Unavailable;
                if (WlanNative.WlanEnumInterfaces(clientHandle, IntPtr.Zero, out interfaceList) != 0
                    || interfaceList == IntPtr.Zero) return WifiConnectionSnapshot.Unavailable;

                int count = Marshal.ReadInt32(interfaceList, 0);
                int itemSize = Marshal.SizeOf(typeof(WlanInterfaceInfo));
                IntPtr firstItem = IntPtr.Add(interfaceList, 8);
                for (int index = 0; index < count; index++)
                {
                    WlanInterfaceInfo item = (WlanInterfaceInfo)Marshal.PtrToStructure(
                        IntPtr.Add(firstItem, index * itemSize), typeof(WlanInterfaceInfo));
                    if (item.State != WlanInterfaceStateConnected) continue;

                    int signalQuality = -1;
                    int dataSize;
                    IntPtr data;
                    int result = WlanNative.WlanQueryInterface(clientHandle, ref item.InterfaceGuid,
                        WlanIntfOpcodeRssi, IntPtr.Zero, out dataSize, out data, IntPtr.Zero);
                    if (result == 0 && data != IntPtr.Zero)
                    {
                        try
                        {
                            int rssi = Marshal.ReadInt32(data);
                            signalQuality = rssi <= -100 ? 0 : rssi >= -50 ? 100
                                : Math.Max(0, Math.Min(100, 2 * (rssi + 100)));
                        }
                        finally { WlanNative.WlanFreeMemory(data); }
                    }

                    string profileName = string.Empty;
                    data = IntPtr.Zero;
                    result = WlanNative.WlanQueryInterface(clientHandle, ref item.InterfaceGuid,
                        WlanIntfOpcodeCurrentConnection, IntPtr.Zero, out dataSize, out data, IntPtr.Zero);
                    if (result == 0 && data != IntPtr.Zero)
                    {
                        try
                        {
                            if (dataSize >= 520)
                                profileName = (Marshal.PtrToStringUni(IntPtr.Add(data, 8), 256)
                                    ?? string.Empty).TrimEnd('\0').Trim();
                        }
                        finally { WlanNative.WlanFreeMemory(data); }
                    }
                    else if (data != IntPtr.Zero) WlanNative.WlanFreeMemory(data);
                    return new WifiConnectionSnapshot(signalQuality, profileName);
                }
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao consultar intensidade do Wi-Fi: " + exception.Message);
            }
            finally
            {
                if (interfaceList != IntPtr.Zero) WlanNative.WlanFreeMemory(interfaceList);
                if (clientHandle != IntPtr.Zero) WlanNative.WlanCloseHandle(clientHandle, IntPtr.Zero);
            }
            return WifiConnectionSnapshot.Unavailable;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WlanInterfaceInfo
    {
        internal Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Description;
        internal int State;
    }

    internal static class WlanNative
    {
        [DllImport("wlanapi.dll")]
        internal static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved,
            out uint negotiatedVersion, out IntPtr clientHandle);

        [DllImport("wlanapi.dll")]
        internal static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

        [DllImport("wlanapi.dll")]
        internal static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved,
            out IntPtr interfaceList);

        [DllImport("wlanapi.dll")]
        internal static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid,
            int opcode, IntPtr reserved, out int dataSize, out IntPtr data, IntPtr opcodeValueType);

        [DllImport("wlanapi.dll")]
        internal static extern void WlanFreeMemory(IntPtr memory);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BluetoothFindRadioParams
    {
        internal int Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BluetoothDeviceSearchParams
    {
        internal int Size;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] internal bool ReturnConnected;
        [MarshalAs(UnmanagedType.Bool)] internal bool IssueInquiry;
        internal byte TimeoutMultiplier;
        internal IntPtr RadioHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BluetoothSystemTime
    {
        internal ushort Year;
        internal ushort Month;
        internal ushort DayOfWeek;
        internal ushort Day;
        internal ushort Hour;
        internal ushort Minute;
        internal ushort Second;
        internal ushort Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BluetoothDeviceInfo
    {
        internal int Size;
        internal ulong Address;
        internal uint ClassOfDevice;
        [MarshalAs(UnmanagedType.Bool)] internal bool Connected;
        [MarshalAs(UnmanagedType.Bool)] internal bool Remembered;
        [MarshalAs(UnmanagedType.Bool)] internal bool Authenticated;
        internal BluetoothSystemTime LastSeen;
        internal BluetoothSystemTime LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] internal string Name;
    }

    internal static class BluetoothNative
    {
        [DllImport("BluetoothApis.dll", SetLastError = true)]
        internal static extern IntPtr BluetoothFindFirstRadio(
            ref BluetoothFindRadioParams parameters, out IntPtr radioHandle);

        [DllImport("BluetoothApis.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BluetoothFindRadioClose(IntPtr findHandle);

        [DllImport("BluetoothApis.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr BluetoothFindFirstDevice(
            ref BluetoothDeviceSearchParams searchParameters, ref BluetoothDeviceInfo deviceInfo);

        [DllImport("BluetoothApis.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool BluetoothFindDeviceClose(IntPtr findHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);
    }
}
