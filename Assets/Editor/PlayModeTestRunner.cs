// ============================================================
// PLAY MODE TEST BOOTSTRAP — UI Toolkit verification
// ============================================================
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Unity.AI.Assistant.PlayModeTest
{
    [InitializeOnLoad]
    internal static class PlayModeTestRunner
    {
        private const string StateKey = "PlayModeTest.State";
        private const string ResultKey = "PlayModeTest.Result";
        private const string ScriptPathKey = "PlayModeTest.ScriptPath";
        private const string SentinelLog = "PLAY_MODE_TEST_COMPLETE";

        private static readonly int WaitFrames = SessionState.GetInt("PlayModeTest.WaitFrames", 5);

        private static List<string> _capturedLogs = new List<string>();
        private const int MaxCapturedLogs = 50;

        static PlayModeTestRunner()
        {
            string state = SessionState.GetString(StateKey, "Idle");

            switch (state)
            {
                case "Idle":
                    break;

                case "WaitingForCompile":
                    Debug.Log("[PlayModeTest] Bootstrap compiled. Scheduling Play Mode entry.");
                    EditorApplication.delayCall += () =>
                    {
                        SessionState.SetString(StateKey, "EnteringPlayMode");
                        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                        EditorApplication.isPlaying = true;
                    };
                    break;

                case "EnteringPlayMode":
                    EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                        SessionState.SetString(StateKey, "InPlayMode");
                        EditorApplication.update += WaitFramesThenRun;
                    }
                    break;

                case "InPlayMode":
                    if (EditorApplication.isPlaying)
                        EditorApplication.update += WaitFramesThenRun;
                    break;

                case "Done":
                    Debug.Log(SentinelLog);
                    EditorApplication.delayCall += SelfDestruct;
                    break;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                SessionState.SetString(StateKey, "InPlayMode");
                EditorApplication.update += WaitFramesThenRun;
            }
        }

        private static int _frameCount = 0;
        private static bool _hasRun = false;

        private static void WaitFramesThenRun()
        {
            _frameCount++;
            if (_frameCount < WaitFrames) return;
            if (_hasRun) return;
            _hasRun = true;
            EditorApplication.update -= WaitFramesThenRun;

            Application.logMessageReceived += OnLogMessage;
            string resultJson;
            try
            {
                resultJson = RunTestLogic();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[PlayModeTest] Test threw exception: " + e);
                resultJson = JsonUtility.ToJson(new TestResult
                {
                    success = false,
                    error = e.Message,
                    logs = _capturedLogs.ToArray()
                });
            }
            finally
            {
                Application.logMessageReceived -= OnLogMessage;
            }

            SessionState.SetString(ResultKey, resultJson);
            SessionState.SetString(StateKey, "Done");
            EditorApplication.isPlaying = false;
        }

        private static void SelfDestruct()
        {
            string scriptPath = SessionState.GetString(ScriptPathKey, "");
            if (!string.IsNullOrEmpty(scriptPath) && AssetDatabase.AssetPathExists(scriptPath))
                AssetDatabase.DeleteAsset(scriptPath);
            SessionState.EraseString(StateKey);
            SessionState.EraseString(ScriptPathKey);
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (_capturedLogs.Count >= MaxCapturedLogs) return;
            if (type == LogType.Error || type == LogType.Exception ||
                message.Contains("[Test]") || message.Contains("TEST_RESULT"))
            {
                _capturedLogs.Add("[" + type + "] " + message);
            }
        }

        [System.Serializable]
        private class TestResult
        {
            public bool success;
            public string error;
            public string[] logs;
        }

        private static string RunTestLogic()
        {
            var go = GameObject.Find("GameUIManager");
            if (go == null)
                return JsonUtility.ToJson(new TestResult { success = false, error = "GameUIManager GameObject not found" });

            var doc = go.GetComponent<UIDocument>();
            if (doc == null)
                return JsonUtility.ToJson(new TestResult { success = false, error = "UIDocument component missing" });

            var root = doc.rootVisualElement;
            if (root == null)
                return JsonUtility.ToJson(new TestResult { success = false, error = "rootVisualElement is null (UXML/PanelSettings not assigned?)" });

            // Verify key named elements resolve
            string[] names = {
                "fame-text","phase-text","turn-text","credits-text",
                "left-panel","btn-move-ship","btn-recover","btn-gain-credits",
                "move-group","move-node-input","btn-move-confirm",
                "btn-buy-bounty","btn-buy-cargo","btn-buy-gear","btn-buy-job","btn-buy-luxury","btn-buy-ship",
                "btn-deliver","btn-bounty","trade-group","trade-target-input","trade-amount-input","btn-trade-send",
                "btn-end-action","right-panel","hd-text","hull-text","combat-text","cargo-text","crew-text",
                "hutt-rep-text","synd-rep-text","imp-rep-text","rebel-rep-text"
            };

            var missing = new List<string>();
            foreach (var n in names)
                if (root.Q(n) == null) missing.Add(n);

            int found = names.Length - missing.Count;
            Debug.Log("TEST_RESULT: elements_found=" + found + "/" + names.Length);

            // Check offline-state text was populated by Refresh()
            var fame = root.Q<Label>("fame-text");
            var phase = root.Q<Label>("phase-text");
            Debug.Log("TEST_RESULT: fame_text=" + (fame != null ? fame.text : "null"));
            Debug.Log("TEST_RESULT: phase_text=" + (phase != null ? phase.text : "null"));

            if (missing.Count > 0)
            {
                Debug.LogError("[PlayModeTest] Missing elements: " + string.Join(",", missing));
                return JsonUtility.ToJson(new TestResult
                {
                    success = false,
                    error = "Missing UI elements: " + string.Join(",", missing),
                    logs = _capturedLogs.ToArray()
                });
            }

            return JsonUtility.ToJson(new TestResult
            {
                success = true,
                logs = _capturedLogs.ToArray()
            });
        }
    }
}
