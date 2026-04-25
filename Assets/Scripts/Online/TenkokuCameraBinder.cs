using Tenkoku.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace RunnerGame.Online
{
    [ExecuteAlways]
    public class TenkokuCameraBinder : MonoBehaviour
    {
        [SerializeField] private TenkokuModule tenkokuModule;
        [SerializeField] private TenkokuLib tenkokuLib;
        [SerializeField] private Transform[] cameraTargets = System.Array.Empty<Transform>();
        [SerializeField] private Material skyboxMaterial;
        [SerializeField] private float minimumSkyScale = 900f;
        [SerializeField] private float farClipScale = 0.95f;
        [SerializeField] private float skyboxCloudIntensity = 0.82f;
        [SerializeField] private float skyboxCloudCoverage = 0.42f;
        [SerializeField] private float skyboxCloudScale = 3.2f;
        [SerializeField] private float skyboxCloudSpeed = 0.018f;
        [SerializeField] private float starfieldBaseSize = 0.02f;
        [SerializeField] private float starfieldConstellationSize = 0.025f;
        [SerializeField] private float starfieldMaxSize = 0.05f;

        private Transform currentTarget;
        private SkyRenderState savedState;
        private bool hasSavedState;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
            Camera.onPreCull += HandleCameraPreCull;
            Camera.onPostRender += HandleCameraPostRender;
            BindCamera();
            ConfigureSky();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
            Camera.onPreCull -= HandleCameraPreCull;
            Camera.onPostRender -= HandleCameraPostRender;
            RestoreSkyState();
        }

        private void LateUpdate()
        {
            BindCamera();
            ConfigureSky();
        }

        private void OnValidate()
        {
            BindCamera();
            ConfigureSky();
        }

        private void BindCamera()
        {
            ResolveReferences();
            if (tenkokuModule == null)
            {
                return;
            }

            Transform target = ResolveCameraTarget();
            if (target == null)
            {
                return;
            }

            tenkokuModule.cameraTypeIndex = 1;
            tenkokuModule.manualCamera = target;

            if (currentTarget != target)
            {
                tenkokuModule.mainCamera = target;
                currentTarget = target;
            }
        }

        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                return;
            }

            ApplySkyState(camera);
        }

        private void HandleEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                return;
            }

            RestoreSkyState();
        }

        private void HandleCameraPreCull(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                return;
            }

            ApplySkyState(camera);
        }

        private void HandleCameraPostRender(Camera camera)
        {
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                return;
            }

            RestoreSkyState();
        }

        private Transform ResolveCameraTarget()
        {
            Transform fallback = null;

            foreach (Transform candidate in cameraTargets)
            {
                if (candidate == null)
                {
                    continue;
                }

                if (fallback == null)
                {
                    fallback = candidate;
                }

                Camera camera = candidate.GetComponent<Camera>();
                if (candidate.gameObject.activeInHierarchy && camera != null && camera.enabled)
                {
                    return candidate;
                }
            }

            return fallback;
        }

        private void ResolveReferences()
        {
            if (tenkokuModule == null)
            {
                tenkokuModule = GetComponent<TenkokuModule>();
            }

            if (tenkokuLib == null)
            {
                tenkokuLib = GetComponentInChildren<TenkokuLib>(true);
            }

            if (skyboxMaterial == null && tenkokuLib != null)
            {
                skyboxMaterial = tenkokuLib.skyMaterialElek;
            }

            if (skyboxMaterial == null && RenderSettings.skybox != null &&
                RenderSettings.skybox.shader != null &&
                RenderSettings.skybox.shader.name == "TENKOKU/Tenkoku_Sky_Elek")
            {
                skyboxMaterial = RenderSettings.skybox;
            }
        }

        private void ConfigureSky()
        {
            ResolveReferences();
            ConfigureTenkokuModule();
            DisableLegacySkyArtifacts();
            ConfigureSkyboxMaterial();
            ConfigureCloudRenderers();
        }

        private void ConfigureTenkokuModule()
        {
            if (tenkokuModule == null)
            {
                return;
            }

            tenkokuModule.useAutoFX = false;
            tenkokuModule.enableFog = false;
            tenkokuModule.autoFog = false;
            tenkokuModule.enableIBL = false;
            tenkokuModule.enableProbe = false;
            tenkokuModule.enableSoundFX = false;
            tenkokuModule.useTemporalAliasing = false;
            tenkokuModule.useSunFlare = false;
            tenkokuModule.flareObject = null;
            tenkokuModule.useSunRays = false;
            tenkokuModule.sunRayIntensity = 0f;
            tenkokuModule.sunRayLength = 0f;
            tenkokuModule.atmosphereModelTypeIndex = 1;
            tenkokuModule.useAtmosphereIndex = -1;
            tenkokuModule.cameraTypeIndex = 1;
            tenkokuModule.useLegacyClouds = false;
            tenkokuModule.weatherTypeIndex = 0;
            tenkokuModule.autoTimeSync = false;
            tenkokuModule.autoDateSync = false;
            tenkokuModule.autoTime = false;
            tenkokuModule.currentHour = 10;
            tenkokuModule.currentMinute = 30;
            tenkokuModule.systemTime = 37800f;
            tenkokuModule.starTypeIndex = 2;
            tenkokuModule.galaxyTypeIndex = 2;
            tenkokuModule.starIntensity = 0f;
            tenkokuModule.galaxyIntensity = 0f;
            tenkokuModule.planetIntensity = 0f;
            tenkokuModule.weather_cloudAltoStratusAmt = Mathf.Max(tenkokuModule.weather_cloudAltoStratusAmt, 0.3f);
            tenkokuModule.weather_cloudCirrusAmt = Mathf.Max(tenkokuModule.weather_cloudCirrusAmt, 0.55f);
            tenkokuModule.weather_cloudCumulusAmt = Mathf.Max(tenkokuModule.weather_cloudCumulusAmt, 0.55f);
            tenkokuModule.weather_cloudScale = Mathf.Max(tenkokuModule.weather_cloudScale, 4f);
            tenkokuModule.weather_cloudSpeed = Mathf.Max(tenkokuModule.weather_cloudSpeed, 0.28f);
            tenkokuModule.weather_WindAmt = Mathf.Max(tenkokuModule.weather_WindAmt, 0.3f);
            tenkokuModule.cloudQuality = Mathf.Max(tenkokuModule.cloudQuality, 0.65f);
            tenkokuModule.cloudBrightness = Mathf.Max(tenkokuModule.cloudBrightness, 1.15f);
        }

        private void DisableLegacySkyArtifacts()
        {
            if (tenkokuLib != null && tenkokuLib.lightObjectWorld != null)
            {
                tenkokuLib.lightObjectWorld.flare = null;
            }

            if (tenkokuLib != null)
            {
                DisableRenderer(tenkokuLib.starParticleSystem);
                DisableRenderer(tenkokuLib.renderObjectGalaxy);
                DisablePlanet(tenkokuLib.planetObjSaturn, tenkokuLib.planetRendererSaturn);
                DisablePlanet(tenkokuLib.planetObjJupiter, tenkokuLib.planetRendererJupiter);
                DisablePlanet(tenkokuLib.planetObjNeptune, tenkokuLib.planetRendererNeptune);
                DisablePlanet(tenkokuLib.planetObjUranus, tenkokuLib.planetRendererUranus);
                DisablePlanet(tenkokuLib.planetObjMercury, tenkokuLib.planetRendererMercury);
                DisablePlanet(tenkokuLib.planetObjVenus, tenkokuLib.planetRendererVenus);
                DisablePlanet(tenkokuLib.planetObjMars, tenkokuLib.planetRendererMars);
            }

            foreach (Tenkoku.Effects.TenkokuSunShafts sunShafts in FindObjectsByType<Tenkoku.Effects.TenkokuSunShafts>(FindObjectsInactive.Include))
            {
                DisableSunShafts(sunShafts);
            }
        }

        private static void DisableSunShafts(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            DisableSunShafts(camera.GetComponent<Tenkoku.Effects.TenkokuSunShafts>());
        }

        private static void DisableSunShafts(Tenkoku.Effects.TenkokuSunShafts sunShafts)
        {
            if (sunShafts != null)
            {
                sunShafts.enabled = false;
            }
        }

        private static void DisablePlanet(ParticlePlanetHandler planet, Renderer renderer)
        {
            if (planet != null)
            {
                planet.planetVis = 0f;
                planet.planetSize = 0f;
            }

            DisableRenderer(renderer);
        }

        private static void DisableRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.enabled = false;
            renderer.forceRenderingOff = true;
        }

        private void ConfigureSkyboxMaterial()
        {
            if (skyboxMaterial == null)
            {
                return;
            }

            if (RenderSettings.skybox != skyboxMaterial)
            {
                RenderSettings.skybox = skyboxMaterial;
            }

            Material cloudPlaneMaterial = tenkokuLib != null && tenkokuLib.renderObjectCloudPlane != null
                ? tenkokuLib.renderObjectCloudPlane.sharedMaterial
                : null;
            Material cloudSphereMaterial = tenkokuLib != null && tenkokuLib.renderObjectCloudSphere != null
                ? tenkokuLib.renderObjectCloudSphere.sharedMaterial
                : null;

            CopyTextureIfAvailable(skyboxMaterial, "_CloudTex", cloudSphereMaterial, "_CloudTex1");
            CopyTextureIfAvailable(skyboxMaterial, "_CloudTex", cloudPlaneMaterial, "_MainTex");
            CopyTextureIfAvailable(skyboxMaterial, "_CloudTexB", cloudPlaneMaterial, "_CloudTexB");

            skyboxMaterial.SetFloat("_CloudIntensity", Mathf.Clamp01(skyboxCloudIntensity));
            skyboxMaterial.SetFloat("_CloudCoverage", Mathf.Clamp01(skyboxCloudCoverage));
            skyboxMaterial.SetFloat("_CloudScale", Mathf.Max(0.1f, skyboxCloudScale));
            skyboxMaterial.SetFloat("_CloudSpeed", Mathf.Max(0f, skyboxCloudSpeed));
            Shader.SetGlobalFloat("_tenkokuTimer", Time.realtimeSinceStartup);
        }

        private static void CopyTextureIfAvailable(Material target, string targetProperty, Material source, string sourceProperty)
        {
            if (target == null || source == null || !target.HasProperty(targetProperty) || !source.HasProperty(sourceProperty))
            {
                return;
            }

            Texture texture = source.GetTexture(sourceProperty);
            if (texture != null)
            {
                target.SetTexture(targetProperty, texture);
            }
        }

        private void ConfigureCloudRenderers()
        {
            if (tenkokuLib == null)
            {
                return;
            }

            if (tenkokuLib.renderObjectCloudSphere != null)
            {
                tenkokuLib.renderObjectCloudSphere.enabled = true;
                tenkokuLib.renderObjectCloudSphere.shadowCastingMode = ShadowCastingMode.Off;
                tenkokuLib.renderObjectCloudSphere.receiveShadows = false;
            }

            if (tenkokuLib.renderObjectCloudPlane != null)
            {
                tenkokuLib.renderObjectCloudPlane.shadowCastingMode = ShadowCastingMode.Off;
                tenkokuLib.renderObjectCloudPlane.receiveShadows = false;
            }
        }

        private void ApplySkyState(Camera renderCamera)
        {
            if (renderCamera == null || renderCamera.cameraType == CameraType.Preview)
            {
                return;
            }

            DisableSunShafts(renderCamera);
            ResolveReferences();
            if (tenkokuLib == null || tenkokuLib.skyObject == null)
            {
                return;
            }

            DisableLegacySkyArtifacts();
            SaveSkyState();

            float farClip = Mathf.Max(renderCamera.farClipPlane, minimumSkyScale);
            float skyScale = Mathf.Max(minimumSkyScale, farClip * farClipScale);
            Vector3 celestialScale = Vector3.one * Mathf.Max(1f, (farClip / 20f) * 1.9f);
            Vector3 innerCelestialScale = celestialScale * 0.76f;

            tenkokuLib.skyObject.position = renderCamera.transform.position;
            tenkokuLib.skyObject.localScale = Vector3.one * skyScale;

            if (tenkokuLib.sunSphereObject != null)
            {
                tenkokuLib.sunSphereObject.localScale = innerCelestialScale;
            }

            if (tenkokuLib.moonSphereObject != null)
            {
                tenkokuLib.moonSphereObject.localScale = innerCelestialScale;
            }

            if (tenkokuLib.starfieldObject != null)
            {
                tenkokuLib.starfieldObject.localScale = celestialScale;
            }

            if (tenkokuLib.starRenderSystem != null)
            {
                tenkokuLib.starRenderSystem.starDistance = farClip;
                tenkokuLib.starRenderSystem.baseSize = Mathf.Max(0f, starfieldBaseSize);
                tenkokuLib.starRenderSystem.constellationSize = Mathf.Max(0f, starfieldConstellationSize);
                tenkokuLib.starRenderSystem.setSize = Mathf.Min(
                    Mathf.Max(0f, starfieldMaxSize),
                    tenkokuLib.starRenderSystem.baseSize * (farClip / 800f));
            }

            Shader.SetGlobalVector("_TenkokuCameraPos", renderCamera.transform.position);
            Shader.SetGlobalMatrix("_Tenkoku_CameraMV", renderCamera.worldToCameraMatrix.inverse);
            Shader.SetGlobalFloat("_tenkokuTimer", Time.realtimeSinceStartup);
        }

        private void SaveSkyState()
        {
            if (hasSavedState)
            {
                return;
            }

            savedState = new SkyRenderState
            {
                SkyPosition = tenkokuLib.skyObject.position,
                SkyLocalScale = tenkokuLib.skyObject.localScale,
                SunLocalScale = tenkokuLib.sunSphereObject != null ? tenkokuLib.sunSphereObject.localScale : Vector3.zero,
                MoonLocalScale = tenkokuLib.moonSphereObject != null ? tenkokuLib.moonSphereObject.localScale : Vector3.zero,
                StarfieldLocalScale = tenkokuLib.starfieldObject != null ? tenkokuLib.starfieldObject.localScale : Vector3.zero,
                StarDistance = tenkokuLib.starRenderSystem != null ? tenkokuLib.starRenderSystem.starDistance : 0f,
                StarSize = tenkokuLib.starRenderSystem != null ? tenkokuLib.starRenderSystem.setSize : 0f
            };
            hasSavedState = true;
        }

        private void RestoreSkyState()
        {
            if (!hasSavedState || tenkokuLib == null || tenkokuLib.skyObject == null)
            {
                hasSavedState = false;
                return;
            }

            tenkokuLib.skyObject.position = savedState.SkyPosition;
            tenkokuLib.skyObject.localScale = savedState.SkyLocalScale;

            if (tenkokuLib.sunSphereObject != null)
            {
                tenkokuLib.sunSphereObject.localScale = savedState.SunLocalScale;
            }

            if (tenkokuLib.moonSphereObject != null)
            {
                tenkokuLib.moonSphereObject.localScale = savedState.MoonLocalScale;
            }

            if (tenkokuLib.starfieldObject != null)
            {
                tenkokuLib.starfieldObject.localScale = savedState.StarfieldLocalScale;
            }

            if (tenkokuLib.starRenderSystem != null)
            {
                tenkokuLib.starRenderSystem.starDistance = savedState.StarDistance;
                tenkokuLib.starRenderSystem.setSize = savedState.StarSize;
            }

            hasSavedState = false;
        }

        private struct SkyRenderState
        {
            public Vector3 SkyPosition;
            public Vector3 SkyLocalScale;
            public Vector3 SunLocalScale;
            public Vector3 MoonLocalScale;
            public Vector3 StarfieldLocalScale;
            public float StarDistance;
            public float StarSize;
        }
    }
}
