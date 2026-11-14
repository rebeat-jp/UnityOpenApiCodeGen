#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;

namespace PlayerVerification
{
    /// <summary>
    /// Posts the report with the browser's own fetch so that a broken UnityWebRequest transport
    /// cannot hide the result of the probe that exercises it.
    /// </summary>
    internal static class PvWebGlBridge
    {
        [DllImport("__Internal")]
        internal static extern void PvPostReport(string url, string json);
    }
}
#endif
