using System.Runtime.CompilerServices;

namespace RecipeApp.Tests.Conventions;

/// <summary>Enforces the CODING_STANDARDS.md DTO suffix rule, so a new DTO cannot drift from it unnoticed.</summary>
public class DtoNamingTests
{
    private const string DtoNamespace = "RecipeApp.API.DTOs";

    private static readonly string[] AllowedSuffixes = ["Request", "Response", "Query"];

    [Fact]
    public void EveryDto_EndsInRequestResponseOrQuery()
    {
        var dtoTypes = typeof(Program).Assembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith(DtoNamespace, StringComparison.Ordinal) == true)
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(type => !type.IsNested)
            .Where(type => !(type.IsAbstract && type.IsSealed)) // Static classes such as Mappings hold mapping code, not DTOs.
            .ToList();

        dtoTypes.Should().NotBeEmpty();
        dtoTypes
            .Where(type => !AllowedSuffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .Should().BeEmpty("every DTO ends in {0}", string.Join(", ", AllowedSuffixes));
    }
}
