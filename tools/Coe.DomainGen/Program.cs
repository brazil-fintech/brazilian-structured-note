using Coe.DomainGen;
using Coe.Ingestion;

// Regenerates domain/figures/generated/ from B3's published catalogue and the field annex of the
// Manual de Operações. Run from the repository root:
//
//     dotnet run --project tools/Coe.DomainGen
//
// Curated files under domain/figures/ are never touched; a figure that has one is skipped.

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var referenceDirectory = Path.Combine(root, "reference", "b3");
var domainDirectory = Path.Combine(root, "domain");

if (!Directory.Exists(referenceDirectory) || !Directory.Exists(domainDirectory))
{
    Console.Error.WriteLine($"Run from the repository root, or pass it as an argument (looked in {root}).");
    return 2;
}

var reference = B3Reference.Load(referenceDirectory);
foreach (var error in reference.Errors) Console.Error.WriteLine($"reference: {error}");
foreach (var error in reference.FigureFields.Errors) Console.Error.WriteLine($"annex: {error}");

var domain = new DomainFileLoader(domainDirectory).Load();
foreach (var error in domain.Errors) Console.Error.WriteLine($"domain: {error}");

var generator = new FigureGenerator(reference, domain);
var written = generator.Generate(domainDirectory);

Console.WriteLine($"B3 catalogue          {reference.Figures.Count} figures");
Console.WriteLine($"  curated by hand     {generator.Curated.Count}");
Console.WriteLine($"  generated           {written.Count}");
Console.WriteLine($"  no field annex      {generator.WithoutAnnex.Count}"
                + (generator.WithoutAnnex.Count > 0 ? $"  ({string.Join(", ", generator.WithoutAnnex)})" : ""));
Console.WriteLine();
Console.WriteLine($"{"Figure",-12} {"Fields",6} {"Inherited",9} {"Rules",5} {"Skipped",7}  Name");

foreach (var figure in written)
    Console.WriteLine($"{figure.Code,-12} {figure.Fields,6} {figure.Inherited,9} {figure.Rules,5} {figure.Skipped,7}  {figure.Name}");

Console.WriteLine();
Console.WriteLine($"{written.Sum(f => f.Fields)} attributes written across {written.Count} files.");
return 0;
