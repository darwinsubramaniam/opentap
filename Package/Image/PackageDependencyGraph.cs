using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace OpenTap.Package
{
    /// <summary>
    /// This graph describes every version of every package in a memory-efficient way.
    /// Each unique name version and version specifier is represented by one value, connections between them are some dictionaries.
    /// The graph can be merged from multiple different sources - both file repositories and http repositories, but the source of each becomes lost when building the graph.
    /// When the source is needed a new lookup will be needed from a different place, this code is not really concerned with that.
    /// </summary>
    class PackageDependencyGraph
    {
        // all the existing names and versions.
        readonly List<string> nameLookup = new List<string>();
        readonly List<SemanticVersion> versionLookup = new List<SemanticVersion>();
        readonly List<VersionSpecifier> versionSpecifierLookup = new List<VersionSpecifier>();
     
        // lookup of X to id, ID being the index in the above tables.
        readonly Dictionary<string, int> name2Id = new Dictionary<string, int>(); 
        readonly Dictionary<SemanticVersion, int> version2Id = new Dictionary<SemanticVersion, int>();
        readonly Dictionary<VersionSpecifier, int> versionSpecifier2Id = new Dictionary<VersionSpecifier, int>();
        
        // Data structures for hosting versions.
        readonly Dictionary<string, SemanticVersion> stringToVersion = new Dictionary<string, SemanticVersion>();
        readonly Dictionary<string, VersionSpecifier> stringToVersionSpecifier = new Dictionary<string, VersionSpecifier>();
        
        // versions contains all versions of a given name. Eg all versions of OpenTAP/
        readonly Dictionary<int, HashSet<int>> versions = new Dictionary<int, HashSet<int>>();
        
        // Dependencies for a given version 
        readonly Dictionary<(int packageNameId, int packageVersion), (int packageNameId, int versionSpecifierId)[]> dependencies = new Dictionary<(int, int), (int, int)[]>();

        /// <summary>
        /// Callback for when additional packages are needed. For example if only release packages of one specific
        /// package name has been defined, this can be called to extend with e.g beta packages.
        /// </summary>
        public Action<string, string> UpdatePrerelease;

        /// <summary> The total number of packages contained in this graph. </summary>
        public int Count => versions.Sum(x => x.Value.Count);

        int GetNameId(string name)
        {
            if (!name2Id.TryGetValue(name, out var id))
            {
                name2Id[name] = nameLookup.Count;
                id = nameLookup.Count;
                versions[id] = new HashSet<int>();
                nameLookup.Add(name);
            }

            return id;
        }
        
        int GetVersionId(SemanticVersion v)
        {
            if (!version2Id.TryGetValue(v, out var id))
            {
                version2Id[v] = id = versionLookup.Count;
                versionLookup.Add(v);
            }
            return id;
        }
        
        int GetVersionId(string v2)
        {
            var v = stringToVersion.GetOrCreateValue(v2, SemanticVersion.Parse);
            return GetVersionId(v);
        }

        int GetVersionSpecifier(string v2)
        {
            var v = stringToVersionSpecifier.GetOrCreateValue(v2, VersionSpecifier.Parse);
            return GetVersionSpecifier(v);
        }
        
        int GetVersionSpecifier(VersionSpecifier v)
        {
            if (!versionSpecifier2Id.TryGetValue(v, out var id))
            {
                versionSpecifier2Id[v] = id = versionSpecifierLookup.Count;
                versionSpecifierLookup.Add(v);    
            }
            return id;
        }

        public void LoadFromPackageDefs(IEnumerable<PackageDef> packages)
        {
            int addPackages = 0;
            foreach (var elem in packages.ToArray())
            {
                var name = elem.Name;
                var version = elem.Version ?? new SemanticVersion(0, 1, 0 , null, null);
                
                var id = GetNameId(name);
                var thisVersions = versions[id];
                if (thisVersions.Add(GetVersionId(version)))
                    addPackages += 1;
                if(elem.Dependencies.Count > 0)
                {
                    var deps = new (int id, int y)[ elem.Dependencies.Count];
                    
                    for (int i = 0; i < deps.Length; i++)
                    {
                        var dep = elem.Dependencies[i];
                        var depname = dep.Name;
                        var depid = GetNameId(depname);
                        var depver = dep.Version;
                        deps[i] = (depid, GetVersionSpecifier(depver));
                    }

                    dependencies[(id, GetVersionId(version))] = deps;
                }
            }

        }

        public void LoadFromJson(JsonDocument json)
        {
            var packages = json.RootElement.GetProperty("packages");

            int addPackages = 0;
            foreach (var elem in packages.EnumerateArray())
            {
                var name = elem.GetProperty("name").GetString();
                var version = elem.GetProperty("version").GetString();
                if (!SemanticVersion.TryParse(version, out var v))
                    continue;

                var id = GetNameId(name);
                var thisVersions = versions[id];
                if (!thisVersions.Add(GetVersionId(version)))
                    continue; // package already added.
                addPackages += 1;
                if(elem.TryGetProperty("dependencies", out var obj))
                {
                    var l = obj.GetArrayLength();
                    if (l != 0)
                    {
                        var deps = new (int id, int y)[l];
                        int i = 0; 
                        foreach (var dep in obj.EnumerateArray())
                        {
                            var depname = dep.GetProperty("name").GetString();
                            var depid = GetNameId(depname);
                            var depver = dep.GetProperty("version").GetString();
                            deps[i] = (depid, GetVersionSpecifier(depver));
                            i++;
                        }

                        dependencies[(id, GetVersionId(version))] = deps;
                    }
                    
                }
            }
        }

        // tracks currently fetched pre-releases. This is an optimization to avoid having to 
        // pull absolutely all packages from the repository.
        readonly Dictionary<string, string> currentPreReleases = new Dictionary<string, string>();
        public IEnumerable<SemanticVersion> PackagesSatisfying(PackageSpecifier packageSpecifier)
        {
            if (!string.IsNullOrWhiteSpace(packageSpecifier.Version.PreRelease) || packageSpecifier.Version.MatchBehavior.HasFlag(VersionMatchBehavior.AnyPrerelease))
            {
                string newPreRelease;
                if (packageSpecifier.Version.MatchBehavior.HasFlag(VersionMatchBehavior.AnyPrerelease))
                {
                    newPreRelease = "alpha";
                }
                else
                {
                    newPreRelease = packageSpecifier.Version.PreRelease.Split('.')[0];
                }

                if (!currentPreReleases.TryGetValue(packageSpecifier.Name, out var currentPrerelease) || newPreRelease.CompareTo(currentPrerelease) > 0 )
                {
                    currentPreReleases[packageSpecifier.Name] = newPreRelease;
                    // update the package dependency graph.
                    if (UpdatePrerelease != null)
                        UpdatePrerelease(packageSpecifier.Name, newPreRelease);
                }
            }
            if (name2Id.TryGetValue(packageSpecifier.Name, out var Id))
            {
                var thisVersions = versions[Id];
                foreach (var v in thisVersions)
                {
                    var semv = versionLookup[v];
                    {
                        if (packageSpecifier.Version == VersionSpecifier.Any)
                        {
                            yield return semv;
                        }else if (packageSpecifier.Version.IsCompatible(semv))
                            yield return semv;
                        
                    }
                }
            }
        }

        /// <summary>
        /// Turn all the data back into PackageDefs, note that these PackageDefs are severly limited and
        /// should only be used for testing or for building another graph.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<PackageDef> PackageSpecifiers()
        {
            
            foreach (var thing in versions)
            {
                var pkgName = nameLookup[thing.Key];
                foreach (var v in thing.Value)
                {
                    var version = this.versionLookup[v];
                    var pkg = new PackageDef
                    {
                        Name = pkgName,
                        Version = version
                    };
                    if (dependencies.TryGetValue((thing.Key, v), out var deps))
                    {
                        foreach (var dep in deps)
                        {
                            var name = nameLookup[dep.packageNameId];
                            var version2 = versionSpecifierLookup[dep.versionSpecifierId];
                            pkg.Dependencies.Add(new PackageDependency(name, version2));
                        }
                    }

                    yield return pkg;
                }
            }
        }

        public bool CouldSatisfy(string pkgName, VersionSpecifier version, PackageSpecifier[] others, PackageSpecifier[] fixedPackages)
        {
            // we only do this if version can actually be interpreted as a semantic version.
            // if version is ^ or incomplete we just assume that it 'could' satisfy the others.
            if (name2Id.TryGetValue(pkgName, out var Id) && version.TryAsExactSemanticVersion(out var v))
            {
                if (!dependencies.TryGetValue((Id, GetVersionId(v)), out var deps))
                    return true; // this package has no dependencies, so yes.
                foreach (var dep in deps)
                {
                    var depVersion = versionSpecifierLookup[dep.Item2];
                    
                    var ps = new PackageSpecifier(nameLookup[dep.Item1], depVersion);
                    var o = others.FirstOrDefault(x => x.Name == ps.Name);
                    if (o == null)
                        continue;
                    if (ps.Version.IsSatisfiedBy(o.Version) == false && o.Version.IsSatisfiedBy(ps.Version) == false)
                        return false;
                    
                    var o2 = fixedPackages.FirstOrDefault(x => x.Name == ps.Name);
                    if (o2 == null)
                        continue;
                    if (ps.Version.IsSatisfiedBy(o.Version) == false && o2.Version.IsSatisfiedBy(ps.Version) == false)
                        return false;
                    
                    // todo protect from circular dependencies here.
                    if (!CouldSatisfy(ps.Name, depVersion, others, Array.Empty<PackageSpecifier>()))
                        return false;
                }

                return true;
            }

            return true;
        }

        public IEnumerable<PackageSpecifier> GetDependencies(string pkgName, SemanticVersion version)
        {
            if (name2Id.TryGetValue(pkgName, out var Id))
            {
                if (dependencies.TryGetValue((Id, GetVersionId(version)), out var deps))
                {
                    foreach (var dep in deps)
                    {
                        var depVersion = versionSpecifierLookup[dep.Item2];

                        yield return new PackageSpecifier(nameLookup[dep.Item1], depVersion);
                    }
                }
            }
        }

        public bool HasPackage(string pkgName, SemanticVersion version)
        {
            return name2Id.TryGetValue(pkgName, out var Id)
                   && version2Id.TryGetValue(version, out var vId)
                   && versions.TryGetValue(Id, out var set)
                   && set.Contains(vId);
        }

        /// <summary> Absorbs another package dependency graph into the structure. </summary>
        public void Absorb(PackageDependencyGraph graph)
        {
            LoadFromPackageDefs(graph.PackageSpecifiers());
        }
    }
}