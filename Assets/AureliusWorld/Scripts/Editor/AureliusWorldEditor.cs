using UnityEditor;
using UnityEngine;

namespace Aurelius.World.EditorTools
{
    /// <summary>AureliusTerrainGenerator: the main inspector for the world root.</summary>
    [CustomEditor(typeof(AureliusWorld))]
    public class AureliusWorldEditor : Editor
    {
        Editor settingsEditor;
        bool showSettings = true;
        int tileToRegenerate;

        public override void OnInspectorGUI()
        {
            var world = (AureliusWorld)target;
            EditorGUILayout.LabelField("Aurelius Terrain Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Builds the natural world around the reserved Aurelius city circle. Same seed = same terrain. " +
                                    "Rivers, paths and the lake are editable components under this object; move their handles, then press GENERATE TERRAIN.", MessageType.None);

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("settings"));
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("debugMode"), new GUIContent("Debug View"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("drawGizmos"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                world.ApplyDebugGlobals();
                SceneView.RepaintAll();
            }
            serializedObject.ApplyModifiedProperties();

            if (world.settings == null)
            {
                if (GUILayout.Button("Create Settings")) { world.settings = AureliusAssetFactory.GetOrCreateSettings(); EditorUtility.SetDirty(world); }
                return;
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
                if (GUILayout.Button("GENERATE ALL", GUILayout.Height(34))) AureliusWorldBuilder.GenerateAll(world);
                GUI.backgroundColor = Color.white;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("GENERATE TERRAIN", GUILayout.Height(26))) AureliusWorldBuilder.GenerateTerrain(world);
                    if (GUILayout.Button("GENERATE BIOMES", GUILayout.Height(26))) AureliusWorldBuilder.GenerateBiomes(world);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("GENERATE RIVERS", GUILayout.Height(26))) AureliusWorldBuilder.GenerateRivers(world);
                    if (GUILayout.Button("GENERATE PATHS", GUILayout.Height(26))) AureliusWorldBuilder.GeneratePaths(world);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("GENERATE LAKE", GUILayout.Height(26))) AureliusWorldBuilder.GenerateLake(world);
                    if (GUILayout.Button("GENERATE VEGETATION", GUILayout.Height(26))) AureliusWorldBuilder.GenerateVegetation(world);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    int n = world.settings.tilesPerSide;
                    var names = new string[n * n];
                    for (int i = 0; i < names.Length; i++) names[i] = AureliusWorldBuilder.TileName(world.settings, i % n, i / n);
                    tileToRegenerate = EditorGUILayout.Popup(Mathf.Clamp(tileToRegenerate, 0, names.Length - 1), names);
                    if (GUILayout.Button("Regenerate Tile", GUILayout.Width(120))) AureliusWorldBuilder.GenerateTerrain(world, tileToRegenerate);
                }
                if (GUILayout.Button("Detect City Landmarks (pads + city radius)"))
                {
                    AureliusLandmarkDetector.Detect(world.settings, true);
                }

                EditorGUILayout.Space(4);
                GUI.backgroundColor = new Color(1f, 0.7f, 0.6f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("CLEAR GENERATED CONTENT"))
                        AureliusWorldBuilder.ClearGenerated(world, false, false);
                    if (GUILayout.Button("Clear All (incl. splines & assets)") &&
                        EditorUtility.DisplayDialog("Clear Aurelius World", "Remove terrain, water, vegetation, lake, rivers, paths and delete generated TerrainData / mask / mesh assets?", "Clear", "Cancel"))
                        AureliusWorldBuilder.ClearGenerated(world, true, true);
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.Space();
            showSettings = EditorGUILayout.Foldout(showSettings, "World Settings", true);
            if (showSettings)
            {
                CreateCachedEditor(world.settings, null, ref settingsEditor);
                settingsEditor.OnInspectorGUI();
            }
        }
    }

    [CustomEditor(typeof(AureliusRiver)), CanEditMultipleObjects]
    public class AureliusRiverEditor : AureliusSplineEditor { }

    [CustomEditor(typeof(AureliusPath)), CanEditMultipleObjects]
    public class AureliusPathEditor : AureliusSplineEditor { }

    /// <summary>Scene handles for spline control points (drag to reshape; Shift+click a handle to insert after it).</summary>
    public class AureliusSplineEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Drag the points in the Scene view. Then press GENERATE TERRAIN on AureliusWorld to re-carve.", MessageType.Info);
            var world = ((Component)target).GetComponentInParent<AureliusWorld>();
            if (world != null && GUILayout.Button("Apply to Terrain (Generate Terrain)")) AureliusWorldBuilder.GenerateTerrain(world);
        }

        void OnSceneGUI()
        {
            var spline = (AureliusSpline)target;
            for (int i = 0; i < spline.points.Count; i++)
            {
                Vector3 p = spline.points[i];
                float size = HandleUtility.GetHandleSize(p) * 0.12f;
                EditorGUI.BeginChangeCheck();
                var np = Handles.FreeMoveHandle(p, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(spline, "Move Spline Point");
                    np.y = p.y;
                    if (Event.current.shift && i < spline.points.Count - 1)
                        spline.points.Insert(i + 1, (p + spline.points[i + 1]) * 0.5f);
                    else spline.points[i] = np;
                    EditorUtility.SetDirty(spline);
                }
            }
        }
    }

    [CustomEditor(typeof(AureliusLake))]
    public class AureliusLakeEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Move the object to move the lake (Y = water level). Drag the shore handles to reshape and the island handles to move islands, then GENERATE TERRAIN.", MessageType.Info);
            var world = ((Component)target).GetComponentInParent<AureliusWorld>();
            if (world != null && GUILayout.Button("Apply to Terrain (Generate Terrain)")) AureliusWorldBuilder.GenerateTerrain(world);
        }

        void OnSceneGUI()
        {
            var lake = (AureliusLake)target;
            Handles.color = new Color(0.3f, 0.7f, 1f);
            for (int i = 0; i < lake.shape.Count; i++)
            {
                Vector3 p = lake.ShapeHandlePosition(i);
                EditorGUI.BeginChangeCheck();
                var np = Handles.FreeMoveHandle(p, HandleUtility.GetHandleSize(p) * 0.1f, Vector3.zero, Handles.CubeHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(lake, "Reshape Lake");
                    float r = Vector2.Distance(new Vector2(np.x, np.z), lake.Center);
                    lake.shape[i] = Mathf.Max(0.15f, r / lake.radius);
                    EditorUtility.SetDirty(lake);
                }
            }
            Handles.color = new Color(0.4f, 0.9f, 0.4f);
            for (int i = 0; i < lake.islands.Count; i++)
            {
                var isl = lake.islands[i];
                var p = new Vector3(lake.Center.x + isl.offset.x, lake.WaterLevel + isl.height, lake.Center.y + isl.offset.y);
                EditorGUI.BeginChangeCheck();
                var np = Handles.FreeMoveHandle(p, HandleUtility.GetHandleSize(p) * 0.12f, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(lake, "Move Island");
                    isl.offset = new Vector2(np.x, np.z) - lake.Center;
                    EditorUtility.SetDirty(lake);
                }
            }
        }
    }
}
