using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeepTools
{
    // Переключение устройства вывода звука в один клик из трея (наушники <-> колонки).
    // Штатного API для смены устройства по умолчанию нет - используется тот же
    // недокументированный IPolicyConfig, что и у всех переключалок (SoundSwitch и т.п.).
    // Работает на Win7+ и не менялся годами, но всё обёрнуто в try/catch на всякий случай
    public static class AudioSwitcher
    {
        public class Device
        {
            public string Id;
            public string Name;
            public bool IsDefault;
        }

        private const int ERender = 0;
        private const int DeviceStateActive = 1;

        // Активные устройства вывода; Name - как в панели управления звуком
        public static List<Device> GetPlaybackDevices()
        {
            var result = new List<Device>();
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();

                string defaultId = null;
                try
                {
                    IMMDevice def;
                    if (enumerator.GetDefaultAudioEndpoint(ERender, 1 /* eMultimedia */, out def) == 0 && def != null)
                    {
                        def.GetId(out defaultId);
                        Marshal.ReleaseComObject(def);
                    }
                }
                catch { }

                IMMDeviceCollection collection;
                if (enumerator.EnumAudioEndpoints(ERender, DeviceStateActive, out collection) != 0 || collection == null)
                    return result;

                int count;
                collection.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IMMDevice dev;
                    if (collection.Item(i, out dev) != 0 || dev == null) continue;

                    string id;
                    dev.GetId(out id);
                    string name = ReadFriendlyName(dev);
                    Marshal.ReleaseComObject(dev);

                    if (string.IsNullOrEmpty(name)) continue;
                    result.Add(new Device
                    {
                        Id = id,
                        Name = name,
                        IsDefault = id != null && id == defaultId
                    });
                }
                Marshal.ReleaseComObject(collection);
                Marshal.ReleaseComObject(enumerator);
            }
            catch { }
            return result;
        }

        // Имя текущего устройства для пункта меню (обрезанное, чтобы влезло)
        public static string CurrentShortName()
        {
            try
            {
                List<Device> devs = GetPlaybackDevices();
                foreach (Device d in devs)
                {
                    if (!d.IsDefault) continue;
                    // "Динамики (Realtek High Definition Audio)" -> "Динамики"
                    string name = d.Name;
                    int paren = name.IndexOf(" (");
                    if (paren > 0) name = name.Substring(0, paren);
                    if (name.Length > 18) name = name.Substring(0, 18) + "…";
                    return name;
                }
            }
            catch { }
            return null;
        }

        // Делает следующее по списку устройство дефолтным. Возвращает его имя или null
        public static string CycleNext()
        {
            try
            {
                List<Device> devs = GetPlaybackDevices();
                if (devs.Count < 2) return null;

                int current = 0;
                for (int i = 0; i < devs.Count; i++)
                {
                    if (devs[i].IsDefault) { current = i; break; }
                }
                Device next = devs[(current + 1) % devs.Count];

                var policy = (IPolicyConfig)new PolicyConfigClientComObject();
                // Все три роли: мультимедиа, консоль (системные звуки), связь
                policy.SetDefaultEndpoint(next.Id, 0);
                policy.SetDefaultEndpoint(next.Id, 1);
                policy.SetDefaultEndpoint(next.Id, 2);
                Marshal.ReleaseComObject(policy);

                return next.Name;
            }
            catch
            {
                return null;
            }
        }

        private static string ReadFriendlyName(IMMDevice dev)
        {
            try
            {
                IPropertyStore store;
                if (dev.OpenPropertyStore(0 /* STGM_READ */, out store) != 0 || store == null) return null;

                var key = new PropertyKey
                {
                    FmtId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
                    Pid = 14 // PKEY_Device_FriendlyName
                };
                PropVariant value;
                store.GetValue(ref key, out value);
                Marshal.ReleaseComObject(store);

                string name = null;
                if (value.Vt == 31 /* VT_LPWSTR */ && value.Pointer != IntPtr.Zero)
                    name = Marshal.PtrToStringUni(value.Pointer);
                PropVariantClear(ref value);
                return name;
            }
            catch
            {
                return null;
            }
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        // ---------- COM-интероп Core Audio ----------

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FmtId;
            public int Pid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort Vt;
            public ushort R1;
            public ushort R2;
            public ushort R3;
            public IntPtr Pointer;
            public IntPtr Pointer2;
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, out IntPtr iface);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        private class PolicyConfigClientComObject { }

        // Недокументированный интерфейс. Первые 10 слотов vtable не используем -
        // объявлены заглушками только ради правильного смещения SetDefaultEndpoint
        [Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int Unused0();
            [PreserveSig] int Unused1();
            [PreserveSig] int Unused2();
            [PreserveSig] int Unused3();
            [PreserveSig] int Unused4();
            [PreserveSig] int Unused5();
            [PreserveSig] int Unused6();
            [PreserveSig] int Unused7();
            [PreserveSig] int Unused8();
            [PreserveSig] int Unused9();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
        }
    }
}
