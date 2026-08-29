using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Disables client-only presentation without touching networking, Physics2D, gameplay scripts,
/// colliders, rigidbodies, or transforms. Scene scans run only at load boundaries; network-prefab
/// instances are sanitized by PooledNetworkObjectProvider when they are created or reused.
/// </summary>
public static class DedicatedServerPresentation
{
    private static bool active;
    private static bool logged;
#if !UNITY_SERVER
    private static bool roleResolved;
    private static bool processIsHeadless;
#endif

    public static bool IsHeadless
    {
        get
        {
#if UNITY_SERVER
            return true;
#else
            if (active) return true;
            if (!Application.isPlaying) return false;

            if (!roleResolved)
            {
                processIsHeadless = NetworkBootMode.Resolve(
                    Application.isBatchMode,
                    System.Environment.GetCommandLineArgs()) == NetworkBootKind.DedicatedServer;
                roleResolved = true;
            }

            return processIsHeadless;
#endif
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeServerBuild()
    {
#if UNITY_SERVER
        Activate();
#else
        // Local Windows/macOS headless builds use the same boot contract as the Linux server.
        if (NetworkBootMode.Resolve(Application.isBatchMode, System.Environment.GetCommandLineArgs())
            == NetworkBootKind.DedicatedServer)
        {
            Activate();
        }
#endif
    }

    public static void Activate()
    {
        if (!active)
        {
            active = true;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        DisableLoadedScenes();

        if (!logged)
        {
            Debug.Log("[Server] Headless presentation disabled: render callbacks, cameras, audio, UI, and cosmetic animation are inactive.");
            logged = true;
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        DisableScene(scene);
    }

    public static void DisableLoadedScenes()
    {
        if (!active) return;

        for (int i = 0; i < SceneManager.sceneCount; i++)
            DisableScene(SceneManager.GetSceneAt(i));
    }

    public static void DisableHierarchy(GameObject root)
    {
        if (!active || root == null) return;

        Component[] components = root.GetComponentsInChildren<Component>(true);
        foreach (Component component in components)
        {
            if (component == null) continue;

            if (component is ParticleSystem particles)
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                continue;
            }

            if (component is Renderer renderer)
            {
                renderer.enabled = false;
                continue;
            }

            if (component is Camera camera)
            {
                camera.enabled = false;
                continue;
            }

            if (component is AudioSource source)
            {
                source.Stop();
                source.enabled = false;
                continue;
            }

            if (component is AudioListener listener)
            {
                listener.enabled = false;
                continue;
            }

            if (component is Animator animator)
            {
                animator.enabled = false;
                continue;
            }

            if (component is Light light)
            {
                light.enabled = false;
                continue;
            }

            if (component is Canvas canvas)
            {
                canvas.enabled = false;
                continue;
            }

            if (component is Graphic graphic)
            {
                graphic.enabled = false;
                continue;
            }

            if (component is EventSystem eventSystem)
            {
                eventSystem.enabled = false;
                continue;
            }

            if (component is BaseInputModule inputModule)
            {
                inputModule.enabled = false;
                continue;
            }

            if (component is MonoBehaviour behaviour && IsClientPresentationBehaviour(behaviour))
                behaviour.enabled = false;
        }
    }

    private static void DisableScene(Scene scene)
    {
        if (!active || !scene.IsValid() || !scene.isLoaded) return;

        foreach (GameObject root in scene.GetRootGameObjects())
            DisableHierarchy(root);
    }

    private static bool IsClientPresentationBehaviour(MonoBehaviour behaviour)
    {
        return behaviour is AudioManager
            || behaviour is MainMenuUI
            || behaviour is LobbyScreenUI
            || behaviour is SettingsPanel
            || behaviour is VideoSettingsSection
            || behaviour is PlayerHud
            || behaviour is HudToastFeed
            || behaviour is MatchPhaseHud
            || behaviour is ScoreboardInputReader
            || behaviour is ScoreboardPanel
            || behaviour is ScoreboardRowView
            || behaviour is TeamScoreDisplay
            || behaviour is HealthSegmentDisplay
            || behaviour is BuffIconDisplay
            || behaviour is CoinDisplay
            || behaviour is FlagDirectionHud
            || behaviour is FlagCarrierMarker
            || behaviour is CoinCarrierAura
            || behaviour is HitFeedback
            || behaviour is HitFlash
            || behaviour is DamageNumber
            || behaviour is CosmeticTracer
            || behaviour is MenuCamera
            || behaviour is PlayerCamera
            || behaviour is PlayerCameraFeelHandler
            || behaviour is PlayerCameraRespawnHandler
            || behaviour is PlayerCameraShakeHandler
            || behaviour is StarfieldGenerator
            || behaviour is ConstellationPulse;
    }
}
