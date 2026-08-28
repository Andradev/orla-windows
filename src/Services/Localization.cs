using System.Globalization;
using Microsoft.Win32;

namespace Orla
{
    // Usa o idioma de interface do Windows sem carregar arquivos, pacotes ou
    // serviços extras. Português e espanhol são explícitos; os demais idiomas
    // recebem o fallback internacional em inglês.
    internal static class Loc
    {
        private static readonly CultureInfo InterfaceCulture = ResolveInterfaceCulture();
        private static readonly CultureInfo RegionalCulture = ResolveRegionalCulture();
        private static readonly string Language = InterfaceCulture.TwoLetterISOLanguageName;

        internal static CultureInfo FormattingCulture { get { return RegionalCulture; } }
        internal static string LanguageName { get { return Language; } }

        private static CultureInfo ResolveInterfaceCulture()
        {
            // PreferredUILanguages é o "Idioma de exibição do Windows". A
            // lista de idiomas do perfil também contém layouts de teclado e
            // não deve decidir sozinha o idioma da interface do Orla.
            try
            {
                using (RegistryKey desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    string[] languages = desktop == null ? null : desktop.GetValue("PreferredUILanguages") as string[];
                    if (languages != null && languages.Length > 0 && !string.IsNullOrWhiteSpace(languages[0]))
                        return CultureInfo.GetCultureInfo(languages[0]);
                }
            }
            catch { }
            return CultureInfo.CurrentUICulture ?? CultureInfo.GetCultureInfo("en-US");
        }

        private static CultureInfo ResolveRegionalCulture()
        {
            // LocaleName corresponde a Região > Formato regional e preserva
            // ordem de data, nomes, separadores e relógio de 12/24 horas.
            try
            {
                using (RegistryKey international = Registry.CurrentUser.OpenSubKey(@"Control Panel\International"))
                {
                    string localeName = international == null ? null : international.GetValue("LocaleName") as string;
                    if (!string.IsNullOrWhiteSpace(localeName))
                        return CultureInfo.GetCultureInfo(localeName);
                }
            }
            catch { }
            return CultureInfo.CurrentCulture ?? InterfaceCulture;
        }

        private static string T(string portuguese, string english, string spanish)
        {
            if (Language == "pt") return portuguese;
            if (Language == "es") return spanish;
            return english;
        }

        internal static string Desktop { get { return T("Área de Trabalho", "Desktop", "Escritorio"); } }
        internal static string FileExplorer { get { return T("Explorador de Arquivos", "File Explorer", "Explorador de archivos"); } }
        internal static string OpenFluentSearch { get { return T("Abrir Fluent Search", "Open Fluent Search", "Abrir Fluent Search"); } }
        internal static string OpenFluentSearchHint { get { return T("Abrir Fluent Search — toque na tecla Windows", "Open Fluent Search — tap the Windows key", "Abrir Fluent Search — pulsa la tecla Windows"); } }
        internal static string DateAndTime { get { return T("Data e hora", "Date and time", "Fecha y hora"); } }
        internal static string NetworkAndInternet { get { return T("Rede e Internet", "Network and Internet", "Red e Internet"); } }
        internal static string Wifi { get { return "Wi-Fi"; } }
        internal static string Sound { get { return T("Som", "Sound", "Sonido"); } }
        internal static string PowerAndBattery { get { return T("Energia e bateria", "Power and battery", "Energía y batería"); } }
        internal static string Bluetooth { get { return "Bluetooth"; } }
        internal static string BluetoothDevice { get { return T("Dispositivo Bluetooth", "Bluetooth device", "Dispositivo Bluetooth"); } }
        internal static string NoBluetoothDevice { get { return T("Nenhum dispositivo conectado", "No device connected", "Ningún dispositivo conectado"); } }
        internal static string Disabled { get { return T("Desativado", "Off", "Desactivado"); } }
        internal static string Settings { get { return T("Configurações", "Settings", "Configuración"); } }
        internal static string OpenNewWindow { get { return T("Abrir nova janela", "Open new window", "Abrir nueva ventana"); } }
        internal static string UnpinFromDock { get { return T("Desafixar do Dock", "Unpin from Dock", "Desanclar del Dock"); } }
        internal static string PinToDock { get { return T("Fixar no Dock", "Pin to Dock", "Anclar al Dock"); } }
        internal static string CloseAllWindows { get { return T("Fechar todas as janelas", "Close all windows", "Cerrar todas las ventanas"); } }
        internal static string CloseWindow { get { return T("Fechar janela", "Close window", "Cerrar ventana"); } }
        internal static string ShowDesktop { get { return T("Mostrar área de trabalho", "Show desktop", "Mostrar escritorio"); } }
        internal static string RecycleBin { get { return T("Lixeira", "Recycle Bin", "Papelera de reciclaje"); } }
        internal static string RestoreTaskbarAndExit { get { return T("Restaurar barra do Windows e sair", "Restore Windows taskbar and exit", "Restaurar la barra de Windows y salir"); } }

