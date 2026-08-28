using System;
using System.Runtime.InteropServices;

namespace Orla
{
    internal sealed class AudioStateChangedEventArgs : EventArgs
    {
        internal readonly int VolumePercent;
        internal readonly bool IsMuted;

        internal AudioStateChangedEventArgs(int volumePercent, bool isMuted)
        {
            VolumePercent = volumePercent;
            IsMuted = isMuted;
        }
    }

    // O monitor compartilhado mantém uma instância; o Quick Panel cria outra
    // somente enquanto está aberto. O callback substitui polling e é removido
    // antes de liberar os objetos COM.
    internal sealed class AudioService : IDisposable
    {
        private static readonly Guid EndpointVolumeId = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static readonly Guid EventContext = new Guid("25CD89A9-49E5-4B30-9055-9937632A5DE1");
        private IMMDeviceEnumerator _enumerator;
        private IMMDevice _device;
        private IAudioEndpointVolume _endpoint;
        private EndpointVolumeCallback _callback;
        private bool _disposed;

        internal event EventHandler<AudioStateChangedEventArgs> StateChanged;

        internal bool IsAvailable
        {
            get { return !_disposed && _endpoint != null; }
        }

        internal AudioService()
        {
            try
            {
                _enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
                Marshal.ThrowExceptionForHR(_enumerator.GetDefaultAudioEndpoint(AudioDataFlow.Render, AudioRole.Multimedia, out _device));
                object endpoint;
                Guid interfaceId = EndpointVolumeId;
                Marshal.ThrowExceptionForHR(_device.Activate(ref interfaceId, ComClassContext.All, IntPtr.Zero, out endpoint));
                _endpoint = (IAudioEndpointVolume)endpoint;
                _callback = new EndpointVolumeCallback(this);
                Marshal.ThrowExceptionForHR(_endpoint.RegisterControlChangeNotify(_callback));
            }
            catch (Exception exception)
            {
                Logger.Write("Áudio nativo indisponível na Orla: " + exception.Message);
                Dispose();
            }
        }

        internal AudioStateChangedEventArgs ReadState()
        {
            if (!IsAvailable) return new AudioStateChangedEventArgs(0, false);
            try
            {
                float scalar;
                bool muted;
                Marshal.ThrowExceptionForHR(_endpoint.GetMasterVolumeLevelScalar(out scalar));
                Marshal.ThrowExceptionForHR(_endpoint.GetMute(out muted));
                return CreateState(scalar, muted);
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao ler volume: " + exception.Message);
                return new AudioStateChangedEventArgs(0, false);
            }
        }

        internal void SetVolumePercent(double value)
        {
            if (!IsAvailable) return;
            try
            {
                float scalar = (float)Math.Max(0, Math.Min(1, value / 100.0));
                Guid context = EventContext;
                Marshal.ThrowExceptionForHR(_endpoint.SetMasterVolumeLevelScalar(scalar, ref context));
                bool muted;
                Marshal.ThrowExceptionForHR(_endpoint.GetMute(out muted));
                NativeStateChanged(scalar, muted);
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao ajustar volume: " + exception.Message);
            }
        }

        internal void ToggleMute()
        {
            if (!IsAvailable) return;
            try
            {
                bool muted;
                Marshal.ThrowExceptionForHR(_endpoint.GetMute(out muted));
                Guid context = EventContext;
                Marshal.ThrowExceptionForHR(_endpoint.SetMute(!muted, ref context));
                float scalar;
                Marshal.ThrowExceptionForHR(_endpoint.GetMasterVolumeLevelScalar(out scalar));
                NativeStateChanged(scalar, !muted);
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao alternar mute: " + exception.Message);
            }
        }

        private static AudioStateChangedEventArgs CreateState(float scalar, bool muted)
        {
            int percent = Math.Max(0, Math.Min(100, (int)Math.Round(scalar * 100)));
            return new AudioStateChangedEventArgs(percent, muted);
        }

        private void NativeStateChanged(float scalar, bool muted)
        {
            if (_disposed) return;
            EventHandler<AudioStateChangedEventArgs> handler = StateChanged;
            if (handler == null) return;
            try
            {
                handler(this, CreateState(scalar, muted));
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao entregar evento de volume: " + exception.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_endpoint != null && _callback != null)
            {
                try { _endpoint.UnregisterControlChangeNotify(_callback); }
                catch { }
            }
            StateChanged = null;
            _callback = null;
            ReleaseComObject(_endpoint);
            ReleaseComObject(_device);
            ReleaseComObject(_enumerator);
            _endpoint = null;
            _device = null;
            _enumerator = null;
        }

        private static void ReleaseComObject(object value)
        {
            if (value == null || !Marshal.IsComObject(value)) return;
            try { Marshal.FinalReleaseComObject(value); }
            catch { }
        }

        private sealed class EndpointVolumeCallback : IAudioEndpointVolumeCallback
        {
            private readonly AudioService _owner;

            internal EndpointVolumeCallback(AudioService owner)
            {
                _owner = owner;
            }

            public int OnNotify(IntPtr notificationData)
            {
                if (notificationData == IntPtr.Zero) return 0;
                AudioVolumeNotificationData data = (AudioVolumeNotificationData)Marshal.PtrToStructure(
                    notificationData, typeof(AudioVolumeNotificationData));
                _owner.NativeStateChanged(data.MasterVolume, data.Muted != 0);
                return 0;
            }
        }
    }

    internal enum AudioDataFlow
    {
        Render,
        Capture,
        All
    }

    internal enum AudioRole
    {
        Console,
        Multimedia,
        Communications
    }

    [Flags]
    internal enum ComClassContext : uint
    {
        InProcessServer = 0x1,
        InProcessHandler = 0x2,
        LocalServer = 0x4,
        RemoteServer = 0x10,
        All = InProcessServer | InProcessHandler | LocalServer | RemoteServer
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioVolumeNotificationData
    {
        internal Guid EventContext;
        internal int Muted;
        internal float MasterVolume;
        internal uint ChannelCount;
        internal float FirstChannelVolume;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal sealed class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(AudioDataFlow dataFlow, uint stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(AudioDataFlow dataFlow, AudioRole role, out IMMDevice device);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid interfaceId, ComClassContext context, IntPtr activationParameters,
            [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        [PreserveSig]
        int OpenPropertyStore(int storageMode, out IntPtr properties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig]
        int GetState(out uint state);
    }

    [ComImport]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr notificationData);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float minimum, out float maximum, out float increment);
    }
}
