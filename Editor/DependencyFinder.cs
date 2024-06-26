using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Reflection;

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

    
    bool ToggleButton(bool was, string name, GUIStyle style, params GUILayoutOption[] options)
    {
        var later = GUILayout.Toggle(was, name, style, options);
        if (later != was)
        {
            return true;
        }
        return false;
    }

    bool ToggleButton(bool was, string name, params GUILayoutOption[] options)
    {
        var later = GUILayout.Toggle(was, name, options);
        if (later != was)
        {
            return true;
        }
        return false;
    }

    bool referenceMode = false;
    bool drawNamespace = false;

    void DrawToolBar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (ToggleButton(referenceMode, "Reference Mode", EditorStyles.toolbarButton))
        {
            referenceMode = !referenceMode;
        }
        GUILayout.FlexibleSpace();
        if (ToggleButton(drawNamespace, "Draw Namespace", EditorStyles.toolbarButton))
        {
            drawNamespace = !drawNamespace;
        }
        GUILayout.EndHorizontal();
    }

    void OnGUI()
    {
        DrawToolBar();
        if (referenceMode)
        {
            DrawReferenceFinder();
        }
        else
        {
            DrawDependencyFinder();
        }
    }

    #region === Dependency Finder ===
    private void DrawDependencyFinder()
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
            var typeName = drawNamespace ? type.FullName : type.Name;
            if (GUILayout.Button(typeName, GUILayout.Height(buttonHeight)))
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
                    Debug.Log($"found error for guid: '{last}', from line:\n{line}\n, in file:\n{assetPath}\n see error at next error log");
                    Debug.LogError(ex);
                }
            }
        }
    }

    string GetNextBlock(string text, string a)
    {
        int pFrom = text.IndexOf(a) + a.Length;
        int pTo = text.IndexOf(",", pFrom + 1);
        if (pTo < pFrom)
        {
            pTo = text.Length;
        }

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
    #endregion
    #region === Reference Finder ===

    [System.Serializable]
    public class UsageMapData
    {
        public string FieldName;
        public string FieldType;
        public object referencedBy;
        public string referencedName;
    }

    public enum ShaderPropertyType
    {
        Int = 0,
        Float,
        Color,
        Vector,
        Texture,
        Count
    }
    [System.Serializable]
    public class ShaderProperties
    {
        public string ShaderName;
        public string[] Properties;
        public int[] PropertyTypes;

        public bool HasProperty(string name, ShaderPropertyType type)
        {
            for (int i = Properties.Length - 1; i >= 0; --i)
            {
                string property = Properties[i];
                if (property == name)
                {
                    return PropertyTypes[i] == (int)type;
                }
            }
            return false;
        }

        public bool HasProperty(int id, ShaderPropertyType type)
        {
            for (int i = Properties.Length - 1; i >= 0; --i)
            {
                int propertyID = Shader.PropertyToID(Properties[i]);
                if (propertyID == id)
                {
                    return PropertyTypes[i] == (int)type;
                }
            }
            return false;
        }
    }

    Rect lastScrollViewRect;
    Vector2 dependenciesScroll;
    Dictionary<Object, List<UsageMapData>> usageMap = new();

    List<UsageMapData> inspectList = null;
    string searchBarStr = string.Empty;

    bool drawTypeField;
    string typeSearchBarStr = string.Empty;
    HashSet<System.Type> listTypes = new HashSet<System.Type>();
    System.Type selectedType = null;
    string selectedTypeFullName = string.Empty;

    private void DrawReferenceFinder()
    {
        if (ToggleButton(drawTypeField, drawTypeField ? "\u25BC Type Field" : "\u25BA Type Field", GUI.skin.label))
        {
            drawTypeField = !drawTypeField;
        }
        if (drawTypeField)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Loaded Type Count: ({allTypes.Count})");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search Type:", GUILayout.Width(100f));
            var newStr = GUILayout.TextField(typeSearchBarStr);
            if (newStr != typeSearchBarStr)
            {
                typeSearchBarStr = newStr;
                if (!string.IsNullOrEmpty(typeSearchBarStr))
                {
                    MatchSearchBar(typeSearchBarStr);
                }
            }
            GUILayout.EndHorizontal();

            typeScroll = EditorGUILayout.BeginScrollView(typeScroll, GUILayout.MinHeight(50), GUILayout.MaxHeight(200));
            GUILayout.Label("Types:");
            var originalColor = GUI.color;

            int colMax = (int)(this.position.width / buttonWidth);
            int colCount = -1;
            GUILayout.BeginHorizontal();
            foreach (var type in listTypes)
            {
                if (++colCount == colMax)
                {
                    colCount = 0;
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
                GUI.color = type == selectedType ? Color.green : originalColor;
                var typeName = drawNamespace ? type.FullName : type.Name;
                if (GUILayout.Button(typeName, GUILayout.Height(buttonHeight)))
                {
                    selectedType = type;
                    selectedTypeFullName = selectedType.FullName;
                }
            }
            GUI.color = originalColor;
            GUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }

        if (selectedType == null)
        {
            if (!string.IsNullOrEmpty(selectedTypeFullName))
            {
                selectedType = System.Type.GetType(selectedTypeFullName);
            }
            EditorGUILayout.HelpBox("No Type Selected. Select type from Type Field first.", MessageType.Warning);
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Collect Usage"))
        {
            CollectReferenceFromType(selectedType);
        }
        if (GUILayout.Button("Clear", GUILayout.Width(80f)))
        {
            usageMap.Clear();
            inspectList = null;
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Search in ({usageMap.Count}) Objects:");
        searchBarStr = GUILayout.TextField(searchBarStr);
        GUILayout.EndHorizontal();
        var searchBarStrLower = searchBarStr.ToLowerInvariant();

        scroll = GUILayout.BeginScrollView(scroll);
        foreach (var pair in usageMap)
        {
            var obj = pair.Key;
            var key = obj == null ? "null" : obj.name;
            if (!string.IsNullOrEmpty(searchBarStrLower) &&
                !key.Contains(searchBarStrLower) &&
                !searchBarStrLower.Contains(key))
                continue;
            var rect = GUILayoutUtility.GetRect(position.width - 5, 20f);
            var p = new Vector2(rect.x + 1f, rect.y + 1f + lastScrollViewRect.y - scroll.y);
            var p2 = new Vector2(rect.width - 1f, rect.height - 1f) + p;
            var inView = lastScrollViewRect.Contains(p) || lastScrollViewRect.Contains(p2);
            if (inView)
            {
                var pos = rect.position;
                var size = rect.size;
                size.x = size.x * 0.5f;
                var left = new Rect(pos, size);
                pos.x += size.x;
                var right = new Rect(pos, size);

                var typeName = obj == null ? "null" : obj.GetType().Name;

                if (GUI.Button(left, $"{key} ({typeName})"))
                {
                    inspectList = pair.Value;
                }
                EditorGUI.ObjectField(right, obj, typeof(Object), true);
            }
        }
        GUILayout.EndScrollView();
        if (Event.current.type == EventType.Repaint)
            lastScrollViewRect = GUILayoutUtility.GetLastRect();
        if (inspectList != null)
        {
            dependenciesScroll = GUILayout.BeginScrollView(dependenciesScroll);
            foreach (var obj in inspectList)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20f);
                GUILayout.Label($"Field Name: {obj.FieldName} ({obj.FieldType}) Referenced by:");
                if (obj.referencedBy is Object @object)
                {
                    EditorGUILayout.ObjectField(@object, typeof(Object), true);
                }
                else
                {
                    GUILayout.Label(obj.referencedName);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
    }

    public IEnumerable<System.Type> FindDerivedTypes(Assembly assembly, System.Type baseType)
    {
        return assembly.GetTypes().Where(t => baseType.IsAssignableFrom(t));
    }

    HashSet<System.Type> allTypes = new HashSet<System.Type>();
    void SearchTypes()
    {
        allTypes.Clear();
        var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            foreach (var type in FindDerivedTypes(assembly, typeof(UnityEngine.Object)))
            {
                allTypes.Add(type);
            }
        }
    }

    void MatchSearchBar(string str)
    {
        str = str.ToLowerInvariant();
        if (string.IsNullOrEmpty(str))
        {
            return;
        }

        if (allTypes.Count == 0)
        {
            SearchTypes();
        }

        listTypes.Clear();
        foreach (var type in allTypes)
        {
            if (type == null) continue;
            var lower = type.FullName.ToLowerInvariant();
            if (string.IsNullOrEmpty(lower) || (!str.Contains(lower) && !lower.Contains(str)))
            {
                continue;
            }

            listTypes.Add(type);
        }
    }

    void CollectReferenceFromType(System.Type type)
    {
        usageMap.Clear();
        var managers = GameObject.FindObjectsOfType(type);
        foreach (var manager in managers)
        {
            try
            {
                CollectReference(manager.name, manager);
            }
            catch (System.Exception ex)
            {
                Debug.LogError(ex);
            }
        }
    }

    void CollectUsageMap(Object obj, FieldInfo field, object referencedBy, string referencedName)
    {
        if (!usageMap.ContainsKey(obj)) usageMap[obj] = new();
        usageMap[obj].Add(new UsageMapData()
        {
            FieldName = field.Name,
            FieldType = field.FieldType.Name,
            referencedBy = referencedBy,
            referencedName = referencedName,
        });
    }

    void CollectUsageMap(Object obj, PropertyInfo field, object referencedBy, string referencedName)
    {
        if (!usageMap.ContainsKey(obj)) usageMap[obj] = new();
        usageMap[obj].Add(new UsageMapData()
        {
            FieldName = field.Name,
            FieldType = field.PropertyType.Name,
            referencedBy = referencedBy,
            referencedName = referencedName,
        });
    }

    void CollectUsageMapTexture(Object obj, string propertyName, object referencedBy, string referencedName)
    {
        if (!usageMap.ContainsKey(obj)) usageMap[obj] = new();
        usageMap[obj].Add(new UsageMapData()
        {
            FieldName = $"{propertyName}",
            FieldType = "Material.Texture",
            referencedBy = referencedBy,
            referencedName = referencedName,
        });
    }

    static ShaderPropertyType UnityShaderPropertyTypeConvert(ShaderUtil.ShaderPropertyType type)
    {
        switch (type)
        {
            case ShaderUtil.ShaderPropertyType.Color:
                return ShaderPropertyType.Color;
            case ShaderUtil.ShaderPropertyType.Vector:
                return ShaderPropertyType.Vector;
#if UNITY_2021 || UNITY_2021_1_OR_NEWER
            case ShaderUtil.ShaderPropertyType.Int:
                return ShaderPropertyType.Int;
#endif
            case ShaderUtil.ShaderPropertyType.Float:
            case ShaderUtil.ShaderPropertyType.Range:
                return ShaderPropertyType.Float;
            case ShaderUtil.ShaderPropertyType.TexEnv:
                return ShaderPropertyType.Texture;
        }
        throw new System.Exception($"Unknown Shader type: {type}");
    }

    public static ShaderProperties CollectShaderProperties(Shader shader)
    {
        var count = ShaderUtil.GetPropertyCount(shader);
        var properties = new ShaderProperties
        {
            ShaderName = shader.name,
            Properties = new string[count],
            PropertyTypes = new int[count],
        };
        for (int i = 0; i < count; ++i)
        {
            properties.Properties[i] = ShaderUtil.GetPropertyName(shader, i);
            properties.PropertyTypes[i] = (int)UnityShaderPropertyTypeConvert(ShaderUtil.GetPropertyType(shader, i));
        }
        return properties;
    }

    void CollectForMaterial(string inputName, Material mat)
    {
        var property = CollectShaderProperties(mat.shader);
        for (int i = property.Properties.Length - 1; i >= 0; --i)
        {
            var type = property.PropertyTypes[i];
            if (type == (int)ShaderPropertyType.Texture)
            {
                var propName = property.Properties[i];
                var tex = mat.GetTexture(propName);
                if (tex != null)
                {
                    Debug.Log($"<color=cyan>Collect Texture: {tex}</color>");
                    CollectUsageMapTexture(tex, propName, mat, inputName);
                }
            }
        }
    }

    bool IsSkipType(System.Type type)
    {
        if (type == null) return true;
        if (!string.IsNullOrEmpty(type.Namespace) && type.Namespace.StartsWith("System"))
        {
            if (type.IsArray && !IsSkipType(type.GetElementType()))
            {
                return false;
            }

            var args = type.GetGenericArguments();
            foreach (var arg in args)
            {
                if (!IsSkipType(arg))
                {
                    return false;
                }
            }

            return true;
        }
        return false;
    }

    void CollectReference(string inputName, object input, HashSet<object> collected = null)
    {
        collected ??= new HashSet<object>();
        if (input == null)
        {
            return;
        }

        var type = input.GetType();

        if (IsSkipType(type))
        {
            // Debug.Log($"Skipped: {type}, {type.Name}, {type.FullName}");
            return;
        }

        if (type.GetCustomAttribute(typeof(System.ObsoleteAttribute)) != null)
        {
            return;
        }

        Debug.LogWarning($"CollectReference: {input} (Name: {inputName}, Type: {type})");
        if (!collected.Add(input))
        {
            return;
        }

        if (input is Material mat)
        {
            try
            {
                CollectForMaterial(inputName, mat);
            }
            catch (System.Exception ex)
            {
                Debug.LogError(ex);
            }
            return;
        }

        if (input is IEnumerable collection)
        {
            try
            {
                int count = 0;
                foreach (var item in collection)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    CollectReference($"{inputName}_{count++}", item, collected);
                }
            }
            catch (UnityEngine.MissingReferenceException)
            {
                // Nothing to do
            }
            catch (System.Exception ex)
            {
                Debug.LogError(ex);
            }
            return;
        }

        var members = type.GetMembers(
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.GetField |
            BindingFlags.GetProperty
        );
        foreach (var member in members)
        {
            if (member.GetCustomAttribute(typeof(System.ObsoleteAttribute)) != null)
            {
                continue;
            }

            if (member.MemberType == MemberTypes.Field && member is FieldInfo field)
            {
                try
                {
                    CollectField(field, inputName, input, collected);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(ex);
                }
            }
            else if (member.MemberType != MemberTypes.Property && member is PropertyInfo property)
            {
                if (property.GetIndexParameters().Any())
                {
                    continue;
                }

                try
                {
                    CollectProperty(property, inputName, input, collected);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError(ex);
                }
            }
        }
    }

    void CollectField(FieldInfo field, string inputName, object input, HashSet<object> collected)
    {
        var val = field.GetValue(input);
        if (val == null)
        {
            return;
        }

        Debug.Log($"field: {field.Name} val: {val}");
        if (val is Object obj)
        {
            if (obj != null)
            {
                Debug.Log($"<color=cyan>Collect: {obj}</color>");
                CollectUsageMap(obj, field, input, inputName);
            }
        }
        if (val != input)
        {
            CollectReference(field.Name, val, collected);
        }
    }

    void CollectProperty(PropertyInfo property, string inputName, object input, HashSet<object> collected)
    {
        var val = property.GetValue(input);
        if (val == null)
        {
            return;
        }

        Debug.Log($"property: {property.Name} val: {val}");
        if (val is Object obj)
        {
            if (obj != null)
            {
                Debug.Log($"<color=cyan>Collect: {obj}</color>");
                CollectUsageMap(obj, property, input, inputName);
            }
        }
        if (val != input)
        {
            CollectReference(property.Name, val, collected);
        }
    }
    #endregion
}