        internal static string QuickPanelTitle { get { return T("Controles rápidos da Orla", "Orla quick controls", "Controles rápidos de Orla"); } }
        internal static string Controls { get { return T("Controles", "Controls", "Controles"); } }
        internal static string Volume { get { return T("Volume", "Volume", "Volumen"); } }
        internal static string Brightness { get { return T("Brilho", "Brightness", "Brillo"); } }
        internal static string EnergySaver { get { return T("Economia de energia", "Energy saver", "Ahorro de energía"); } }
        internal static string EnergySaverShort { get { return T("Economia", "Energy saver", "Ahorro"); } }
        internal static string NightLight { get { return T("Luz noturna", "Night light", "Luz nocturna"); } }
        internal static string OpenEnergySaverSettings { get { return T("Abrir configurações de economia de energia", "Open energy saver settings", "Abrir configuración de ahorro de energía"); } }
        internal static string OpenNightLightSettings { get { return T("Abrir configurações de luz noturna", "Open night light settings", "Abrir configuración de luz nocturna"); } }
        internal static string HiddenTrayIcons { get { return T("Mostrar ícones ocultos", "Show hidden icons", "Mostrar iconos ocultos"); } }
        internal static string CheckingMonitorSupport { get { return T("Verificando suporte dos monitores…", "Checking monitor support…", "Comprobando compatibilidad de monitores…"); } }
        internal static string DdcUnavailable { get { return T("Nenhum monitor oferece controle DDC/CI", "No monitor offers DDC/CI control", "Ningún monitor ofrece control DDC/CI"); } }
        internal static string BrightnessTargets(int integrated, int ddc)
        {
            if (Language == "pt")
            {
                if (integrated > 0 && ddc > 0) return "Notebook + " + ddc.ToString(FormattingCulture) + " DDC/CI detectado" + (ddc == 1 ? "" : "s");
                if (integrated > 0) return "Tela integrada do notebook";
                return ddc.ToString(FormattingCulture) + " monitor" + (ddc == 1 ? "" : "es") + " DDC/CI detectado" + (ddc == 1 ? "" : "s");
            }
            if (Language == "es")
            {
                if (integrated > 0 && ddc > 0) return "Portátil + " + ddc.ToString(FormattingCulture) + " DDC/CI detectado" + (ddc == 1 ? "" : "s");
                if (integrated > 0) return "Pantalla integrada del portátil";
                return ddc.ToString(FormattingCulture) + " monitor" + (ddc == 1 ? "" : "es") + " DDC/CI detectado" + (ddc == 1 ? "" : "s");
            }
            if (integrated > 0 && ddc > 0) return "Laptop + " + ddc.ToString(FormattingCulture) + " DDC/CI detected";
            if (integrated > 0) return "Built-in laptop display";
            return ddc.ToString(FormattingCulture) + " DDC/CI monitor" + (ddc == 1 ? "" : "s") + " detected";
        }
        internal static string MasterVolume { get { return T("Volume principal", "Master volume", "Volumen principal"); } }
        internal static string ToggleMute { get { return T("Alternar mute", "Toggle mute", "Alternar silencio"); } }
        internal static string OpenWindowsSettings { get { return T("Abrir configurações do Windows", "Open Windows settings", "Abrir configuración de Windows"); } }
        internal static string OpenNetworkSettings { get { return T("Abrir configurações de rede", "Open network settings", "Abrir configuración de red"); } }
        internal static string OpenBluetoothSettings { get { return T("Abrir configurações de Bluetooth", "Open Bluetooth settings", "Abrir configuración de Bluetooth"); } }
        internal static string ToggleWifi { get { return T("Ativar ou desativar Wi-Fi", "Turn Wi-Fi on or off", "Activar o desactivar Wi-Fi"); } }
        internal static string ToggleBluetooth { get { return T("Ativar ou desativar Bluetooth", "Turn Bluetooth on or off", "Activar o desactivar Bluetooth"); } }
        internal static string OpenPowerSettings { get { return T("Abrir configurações de energia", "Open power settings", "Abrir configuración de energía"); } }
        internal static string Status { get { return T("Estado", "Status", "Estado"); } }

