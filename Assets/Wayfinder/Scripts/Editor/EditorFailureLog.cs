using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Wayfinder.Editor
{
    /// <summary>
    /// Batchmode safety net for the free cloud build (D-035).
    /// <para>
    /// game-ci starts the editor with <c>-logfile /dev/stdout</c>, so everything Unity prints —
    /// including the stack trace of an <c>-executeMethod</c> entry point that throws — exists only
    /// in the GitHub Actions job log, never in the workspace. Run 38875757162 failed inside
    /// <c>ProjectSetup.Setup</c> and left nothing behind: the whole build reported one useless
    /// annotation, "Build failed with exit code 1".
    /// </para>
    /// <para>
    /// Every editor entry point therefore runs through <see cref="Run"/>, which appends the cause
    /// to <see cref="LogPath"/> before rethrowing. That file survives in the workspace, is uploaded
    /// in the <c>Unity-logs</c> artifact, and <c>Tools/Build/report_unity_failure.sh</c> turns its
    /// <see cref="Marker"/> line into a check-run annotation.
    /// </para>
    /// </summary>
    public static class EditorFailureLog
    {
        public const string LogDirectory = "Logs";
        public const string LogPath = LogDirectory + "/wayfinder-editor-failure.log";

        /// <summary>Exact prefix that <c>Tools/Build/report_unity_failure.sh</c> greps for.</summary>
        public const string Marker = "[Wayfinder] Editor method failed: ";

        /// <summary>
        /// Runs an editor entry point, recording the cause of any failure before rethrowing it.
        /// The rethrow is deliberate: Unity's non-zero exit code stays the build's verdict.
        /// </summary>
        public static void Run(string method, Action body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            try
            {
                body();
            }
            catch (Exception error)
            {
                Record(method, error);
                throw;
            }
        }

        /// <summary>
        /// Appends one grep-able single-line headline plus the full exception. Never throws:
        /// a diagnostic that fails must not replace the failure it is describing.
        /// </summary>
        public static void Record(string method, Exception error)
        {
            string headline = Marker + method + " - " + Describe(error);
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath, string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-ddTHH:mm:ss}Z {1}{2}{3}{2}",
                    DateTime.UtcNow, headline, Environment.NewLine, error));
            }
            catch (Exception writeFailure)
            {
                Debug.LogWarning("[Wayfinder] Could not write " + LogPath + ": " + writeFailure.Message);
            }
            Debug.LogError(headline);
        }

        /// <summary>Type and message on one line, so the headline stays one annotation.</summary>
        static string Describe(Exception error)
        {
            if (error == null) return "unknown error";
            string message = (error.Message ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
            return error.GetType().FullName + ": " + message;
        }
    }
}
