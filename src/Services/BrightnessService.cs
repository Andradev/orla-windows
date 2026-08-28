using System;
using System.Collections.Generic;
using System.Management;
using Forms = System.Windows.Forms;

namespace Orla
{
    // Um único controle representa o brilho do computador, como no painel do
    // Windows. Monitores externos compatíveis são descobertos por DDC/CI e o
    // mesmo valor é aplicado a todos; os demais apenas entram no diagnóstico.
    internal sealed class BrightnessService : IDisposable
    {
        private sealed class Target
        {
            internal IntPtr Handle;
            internal uint Minimum;
            internal uint Maximum;
        }

        private readonly object _sync = new object();
        private readonly List<NativeMethods.PhysicalMonitor[]> _physicalMonitorArrays;
        private readonly List<Target> _targets;
        private readonly List<string> _integratedMonitorInstances;
        private bool _disposed;

        private BrightnessService(List<NativeMethods.PhysicalMonitor[]> physicalMonitorArrays,
            List<Target> targets, List<string> integratedMonitorInstances)
        {
            _physicalMonitorArrays = physicalMonitorArrays;
            _targets = targets;
            _integratedMonitorInstances = integratedMonitorInstances;
        }

        internal int SupportedCount { get { return _targets.Count + _integratedMonitorInstances.Count; } }
        internal int IntegratedCount { get { return _integratedMonitorInstances.Count; } }
        internal int DdcCount { get { return _targets.Count; } }

