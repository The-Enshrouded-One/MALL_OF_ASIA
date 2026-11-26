using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.EventSystems;

public static class XRProjectSetup
{
    const float MallWorldScale = 100f;
    const string MainScenePath = "Assets/Scenes/SampleScene.unity";
    const string RigPrefabPath = "Assets/XR/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string XrDemoScenePath = "Assets/XR/Starter Assets/DemoScene.unity";
    const string XrSettingsFolder = "Assets/XR/Settings";

    public static void Configure()
    {
        ConfigureOpenXR();
        ConfigureNavigationScene();
    }

    static void ConfigureOpenXR()
    {
        EnsureFolder("Assets/XR", "Settings");

        const string settingsPath = XrSettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset";
        XRGeneralSettingsPerBuildTarget perBuildTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(settingsPath);
        if (perBuildTarget == null)
        {
            perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perBuildTarget, settingsPath);
        }

        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTarget, true);
        if (!perBuildTarget.HasSettingsForBuildTarget(BuildTargetGroup.Standalone))
            perBuildTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (!perBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
            perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);

        XRGeneralSettings generalSettings = perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
        generalSettings.InitManagerOnStart = true;

        XRManagerSettings manager = perBuildTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
        manager.automaticLoading = true;
        manager.automaticRunning = true;
        if (!manager.activeLoaders.Any(loader => loader is OpenXRLoader))
        {
            if (!XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Standalone))
                throw new InvalidOperationException("Unity could not assign the OpenXR loader to Windows Standalone.");
        }

        // OpenXR keeps provider settings in an editor-only package asset. Create it through
        // the package's setup API, then enable the controller profile used by common headsets.
        Type packageSettingsType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("UnityEditor.XR.OpenXR.OpenXRPackageSettings"))
            .FirstOrDefault(type => type != null);
        MethodInfo createSettings = packageSettingsType?.GetMethod("GetOrCreateInstance", BindingFlags.Public | BindingFlags.Static);
        createSettings?.Invoke(null, null);

        FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
        OpenXRSettings openXRSettings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
        if (openXRSettings != null)
        {
            EnableControllerProfile<OculusTouchControllerProfile>(openXRSettings);
            EnableControllerProfile<MetaQuestTouchPlusControllerProfile>(openXRSettings);
            EnableControllerProfile<MetaQuestTouchProControllerProfile>(openXRSettings);
            EnableControllerProfile<ValveIndexControllerProfile>(openXRSettings);
            EnableControllerProfile<HTCViveControllerProfile>(openXRSettings);
            EnableControllerProfile<MicrosoftMotionControllerProfile>(openXRSettings);
            EnableControllerProfile<KHRSimpleControllerProfile>(openXRSettings);
            EditorUtility.SetDirty(openXRSettings);
        }

        EditorUtility.SetDirty(perBuildTarget);
        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(manager);
        AssetDatabase.SaveAssets();
    }

    static void ConfigureNavigationScene()
    {
        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        GameObject oldSeeker = GameObject.Find("Seeker");
        GameObject searchController = GameObject.Find("SearchController");
        GameObject rig = GameObject.Find("XR Origin (Mall Explorer)");
        bool sceneWasScaled = EnsureMallWorldScale();
        Unit oldUnit = oldSeeker != null ? oldSeeker.GetComponent<Unit>() : null;
        SearchStoreUI searchUI = searchController != null ? searchController.GetComponent<SearchStoreUI>() : null;
        if (searchUI == null)
            throw new InvalidOperationException("The startup scene is missing its search controller.");
        if (rig == null && (oldSeeker == null || oldUnit == null))
            throw new InvalidOperationException("The startup scene needs either its original seeker or the configured XR Origin.");

        Transform uiAnchor = null;
        if (rig == null)
        {
            uiAnchor = oldSeeker.transform.Find("UIAnchor");
            Vector3 startPosition = oldSeeker.transform.position;
            Quaternion startRotation = oldSeeker.transform.rotation;

            GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            if (rigPrefab == null)
                throw new System.IO.FileNotFoundException("The imported XR Origin prefab is missing.", RigPrefabPath);

            rig = PrefabUtility.InstantiatePrefab(rigPrefab, scene) as GameObject;
            rig.name = "XR Origin (Mall Explorer)";
            rig.transform.SetPositionAndRotation(ScaleRigStartPosition(startPosition), startRotation);
            rig.transform.localScale = Vector3.one;

            Unit unit = rig.GetComponent<Unit>();
            if (unit == null)
                unit = rig.AddComponent<Unit>();
            unit.speed = oldUnit.speed;
            unit.rotationSpeed = oldUnit.rotationSpeed;
            unit.currentFloor = oldUnit.currentFloor;
            unit.floorHeight = oldUnit.floorHeight;
            unit.liftXZ = oldUnit.liftXZ;

            PlayerMovement desktopControls = rig.GetComponent<PlayerMovement>();
            if (desktopControls == null)
                desktopControls = rig.AddComponent<PlayerMovement>();
            desktopControls.moveSpeed = 1.4f;
            desktopControls.runMultiplier = 2f;
            desktopControls.mouseSensitivity = 2f;

            FloorTag floorTag = rig.GetComponent<FloorTag>();
            if (floorTag == null)
                floorTag = rig.AddComponent<FloorTag>();
            floorTag.floorId = unit.currentFloor;

            XROrigin origin = rig.GetComponent<XROrigin>();
            Camera rigCamera = rig.GetComponentInChildren<Camera>(true);
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            if (origin == null || rigCamera == null || cameraOffset == null)
                throw new InvalidOperationException("The XR Origin prefab is missing its camera or floor offset.");
            origin.Camera = rigCamera;
            origin.Origin = rig;
            origin.CameraFloorOffsetObject = cameraOffset.gameObject;

            rigCamera.nearClipPlane = 0.03f;
            rigCamera.fieldOfView = 60f;
            rigCamera.stereoTargetEye = StereoTargetEyeMask.Both;

            if (uiAnchor != null)
            {
                uiAnchor.SetParent(rigCamera.transform, false);
                uiAnchor.localPosition = new Vector3(0f, 0f, 0.75f);
                uiAnchor.localRotation = Quaternion.identity;
                uiAnchor.localScale = Vector3.one;
            }

            searchUI.unit = unit;
            EditorUtility.SetDirty(searchUI);
            UnityEngine.Object.DestroyImmediate(oldSeeker);
        }
        else
        {
            if (sceneWasScaled || rig.transform.localScale.x < 0.1f)
                rig.transform.position = ScaleRigStartPosition(rig.transform.position);
            rig.transform.localScale = Vector3.one;

            Unit unit = rig.GetComponent<Unit>();
            if (unit == null)
            {
                unit = rig.AddComponent<Unit>();
                if (oldUnit != null)
                {
                    unit.speed = oldUnit.speed;
                    unit.rotationSpeed = oldUnit.rotationSpeed;
                    unit.currentFloor = oldUnit.currentFloor;
                    unit.floorHeight = oldUnit.floorHeight;
                    unit.liftXZ = oldUnit.liftXZ;
                }
            }

            searchUI.unit = unit;
            EditorUtility.SetDirty(searchUI);

            if (oldSeeker != null)
            {
                Transform existingUiAnchor = oldSeeker.transform.Find("UIAnchor");
                Camera existingRigCamera = rig.GetComponentInChildren<Camera>(true);
                if (existingUiAnchor != null && existingRigCamera != null)
                {
                    existingUiAnchor.SetParent(existingRigCamera.transform, false);
                    existingUiAnchor.localPosition = new Vector3(0f, 0f, 0.75f);
                    existingUiAnchor.localRotation = Quaternion.identity;
                    existingUiAnchor.localScale = Vector3.one;
                }

                UnityEngine.Object.DestroyImmediate(oldSeeker);
            }
        }

        Camera camera = rig.GetComponentInChildren<Camera>(true);
        if (camera != null)
        {
            camera.nearClipPlane = 0.03f;
            camera.fieldOfView = 60f;
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera);

            XROrigin origin = rig.GetComponent<XROrigin>();
            Transform cameraOffset = rig.transform.Find("Camera Offset");
            if (origin != null && cameraOffset != null)
            {
                origin.Camera = camera;
                origin.Origin = rig;
                origin.CameraFloorOffsetObject = cameraOffset.gameObject;
                PrefabUtility.RecordPrefabInstancePropertyModifications(origin);
            }

            Transform cameraUiAnchor = rig.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => transform.name == "UIAnchor");
            if (cameraUiAnchor != null)
            {
                cameraUiAnchor.SetParent(camera.transform, false);
                cameraUiAnchor.localPosition = new Vector3(0f, 0f, 0.75f);
                cameraUiAnchor.localRotation = Quaternion.identity;
                cameraUiAnchor.localScale = Vector3.one;
                EditorUtility.SetDirty(cameraUiAnchor);
            }

            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (canvas.renderMode == RenderMode.WorldSpace)
                {
                    if (cameraUiAnchor != null && canvas.transform.IsChildOf(cameraUiAnchor))
                        ConfigureWorldCanvas(canvas);
                    canvas.worldCamera = camera;
                    EditorUtility.SetDirty(canvas);
                }
            }
        }

        CharacterController characterController = rig.GetComponent<CharacterController>();
        if (characterController != null)
        {
            characterController.stepOffset = 0.3f;
            characterController.skinWidth = 0.01f;
            EditorUtility.SetDirty(characterController);
            PrefabUtility.RecordPrefabInstancePropertyModifications(characterController);
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(rig.transform);

        ConfigureXRInteractionObjects(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    static void ConfigureXRInteractionObjects(Scene scene)
    {
        EventSystem[] existingEventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (!existingEventSystems.Any(eventSystem => eventSystem.GetComponent<XRUIInputModule>() != null))
        {
            Scene demoScene = EditorSceneManager.OpenScene(XrDemoScenePath, OpenSceneMode.Additive);
            GameObject sampleEventSystem = null;
            GameObject sampleInteractionManager = null;

            foreach (GameObject root in demoScene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<EventSystem>(true) != null)
                    sampleEventSystem = root;
                if (root.GetComponentInChildren<XRInteractionManager>(true) != null)
                    sampleInteractionManager = root;
            }

            if (sampleEventSystem == null || sampleInteractionManager == null)
                throw new InvalidOperationException("Unity's XRI sample is missing its UI module or interaction manager.");

            EventSystem[] oldEventSystems = existingEventSystems.Where(item => item.gameObject.scene == scene).ToArray();
            foreach (EventSystem oldEventSystem in oldEventSystems)
                UnityEngine.Object.DestroyImmediate(oldEventSystem.gameObject);

            SceneManager.MoveGameObjectToScene(sampleEventSystem, scene);
            SceneManager.MoveGameObjectToScene(sampleInteractionManager, scene);
            sampleEventSystem.name = "EventSystem";
            sampleInteractionManager.name = "XR Interaction Manager";
            sampleEventSystem.transform.SetParent(null, false);
            sampleInteractionManager.transform.SetParent(null, false);

            EditorSceneManager.CloseScene(demoScene, true);
        }

        GameObject rig = GameObject.Find("XR Origin (Mall Explorer)");
        if (rig == null)
            return;

        foreach (XRRayInteractor rayInteractor in rig.GetComponentsInChildren<XRRayInteractor>(true))
            rayInteractor.enableUIInteraction = true;
        foreach (NearFarInteractor nearFarInteractor in rig.GetComponentsInChildren<NearFarInteractor>(true))
            nearFarInteractor.enableUIInteraction = true;
    }

    static void EnableControllerProfile<T>(OpenXRSettings settings) where T : OpenXRInteractionFeature
    {
        T profile = settings.GetFeature<T>();
        if (profile != null)
            profile.enabled = true;
    }

    static bool EnsureMallWorldScale()
    {
        GameObject mapRootObject = GameObject.Find("Floor_A*");
        GameObject mallRootObject = GameObject.Find("Pheonix Mall of Asia");
        if (mapRootObject == null || mallRootObject == null)
            throw new InvalidOperationException("The mall map and model roots are required to configure the XR world scale.");

        Transform mapRoot = mapRootObject.transform;
        bool wasSmallWorld = mapRoot.localScale.x < 10f;
        ScaleRootIfNeeded(mapRoot);
        ScaleRootIfNeeded(mallRootObject.transform);

        foreach (GridFromTexture grid in UnityEngine.Object.FindObjectsByType<GridFromTexture>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (grid.gridWorldSize.x < 10f)
                grid.gridWorldSize *= MallWorldScale;
            if (grid.nodeRadius < 0.05f)
                grid.nodeRadius *= MallWorldScale;
            if (grid.mirrorOffsetX < 10f)
                grid.mirrorOffsetX *= MallWorldScale;
            // Use the imported floor plane's exact depth after applying the shared factor.
            if (grid.gridWorldSize.y > 10f && grid.gridWorldSize.y < 37f)
                grid.gridWorldSize.y = 36.644791f;
            EditorUtility.SetDirty(grid);
        }

        foreach (WalkableNodeGenerator generator in UnityEngine.Object.FindObjectsByType<WalkableNodeGenerator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (generator.worldWidth < 10f)
            {
                generator.worldWidth *= MallWorldScale;
                generator.worldHeight *= MallWorldScale;
            }
            EditorUtility.SetDirty(generator);
        }

        foreach (Unit unit in UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (unit.speed < 0.5f)
                unit.speed *= MallWorldScale;
            if (unit.floorHeight < 1f)
                unit.floorHeight *= MallWorldScale;
            EditorUtility.SetDirty(unit);
        }

        foreach (InterFloorController floors in UnityEngine.Object.FindObjectsByType<InterFloorController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (floors.floorHeight < 1f)
                floors.floorHeight *= MallWorldScale;
            EditorUtility.SetDirty(floors);
        }

        foreach (PlayerMovement controls in UnityEngine.Object.FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (controls.moveSpeed < 0.1f)
                controls.moveSpeed *= MallWorldScale;
            EditorUtility.SetDirty(controls);
        }

        EditorUtility.SetDirty(mapRootObject);
        EditorUtility.SetDirty(mallRootObject);
        return wasSmallWorld;
    }

    static void ScaleRootIfNeeded(Transform root)
    {
        if (root.localScale.x >= 10f)
            return;

        root.position *= MallWorldScale;
        root.localScale *= MallWorldScale;
        EditorUtility.SetDirty(root);
    }

    static Vector3 ScaleRigStartPosition(Vector3 position)
    {
        return new Vector3(position.x * MallWorldScale, 0f, position.z * MallWorldScale);
    }

    static void ConfigureWorldCanvas(Canvas canvas)
    {
        RectTransform rect = canvas.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition3D = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * 0.0015f;
        }

        if (canvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        EditorUtility.SetDirty(canvas);
    }

    static void EnsureFolder(string parent, string child)
    {
        string fullPath = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(parent))
            AssetDatabase.CreateFolder("Assets", parent.Substring("Assets/".Length));
        if (!AssetDatabase.IsValidFolder(fullPath))
            AssetDatabase.CreateFolder(parent, child);
    }
}
