using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BPGPUpdater
{
    /// <summary>
    /// Whether this copy runs from the Microsoft Store (an MSIX package). The Store signs and updates the
    /// app itself, so a Store install turns off the self-update; Windows also ignores the Run key for
    /// packaged apps, so "Start with Windows" is hidden there.
    /// </summary>
    internal static class Packaging
    {
        private const int AppModelErrorNoPackage = 15700;
        private static readonly Lazy<bool> Packaged = new Lazy<bool>(Detect);

        public static bool IsStoreInstall
        {
            get { return Packaged.Value; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder packageFullName);

        private static bool Detect()
        {
            try
            {
                int length = 0;
                // Without a package this returns APPMODEL_ERROR_NO_PACKAGE; with one, "buffer too small"
                return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
            }
            catch (EntryPointNotFoundException)
            {
                return false; // Windows 7: no packages at all
            }
        }
    }
}
