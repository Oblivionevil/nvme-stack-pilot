using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NvmeDriverSwitch.Infrastructure
{
    /// <summary>
    /// P/Invokes fuer die Geraetebaum-Navigation (CfgMgr32) und den Neustart (advapi32).
    /// CfgMgr32 liefert die Eltern-Kind-Beziehung Controller -> Datentraeger, die WMI nicht hergibt.
    /// </summary>
    internal static class NativeMethods
    {
        public const int CR_SUCCESS = 0;
        public const int CM_LOCATE_DEVNODE_NORMAL = 0x00000000;

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, int ulFlags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        public static extern int CM_Get_Child(out uint pdnDevInst, uint dnDevInst, int ulFlags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        public static extern int CM_Get_Sibling(out uint pdnDevInst, uint dnDevInst, int ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int CM_Get_Device_IDW(uint dnDevInst, StringBuilder buffer, int bufferLen, int ulFlags);

        [DllImport("cfgmgr32.dll", ExactSpelling = true)]
        public static extern int CM_Get_Device_ID_Size(out int pulLen, uint dnDevInst, int ulFlags);

        /// <summary>Instanz-ID eines Devnodes, oder null.</summary>
        public static string GetDeviceId(uint devInst)
        {
            int len;
            if (CM_Get_Device_ID_Size(out len, devInst, 0) != CR_SUCCESS || len <= 0)
                return null;

            var sb = new StringBuilder(len + 1);
            if (CM_Get_Device_IDW(devInst, sb, sb.Capacity, 0) != CR_SUCCESS)
                return null;

            return sb.ToString();
        }

        // ---- Neustart ----

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID_AND_ATTRIBUTES
        {
            public LUID Luid;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privilege0;
        }

        private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        private const uint TOKEN_QUERY = 0x0008;
        private const uint SE_PRIVILEGE_ENABLED = 0x0002;
        private const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

        // Neustart nach Anwendungs-/Planungswartung - taucht so im Ereignisprotokoll auf.
        private const uint SHTDN_REASON_MAJOR_OPERATINGSYSTEM = 0x00020000;
        private const uint SHTDN_REASON_MINOR_RECONFIG = 0x00000004;
        private const uint SHTDN_REASON_FLAG_PLANNED = 0x80000000;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValueW(string systemName, string name, out LUID luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
            ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool InitiateSystemShutdownExW(string machineName, string message,
            uint timeout, [MarshalAs(UnmanagedType.Bool)] bool forceAppsClosed,
            [MarshalAs(UnmanagedType.Bool)] bool rebootAfterShutdown, uint reason);

        /// <summary>Fordert SeShutdownPrivilege an und startet neu. Wirft bei Fehlschlag.</summary>
        public static void Reboot(int delaySeconds)
        {
            EnableShutdownPrivilege();

            bool ok = InitiateSystemShutdownExW(
                null,
                "Neustart zum Umschalten des NVMe-Treibers.",
                (uint)delaySeconds,
                false,
                true,
                SHTDN_REASON_MAJOR_OPERATINGSYSTEM | SHTDN_REASON_MINOR_RECONFIG | SHTDN_REASON_FLAG_PLANNED);

            if (!ok)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        private static void EnableShutdownPrivilege()
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                LUID luid;
                if (!LookupPrivilegeValueW(null, SE_SHUTDOWN_NAME, out luid))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Privilege0 = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }
                };

                if (!AdjustTokenPrivileges(token, false, ref tp, (uint)Marshal.SizeOf(typeof(TOKEN_PRIVILEGES)), IntPtr.Zero, IntPtr.Zero))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

                // AdjustTokenPrivileges meldet Erfolg, auch wenn das Privileg nicht zugewiesen wurde.
                int err = Marshal.GetLastWin32Error();
                if (err != 0)
                    throw new System.ComponentModel.Win32Exception(err);
            }
            finally
            {
                CloseHandle(token);
            }
        }
    }
}
