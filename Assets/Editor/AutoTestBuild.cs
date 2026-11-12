using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
///  Compila una build de Windows para la PRUEBA AUTOMÁTICA (ver AutoPlayTest).
///
///  Se usa desde la línea de comandos (lo hace tools/autotest.ps1):
///    Unity.exe -batchmode -quit -projectPath &lt;repo&gt;
///              -executeMethod AutoTestBuild.BuildWindows
///              -autoTestBuildPath &lt;ruta\Ratms.exe&gt;
///
///  Usa las escenas activas de Build Settings, saltándose las que ya no
///  existen en disco (p. ej. Test_Network.unity, que quedó huérfana).
///  También se puede lanzar a mano: menú Ratms → Build prueba automática.
/// </summary>
public static class AutoTestBuild
{
    [MenuItem("Ratms/Build prueba automática")]
    public static void BuildWindows()
    {
        string outPath = GetArg("-autoTestBuildPath") ?? Path.GetFullPath("AutoTest/Build/Ratms.exe");

        var scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled && File.Exists(s.path))
            .Select(s => s.path)
            .ToArray();

        var skipped = EditorBuildSettings.scenes.Where(s => s.enabled && !File.Exists(s.path)).Select(s => s.path);
        foreach (var s in skipped) Debug.LogWarning($"[AutoTestBuild] Escena en Build Settings que no existe, se ignora: {s}");

        Debug.Log($"[AutoTestBuild] Escenas: {string.Join(", ", scenes)}");
        Debug.Log($"[AutoTestBuild] Salida: {outPath}");

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outPath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });

        var summary = report.summary;
        Debug.Log($"[AutoTestBuild] Resultado: {summary.result} · errores: {summary.totalErrors} · tiempo: {summary.totalTime}");

        if (Application.isBatchMode)
            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
    }

    private static string GetArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
