using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace PlayerVerification
{
    /// <summary>
    /// Collects the checks of one probe and reports them as JSON. Standalone players write the report to the file
    /// named by <c>-pvReport</c> and exit with a non-zero code on failure; WebGL players post it to the same origin.
    /// The report is built by hand so that it does not depend on the serializer under test.
    /// </summary>
    public sealed class PvContext
    {
        private readonly List<string> checks = new List<string>();

        internal PvContext(Uri serverOrigin)
        {
            ServerOrigin = serverOrigin;
        }

        public Uri ServerOrigin { get; }

        public bool Passed { get; private set; } = true;

        public void Check(string name, bool passed, string detail = "")
        {
            if (!passed)
            {
                Passed = false;
            }

            checks.Add(
                "{\"name\":" + PvRunner.Quote(name)
                + ",\"passed\":" + (passed ? "true" : "false")
                + ",\"detail\":" + PvRunner.Quote(detail ?? string.Empty) + "}");
        }

        public void Equal<T>(string name, T expected, T actual)
        {
            bool equal = EqualityComparer<T>.Default.Equals(expected, actual);
            Check(name, equal, equal ? string.Empty : "expected " + expected + " but was " + actual);
        }

        internal string ChecksJson() => "[" + string.Join(",", checks) + "]";
    }

    public static class PvRunner
    {
        private const float WatchdogSeconds = 90f;

        private static bool finished;

        /// <summary>Runs <paramref name="body"/> once on the player's main thread and reports the result.</summary>
        public static void Run(string combo, string transport, string serializer, Func<PvContext, Task> body)
        {
            var watchdog = new GameObject("PvWatchdog");
            UnityEngine.Object.DontDestroyOnLoad(watchdog);
            watchdog.AddComponent<PvWatchdog>().Initialize(combo, transport, serializer);
            _ = RunCore(combo, transport, serializer, body);
        }

        private static async Task RunCore(string combo, string transport, string serializer, Func<PvContext, Task> body)
        {
            DateTime started = DateTime.UtcNow;
            var context = new PvContext(ResolveServerOrigin(out string originFailure));
            if (originFailure != null)
            {
                context.Check("server-origin", false, originFailure);
            }
            else
            {
                try
                {
                    await body(context);
                }
                catch (Exception exception)
                {
                    context.Check("unhandled-exception", false, exception.ToString());
                }
            }

            Finish(combo, transport, serializer, context, (DateTime.UtcNow - started).TotalMilliseconds);
        }

        internal static void ReportTimeout(string combo, string transport, string serializer)
        {
            var context = new PvContext(null);
            context.Check("watchdog", false, "the probe did not finish within " + WatchdogSeconds + " seconds");
            Finish(combo, transport, serializer, context, WatchdogSeconds * 1000.0);
        }

        internal static bool IsFinished => finished;

        internal static float Watchdog => WatchdogSeconds;

        private static void Finish(string combo, string transport, string serializer, PvContext context, double durationMs)
        {
            if (finished)
            {
                return;
            }

            finished = true;
            var json = new StringBuilder();
            json.Append("{\"schemaVersion\":1");
            json.Append(",\"combo\":").Append(Quote(combo));
            json.Append(",\"transport\":").Append(Quote(transport));
            json.Append(",\"serializer\":").Append(Quote(serializer));
            json.Append(",\"platform\":").Append(Quote(Application.platform.ToString()));
            json.Append(",\"unityVersion\":").Append(Quote(Application.unityVersion));
#if ENABLE_IL2CPP
            json.Append(",\"scriptingBackend\":\"il2cpp\"");
#else
            json.Append(",\"scriptingBackend\":\"mono\"");
#endif
            json.Append(",\"development\":").Append(Debug.isDebugBuild ? "true" : "false");
            json.Append(",\"passed\":").Append(context.Passed ? "true" : "false");
            json.Append(",\"durationMs\":").Append(durationMs.ToString("F0", CultureInfo.InvariantCulture));
            json.Append(",\"checks\":").Append(context.ChecksJson());
            json.Append('}');
            string report = json.ToString();
            Debug.Log("PV_REPORT " + report);

#if UNITY_WEBGL && !UNITY_EDITOR
            string url = new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority) + "/report";
            PvWebGlBridge.PvPostReport(url, report);
#else
            string path = GetArgument("-pvReport");
            if (path != null)
            {
                try
                {
                    File.WriteAllText(path, report, new UTF8Encoding(false));
                }
                catch (Exception exception)
                {
                    Debug.LogError("PV_REPORT_WRITE_FAILED " + exception);
                }
            }

            if (!Application.isEditor)
            {
                Application.Quit(context.Passed ? 0 : 1);
            }
#endif
        }

        private static Uri ResolveServerOrigin(out string failure)
        {
            failure = null;
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                return new Uri(new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority) + "/");
            }
            catch (Exception exception)
            {
                failure = "Application.absoluteURL is unusable: " + exception.Message;
                return null;
            }
#else
            string server = GetArgument("-pvServer");
            if (server == null)
            {
                failure = "the -pvServer argument is missing";
                return null;
            }

            return new Uri(server.TrimEnd('/') + "/");
#endif
        }

        private static string GetArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (arguments[index] == name)
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }

        internal static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20)
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            return builder.Append('"').ToString();
        }
    }

    internal sealed class PvWatchdog : MonoBehaviour
    {
        private string combo;
        private string transport;
        private string serializer;

        internal void Initialize(string comboName, string transportName, string serializerName)
        {
            combo = comboName;
            transport = transportName;
            serializer = serializerName;
        }

        private void Update()
        {
            if (!PvRunner.IsFinished && Time.realtimeSinceStartup > PvRunner.Watchdog)
            {
                PvRunner.ReportTimeout(combo, transport, serializer);
            }
        }
    }
}
