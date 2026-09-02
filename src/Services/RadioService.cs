using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Orla
{
    internal enum RadioKind
    {
        Wifi,
        Bluetooth
    }

    internal sealed class RadioStateSnapshot
    {
        internal readonly bool IsAvailable;
        internal readonly bool IsEnabled;

        internal RadioStateSnapshot(bool isAvailable, bool isEnabled)
        {
            IsAvailable = isAvailable;
            IsEnabled = isEnabled;
        }
    }

    internal sealed class RadiosSnapshot
    {
        internal readonly RadioStateSnapshot Wifi;
        internal readonly RadioStateSnapshot Bluetooth;

        internal RadiosSnapshot(RadioStateSnapshot wifi, RadioStateSnapshot bluetooth)
        {
            Wifi = wifi;
            Bluetooth = bluetooth;
        }

        internal static RadiosSnapshot Unavailable
        {
            get
            {
                RadioStateSnapshot missing = new RadioStateSnapshot(false, false);
                return new RadiosSnapshot(missing, missing);
            }
        }
    }

    // The official WinRT API is loaded through reflection to preserve a single EXE.
    // .NET Framework, sem empacotamento MSIX nem DLLs extras ao lado do Orla.
    internal static class RadioService
    {
        private static readonly object Sync = new object();
        private static Type _radioType;
        private static Type _radioStateType;
        private static Type _radioAccessType;
        private static MethodInfo _asTask;
        private static bool _initialized;
        private static bool? _accessAllowed;

        internal static RadiosSnapshot Read()
        {
            lock (Sync)
            {
                try
                {
                    EnsureInitialized();
                    IEnumerable radios = GetRadios();
                    bool wifiAvailable = false;
                    bool wifiEnabled = false;
                    bool bluetoothAvailable = false;
                    bool bluetoothEnabled = false;
                    foreach (object radio in radios)
                    {
                        string kind = ReadProperty(radio, "Kind");
                        bool enabled = string.Equals(ReadProperty(radio, "State"), "On",
                            StringComparison.OrdinalIgnoreCase);
                        if (string.Equals(kind, "WiFi", StringComparison.OrdinalIgnoreCase))
                        {
                            wifiAvailable = true;
                            wifiEnabled = enabled;
                        }
                        else if (string.Equals(kind, "Bluetooth", StringComparison.OrdinalIgnoreCase))
                        {
                            bluetoothAvailable = true;
                            bluetoothEnabled = enabled;
                        }
                    }
                    return new RadiosSnapshot(
                        new RadioStateSnapshot(wifiAvailable, wifiEnabled),
                        new RadioStateSnapshot(bluetoothAvailable, bluetoothEnabled));
                }
                catch (Exception exception)
                {
                    Logger.Write("Could not query Windows radios: " + Unwrap(exception).Message);
                    return RadiosSnapshot.Unavailable;
                }
            }
        }

        internal static bool Toggle(RadioKind requestedKind)
        {
            lock (Sync)
            {
                try
                {
                    EnsureInitialized();
                    if (!EnsureAccess()) return false;
                    string target = requestedKind == RadioKind.Wifi ? "WiFi" : "Bluetooth";
                    object desiredState = null;
                    List<object> targets = new List<object>();
                    foreach (object radio in GetRadios())
                    {
                        if (!string.Equals(ReadProperty(radio, "Kind"), target,
                            StringComparison.OrdinalIgnoreCase)) continue;
                        targets.Add(radio);
                        bool currentlyEnabled = string.Equals(ReadProperty(radio, "State"), "On",
                            StringComparison.OrdinalIgnoreCase);
                        desiredState = Enum.Parse(_radioStateType, currentlyEnabled ? "Off" : "On");
                    }
                    if (targets.Count == 0 || desiredState == null) return false;

                    MethodInfo setState = _radioType.GetMethod("SetStateAsync", new[] { _radioStateType });
                    bool changed = false;
                    foreach (object radio in targets)
                    {
                        object operation = setState.Invoke(radio, new[] { desiredState });
                        object result = AwaitResult(operation, _radioAccessType);
                        changed |= string.Equals(Convert.ToString(result), "Allowed",
                            StringComparison.OrdinalIgnoreCase);
                    }
                    return changed;
                }
                catch (Exception exception)
                {
                    Logger.Write("Could not toggle a Windows radio: " + Unwrap(exception).Message);
                    return false;
                }
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _radioType = Type.GetType("Windows.Devices.Radios.Radio, Windows, ContentType=WindowsRuntime", true);
            _radioStateType = Type.GetType("Windows.Devices.Radios.RadioState, Windows, ContentType=WindowsRuntime", true);
            _radioAccessType = Type.GetType("Windows.Devices.Radios.RadioAccessStatus, Windows, ContentType=WindowsRuntime", true);
            string runtimePath = Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),
                "System.Runtime.WindowsRuntime.dll");
            Assembly runtimeAssembly = Assembly.LoadFrom(runtimePath);
            Type extensions = runtimeAssembly.GetType("System.WindowsRuntimeSystemExtensions", true);
            _asTask = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(delegate(MethodInfo method)
                {
                    return method.Name == "AsTask" && method.IsGenericMethodDefinition
                        && method.GetGenericArguments().Length == 1
                        && method.GetParameters().Length == 1
                        && method.GetParameters()[0].ParameterType.Name.StartsWith("IAsyncOperation`1",
                            StringComparison.Ordinal);
                }).First();
            _initialized = true;
        }

        private static bool EnsureAccess()
        {
            if (_accessAllowed.HasValue) return _accessAllowed.Value;
            MethodInfo request = _radioType.GetMethod("RequestAccessAsync", BindingFlags.Public | BindingFlags.Static);
            object result = AwaitResult(request.Invoke(null, null), _radioAccessType);
            _accessAllowed = string.Equals(Convert.ToString(result), "Allowed", StringComparison.OrdinalIgnoreCase);
            return _accessAllowed.Value;
        }

        private static IEnumerable GetRadios()
        {
            MethodInfo getRadios = _radioType.GetMethod("GetRadiosAsync", BindingFlags.Public | BindingFlags.Static);
            Type listType = typeof(IReadOnlyList<>).MakeGenericType(_radioType);
            return (IEnumerable)AwaitResult(getRadios.Invoke(null, null), listType);
        }

        private static object AwaitResult(object operation, Type resultType)
        {
            Task task = (Task)_asTask.MakeGenericMethod(resultType).Invoke(null, new[] { operation });
            if (!task.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("The radio API did not respond within five seconds.");
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        private static string ReadProperty(object instance, string name)
        {
            object value = _radioType.GetProperty(name).GetValue(instance, null);
            return Convert.ToString(value);
        }

        private static Exception Unwrap(Exception exception)
        {
            while (exception is TargetInvocationException && exception.InnerException != null)
                exception = exception.InnerException;
            return exception;
        }
    }
}