        internal static string NoConnection { get { return T("Sem conexão", "No connection", "Sin conexión"); } }
        internal static string CheckNetwork { get { return T("Verifique o Wi-Fi ou o cabo de rede", "Check Wi-Fi or the network cable", "Comprueba el Wi-Fi o el cable de red"); } }
        internal static string WifiConnected { get { return T("Wi-Fi conectado", "Wi-Fi connected", "Wi-Fi conectado"); } }
        internal static string EthernetConnected { get { return T("Ethernet conectado", "Ethernet connected", "Ethernet conectado"); } }
        internal static string NetworkConnected { get { return T("Rede conectada", "Network connected", "Red conectada"); } }
        internal static string InternetAvailable { get { return T("Internet disponível", "Internet available", "Internet disponible"); } }
        internal static string WifiSignal(int quality)
        {
            return T("Sinal: ", "Signal: ", "Señal: ") + quality.ToString(FormattingCulture) + "%";
        }
        internal static string NetworkStatus { get { return T("Estado da rede", "Network status", "Estado de la red"); } }
        internal static string TemporarilyUnavailable { get { return T("Informação temporariamente indisponível", "Information temporarily unavailable", "Información temporalmente no disponible"); } }
        internal static string Unavailable { get { return T("Indisponível", "Unavailable", "No disponible"); } }
        internal static string Enabled { get { return T("Ativado", "On", "Activado"); } }
        internal static string Connected { get { return T("Conectado", "Connected", "Conectado"); } }

        internal static string ExternalPower { get { return T("Alimentação externa", "External power", "Alimentación externa"); } }
        internal static string NoBatteryReported { get { return T("Este computador não informa uma bateria", "This computer does not report a battery", "Este equipo no informa una batería"); } }
        internal static string Charging { get { return T("Carregando", "Charging", "Cargando"); } }
        internal static string PluggedIn { get { return T("Conectado à energia", "Plugged in", "Conectado a la corriente"); } }
        internal static string OnBattery { get { return T("Usando bateria", "On battery", "Usando la batería"); } }
        internal static string Energy { get { return T("Energia", "Power", "Energía"); } }
        internal static string RemainingMinutes(int minutes)
        {
            return T(minutes.ToString(FormattingCulture) + " min restantes",
                minutes.ToString(FormattingCulture) + " min remaining",
                minutes.ToString(FormattingCulture) + " min restantes");
        }

        internal static string VolumeStatus(int percent, bool muted)
        {
            if (muted) return T("Som silenciado", "Sound muted", "Sonido silenciado");
            return T("Volume: ", "Volume: ", "Volumen: ") + percent.ToString(FormattingCulture) + "%";
        }

        internal static string BluetoothStatus(BluetoothSnapshot state)
        {
            if (state == null || !state.IsEnabled)
                return T("Bluetooth desativado", "Bluetooth off", "Bluetooth desactivado");
            if (state != null && state.IsConnected)
                return T("Bluetooth conectado: ", "Bluetooth connected: ", "Bluetooth conectado: ") + state.DeviceName;
            return T("Bluetooth sem dispositivo conectado", "No Bluetooth device connected", "Bluetooth sin dispositivo conectado");
        }

        internal static string BatteryStatus(BatterySnapshot state)
        {
            if (state == null) return PowerAndBattery;
            return state.Status + " • " + state.Detail;
        }
    }
}
