using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ObjectFactorySourceGen.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void Test_GenerateCode()
        {
            // Arrange
            var inputSourceCode = @"
            // Your test input code goes here
            ";

            var generator = new ObjectFactorySourceGenerator();
            var syntaxTree = CSharpSyntaxTree.ParseText(inputSourceCode);
            var compilation = CSharpCompilation.Create("TestAssembly", new[] { syntaxTree },
                new[]
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    // Add additional metadata references as needed, e.g.
                    // MetadataReference.CreateFromFile(typeof(IServiceProvider).Assembly.Location),
                },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            // Act
            var generatorDriver = CSharpGeneratorDriver.Create(generator);
            generatorDriver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

            // Assert
            // Add specific assertions for your source generator, e.g.
            // Assert.Contains("GeneratedFactoryMethods", outputCompilation.SyntaxTrees.Select(tree => tree.FilePath));
        }
    }
}