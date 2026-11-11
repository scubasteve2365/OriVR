using System.Collections;
using System.Text;
using Core;
using Game;
using UnityEngine;

namespace OriVR
{
    // Lab only, started from the command file: move to any scene and start a cross fade, so effects can be tested.
    internal class LabTools : MonoBehaviour
    {
        internal void ListScenes(string word)
        {
            if (Scenes.Manager == null) return;
            var sb = new StringBuilder("SCENES:");
            foreach (var scene in Scenes.Manager.AllScenes)
            {
                if (scene.Scene.ToLowerInvariant().Contains(word.ToLowerInvariant())) sb.Append(' ').Append(scene.Scene);
            }
            Plugin.Log.LogInfo(sb.ToString());
        }

        internal void GoTo(string sceneName)
        {
            StartCoroutine(GoToRoutine(sceneName));
        }

        // The same steps as the game's own debug menu (DebugMenuB.GoToScene).
        IEnumerator GoToRoutine(string sceneName)
        {
            var info = Scenes.Manager.GetSceneInformation(sceneName);
            if (info == null)
            {
                Plugin.Log.LogWarning("goto: no scene " + sceneName);
                yield break;
            }
            Scenes.Manager.AutoLoadingUnloading = false;
            Scenes.Manager.UnloadAllScenes();
            Scenes.Manager.DestroyManager.DestroyAll();
            SuspensionManager.SuspendAll();
            while (Scenes.Manager.ResourcesNeedUnloading) yield return new WaitForFixedUpdate();
            SuspensionManager.ResumeAll();
            GameController.Instance.ResetStateForDebugMenuGoToScene();
            GoToSceneController.Instance.GoToScene(info, null, false);
            Plugin.Log.LogInfo("GOTO " + sceneName);
        }

        // Lab: from the logos to a loaded save without keyboard or pad, for test runs on a PC without a headset.
        internal void LoadSlot(int slot)
        {
            StartCoroutine(LoadSlotRoutine(slot));
        }

        IEnumerator LoadSlotRoutine(int slot)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            for (int tries = 0; tries < 120 && (GameStateMachine.Instance.CurrentState == GameStateMachine.State.Logos
                                                || GameStateMachine.Instance.CurrentState == GameStateMachine.State.StartScreen); tries++)
            {
                foreach (var skip in Object.FindObjectsOfType<IntroLogosSkip>())
                {
                    if (skip.TimelineRunning != null && skip.TimelineRunning.IsRunning)
                    {
                        typeof(IntroLogosSkip).GetMethod("SkipLogos", flags).Invoke(skip, null);
                        Plugin.Log.LogInfo("LOADSLOT logos skipped");
                    }
                }
                var press = Object.FindObjectOfType<TitleScreenPressStartLogic>();
                if (press != null && press.isActiveAndEnabled)
                {
                    press.OnStartPressedCallback();
                    Plugin.Log.LogInfo("LOADSLOT start pressed");
                }
                yield return new WaitForSeconds(0.5f);
            }
            // Main menu: open the save slots screen, as its first entry does.
            for (int tries = 0; tries < 20 && TitleScreenManager.CurrentScreen != TitleScreenManager.Screen.MainMenu; tries++)
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(1f);
            TitleScreenManager.SetScreen(TitleScreenManager.Screen.SaveSlots);
            Plugin.Log.LogInfo("LOADSLOT save slots opened from " + GameStateMachine.Instance.CurrentState);
            yield return new WaitForSeconds(1.5f);
            for (int tries = 0; tries < 60; tries++)
            {
                var ui = SaveSlotsUI.Instance;
                if (ui != null && ui.isActiveAndEnabled && ui.CurrentSaveSlot != null)
                {
                    ui.SetCurrentItem(slot);
                    yield return new WaitForSeconds(0.5f);
                    ui.UsedSaveSlotSelected();
                    Plugin.Log.LogInfo("LOADSLOT slot " + slot + " selected");
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }
            Plugin.Log.LogWarning("LOADSLOT no save slot screen, state " + GameStateMachine.Instance.CurrentState);
        }

        internal void CrossFade(float seconds)
        {
            var character = Characters.Current;
            var root = character != null ? Scenes.Manager.FindLoadedSceneRootFromPosition(character.Position) : null;
            if (root == null || root.MetaData == null)
            {
                Plugin.Log.LogWarning("crossfade: no scene at Ori's position");
                return;
            }
            UI.Cameras.System.CrossFadeManager.PerformCrossFade(root.MetaData, seconds);
            Plugin.Log.LogInfo("CROSSFADE to " + root.MetaData.name + " over " + seconds + " s");
        }
    }
}
