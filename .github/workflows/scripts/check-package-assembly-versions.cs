// Pack-time check (TR-RAG-048): every TechieRag assembly inside every packed .nupkg carries the
// package's version, and every reference one TechieRag assembly makes to another binds to that same
// version. Called by the `Check assembly versions` step of both publishing workflows, after Pack.
//
//   dotnet run .github/workflows/scripts/check-package-assembly-versions.cs -- <nupkg-folder> <version>
//
// Why it exists: TechieRag 1.0.9 to 1.1.1 shipped lib/net10.0/TechieRag.dll and TechieRag.Embedded.dll
// stamped 1.0.0.0 while their net8.0 copies and the sibling packages said 1.1.1.0. The test step built
// without -p:Version, rebuilt the net10.0 outputs over the release build, and `pack --no-build` packed
// what was left on disk. The package file name was right, so the only place the fault showed was a
// consumer's FileNotFoundException at run time. The workflows now pass the version to every step, and
// this check reads the packed assemblies themselves, so no future build path can repeat it unseen.
//
// Exit 0 every assembly matches · 1 a mismatch (each printed as a ::error:: line) · 2 bad arguments.
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 2 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("usage: check-package-assembly-versions.cs <nupkg-folder> <package-version>");
    return 2;
}

var folder = args[0];
var packageVersion = args[1];
var core = packageVersion.Split('-', '+')[0];
if (!Version.TryParse(core, out var parsed) || parsed.Build < 0)
{
    Console.Error.WriteLine($"'{packageVersion}' is not a version like 1.1.2 or 1.1.2-preview.3");
    return 2;
}

// AssemblyVersion is derived from Version by the SDK as Major.Minor.Patch.0.
var expected = new Version(parsed.Major, parsed.Minor, parsed.Build, 0);
var packages = Directory.GetFiles(folder, "*.nupkg").Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase)).OrderBy(path => path).ToList();
if (packages.Count == 0)
{
    Console.Error.WriteLine($"::error::No .nupkg files in {folder}");
    return 1;
}

var failures = new List<string>();
var checkedCount = 0;
foreach (var package in packages)
{
    using var zip = ZipFile.OpenRead(package);
    var assemblies = zip.Entries.Where(entry => entry.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
        && entry.Name.StartsWith("TechieRag", StringComparison.OrdinalIgnoreCase)
        && entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToList();
    if (assemblies.Count == 0)
    {
        failures.Add($"{Path.GetFileName(package)}: no lib/*/TechieRag*.dll inside");
        continue;
    }

    foreach (var entry in assemblies)
    {
        using var buffer = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(buffer);
        }

        buffer.Position = 0;
        using var reader = new PEReader(buffer);
        var metadata = reader.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        var name = metadata.GetString(definition.Name);
        var where = $"{Path.GetFileName(package)} {entry.FullName}";
        checkedCount++;

        if (definition.Version != expected)
        {
            failures.Add($"{where}: AssemblyVersion {definition.Version}, expected {expected}");
        }

        foreach (var handle in metadata.AssemblyReferences)
        {
            var reference = metadata.GetAssemblyReference(handle);
            var referenceName = metadata.GetString(reference.Name);
            if (referenceName.StartsWith("TechieRag", StringComparison.OrdinalIgnoreCase) && reference.Version != expected)
            {
                failures.Add($"{where}: {name} references {referenceName} {reference.Version}, expected {expected}");
            }
        }

        Console.WriteLine($"{where}: {name} {definition.Version}");
    }
}

foreach (var failure in failures)
{
    Console.WriteLine($"::error::{failure}");
}

Console.WriteLine(failures.Count == 0
    ? $"PASS {checkedCount} assemblies in {packages.Count} packages all carry {expected}"
    : $"FAIL {failures.Count} mismatch(es) across {checkedCount} assemblies; expected {expected}");
return failures.Count == 0 ? 0 : 1;
