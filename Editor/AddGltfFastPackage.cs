using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    [InitializeOnLoad]
    public class AddGltfFastPackage
    {
        static readonly string PackagesPath = Path.Combine(Application.dataPath, "..", "Packages");
        static readonly string ManifestJsonPath = Path.Combine(PackagesPath, "manifest.json");

        private static readonly ScopedRegistry OpenupmRegistry = new ScopedRegistry()
        {
            name = "package.openupm.com",
            url = "https://package.openupm.com",
            scopes = new[] { 
                "com.atteneder.gltfast",
                "com.openupm" 
            }
        };

        static AddGltfFastPackage()
        {
            InstallGltfFast();
            InstallDraco();
        }

        [MenuItem("glTFFast Installer/Install glTFFast")]
        private static void InstallGltfFast()
        {
            const string gtlffastVersion = "2.0.0";
            const string packageKey = "\"com.atteneder.gltfast\"";
            var manifest = Manifest.JsonDeserialize(ManifestJsonPath);
            AddRegistry(manifest, OpenupmRegistry);
            if (CheckIfDependencyExists(manifest, packageKey)) return;
            if (!EditorUtility.DisplayDialog("Add glTFFast Package",
                "Would you like to add the glTFFast package to your project?", "Yes", "No")) return;
            Debug.Log("Installing glTFFast...");
            UpdateManifest(manifest,packageKey,gtlffastVersion);
        }
        
        [MenuItem("glTFFast Installer/Install Draco")]
        private static void InstallDraco()
        {
            var manifest = Manifest.JsonDeserialize(ManifestJsonPath);
            AddRegistry(manifest, OpenupmRegistry);
            const string dracoPackageKey = "\"com.atteneder.draco\"";
            if (CheckIfDependencyExists(manifest, dracoPackageKey)) return;
            if (!EditorUtility.DisplayDialog("Add Draco Package",
                "Would you like to add the draco package to your project?", "Yes", "No")) return;
            const string dracoVersion = "https://gitlab.com/atteneder/DracoUnity.git";
            Debug.Log("Installing Draco...");
            UpdateManifest(manifest, dracoPackageKey, dracoVersion);
        }

        private static bool CheckIfDependencyExists(Manifest manifest,string packageKey)
        {
            var dependencies = manifest.Dependencies.Split(',').ToList();
            return dependencies.Any(d => d.Trim('\n').Trim(' ').Split(':')[0].Contains(packageKey));
        }

        private static void UpdateManifest(Manifest manifest,string packageKey,string packageVersion)
        {
            if (CheckIfDependencyExists(manifest, packageKey)) return;
            SetVersion(manifest,packageKey,packageVersion);
            manifest.JsonSerialize(ManifestJsonPath);
            AssetDatabase.Refresh();
        }

        private static void SetVersion(Manifest manifest,string packageKey,string packageVersion)
        {
            var dependencies = manifest.Dependencies.Split(',').ToList();

            var versionSet = false;
            for(var i = 0; i < dependencies.Count; i++)
            {
                if(!dependencies[i].Contains(packageKey))
                    continue;

                var kvp = dependencies[i].Split(':');

                kvp[1] = $"\"{packageVersion}\""; //version string of the package

                dependencies[i] = string.Join(":", kvp);

                versionSet = true;
            }

            if (!versionSet)
                dependencies.Insert(0, $"\n    {packageKey}: \"{packageVersion}\"");
        

            manifest.Dependencies = string.Join(",", dependencies);
        }

        private static bool AddRegistry(Manifest manifest, ScopedRegistry scopedRegistry)
        {
            var registries = manifest.ScopedRegistries.ToList();
            if (registries.Any(r => r == scopedRegistry))
                return false;

            registries.Add(scopedRegistry);

            manifest.ScopedRegistries = registries.ToArray();
            return true;
        }

        private class Manifest
        {
            private const int IndexNotFound = -1;
            private const string DependenciesKey = "\"dependencies\"";

            public ScopedRegistry[] ScopedRegistries;
            public string Dependencies;

            public void JsonSerialize(string path)
            {
                var jsonString = JsonUtility.ToJson(
                    new UnitySerializableManifest {scopedRegistries = ScopedRegistries, dependencies = new DependencyPlaceholder()},
                    true);

                var startIndex = GetDependenciesStart(jsonString);
                var endIndex = GetDependenciesEnd(jsonString, startIndex);

                var stringBuilder = new StringBuilder();

                stringBuilder.Append(jsonString.Substring(0, startIndex));
                stringBuilder.Append(Dependencies);
                stringBuilder.Append(jsonString.Substring(endIndex, jsonString.Length - endIndex));

                File.WriteAllText(path, stringBuilder.ToString());
            }

            public static Manifest JsonDeserialize(string path)
            {
                var jsonString = File.ReadAllText(path);

                var registries = JsonUtility.FromJson<UnitySerializableManifest>(jsonString).scopedRegistries ?? new ScopedRegistry[0];
                var dependencies = DeserializeDependencies(jsonString);

                return new Manifest { ScopedRegistries = registries, Dependencies = dependencies };
            }

            private static string DeserializeDependencies(string json)
            {
                var startIndex = GetDependenciesStart(json);
                var endIndex = GetDependenciesEnd(json, startIndex);

                if (startIndex == IndexNotFound || endIndex == IndexNotFound)
                    return null;

                var dependencies = json.Substring(startIndex, endIndex - startIndex);
                return dependencies;
            }

            private static int GetDependenciesStart(string json)
            {
                var dependenciesIndex = json.IndexOf(DependenciesKey, StringComparison.InvariantCulture);
                if (dependenciesIndex == IndexNotFound)
                    return IndexNotFound;

                var dependenciesStartIndex = json.IndexOf('{', dependenciesIndex + DependenciesKey.Length);

                if (dependenciesStartIndex == IndexNotFound)
                    return IndexNotFound;

                dependenciesStartIndex++; //add length of '{' to starting point

                return dependenciesStartIndex;
            }

            static int GetDependenciesEnd(string jsonString, int dependenciesStartIndex)
            {
                return jsonString.IndexOf('}', dependenciesStartIndex);
            }
        }

        private class UnitySerializableManifest
        {
            public ScopedRegistry[] scopedRegistries;
            public DependencyPlaceholder dependencies;
        }

        [Serializable]
        private struct ScopedRegistry
        {
            public string name;
            public string url;
            public string[] scopes;

            public override bool Equals(object obj)
            {
                if (!(obj is ScopedRegistry))
                    return false;

                var other = (ScopedRegistry) obj;

                return name == other.name &&
                       url == other.url &&
                       scopes.SequenceEqual(other.scopes);
            }

            public static bool operator ==(ScopedRegistry a, ScopedRegistry b)
            {
                return a.Equals(b);
            }

            public static bool operator !=(ScopedRegistry a, ScopedRegistry b)
            {
                return !a.Equals(b);
            }

            public override int GetHashCode()
            {
                var hash = scopes.Aggregate(17, (current, scope) => current * 23 + (scope == null ? 0 : scope.GetHashCode()));

                hash = hash * 23 + (name == null ? 0 : name.GetHashCode());
                hash = hash * 23 + (url == null ? 0 : url.GetHashCode());

                return hash;
            }
        }

        [Serializable]
        private struct DependencyPlaceholder { }
    }
}