        internal static BrightnessService Create()
        {
            List<NativeMethods.PhysicalMonitor[]> arrays = new List<NativeMethods.PhysicalMonitor[]>();
            List<Target> targets = new List<Target>();
            List<string> integratedMonitors = ReadIntegratedMonitorInstances();
            HashSet<IntPtr> logicalMonitors = new HashSet<IntPtr>();
            try
            {
                foreach (Forms.Screen screen in Forms.Screen.AllScreens)
                {
                    System.Drawing.Rectangle bounds = screen.Bounds;
                    NativeMethods.ScreenPoint center = new NativeMethods.ScreenPoint
                    {
                        X = bounds.Left + bounds.Width / 2,
                        Y = bounds.Top + bounds.Height / 2
                    };
                    IntPtr logicalMonitor = NativeMethods.MonitorFromPoint(center, NativeMethods.MonitorDefaultToNearest);
                    if (logicalMonitor == IntPtr.Zero || !logicalMonitors.Add(logicalMonitor)) continue;

                    uint count;
                    if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(logicalMonitor, out count)
                        || count == 0 || count > 16) continue;
                    NativeMethods.PhysicalMonitor[] physicalMonitors = new NativeMethods.PhysicalMonitor[(int)count];
                    if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(logicalMonitor, count, physicalMonitors))
                        continue;
                    arrays.Add(physicalMonitors);

                    foreach (NativeMethods.PhysicalMonitor monitor in physicalMonitors)
                    {
                        uint minimum;
                        uint current;
                        uint maximum;
                        if (NativeMethods.GetMonitorBrightness(monitor.Handle, out minimum, out current, out maximum)
                            && maximum > minimum)
                        {
                            targets.Add(new Target { Handle = monitor.Handle, Minimum = minimum, Maximum = maximum });
                        }
                    }
                }
                return new BrightnessService(arrays, targets, integratedMonitors);
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao detectar suporte DDC/CI: " + exception.Message);
                foreach (NativeMethods.PhysicalMonitor[] array in arrays)
                    NativeMethods.DestroyPhysicalMonitors((uint)array.Length, array);
                return new BrightnessService(new List<NativeMethods.PhysicalMonitor[]>(),
                    new List<Target>(), integratedMonitors);
            }
        }

        private static List<string> ReadIntegratedMonitorInstances()
        {
            List<string> instances = new List<string>();
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"root\wmi", "SELECT Active, InstanceName FROM WmiMonitorBrightnessMethods WHERE Active = TRUE"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject monitor in results)
                    using (monitor)
                    {
                        string instance = monitor["InstanceName"] as string;
                        if (!string.IsNullOrWhiteSpace(instance)) instances.Add(instance);
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.Write("Brilho integrado WMI indisponível: " + exception.Message);
            }
            return instances;
        }

        internal bool TryReadPercent(out int percent)
        {
            lock (_sync)
            {
                percent = 0;
                if (_disposed || SupportedCount == 0) return false;
                int sum = 0;
                int read = 0;
                int integratedPercent;
                if (TryReadIntegratedBrightness(out integratedPercent))
                {
                    // O Quick Settings do Windows representa o painel interno,
                    // não uma média entre telas com níveis independentes.
                    percent = integratedPercent;
                    return true;
                }
                foreach (Target target in _targets)
                {
                    uint minimum;
                    uint current;
                    uint maximum;
                    if (!NativeMethods.GetMonitorBrightness(target.Handle, out minimum, out current, out maximum)
                        || maximum <= minimum) continue;
                    target.Minimum = minimum;
                    target.Maximum = maximum;
                    sum += Math.Max(0, Math.Min(100,
                        (int)Math.Round((current - minimum) * 100.0 / (maximum - minimum))));
                    read++;
                }
                if (read == 0) return false;
                percent = (int)Math.Round(sum / (double)read);
                return true;
            }
        }

        internal bool SetPercent(int percent)
        {
            lock (_sync)
            {
                if (_disposed || SupportedCount == 0) return false;
                int bounded = Math.Max(0, Math.Min(100, percent));
                bool changed = SetIntegratedBrightness(bounded);
                foreach (Target target in _targets)
                {
                    if (target.Maximum <= target.Minimum) continue;
                    uint nativeValue = target.Minimum
                        + (uint)Math.Round((target.Maximum - target.Minimum) * bounded / 100.0);
                    changed = NativeMethods.SetMonitorBrightness(target.Handle, nativeValue) || changed;
                }

                return changed;
            }
        }

        private bool TryReadIntegratedBrightness(out int percent)
        {
            percent = 0;
            if (_integratedMonitorInstances.Count == 0) return false;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"root\wmi", "SELECT Active, CurrentBrightness, InstanceName FROM WmiMonitorBrightness WHERE Active = TRUE"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject monitor in results)
                    using (monitor)
                    {
                        string instance = monitor["InstanceName"] as string;
                        if (!_integratedMonitorInstances.Contains(instance)) continue;
                        percent = Convert.ToInt32(monitor["CurrentBrightness"]);
                        percent = Math.Max(0, Math.Min(100, percent));
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao ler brilho integrado: " + exception.Message);
            }
            return false;
        }

        private bool SetIntegratedBrightness(int percent)
        {
            if (_integratedMonitorInstances.Count == 0) return false;
            bool changed = false;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    @"root\wmi", "SELECT Active, InstanceName FROM WmiMonitorBrightnessMethods WHERE Active = TRUE"))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject monitor in results)
                    using (monitor)
                    {
                        string instance = monitor["InstanceName"] as string;
                        if (!_integratedMonitorInstances.Contains(instance)) continue;
                        using (ManagementBaseObject parameters = monitor.GetMethodParameters("WmiSetBrightness"))
                        {
                            parameters["Timeout"] = (uint)0;
                            parameters["Brightness"] = (byte)percent;
                            using (ManagementBaseObject result = monitor.InvokeMethod(
                                "WmiSetBrightness", parameters, null))
                            {
                                changed = (result != null && Convert.ToUInt32(result["ReturnValue"]) == 0) || changed;
                            }
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.Write("Falha ao ajustar brilho integrado: " + exception.Message);
            }
            return changed;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (NativeMethods.PhysicalMonitor[] array in _physicalMonitorArrays)
                    NativeMethods.DestroyPhysicalMonitors((uint)array.Length, array);
                _physicalMonitorArrays.Clear();
                _targets.Clear();
                _integratedMonitorInstances.Clear();
            }
        }
    }
}
