using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

public class DependencyFinder : EditorWindow
{
    [MenuItem("Custom/Dependency Finder")]
    static void Open()
    {
        GetWindow<DependencyFinder>().titleContent = new GUIContent("Dependency Finder");
    }

    string path;
    Object lastSelected;
    Object lastSearched;
    HashSet<Object> dependencies = new HashSet<Object>();
    HashSet<System.Type> allDependencyTypes = new HashSet<System.Type>();
    Vector2 scroll;
    Vector2 typeScroll;
    System.Type filter = null;
    const int buttonWidth = 150;
    const int buttonHeight = 50;
    bool showMaterialField = false;

    bool Toggle(string name, ref bool show)
    {
        string label = show ? $"\u25BC {name}" : $"\u25BA {name}";
        EditorGUILayout.BeginHorizontal();
        show = GUILayout.Toggle(show, label, GUI.skin.label);
        EditorGUILayout.EndHorizontal();
        return show;
    }

    void OnGUI()
    {
        if (lastSelected != Selection.activeObject)
        {
            lastSelected = Selection.activeObject;
        }
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Last Selected");
        EditorGUI.BeginChangeCheck();
        lastSelected = EditorGUILayout.ObjectField(lastSelected, typeof(Object), true);
        if (EditorGUI.EndChangeCheck())
        {
            Selection.activeObject = lastSelected;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginVertical(GUI.skin.box);
        if (Toggle("Material Texture Field", ref showMaterialField))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search Path:");
            path = EditorGUILayout.TextField(path);
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Scan Texture in All Materials from Search Path"))
            {
                lastSearched = lastSelected;
                FindMaterialDependencies();
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Referenced"))
        {
            lastSearched = lastSelected;
            FindReferencedFor(lastSelected, false);
        }
        if (GUILayout.Button("Referenced Recursively"))
        {
            lastSearched = lastSelected;
            FindReferencedFor(lastSelected, true);
        }
        if (GUILayout.Button("Dependencies"))
        {
            lastSearched = lastSelected;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(lastSearched, out var lastSearchedGUID, out long _);
            FindDependenciesFor(lastSelected, lastSearchedGUID);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(GUI.skin.box);

        EditorGUILayout.BeginHorizontal();
        string guid = string.Empty;
        if (lastSearched != null)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(lastSearched, out guid, out long _);
        }
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.LabelField($"Last Searched guid: {guid}:");
        EditorGUILayout.ObjectField(lastSearched, typeof(Object), true);
        EditorGUILayout.EndHorizontal();
        EditorGUI.EndDisabledGroup();

        typeScroll = EditorGUILayout.BeginScrollView(typeScroll, GUILayout.MinHeight(50), GUILayout.MaxHeight(200));
        GUILayout.Label("Types:");
        var originalColor = GUI.color;

        GUI.color = filter == null ? Color.green : originalColor;
        if (GUILayout.Button("Select All Types"))
        {
            filter = null;
        }
        GUI.color = originalColor;

        int colMax = (int)(this.position.width / buttonWidth);
        int colCount = -1;
        GUILayout.BeginHorizontal();
        foreach (var type in allDependencyTypes)
        {
            if (++colCount == colMax)
            {
                colCount = 0;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
            }
            GUI.color = type == filter ? Color.green : originalColor;
            if (GUILayout.Button(type.Name, GUILayout.Height(buttonHeight)))
            {
                filter = type;
            }
        }
        GUI.color = originalColor;
        GUILayout.EndHorizontal();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();

        scroll = EditorGUILayout.BeginScrollView(scroll, GUI.skin.box);
        GUILayout.Label("Dependences:");
        foreach (var dependency in dependencies)
        {
            // EditorGUILayout.BeginHorizontal();
            // EditorGUILayout.LabelField("Last Selected");
            if (filter == null || filter == dependency.GetType())
            {
                EditorGUILayout.ObjectField(dependency, typeof(Object), true);
            }
            // EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select All"))
        {
            var list = dependencies.ToList();
            if (filter != null)
            {
                for (int i = list.Count - 1; i >= 0; --i)
                {
                    if (list[i].GetType() != filter)
                    {
                        list.RemoveAt(i);
                    }
                }
            }
            Selection.objects = list.ToArray();
        }
        if (GUILayout.Button("Clear"))
        {
            dependencies.Clear();
        }
        EditorGUILayout.EndHorizontal();
    }

    void ParseAllDependenciesFor(string assetPath, bool recursively)
    {
        if (!File.Exists(assetPath)) return;
        var data = File.ReadAllLines(assetPath);
        foreach (var line in data)
        {
            if (line.Contains("guid"))
            {
                string last = null;
                try
                {
                    last = GetNextBlock(line, "guid: ");

                    if (!string.IsNullOrEmpty(last))
                    {
                        var filePath = AssetDatabase.GUIDToAssetPath(last);
                        var obj = AssetDatabase.LoadAssetAtPath<Object>(filePath);
                        if (obj != null)
                        {
                            dependencies.Add(obj);
                            allDependencyTypes.Add(obj.GetType());

                            if (recursively)
                            {
                                var ext = Path.GetExtension(filePath);
                                if (checkExts.Contains(ext))
                                {
                                    ParseAllDependenciesFor(filePath, recursively);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.Log(last);
                    Debug.LogError(ex);
                }
            }
        }
    }

    void AddIfMatchGUID(string assetPath, string guid)
    {
        if (!File.Exists(assetPath)) return;
        var data = File.ReadAllLines(assetPath);
        foreach (var line in data)
        {
            if (line.Contains("guid"))
            {
                string last = null;
                try
                {
                    last = GetNextBlock(line, "guid: ");

                    if (!string.IsNullOrEmpty(last) && last == guid)
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                        if (obj != null)
                        {
                            dependencies.Add(obj);
                            allDependencyTypes.Add(obj.GetType());
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.Log(last);
                    Debug.LogError(ex);
                }
            }
        }
    }

    string GetNextBlock(string text, string a)
    {
        int pFrom = text.IndexOf(a) + a.Length;
        int pTo = text.IndexOf(",", pFrom + 1);

        return text[pFrom..pTo];
    }

    void FindReferencedFor(Object @object, bool recursively)
    {
        if (@object == null) return;
        dependencies.Clear();
        var assetPath = AssetDatabase.GetAssetPath(@object);
        ParseAllDependenciesFor(assetPath, recursively);
    }

    static HashSet<string> checkExts = new HashSet<string>()
    {
        ".prefab",
        ".scene",
        ".shader",
        ".shadergraph",
        ".lighting",
        ".mixer",
        ".inputactions",
        ".mat",
        ".asset",
        ".mask",
        ".controller",
        ".overrideController",
        ".anim",
        ".renderTexture",
        ".shadervariants",
        ".scenetemplate",
        ".giparams",
        ".playable",
        ".signal",
        ".physicMaterial",
        ".physicsMaterial2D",
        ".compute",
        ".raytrace",
        ".guiskin",
        ".terrainlayer",
        ".fontsettings",
    };
    void FindDependenciesFor(Object @object, string guid)
    {
        if (@object == null) return;
        dependencies.Clear();
        foreach (var file in Directory.EnumerateFileSystemEntries("Assets/", "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (!checkExts.Contains(ext))
            {
                continue;
            }

            AddIfMatchGUID(file, guid);
        }
    }

    void FindMaterialDependencies()
    {
        dependencies.Clear();
        foreach (var file in Directory.EnumerateFileSystemEntries(path, "*.mat", SearchOption.AllDirectories))
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(file);
            if (mat && mat.shader != null)
            {
                if (!map.ContainsKey(mat.shader))
                {
                    map[mat.shader] = CollectProperty(mat.shader);
                }

                CollectDependencies(mat, map[mat.shader]);
            }
        }
    }

    public struct PropertyData
    {
        public string property;
        public ShaderUtil.ShaderPropertyType type;
    }

    Dictionary<Shader, List<PropertyData>> map = new ();

    List<PropertyData> CollectProperty(Shader shader)
    {
        var data = new List<PropertyData>();
        var count = ShaderUtil.GetPropertyCount(shader);

        for (int i = 0; i < count; ++i)
        {
            data.Add(new PropertyData()
            {
                property = ShaderUtil.GetPropertyName(shader, i),
                type = ShaderUtil.GetPropertyType(shader, i),
            });
        }
        return data;
    }

    void CollectDependencies(Material mat, List<PropertyData> data)
    {
        foreach (var property in data)
        {
            if (property.type == ShaderUtil.ShaderPropertyType.TexEnv)
            {
                var tex = mat.GetTexture(property.property);
                if (tex && lastSelected == tex)
                {
                    dependencies.Add(mat);
                    allDependencyTypes.Add(mat.GetType());
                }
            }
        }
    }
}
