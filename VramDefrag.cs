using System;
using System.Runtime.InteropServices;

namespace DeepTools
{
    // Дефрагментация VRAM: сбрасывает неиспользуемые GPU-аллокации через
    // IDXGIDevice3::Trim() (DirectX 11.2+, Windows 8.1+). Создаём временное
    // D3D11-устройство, вызываем Trim и освобождаем - игра не перезапускается.
    // Для Windows 10/11 этого достаточно: драйвер принудительно возвращает
    // фрагментированные блоки обратно в пул и перераспределяет их компактно.
    public static class VramDefrag
    {
        // ---- COM-интерфейсы через P/Invoke ----

        [DllImport("d3d11.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int D3D11CreateDevice(
            IntPtr pAdapter,
            int DriverType,         // D3D_DRIVER_TYPE_HARDWARE = 1
            IntPtr Software,
            uint Flags,
            IntPtr pFeatureLevels,
            uint FeatureLevels,
            uint SDKVersion,        // D3D11_SDK_VERSION = 7
            out IntPtr ppDevice,
            IntPtr pFeatureLevel,
            out IntPtr ppImmediateContext);

        // IUnknown::QueryInterface
        [DllImport("ole32.dll")]
        private static extern int QueryInterface(IntPtr pUnk, ref Guid riid, out IntPtr ppvObject);

        // IUnknown::Release
        [DllImport("ole32.dll")]
        private static extern uint Release(IntPtr pUnk);

        // IDXGIDevice3 GUID
        private static readonly Guid IID_IDXGIDevice3 =
            new Guid("6007896c-3244-4afd-bf18-a6d3beda5023");

        // IDXGIDevice3::Trim() - смещение 15 в vtable (после всех методов родителей)
        // IDXGIDevice3 : IDXGIDevice2 : IDXGIDevice1 : IDXGIDevice : IDXGIObject : IUnknown
        // IUnknown(3) + IDXGIObject(4) + IDXGIDevice(4) + IDXGIDevice1(2) + IDXGIDevice2(2) = 15
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void TrimDelegate(IntPtr self);

        public static bool LastResult { get; private set; }
        public static string LastMessage { get; private set; }

        public static void Run()
        {
            LastResult = false;
            LastMessage = "";

            IntPtr device = IntPtr.Zero;
            IntPtr context = IntPtr.Zero;
            IntPtr dxgiDev3 = IntPtr.Zero;

            try
            {
                // Создаём временное D3D11 Hardware устройство
                int hr = D3D11CreateDevice(
                    IntPtr.Zero, 1, IntPtr.Zero, 0,
                    IntPtr.Zero, 0, 7,
                    out device, IntPtr.Zero, out context);

                if (hr < 0 || device == IntPtr.Zero)
                {
                    LastMessage = "D3D11CreateDevice failed (hr=0x" + hr.ToString("X8") + ")";
                    return;
                }

                // QI до IDXGIDevice3
                Guid iid = IID_IDXGIDevice3;
                hr = Marshal.QueryInterface(device, ref iid, out dxgiDev3);
                if (hr < 0 || dxgiDev3 == IntPtr.Zero)
                {
                    LastMessage = "IDXGIDevice3 unavailable (DirectX 11.2 requires Win 8.1+)";
                    return;
                }

                // IDXGIDevice3::Trim находится в слоте 17 vtable:
                // IUnknown 3 + IDXGIObject 4 + IDXGIDevice 4 + IDXGIDevice1 2 + IDXGIDevice2 2 + Trim 1
                IntPtr vtable = Marshal.ReadIntPtr(dxgiDev3);
                IntPtr trimPtr = Marshal.ReadIntPtr(vtable, 17 * IntPtr.Size);
                TrimDelegate trim = (TrimDelegate)Marshal.GetDelegateForFunctionPointer(
                    trimPtr, typeof(TrimDelegate));
                trim(dxgiDev3);

                LastResult = true;
                LastMessage = "VRAM trimmed successfully";
            }
            catch (Exception ex)
            {
                LastMessage = ex.Message;
            }
            finally
            {
                if (dxgiDev3 != IntPtr.Zero) Marshal.Release(dxgiDev3);
                if (context != IntPtr.Zero)  Marshal.Release(context);
                if (device != IntPtr.Zero)   Marshal.Release(device);
            }
        }
    }
}
